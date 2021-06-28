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
> // TODO

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

当调度任务被调用时，它会从元数据映射的服务器列表中随机选取一小组节点。一个小的常量定义为 gossip fanout，它决定了有多少个节点可以选定为 gossip 目标。如果什么都还不知道，它就随机选择一个种子节点，并将它的元数据映射发送到该节点。

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

```go
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

