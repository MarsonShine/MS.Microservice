# 两阶段提交（Two Phase Commit）

在一个原子操作中更新多个节点的资源

## 问题

当数据要以原子的方式存储在多个集群节点上时，集群节点在知道其他集群节点的决定之前不能让客户端访问这些数据。每个节点都需要知道其他节点存储的数据是成功还是失败了。

## 解决方案

两阶段提交的本质，是它在两个阶段执行更新：

- 第一阶段为**准备阶段**，询问每个节点是否准备（promise）好执行更新值
- 第二阶段为**提交阶段**，即提交更新值

作为准备阶段的一部分，参与事务的每个节点都需要获得所需的任何东西，以确保它能够在第二阶段完成提交，例如所需的任何锁。一旦每个节点能够确保它可以在第二阶段提交，它就会让**协调器**知道，从而有效地向协调器承诺它可以并且将在第二阶段提交。如果其中有任何节点无法作出承诺，那么协调器就会告知其它节点回退并释放它们占有的任何锁，以及放弃这次事务操作。只有当所有参与者都同意继续进行，第二阶段才会开始，在这一点上，他们都将成功更新。

假设一个简单分布式 kv 存储实现场景，两阶段提交协议的工作原理如下。

事务客户端创建一个称为事务标识符的唯一 id。客户端还跟踪事务启动时间等其他细节。是用来防止死锁的，后面的锁机制将对此进行详细描述。客户端跟踪的唯一 id 以及额外的细节（如开始时间戳）用于跨集群节点引用事务。客户端维护了一个如下的事务引用，它与客户端的每个请求一起传递到集群的其它节点。

```java
class TransactionRef...
	private UUID txnId;
	private long startTimestamp;
	public TransactionRef(long startTimestamp) {
			this.txnId = UUID.randomUUID();
      this.startTimestamp = startTimestamp;
	}
	
class TransactionClient...
	TransactionRef transactionRef;
	public TransactionClient(ReplicaMapper replicaMapper, SystemClock systemClock) {
			this.clock = systemClock;
			this.transactionRef = new TransactionRef(clock.now());
			this.replicaMapper = replicaMapper;
	}
```

其中集群的一个节点扮演协调者的角色，来追踪客户端的事务状态。在 KV 存储器中，它通常是保存其中一个键的数据的集群节点（协调者）。它通常作为集群节点，用于给客户端使用的第一个 key 的数据。

在存值之前，客户端与协调器通信，通知它（协调器）事务开始。因为协调器是存储值的集群节点之一，所以当客户端使用特定的 key 发起 get 或 put 操作时，它会被动态地获取。

```java
class TransactionClient...
	private TransactionalKVStore coordinator;
	private void maybeBeginTransaction(String key) {
			if (coordinator == null) {
					coordinator = replicaMapper.serverFor(key);
					coordinator.begin(transactionRef);
			}
	}
```

事务协调器跟踪事务的状态。它在[预写日志(Write-Ahead Log)](WAL.md)中记录每一个更改，以确保在崩溃的情况下提供详细信息。

```java
class TransactionCoordinator… 
	Map<TransactionRef, TransactionMetadata> transactions = new ConcurrentHashMap<>();
	WriteAheadLog transactionLog;
	
	public void begin(TransactionRef transactionRef) {
			TransactionMetadata txnMetadata = new TransactionMetadata(transactionRef, systemClock, transactionTimeoutMs);
			transactionLog.writeEntry(txnMetadata.serialize());
			transactions.put(transactionRef, txnMetadata);
	}
	
	class TransactionMetadata...
		private TransactionRef txn;
		private List<String> participatingKeys = new ArrayList<>();
		private TransactionStatus transactionStatus;
```

客户端将每个 key 作为事务一部分发送给协调器。通过这种方式，协调器跟踪事务部分所有的 key。 协调器在事务元数据中记录作为事务一部分的键 key。然后，可以使用这些 key 来了解在事务一部分的所有集群节点。因为每个 KV 通常都是用 [Replicated Log](Replicated-Log.md) 进行复制的，leader 服务器为这些在事务的生命周期中会发生变化的特定的 key 处理请求，所以会跟踪键，而不是实际的服务器地址。然后，客户端向持有 key 数据的服务器发送 put 或 get 请求。服务器是根据分区策略选择的。需要注意的是，客户端直接与服务器通信，而不是通过协调器。这避免了在网络上两次发送数据，从客户端到协调器，然后从协调器到各自的服务器。

作为事务一部分，还可以使用这些键来了解所有相关集群节点。因为每个 kv 通常都是用 [Replicated Log](Replicated-Log.md) 来复制的，所以处理特定 key 请求的 leader 服务器可能会在事务的生命周期中发生变化，所以会跟踪键，而不是实际的服务器地址。

```java
class TransactionClient… 
	public CompletableFuture<String> get(String key) {
			meybeBeginTransaction(key);
			coordinator.addKeyToTransaction(transactionRef, key);
			TransactionalKVStore kvStore = replicaMapper.serverFor(key);
			return kvStore.get(transactionRef, key);
	}
	
	public void put(String key, String value) {
			maybeBeginTransaction(key);
			coordinator.addKeyToTransaction(transactionRef, key);
			replicaMapper.serverFor(key).put(transactionRef, key, value);
	}
	
	class TransactionCoordinator...
		public synchronized void addKeyToTransaction(TransactionRef transactionRef, String key) {
				TransactionMetadata metadata = transactions.get(transactionRef);
				if (!metadata.getParticipatingKeys().contains(key)) {
						metadata.addKey(key);
						transactionRef.writeEntry(metadata.serialize());
				}
		}
```

处理该请求的集群节点会检测到该请求是携带事务 ID 的事务的一部分。它管理事务的状态，存储请求中的键和值。键值不是直接提供给KVStore，而是单独存储。

```java
class TransactionalKVStore… 
	public void put(TransactionRef transactionRef, String key, String value) {
			TransactionState state = getOrCreateTransactionState(transactionRef);
			state.addPendingUpdates(key, value);
	}
```

### 锁和事物隔离

这些请求还需要在 key 上上锁。尤其是 get 请求有一个读锁，而 put 请求有一个写锁。读取值时获取读锁。

```java
class TransactionalKVStore...
	public CompletableFuture<String> get(TransactionRef txn, String key) {
			CompletableFuture<TrasactionRef> lockFuture
							= lockManager.acquire(txn, key, LockModel.READ);
			return lockFuture.thenApply(transactionRef -> {
					getOrCreateTransactionState(transactionRef);
					return kv.get(key);
			})
	}
	
	synchronized TransactionState getOrCreateTransactionState(TransactionRef txnRef) {
			TransactionState state = this.ongoingTransactions.get(txnRef);
			if (state == null) {
					state = new TransactionState();
					this.ongoingTransactions.put(txnRef, state);
			}
			return state;
	}
```

只有当事务提交且值在 KVStore 中可见时，才可以使用写锁。在此之前，集群节点只能将修改的值作为挂起操作（pending）跟踪。

延迟锁定降低了事务冲突的机会。

```
class TransactionalKVStore… 
	public void put(TransactionRef transactionRef, String key, String value) {
			TransactionState state = getOrCreateTransactionState(transactionRef);
			state.addPendingUpdates(key, value);
	}
```

**需要注意的是，这些锁是 long-alive 的，并且在请求完成时不会被释放。只有在事务提交完成之后才会释放。**这种在事务期间持有锁并只有在事务提交或回滚时才释放锁的技术称为[两阶段锁定](https://en.wikipedia.org/wiki/Two-phase_locking)。两阶段锁定对于提供可序列化（serializable） 的隔离级别至关重要。可序列化意味着事务的效果是可见的，就像它们一次只执行一个一样（串行化）。

#### 死锁预防

使用锁不当会导致死锁，两个事务相互等待对方释放锁。如果在检测到冲突时不允许事务等待和中止，则可以避免死锁。有不同的策略用来决定哪些事务被中止，哪些事务被允许继续执行。

LockManager 实现了如下等待策略：

```java
class LockManager...
	WaitPolicy waitPolicy;
```

`WaitPolicy` 的值决定了在请求冲突时要做什么。

```java
public enum WaitPolicy {
		WoundWait,
		WaitDie,
		Error
}
```

锁是一个对象，它跟踪当前拥**有该锁的事务**和**等待该锁的事务**。

```java
class Lock...
	Queue<LockRequest> waitQueue = new LinkedList<>();
	List<TransactionRef> owners = new ArrayList<>();
	LockMode lockMode;
```

当一个事务请求占有锁时，如果这里不存在冲突的事务拥有该锁，则 LockManager 立即同意占有该锁。

```java
class LockManager...
	public synchronized CompletableFuture<TransactionRef> acquire(TransactionRef txn, String key, LockMode lockMode) {
			return acquire(txn, key, lockMode, new CompletableFuture<>());
	}
	
	CompletableFuture<TransactionRef> acquire(TransactionRef txnRef, String key, LockMode askedLockMode, CompletableFuture<TransactionRef> lockFuture) {
			Lock lock = getOrCreateLock(key);
			logger.debug("acquiring lock for = " + txnRef + " on key = " + key + " with lock mode = " + askedLockMode);
			if (lock.isCompletible(txnRef, askedLockMode)) {
					lock.addOwner(txnRef, askedLockMode);
					lockFuture.complete(txnRef);
					logger.debug("acquired lock for = " + txnRef);
					return lockFuture;
			}
	}
	
	class Lock...
		public boolean isCompletible(TransactionRef txnRef, LockMode lockMode) {
				if (hasOwner()) {
						return (inReadMode() && lockMode == LockMode.READ)
										|| isUpgrade(txnRef, lockMode);
				}
				return true;
		}
```

如果它们冲突了，LockManager 就会根据等待策略（WaitPolicy）作出相应的逻辑。

##### 冲突错误（error on conflict）

如果等待策略出错了，它将抛出一个错误，调用事务将回滚并在随机的超时时间后重试。

```java
class LockManager… 
	private CompletableFuture<TransactionRef> handleConflict(Lock lock,
                                                           TransactionRef txnRef,
                                                           String key,
                                                           LockMode askedLockMode,
                                                           CompletableFuture<TransactionRef> lockFuture) {
  		switch (waitPolicy) {
  				case Error: {
  						lockFuture.completeExceptionally(new WriteConflictException(txnRef, key, lock.owners));
  						return lockFuture;
  				}
  				case WoundWait: {
  						return lock.woundWait(txnRef, key, askedLockMode, lockFuture, this);
  				}
  				case WaitDie: {
              return lock.waitDie(txnRef, key, askedLockMode, lockFuture, this);
          }
  		}   
      throw new IllegalArgumentException("Unknown waitPolicy " + waitPolicy);
	}
```

在并发的情况下，当有很多用户事务试图获取锁时，如果所有的用户事务都需要重新启动，势必会严重限制系统吞吐量。 数据存储试图确保有最少的事务重启。

一种常见的技术是为事务分配唯一的 ID 并对其排序。例如，[Spanner 分配唯一的 id](https://dahliamalkhi.github.io/files/SpannerExplained-SIGACT2013b.pdf)给事务，以这样一种方式来排序。该技术与在 [Paxos](Paxos.md) 中讨论的跨集群节点排序请求的技术非常相似。一旦可以对事务进行排序，就可以使用两种技术来避免死锁，并且仍然允许事务在不重启的情况下继续进行。

事务引用的创建方式可以与其他事务引用进行比较和排序。最简单的方法是为每个事务分配一个时间戳，并基于时间戳进行比较。

```java
class TransactionRef… 
	boolean after(TransactionRef otherTransactionRef) {
			return this.startTimestamp > otherTransactionRef.startTimestamp;
	}
```

但是在分布式系统中，[时钟不是单调的](https://martinfowler.com/articles/patterns-of-distributed-systems/time-bound-lease.html#wall-clock-not-monotonic)，所以使用了一种不同的方法，比如为事务分配唯一的 id，这样它们就可以被排序。除了已排序的 id 外，还会跟踪每个 id 的大小，以便能够对事务进行排序。[Spanner](https://cloud.google.com/spanner) 通过在系统中追踪每个事务的大小来给这些事务排序。

为了能够对所有事务进行排序，为每个集群节点分配一个唯一的 ID。客户端在事务开始时获取协调器，并从协调器获取事务 ID。作为协调器的集群节点负责生成事务 ID，如下所示。

```java
class TransactionCoordinator… 
	private int requestId;
	public MonotonicId begin() {
			return new MonotonicId(requestId++, config.getServerId());
	}
	
class MonotonicId...
	public class MonotonicId implements Comparable<MonotonicId> {
			public int requestId;
			int serverId;
			
			public MonotonicId(int requestId, int serverId) {
          this.serverId = serverId;
          this.requestId = requestId;
      }
      
      public static MonotonicId empty() {
          return new MonotonicId(-1, -1);
      }
      
      public boolean isAfter(MonotonicId other) {
          if (this.requestId == other.requestId) {
              return this.serverId > other.serverId;
          }
          return this.requestId > other.requestId;
      } 
	}
	
	class TransactionClient… 
		private void beginTransaction(String key) {
				if (coordinator == null) {
					coordinator = replicaMapper.serverFor(key);
          MonotonicId transactionId = coordinator.begin();
          transactionRef = new TransactionRef(transactionId, clock.nanoTime());
				}
		}
```

客户端通过记录事务的年龄（事务开始以来所经过的时间称为年龄）跟踪事务。

```java
class TransactionRef…
  public void incrementAge(SystemClock clock) {
      age = clock.nanoTime() - startTimestamp;
  }
```

每次向服务器发送 get 或 put 请求时，客户端都会增加时间。然后事务就会按照它们的年龄进行排序。当存在相同的事务年龄时，就通过比较事务 id。

```java
class TransactionRef…
  public boolean isAfter(TransactionRef other) {
       return age == other.age?
                  this.id.isAfter(other.id)
                  :this.age > other.age;
  }
```

##### 有害等待（wound-wait）

在[有害等待(wound-wait)](http://www.mathcs.emory.edu/~cheung/Courses/554/Syllabus/8-recv+serial/deadlock-woundwait.html)方法中，如果存在冲突，则将请求锁的事务引用与当前拥有锁的所有事务进行比较。如果当前锁的所有者都要比请求锁的事务更年轻，那么这些事务都要被中止。但是，如果请求该锁的事务比拥有该锁的事务年轻，那么它将等待该锁。

```java
class Lock...
	public CompletableFuture<TransactionRef> woundwait(TransactionRef txnRef,
																										 String key,
																										 LockMode askedLockMode,
																										 CompletableFuture<TransactionRef> lockFuture,
																										 LockManager lockManager) {
			if (allOwningTransactionsStartedAfter(txnRef) && !anyOwnerIsPrepared(lockManager)) {	//注意，如果已经处于准备阶段，则不会终止
					abortAllOwners(lockManager, key, txnRef);
					return lockManager.acquire(txnRef, key, askedLockMode, lockFuture);
			}
			
			LockRequest lockRequest = new LockRequest(txnRef, key, askedLockMode, lockFuture);
			lockManager.logger.debug("Adding to wait queue = " + lockRequest);
			addToWaitQueue(lockRequest);
			return lockFuture;
	}
	
class Lock...
	private boolean allOwningTransactionsStartedAfter(TransactionRef txn) {
			return owners.stream().filter(o -> !o.equals(txn)).allMatch(owner -> owner.after(txn));
	}
```

其中一个关键需要注意，如果这个锁拥有者的事务已经处于两阶段提交的准备阶段，那么它就不会被中止。

##### 等待死亡（wait-die）

[wait-die方法](http://www.mathcs.emory.edu/~cheung/Courses/554/Syllabus/8-recv+serial/deadlock-waitdie.html)的工作原理与[wound-wait](http://www.mathcs.emory.edu/~cheung/Courses/554/Syllabus/8-recv+serial/deadlock-woundwait.html)相反。如果锁所有者都比请求锁的事务年轻，那么事务就会等待锁。如果请求锁的事务小于拥有该事务的事务，则该事务将被中止。

```java
class Lock...
	public CompletableFuture<TransactionRef> waitDie(TransactionRef txnRef,
																									 String key,
																									 LockMode askedLockMode,
																									 CompletableFuture<TransactionRef> lockFuture,
																									 LockManager lockManager) {
			if (allOwningTransactionsStartedAfter(txnRef)) {
					addToWaitQueue(new LockRequest(txnRef, key, askedLockMode, lockFuture));
					return lockFuture;
			}
      
      lockManager.abort(txnRef, key);
      lockFuture.completeExceptionally(new WriteConflictException(txnRef, key, owners));
      return lockFuture;
	}
```

与 wait-die 相比 Wound-wait 机制通常很少会重新启动。所以像[Spanner](https://cloud.google.com/spanner)这样的数据存储器就会使用[wound-wait](http://www.mathcs.emory.edu/~cheung/Courses/554/Syllabus/8-recv+serial/deadlock-woundwait.html)的方法。

当持有锁的事务释放锁时，正处于等待的事务就会被授予锁。

```java
class LockManager...
	private void release(TransactionRef txn, String key) {
			Optional<Lock> lock = getLock(key);
			lock.isPresent(l -> {
					l.release(txn, this);
			});
	}
	
class Lock...
	public void release(TransactionRef txn, LockManager lockManager) {
			removeOwner(txn);
			if (hasWaiters()) {
					LockRequest lockRequest = getFirst(lockManager.waitPolicy);
					lockManager.acquire(lockRequest.txn, lockRequest.key, lockRequest.lockMode, lockRequest.future);
			}
	}
```

### 提交以及回退

如果客户端非常顺利没有经过任何冲突成功读写所有键值，它就会通过向协调者发送一个提交请求来启动请求提交。

```java
class TransactionClient...
	public CompletableFuture<Boolean> commit() {
			return coordinator.commit(transactionRef);
	}
```

事务协调者记录了准备提交的事务状态。协调者在第二阶段实现了提交处理程序。

- 它首先向每个参与者发送准备请求。
- 一旦得到所有参与者的响应信息，协调者就会将事务从准备标记为完成。然后发送提交请求给所有的参与者。

```java
class TransactionCoordinator...
	public CompletableFuture<Boolean> commit(TransactionRef transactionRef) {
			TransactionMetadata metadata = transactions.get(transactionRef);
			metadata.markPreparingToCommit(transactionLog);
			List<CompletableFuture<Boolean>> allPrepared = sendPrepareRequestToParticipants(transactionRef);
			CompletableFutrue<List<Boolean>> futureList = sequence(allPrepared);
			return futureList.thenApply(result -> {
					if (!result.stream().allMatch(r -> r)) {
							logger.info("Rolling back = " + transactionRef);
							rollback(transactionRef);
							return false;
					}
					metadata.markPrepared(transactionLog);
					sendCommitMessageToParticipants(transactionRef);
					metadata.markCommitComplete(transactionLog);
					return true;
			});
	}
	
	public List<CompletableFuture<Boolean>> sendPrepareRequestToParticipants(TransactionRef transactionRef) {
			TransactionMetadata transactionMetadata = transactions.get(transactionRef);
			var transactionParticipants = getParticipants(transactionMetadata.getParticipatingKeys());
			return transactionParticipants.keySet()
							.stream()
							.map(server -> server.handlePrepare(transactionRef))
							.collect(Collectors.toList());
	}
	
	private void sendCommitMessageToParticipants(TransactionRef transactionRef) {
			TransactionMetadata transactionMetadata = transactions.get(transactionRef);
			var participantsForKeys = getParticipants(transactionMetadata.getParticipatingKeys());
			return transactionParticipants.keySet()
							.stream()
							.forEach(kvStore -> {
									List<String> keys = participantsForKeys.get(kvStore);
									kvStore.handleCommit(transactionRef, keys);
							});
	}
	
	private Map<TransactionalKVStore, List<String>> getParticipants(List<String> participatingKeys) {
			return participatingKeys.stream()
							.map(k -> Pair.of(serverFor(k), k))
							.collect(Collectors.groupingBy(Pair::getKey, Collectors.mapping(Pair::getValue, Collectors.toList())));
	}
```

集群接收准备请求会做两件事：

- 尝试获取所有键的写锁。
- 一旦成功获取，它将会把所有的变更写到[预写日志](WAL.md)中。

如果成功做完上面两件事，就能保证事务不会冲突，甚至是当集群节点崩溃的情况下也能恢复所有必要的状态最终完成事务。

```java
class TransactionalKVStore… 
	public synchronized CompletableFuture<Boolean> handlePrepare(TransactionRef txn) {
			try {
					TransactionState state = getTransactionState(txn);
					if (state.isPrepared()) {
              return CompletableFuture.completedFuture(true); //already prepared.
          }
          if (state.isAborted()) {
              return CompletableFuture.completedFuture(false); //aborted by another transaction.
          }
          
          Optional<Map<String, String>> pendingUpdates = state.getPendingUpdates();
          CompletableFuture<Boolean> prepareFuture = prepareUpdates(txn, pendingUpdates);
          return prepareFuture.thenApply(ignored -> {
              Map<String, Lock> locksHeldByTxn = lockManager.getAllLocksFor(txn);
              state.markPrepared();
              writeToWAL(new TransactionMarker(txn, locksHeldByTxn, TransactionStatus.PREPARED));
              return true;
          });
			} catch (TransactionException| WriteConflictException e) {
					logger.error(e);
			}
			return CompletableFuture.completedFuture(false);
	}
	
	private CompletableFuture<Boolean> prepareUpdates(TransactionRef txn, Optional<Map<String, String>> pendingUpdates) {
			if (pendingUpdates.isPresent()) {
					Map<String, String> pendingKVs = pendingUpdates.get();
					CompletableFuture<List<TransactionRef>> lockFuture = acquireLocks(txn, pendingKVs.keySet());
          return lockFuture.thenApply(ignored -> {
              writeToWAL(txn, pendingKVs);
              return true;
          });
			}
			return CompletableFuture.completedFuture(true);
	}
	
	TransactionState getTransactionState(TransactionRef txnRef) {
      return ongoingTransactions.get(txnRef);
  }
  
  private void writeToWAL(TransactionRef txn, Map<String, String> pendingUpdates) {
     for (String key : pendingUpdates.keySet()) {
          String value = pendingUpdates.get(key);
          wal.writeEntry(new SetValueCommand(txn, key, value).serialize());
      }
  }
  
  private CompletableFuture<List<TransactionRef>> acquireLocks(TransactionRef txn, Set<String> keys) {
      List<CompletableFuture<TransactionRef>> lockFutures = new ArrayList<>();
      for (String key : keys) {
          CompletableFuture<TransactionRef> lockFuture = lockManager.acquire(txn, key, LockMode.READWRITE);
          lockFutures.add(lockFuture);
      }
      return sequence(lockFutures);
  }
```

当集群节点从协调者接收到提交请求时，可以安全的让 key-value 变更可见。当提交变更时集群节点会做一下三件事：

- 标记事务为已提交。如果集群节点此时发生故障，它知道事务的结果，可以重复以下步骤。
- 将所有的更改保存到 k-v 存储器中
- 释放全部持有的锁。

```java
class TransactionalKVStore… 
	public synchronized void handleCommit(TransactionRef transactionRef, List<String> keys) {
			if (!ongoingTransactions.containsKey(transactionRef)) {
          return; //this is a no-op. Already committed.
      }
      if (!lockManager.hasLocksFor(transactionRef, keys)) {
          throw new IllegalStateException("Transaction " + transactionRef + " should hold all the required locks for keys " + keys);
      }
      writeToWAL(new TransactionMarker(transactionRef, TransactionStatus.COMMITTED, keys));
      
      applyPendingUpdates(transactionRef);
      
      releaseLocks(transactionRef, keys);
	}
	
	private void removeTransactionState(TransactionRef txnRef) {
			ongoingTransactions.remove(txnRef);
	}
	
	private void applyPendingUpdates(TransactionRef txnRef) {
			TransactionState state = getTransactionState(txnRef);
			Optional<Map<String, String>> pendingUpdates = state.getPendingUpdates();
			apply(txnRef, pendingUpdates);
	}
	
	private void apply(TransactionRef txnRef, Optional<Map<String, String>> pendingUpdates) {
			if (pendingUpdates.isPresent()) {
          Map<String, String> pendingKv = pendingUpdates.get();
          apply(pendingKv);
      }
      removeTransactionState(txnRef);
	}
	
	private void apply(Map<String, String> pendingKv) {
      for (String key : pendingKv.keySet()) {
          String value = pendingKv.get(key);
          kv.put(key, value);
      }
  }
  private void releaseLocks(TransactionRef txn, List<String> keys) {
          lockManager.release(txn, keys);
  }
  
  private Long writeToWAL(TransactionMarker transactionMarker) {
     return wal.writeEntry(transactionMarker.serialize());
  }
```

回滚操作是用类似的操作实现的。如果存在任何的失败，客户端就会通知协调器回滚事务。

```java
class TransactionClient…
  public void rollback() {
      coordinator.rollback(transactionRef);
  }
```

事务协调器将事务的状态记录为准备回滚。然后，它将回滚请求转发到所有存储给定事务值的服务器。一旦所有请求都成功，协调器将「事务回滚」标记为「完成」。如果协调器在事务被标记为“准备回滚”后崩溃，它可以继续向所有参与的集群节点发送回滚消息。

```java
class TransactionCoordinator… 
	public void rollback(TransactionRef transactionRef) {
      transactions.get(transactionRef).markPrepareToRollback(this.transactionLog);

      sendRollbackMessageToParticipants(transactionRef);

      transactions.get(transactionRef).markRollbackComplete(this.transactionLog);
  }
  
  private void sendRollbackMessageToParticipants(TransactionRef transactionRef) {
      TransactionMetadata transactionMetadata = transactions.get(transactionRef);
      var participants = getParticipants(transactionMetadata.getParticipatingKeys());
      for (TransactionalKVStore kvStore : participants.keySet()) {
          List<String> keys = participants.get(kvStore);
          kvStore.handleRollback(transactionMetadata.getTxn(), keys);
      }
  }
```

集群节点接收到会滚请求会做两件事：

- 记录回滚事务状态到预写日志中。
- 丢弃事务状态。
- 释放所有锁。

```java
class TransactionalKVStore… 
	public synchronized void handleRollback(TransactionRef transactionRef, List<String> keys) {
      if (!ongoingTransactions.containsKey(transactionRef)) {
          return; //no-op. Already rolled back.
      }
      writeToWAL(new TransactionMarker(transactionRef, TransactionStatus.ROLLED_BACK, keys));
      this.ongoingTransactions.remove(transactionRef);
      this.lockManager.release(transactionRef, keys);
  }
```

#### 幂等操作

### 场景举例

#### 原子写操作

#### 事物冲突

### 使用版本化的值（Versioned Value）

#### 技术讨论

### 使用复制日志

### 失败处理

### 跨异构系统的事务

## 例子

## 原文链接

https://martinfowler.com/articles/patterns-of-distributed-systems/two-phase-commit.html