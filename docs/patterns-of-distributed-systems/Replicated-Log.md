# 复制日志

通过使用复制到所有集群节点的预写日志来保持多个节点的状态同步

## 问题

当多个节点需要共享状态时，这个状态就需要同步。所有集群节点的状态需要达成一致，即使某些节点断开或崩溃。这就需要给每个状态的更改请求要达成共识。

但是仅在单个请求中达成共识还不够。每个副本需要在相同的顺序中执行请求，否则不同的副本最后可能会得到不同的状态，即使它们对单个请求达成了共识。

## 解决方案

集群节点维护一个[预写日志](Write-Ahead-Log.md)。每个日志条目存储着共识所需要的状态以及用户请求。在这些日志条目之上一起达成一致（build consensus），所以集群所有的节点都必须明确相同的预写日志。然后请求按照日志顺序执行。因为所有集群节点都同意每个日志条目，所以它们以相同的顺序执行相同的请求。这确保了所有集群节点共享相同的状态。

使用 [Quorum](Quorum.md) 的容错共识构建机制需要两个阶段。

- 一是建立一个[生成时钟](Generation-Clock.md)以及了解在之前的 [Quorum](Quorum.md) 中复制的日志条目的阶段。
- 二是在集群所有节点上复制请求的阶段

为每个状态更改请求执行这两个阶段效率不高。所以集群节点在启动时选择一个 leader。在 leader 选举阶段建立[生成时钟](Generation-Clock.md)编号并检索前一个 [Quorum](Quorum.md) 的所有日志条目。（前一个领导者可能已经复制了大多数集群节点的条目。）一旦有一个稳定的 leader，只有 leader 协调复制。客户端与 leader 沟通。leader 将每个请求添加到日志中，并确保将其复制到所有 follower 上。一旦日志条目成功复制到大多数 follower，就会达成共识。这样，当有一个稳定的 leader 时，每个状态更改操作只需要一个阶段执行来达成共识。

## Multi-Paxos 和 Raft

[Multi-Paxos](https://www.youtube.com/watch?v=JEpsBg0AO6o&t=1920s) 和 [Raft](https://raft.github.io/) 是目前实现复制日志最受欢迎的算法。Multi-Paxos 只在学术论文中有粗略的描述。[Spanner](https://cloud.google.com/spanner) 和 [Cosmos DB](https://docs.microsoft.com/en-us/azure/cosmos-db/introduction) 等云数据库使用 Multi-Paxos，但实现细节没有很好的文档记录。Raft 在所有的实现细节上都有非常清晰的文档，并且是大多数开源系统首选的，尽管 Paxos 以及它的变体在学术论姐中讨论的多。

下面几节描述了 Raft 如何实现复制日志的。

## 复制客户端请求

![](../asserts/raft-replication.png)

​																									图1：复制

对于每个日志条目，leader 会将它附加到本地预写日志中，然后将其发送给所有的 follower。

```java
leader(class ReplicatedLog...)
	private Long appendAndReplicate(byte[] data) {
			Long lastLogEntryIndex = appendToLocalLog(data);
			replicateOnFollowers(lastLogEntryIndex);
			return lastLogEntryIndex;
	}
	
	private void replicateOnFollowers(Long entryAtIndex) {
			for(final FollowerHanlder follower: followers) {
					replicateOn(follower, entryAtIndex); // 发送复制请求给follower
			}
	}
```

follower 接受复制请求并追加到日志条目到本地日志中。成功追加日志之后，它们会响应 leader 并返回他们最新日志条目索引。响应还包括服务器当前的[生成时钟](Generation-Clock.md)编号。

follower 也会检查这个条目是否已经存在，或者是否存在正在复制条目之外的条目。它会忽略已经存在的条目。但是如果有来自不同的生成始时钟编号，它们就会删除冲突的条目。

```java
follower(class ReplicatedLog...)
	void maybeTruncate(ReplicationRequest replicationRequest) {
			replicationRequest.getEntries().stream()
							.filter(entry -> wal.getLastLogIndex() >= entry.getEntryIndex() &&
											entry.getGeneraion() != wal.readAt(entry.getEntryIndex()).getGeneration())
							.forEach(entry -> wal.truncate(entry.getEntryIndex()));
	}
	
follower(class ReplicatedLog...)
	private ReplicationResponse appendEntries(ReplicationRequest replicationRequest) {
			List<WALEntry> entries = replicationRequest.getEntries();
			entries.stream()
							.filter(e -> !wal.exists(e))
							.forEach(e -> wal.writeEntry(e));
			return new ReplicationResponse(SUCCEEDED, serverId(), replicationState.getGeneration(), wal.getLastLogIndex());
	}
```

当请求中的时钟编号比服务器已知的最新编号的要小时，follower 拒绝复制请求。并通知 leader 降为 follower。

```java
follwer(class ReplicatedLog...)
	Long currentGeneration = replicationRequest.getGeneration();
	if (currentGeneration > request.getGeneration()) {
			return new ReplicationResponse(FAILED, serverId(), currentGeneration, wal.getLastLogIndex());
	}
```

当 Leader 收到响应时，会跟踪在每个服务器上复制的日志索引。Leader 会使用这些日志索引成功的追踪复制到 Quorum 的日志条目，并将索引作为 commitIndex 进行跟踪。 commitIndex 是日志中的[高水位线](High-Water-Mark.md)。

```java
leader(class ReplicatedLog...)
	logger.info("Updating matchIndex for " + response.getServerId() + " to " + response.getReplicatedLogIndex());
	updateMatchingLogIndex(response.getServerId(), response.getReplicatedLogIndex());
	var logIndexAtQuorum = computeHighwaterMark(logIndexesAtAllServers(), config.numberOfServers());
	var currentHighWaterMark = replicationState.getHighWaterMark();
	if (logIndexAtQuorum > currentHighWaterMark && logIndexAtQuorum != 0) {
      applyLogEntries(currentHighWaterMark, logIndexAtQuorum);
      replicationState.setHighWaterMark(logIndexAtQuorum);
  }
  
 leader (class ReplicatedLog...)
  Long computeHighwaterMark(List<Long> serverLogIndexes, int noOfServers) {
      serverLogIndexes.sort(Long::compareTo);
      return serverLogIndexes.get(noOfServers / 2);
  }

leader (class ReplicatedLog...)
  private void updateMatchingLogIndex(int serverId, long replicatedLogIndex) {
      FollowerHandler follower = getFollowerHandler(serverId);
      follower.updateLastReplicationIndex(replicatedLogIndex);
  }

leader (class ReplicatedLog...)
  public void updateLastReplicationIndex(long lastReplicatedLogIndex) {
      this.matchIndex = lastReplicatedLogIndex;
  }
```

### 全复制

重要的是即使它们断开连接或崩溃并恢复，都要确保所有集群节点从 leader 接收到所有日志条目。Raft有一种机制来确保所有集群节点从 leader 接收到所有的日志条目。

对于 Raft 中的每一个复制请求，leader 也会发送日志索引和日志条目的生成，这些日志条目会在新条目被复制之前立即被发送。如果上一个日志索引和术语与其本地日志不匹配，关注者将拒绝该请求。这表明leader需要对一些较旧的条目同步follower日志。如果之前的日志索引和任期（term）与其本地日志不匹配，则 follower 拒绝该请求。 这表明 leader 需要为一些较旧的条目同步 follower 日志。 

```java
follower (class ReplicatedLog...)
  if (!wal.isEmpty() && request.getPrevLogIndex() >= wal.getLogStartIndex() &&
           generationAt(request.getPrevLogIndex()) != request.getPrevLogGeneration()) {
      return new ReplicationResponse(FAILED, serverId(), replicationState.getGeneration(), wal.getLastLogIndex());
  }

follower (class ReplicatedLog...)
  private Long generationAt(long prevLogIndex) {
      WALEntry walEntry = wal.readAt(prevLogIndex);
      return walEntry.getGeneration();
  }
```

因此 leader 递减 matchIndex，并尝试在较低的索引处发送日志条目。这一过程将持续到 follower 接受复制请求为止。

```java
leader (class ReplicatedLog...) 
	//由于条目冲突拒绝，递减matchIndex
	FollowerHandler peer = getFollowerHandler(response.getServerId());
  logger.info("decrementing nextIndex for peer " + peer.getId() + " from " + peer.getNextIndex());
  peer.decrementNextIndex();
  replicateOn(peer, peer.getNextIndex());
```

对于上一个日志索引和时钟编号检查允许 leader 检测两件事。

- 如果 follower 节点日志中有丢失的条目。例如，如果 follower 日志只有一个条目，leader 开始复制第三个条目，请求将被拒绝，直到 leader 复制第二个条目
- 如果日志中的上一条目来自不同的时钟编号，则高于或低于 leader 日志中的对应条目。**leader 将尝试从较低的索引复制条目，直到请求被接受。**follower 则会删除与该编号不匹配的条目。

这样，leader 通过使用上一个索引来检测丢失的条目或冲突的条目，尝试将自己的日志持续推送给所有 follower。这确保所有集群节点最终从 leader 接收所有日志条目，即使它们断开连接一段时间。

Raft 没有单独的提交消息，但将 commitIndex 作为正常复制请求的一部分发送。空的复制请求也作为心跳发送。因此，commitIndex 作为心跳请求的一部分发送给 followers。

### 日志条目按日志顺序执行

一旦 leader 更新了它的 commitIndex，它就会按照从 commitIndex 的最后一个值到 commitIndex 的最新值的顺序执行日志条目。客户端请求完成，一旦执行日志条目，响应就返回给客户端。

```java
class ReplicatedLog…
  private void applyLogEntries(Long previousCommitIndex, Long commitIndex) {
      for (long index = previousCommitIndex + 1; index <= commitIndex; index++) {
          WALEntry walEntry = wal.readAt(index);
          var responses = stateMachine.applyEntries(Arrays.asList(walEntry));
          completeActiveProposals(index, responses);
      }
  }
```

leader 也发送 commitIndex 伴随心跳请求发送给 follower。

```java
class ReplicatedLog…
  private void updateHighWaterMark(ReplicationRequest request) {
      if (request.getHighWaterMark() > replicationState.getHighWaterMark()) {
          var previousHighWaterMark = replicationState.getHighWaterMark();
          replicationState.setHighWaterMark(request.getHighWaterMark());
          applyLogEntries(previousHighWaterMark, request.getHighWaterMark());
      }
  }
```

