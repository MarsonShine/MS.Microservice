# Gossip Dissemination

随机选择集群中的一个节点传递信息来确保这些信息能到达所有集群其它所有的节点，而不会在网络中丢失。

## 问题

在集群中，每个节点都需要将自己的元数据传递到集群中其它的节点，并且不是通过依赖状态共享的。在一个大集群中，如果所有的服务器都能互相之间通信，这样就会消耗大量的宽带。即使某些网络连接出现问题，信息也应该到达所有节点。

## 解决方案

集群节点使用传播风格（gossip style）的通信来传播状态更新。每个节点选择一个随机节点来传递它的信息。这是一个有规律的间隔，比如每1秒。每一次，都选择一个随机节点来传递信息。

在大集群中，下面列出的都是需要考虑的：

- 对每个服务器生成的消息数量设置一个固定的限制
- 这些消息不应该消耗大量的网络带宽。应该有一个上限，比如几百 Kbs，以确保应用程序的数据传输不会受到跨集群的太多消息的影响。
- 元数据传播应该能够容忍网络和少量服务器故障。即使一些网络连接断开，或者一些服务器出现故障，它也应该能够到达所有的集群节点。

正如下面注释中所讨论的，gossip style 的通信满足了所有这些需求。

> ## （流行病、谣言和计算机通信）Epidemics, Rumours and Computer Communication
>
> 信息交换与 gossip 协议本质上是一致的。即使它的 Gossip 状态收敛(converges)得非常快，在整个集群识别新节点或检测到节点故障之前也会有一定的延迟。用 gossip 协议实现信息交换，需要容忍最终一致性。
>
> 对于要求强一致性的操作，就需要用到[一致性核心](Consistent-Core.md)。
>
> 在相同的节点中使用这两种方法是一种常见的做法。例如 [consul](https://www.consul.io/) 对于组成员和失败探测就是用了 gossip 协议，但是也使用了基于 raft 一致性存储一个强一致性的服务目录

每个集群节点将元数据存储为与每个节点关联的键值对列表，如下所示：

```java
class Gossip…
  Map<NodeId, NodeState> clusterMetadata = new HashMap<>();

class NodeState…
  Map<String, VersionedValue> values = new HashMap<>();
```

在启动时，每个集群节点添加关于自身的元数据，需要将其传播到其他节点。例如元数据可以是节点监听的 IP 地址和端口、它负责的分区等。Gossip 实例需要知道至少一个其他节点才能启动 gossip 通信。众所周知，集群节点可以用来初始化 Gossip 实例，它被称为种子节点或是导入器。任何节点都能作为这样的引入器。

```java
class Gossip… 
  public Gossip(InetAddressAndPort listenAddress, 
  			   List<InetAddressAndPort> seedNodes, 
  			   String nodeId) throw IOException {
      this.listenAddress = listenAddress;
      // 过滤这个节点本身，以防它是种子节点的一部分
      this.seedNodes = removeSelfAddress(seedNodes);
      this.nodeId = new NodeId(nodeId);
      addLocalState(GossipKeys.ADDRESS, listenAddress.toString());
      
      this.socketServer = new NIOSocketListener(newGossipRequestConsumer(), listenAddress);
  }
  
  private void addLocalState(String key, String value) {
      NodeState nodeState = clusterMetadata.get(listenAddress);
      if (nodeState == null) {
          nodeState = new NodeState();
          clusterMetadata.put(nodeId, nodeState);
      }
      nodeState.add(key, new VersionedValue(value, incremenetVersion()));
  }
```

每个集群节点调度一个作业，以将其拥有的元数据**定期传输**到其他节点。

```java
class Gossip… 
private ScheduledThreadPoolExecutor gossipExecutor = new ScheduledThreadPoolExecutor(1);
  private long gossipIntervalMs = 1000;
  private ScheduledFuture<?> taskFuture;
  public void start() {
      socketServer.start();
      taskFuture = gossipExecutor.scheduleAtFixedRate(()-> doGossip(),
                  gossipIntervalMs,
                  gossipIntervalMs,
                  TimeUnit.MILLISECONDS);
}
```

当调度任务被调用时，它会从元数据映射的服务器列表中随机选取一小组节点。一个小的常量定义为 gossip fanout，它决定了有多少个节点可以选定为 gossip 目标。如果什么都没有，它就随机选择一个种子节点，并将它的元数据映射发送到该节点。

```java
class Gossip…
  public void doGossip() {
      List<InetAddressAndPort> knownClusterNodes = liveNodes();
      if (knownClusterNodes.isEmpty()) {
          sendGossip(seedNodes, gossipFanout);
      } else {
          sendGossip(knownClusterNodes, gossipFanout);
      }
  }
  
  private List<InetAddressAndPort> liveNodes() {
      Set<InetAddressAndPort> nodes
              = clusterMetadata.values()
              .stream()
              .map(n -> InetAddressAndPort.parse(n.get(GossipKeys.ADDRESS).getValue()))
              .collect(Collectors.toSet());
      return removeSelfAddress(nodes);
  }
  private void sendGossip(List<InetAddressAndPort> knownClusterNodes, int gossipFanout) {
      if (knownClusterNodes.isEmpty()) {
          return;
      }

      for (int i = 0; i < gossipFanout; i++) {
          InetAddressAndPort nodeAddress = pickRandomNode(knownClusterNodes);
          sendGossipTo(nodeAddress);
      }
  }
  private void sendGossipTo(InetAddressAndPort nodeAddress) {
      try {
          getLogger().info("Sending gossip state to " + nodeAddress);
          SocketClient<RequestOrResponse> socketClient = new SocketClient(nodeAddress);
          GossipStateMessage gossipStateMessage
                  = new GossipStateMessage(this.clusterMetadata);
          RequestOrResponse request
                  = createGossipStateRequest(gossipStateMessage);
          byte[] responseBytes = socketClient.blockingSend(request);
          GossipStateMessage responseState = deserialize(responseBytes);
          merge(responseState.getNodeStates());

      } catch (IOException e) {
          getLogger().error("IO error while sending gossip state to " + nodeAddress, e);
      }
  }
  private RequestOrResponse createGossipStateRequest(GossipStateMessage gossipStateMessage) {
      return new RequestOrResponse(RequestId.PushPullGossipState.getId(),
              JsonSerDes.serialize(gossipStateMessage), correlationId++);
  }

```

集群节点接收 gossip 消息并检查其元数据就会发现三件事。

- 该消息正在传过来，但在该节点的状态映射中还不可用
- 这些值在传入的 Gossip 消息中是没有的
- 当节点的值出现在传入消息中时，将选择更高的版本值

然后将缺失的值添加到自己的状态映射中。传入消息中缺少的任何值都将作为响应返回。

集群节点将发送 gossip 信息并从 gossip 返回得到的值添加到自己状态中。

```java
class Gossip… 
  private void handleGossipRequest(org.distrib.patterns.common.Message<RequestOrResponse> request) {
      GossipStateMessage gossipStateMessage = deserialize(request.getRequest());
      Map<NodeId, NodeState> gossipedState = gossipStateMessage.getNodeStates();
      getLogger().info("Merging state from " + request.getClientSocket());
      merge(gossipedState);

      Map<NodeId, NodeState> diff = delta(this.clusterMetadata, gossipedState);
      GossipStateMessage diffResponse = new GossipStateMessage(diff);
      getLogger().info("Sending diff response " + diff);
      request.getClientSocket().write(new RequestOrResponse(RequestId.PushPullGossipState.getId(),
                      JsonSerDes.serialize(diffResponse),
                      request.getRequest().getCorrelationId()));
  }
public Map<NodeId, NodeState> delta(Map<NodeId, NodeState> fromMap, Map<NodeId, NodeState> toMap) {
    Map<NodeId, NodeState> delta = new HashMap<>();
    for (NodeId key : fromMap.keySet()) {
        if (!toMap.containsKey(key)) {
            delta.put(key, fromMap.get(key));
            continue;
        }
        NodeState fromStates = fromMap.get(key);
        NodeState toStates = toMap.get(key);
        NodeState diffStates = fromStates.diff(toStates);
        if (!diffStates.isEmpty()) {
            delta.put(key, diffStates);
        }
    }
    return delta;
}
public void merge(Map<NodeId, NodeState> otherState) {
    Map<NodeId, NodeState> diff = delta(otherState, this.clusterMetadata);
    for (NodeId diffKey : diff.keySet()) {
        if(!this.clusterMetadata.containsKey(diffKey)) {
            this.clusterMetadata.put(diffKey, diff.get(diffKey));
        } else {
            NodeState stateMap = this.clusterMetadata.get(diffKey);
            stateMap.putAll(diff.get(diffKey));
        }
    }
}
```

该过程每秒钟在每个集群节点上发生一次，每次选择一个不同的节点来交换状态。

## 避免不必要的状态交换

上面代码展示的是在 Gossip 消息中发送的节点的完整状态。这对于新加入的节点来说没有问题，但是一旦状态是最新的，就没有必要发送完整的状态。集群节点只需要发送自上次 gossip 的状态更改。为此，每个节点都维护了一个版本号，该版本号在每次新的元数据被添加到本地的时候自增。

```java
class Gossip...
  private int gossipStateVersion = 1;
  
  private int incrementVersion() {
  		return gossipStateVersion++;
  }
```

在集群里的每个值都是用版本号来维护的。这是 [Versioned Value](Versioned-Value.md) 模式的一个例子。

```java
class Gossip...
	int version;
	String value;
	
	public VersionedValue(String value, int version) {
			this.version = version;
			this.value = value;
	}
	
	public int getVersion() {
			return version;
	}
	
	public String getValue() {
			return value;
	}
```

然后每个 Gossip 周期内能从一个指定的版本交换状态。

```java
class Gossip...
	private void sendKnownVersions(InetAddressAndPort gossipTo) throws IOException {
			Map<NoteId, Integer> maxKnownNodeVersions = getMaxKnownNodeVersions();
			RequestOrResponse knownVersionRequest = new RequestOrResponse(RequestId.GossipVersions.getId(),
							JsonSerDes.serialize(new GossipStateVersions(maxKnownNodeVersions)), 0);
			SocketClient<RequestOrResponse> socketClient = new SocketClient(gossipTo);
			byte[] knownVersionResponseBytes = socketClient.blockingSend(knownVersionRequest);
	}
	
	private Map<NodeId, Integer> getMaxKnownNodeVersions() {
			return clusterMetadata.entrySet()
							.stream()
							.collect(Collectors.toMap(e -> e.getKey(), e -> e.getValue().maxVersion()));
	}

class NodeState...
  public int maxVersion() {
  		return values.values().stream().map(v -> v.getVersion()).max(Comparator.naturalOrder()).orElse(0);
}
```

只有接收节点的版本大于请求中的版本时，才能发送值。

```java
class Gossip...
	Map<NodeId, NodeState> getMissingAndStatesHigherThan(Map<NodeId, Integer> nodeMaxVersions) {
			Map<NodeId, NodeState> delta = new HashMap<>();
			delta.putAll(higherVersionNodeStates(nodeMaxVersions));
			delta.putAll(missingNodeStates(nodeMaxVersions));
			return delta;
	}
	
	private Map<NodeId, NodeState> missingNodeStates(Map<NodeId, Integer> nodeMaxVersions) {
			Map<NodeId, NodeState> delta = new HashMap<>();
			List<NodeId> missingKeys = clusterMetadata.keySet().stream().filter(key -> !nodeMaxVersions.containsKey(key)).collect(Collectors.toList());
			for (NodeId missingKey : missingKeys) {
					delta.put(missingKey, clusterMetadata.get(missingKey));
			}
			return delta;
	}
	
	private Map<NodeId, NodeState> higherVersionNodeStates(Map<NodeId, Integer> nodeMaxVersions) {
			Map<NodeId, NodeState> delta = new HashMap<>();
      Set<NodeId> keySet = nodeMaxVersions.keySet();
      for (NodeId node : keySet) {
      		Integer maxVersion = nodeMaxVersions.get(node);
      		NodeState nodeState = clusterMetadata.get(node);
      		if (nodeState == null) {
      				continue;
      		}
      		NodeState deltaState = nodeState.statesGreaterThan(maxVersion);
      		if (!deltaState.isEmpty()) {
      				delta.put(node, deltaState);
      		}
      }
      return delta;
	}
```

[cassandra](http://cassandra.apache.org/) 中的 Gossip 实现通过三次握手优化了状态交换，接收 gossip 消息的节点还会从发送者那里发送它（节点）需要的版本，以及它返回的元数据（where the node receiving the gossip message also sends the versions it needs from the sender, along with the metadata it returns）。发送者能立即响应请求的元数据。这能避免在其他情况下需要额外的消息。

gossip 协议使用在 [cockroachdb](https://www.cockroachlabs.com/docs/stable/) 中为每个已连接的节点维护状态。对于每个连接，它维护发送到该节点的最后一个版本，以及从该节点接收到的版本。这样它就可以发送“自上个版本发送的状态”并请求“自上个接收版本的状态”。

还可以使用其他一些有效的替代方案，如发送整个 Map 的哈希，如果哈希相同，则不执行任何操作。

## 节点选择 gossip 的条件

集群节点随机选择节点发送 Gossip 消息。可以用 java 中的 java.util.Random 来实现：

```java
class Gossip...
	private Random random = new Random();
	private InetAddressAndPort pickRandomNode(List<InetAddressAndPort> knownClusterNodes) {
			int randomNodeIndex = random.nextInt(knownClusterNodes.size());
			InetAddressAndPort gossipTo = knownClusterNodes.get(randomNodeIndex);
			return gossipTo;
	}
```

还可以考虑其他因素，比如最少接触的节点。例如 Gossip 协议在 [Cockroachdb](https://github.com/cockroachdb/cockroach/blob/master/docs/design.md) 就是采用的这种方式。

也有[网络拓扑感知(network-topology-aware)](https://dl.acm.org/doi/10.1109/TPDS.2006.85)的 gossip 目标选择方式存在。

这些都可以在 `pickRandomNode()` 方法中模块化地实现。

## 组成员和故障检测

在集群中维护这些可用的节点集合是 Gossip 协议中最通用的一种用法。里面用到以下两种方法。

- [swim-gossip\]](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) 用一个单独的探针组件，这个组件在集群中连续探测不同的节点，以检测它们是否可用。如果检测出来有节点存活或死亡，那么就通过 Gossip 通信将结果传播给整个集群。探测器随机选择一个节点发送 Gossip 消息。如果接收的节点检测到这是新的信息，它就会立即发送消息到这个随机选择的节点。在集群中以这种方式，就能很快的知道整个集群中失败或新加入的节点。
- 集群节点能周期性更新自己的状态来反馈心跳检查。然后这个状态就会通过 gossip 消息交换传播到整个集群。然后，每个集群节点可以检查是否在固定的时间内收到了特定集群节点的任何更新，或者将该节点标记为停机。在这种情况下，每个集群节点独立的决定一个节点是开启还是停止。

## 节点重启处理

如果节点宕机或重启了，版本值就不能正常工作了，在内存中的所有状态都会丢失。更重要的是，对于相同的 key，这个节点会存在不同的值。例如，集群节点用了不同的 IP 和端口启动，或是用了不同的配置启动。 [Generation Clock](Generation-Clock.md) 可以用来标记生成的每个值，以便当元数据状态发送给一个随机的集群节点，接收端节点不仅可以通过版本号检测到变化，还可以通过生成来检测。

值得注意的是这个机制对于核心 gossip 协议不是必须的。但是在实践中要实现它是为了确保状态更新能够被正确追踪。

## 例子

[cassandra](http://cassandra.apache.org/) 使用 Gossip 协议对集群节点进行组成员和故障检测。每个集群节点的元数据(例如分配给每个集群节点的令牌)也使用 Gossip协议进行传输。

[consul](https://www.consul.io/) 使用了 [swim-gossip](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) 协议对 consul 代理的组成员和故障检测。

[cockroachdb](https://www.cockroachlabs.com/docs/stable/) 使用 Gossip 协议去传播节点元数据

像 [Hyperledger Fabric](https://hyperledger-fabric.readthedocs.io/en/release-2.2/gossip.html) 这样的区块链对于组成员和发送分类账(ledger)元数据的实现也是使用 Gossip 协议。

## 原文链接

https://martinfowler.com/articles/patterns-of-distributed-systems/gossip-dissemination.html

