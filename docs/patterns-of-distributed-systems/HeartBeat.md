# 心跳检查

通过周期性的发送消息给所有服务器来表示这个服务器是否可用。

## 问题

当多个服务器构成一个集群时，这些服务器负责根据各个所使用的分区以及备份方案来存储数据。及时探测服务器故障对于确保发生的行为是正确的是非常重要的，这是通过其他其他服务器来处理失败服务器上的请求的。

## 解决方案

![](../asserts/Heartbeat.png)

定期的发送一个请求到其它所有服务器来表明服务器的活动状态。选择请求间隔，使其大于服务器之间的网络往返时间。如果在所有的服务器都在等待超时间隔，该间隔是用于检查心跳的请求间隔的倍数。一般来说，

超时间隔 > 请求间隔 > 网络在服务器之间的往返时间间隔。

例如，服务器之间的往返时间间隔为 20ms，心跳检查能以每 100ms 发送，服务器在 1 秒后检查，以给予足够的时间发送多个心跳检查，而不是得到错误的否定。如果在这段时间内没有接收到心跳检查，那就是等于申明要发送的这个服务器失败。

发送心跳的服务器和接收心跳的服务器都有如下定义的调度程序。给调度程序一个方法，以固定的时间间隔执行。当开始时，被调度的任务就会执行给定的方法。

```java
// class HeartBeatScheduler
public class HeartBeatScheduler implements Logging {
	private ScheduledThreadPoolExecutor executor = new ScheduledThreadPoolExecutor(1);
	private Runnable action;
	private long heartBeatInterval;
	public HeartBeatScheduler(Runnable action, long heartBeatIntervalMs) {
		this.action = action;
		this.heartBeatInterval = heartBeatIntervalMs;
	}
	private ScheduledFuture<?> scheduledTask;
	public void start() {
		scheduledTask = executor.scheduleWithFixedDelay(new HeartBeatTask(action), heartBeatInterval, heartBeatInterval, TimeUnit.MILLISECONDS);
	}
}
```

在正在发送消息的服务端，调度器执行一个方法来发送心跳检查。

```java
// class SendingServer
private void sendHeartbeat() throws IOException {
  	socketChannel.blockingSend(newHeartbeatRequest(serverId));
}
```

在接收的服务端，失败探测机制启动一个类似的调度器。定期会检查这个心跳是否正常接收。

```java
class AbstractFailureDetector {
    private HeartBeatScheduler heartbeatScheduler = new HeartBeatScheduler(heartBeatCheck, 100l);
    abstract void heartBeatCheck();
    abstract void heartBeatReceived(T serverId);
}
```

失败探测器必须要有两个方法

- 一个是当接收服务器接收心跳的时候调用，来告诉失败探测器心跳请求已接收。

  ```java
  // class ReceivingServer
  private void HandleRequest(Message<RequestOrResponse> request) {
      RequestOrResponse clientRequest = request.getRequest();
      if (isHeartbeatRequest(clientRequest)) {
        HeartbeatRequest heartbeatRequest = JsonSerDes.deserialize(clientRequest.getMessageBodyJson(), HeartbeatRequest.class);
        failureDetector.heartBeatReceived(heartbeatRequest.getServerId());
        sendResponse(request);
      } else {
        //processes other requests
      }
  }
  ```

- 一个是定期检查心跳状态和探测失败的可能。

何时标记一个服务器是否失败依赖于各种标准。各有不同的，一般来说心跳间隔越小，故障检测到的速度就越快，但是却提高了失败探测的几率。因此心跳间隔和缺失心跳的解释是根据集群的要求实现的。一般是分两大类

## 小集群 - 如像 RAFT，Zookeeper 基于一致性的系统

在所有的一致性实现中，心跳检查是从主服务器（leader）向从服务器发送请求。每接收一次心跳检查就会记录请求到达的时间戳：

```java
// class TimeoutBasedFailureDetector ...
	@Override
	void heartBeatReceived(T serverId) {
		Long currentTime = System.nanoTime();
         heartbeatReceivedTimes.put(serverId, currentTime);
         markUp(serverId);
	}
```

如果 leader 服务器在固定的时间没有发起心跳检查，那么就会认为该服务器已经崩溃并重新选举一个新的 leader 服务器。失败探测的原因可能是网络和缓慢的进程。所以[时钟生成器](https://martinfowler.com/articles/patterns-of-distributed-systems/generation.html)是用在探测稳定的服务器。这给系统提供了更好的可用性，在很短的时间内就能探测到服务器崩溃。这对小集群来说是很舒服的，特别是设置了 3-5 个节点的，例如 Zookeeper  或 Raft，这个实现方式大部分都是一致的。

```java
class TimeoutBasedFailureDetector… 
    @Override
    void heartBeatCheck() {
      Long now = System.nanoTime();
      Set<T> serverIds = heartbeatReceivedTimes.keySet();
      for (T serverId : serverIds) {
          Long lastHeartbeatReceivedTime = heartbeatReceivedTimes.get(serverId);
          Long timeSinceLastHeartbeat = now - lastHeartbeatReceivedTime;
          if (timeSinceLastHeartbeat >= timeoutNanos) {
              markDown(serverId);
          }
      }
  }
```

## 技术考量

当[单个 Socket 信道(Single Socket Channel)](https://martinfowler.com/articles/patterns-of-distributed-systems/single-socket-channel.html) 用在服务器之间通信时，必须要小心确保[[行头阻塞(head-of-line-blocking)](https://en.wikipedia.org/wiki/Head-of-line_blocking)]不会阻止 heartbeat 消息被处理。否则，它会导致足够长的延迟，从而错误地检测到发送服务器的消息已经关闭，甚至是在平常间隔内发送的心跳检查。[请求管道](https://martinfowler.com/articles/patterns-of-distributed-systems/request-pipeline.html)可以用来确保在发送的心跳检查之前，服务不会等待上一个请求的响应。有时当使用[单个更新队列(Single Update Queue)](https://martinfowler.com/articles/patterns-of-distributed-systems/singular-update-queue.html)时，有些任务像磁盘写入，可能会造成延迟，这可能会导致延迟处理定时中断和延迟发送心跳。

这个可能用一个单独的线程来做异步心跳检查会解决这个问题。一些框架如 [[consul]](https://www.consul.io/) 和 [[akka]](https://akka.io/) 异步发送心跳检查。这也可能是接收服务器上的问题。接收服务器正在做一个磁盘写入操作，在它写完之后然后发送心跳检查，这会导致错误的故障探测。因此，使用单一更新队列的接收服务器可以重新设置心跳检查机制，以纳入这些延迟。[[raft]](https://raft.github.io/) 和 [[log-cabin]](https://github.com/logcabin/logcabin) 引用的实现就是这样的。

有时，一些特定运行时的事件(如垃圾收集)会导致的[本地暂停]会延迟心跳的处理。这里要有一个机制来检查是进程是否发生在本地暂停之后。一个简单的机制就是检查进程是发生在一个足够时间之后如 5 秒。在这种情况下，在时间窗口没有任何东西被标记失败，并被延迟到下一个周期。[Cassandra](https://issues.apache.org/jira/browse/CASSANDRA-9183) 的实现就是一个很好的例子。

## 大集群 - Gossip 基础协议

上面一节描述了心跳检查在大集群下，如有数百上千的服务器横跨广域网下的伸缩性是不好的。在大集群下，需要有两个因素要考虑：

- 在每个服务器生成大量消息的固定限制。
- 心跳检查消耗的总宽带。它不应该占用太多的网络宽带。它应该在数千字节的上限，要确保不会因太多的心跳检查消息而影响实际的横跨服务器的数据传输。

因此要避免所有服务器之间的心跳检查。在这些情况下，通常会使用故障探测器以及跨集群的传播故障信息的 Gossip 协议。这些集群通常采取诸如在出现故障时跨节点移动数据之类的操作，因此倾向于正确的检测并容忍更多的延迟（有边界）。主要的挑战是不能因为网络延迟或进程缓慢而将节点错误地检测为失败。共同的一个机制是为每个进程分配一个疑问数(suspicion number)，在这个限定的时间内的进程如果没有 gossip，那么这个疑问数就会增长。它是基于上次的统计来计算的，并且只有在这个疑问数达到了配置好的上限值之后，才会标记为失败。

这里有两种实现机制：

​	1）Phi 累积故障检测器（Akka，Cassandra）

​	2）SWIM 警卫提升（Hashicorp Consul，memberlist）这些实现可在具有数千台计算机的广域网上扩展。akka 是已经知道在 [2400](https://www.lightbend.com/blog/running-a-2400-akka-nodes-cluster-on-google-compute-engine) 个机器上尝试了，Hashicorp Consul 通常在一个组中部署数千个 Consul 服务器。拥有可靠的失败探测器，在大集群下在相同的时间提供相同的一致性保障工作更高效，这仍然需要积极的发展。最近的框架中，像 [Rapid](https://www.usenix.org/conference/atc18/presentation/suresh) 看起来就是如此。

## 例子

- Consensus 实现像 ZAB 或 RAFT，它在 3-5 个节点中能很好的工作，基于固定时间窗口的故障检测。
- Akka Actors 以及 Cassandra 使用了 [Phi 累积故障检测器](http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.80.7427&rep=rep1&type=pdf)
- Hashicorp consul 使用了基于 [SWIM](https://www.cs.cornell.edu/projects/Quicksilver/public_pdfs/SWIM.pdf) 的 gossip

原文：https://martinfowler.com/articles/patterns-of-distributed-systems/heartbeat.html