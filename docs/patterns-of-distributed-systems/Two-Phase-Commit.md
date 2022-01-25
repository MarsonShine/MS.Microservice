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

处理该请求的集群节点会检测到该请求是携带事务 ID 的事务的一部分。它管理事务的状态，存储请求中的键和值。键值不是直接提供给键值存储，而是单独存储。

```java
class TransactionalKVStore… 
	public void put(TransactionRef transactionRef, String key, String value) {
			TransactionState state = getOrCreateTransactionState(transactionRef);
			state.addPendingUpdates(key, value);
	}
```



### 锁和事物隔离

#### 死锁预防

##### 冲突错误（error on conflict）

##### 等待（wound-wait）

##### 等待死亡（wait-die）

### 提交以及回退

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