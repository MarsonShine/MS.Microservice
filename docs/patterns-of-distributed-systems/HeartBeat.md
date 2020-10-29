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

## 技术问题

## 大集群 - Gossip 基础协议

## 例子



原文：https://martinfowler.com/articles/patterns-of-distributed-systems/heartbeat.html