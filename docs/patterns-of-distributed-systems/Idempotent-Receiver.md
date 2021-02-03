# 幂等接收（Idempotent Receiver）

给从客户端发起的请求标识唯一，这样就能在客户端重复发送请求的时候忽略重复的请求了。

## 问题

客户端向服务端发送请求的时候，可能会存在服务器无法响应的情况。显然客户端是无法知道服务端响应丢失或是服务端在处理消息的时候奔溃。为了确保这个请求能成功处理，客户端必须要重新发送请求。

如果服务端已经成功处理请求并且在奔溃过后，服务端会再次接受到来自客户端发送的重复请求。

## 解决方案

可以通过分配一个唯一标识 id 给每个客户端。这样在发送消息之前，客户端会向服务器自我注册。

```java
// class ConsistentCoreClient...
private void registerWithLeader() {
		RequestOrResponse request = new RequestOrResponse(RequestId.RegisterClientRequest.getId(), correlationId.incrementAndGet());
		// blockingSend 函数如果在网络异常的时候，将会尝试重新创建新的连接
    RequestOrResponse response = blockingSend(request);
		RegisterClientResponse registerClientResponse = JsonSerDes.deserialize(response.getMessageBodyJson(), RegisterClientResponse.class);
		this.clientId = registerClientResponse.getClientId();
}
```

当服务端接受到来自客户端的注册请求时，它会分配一个唯一 id 给客户端。如果服务端是[一致性核心](Consisten-Core.md)的，它可以将[预写日志](Write-Ahead-Log.md)索引做为客户端标识分配给客户端。

```java
// class ReplicatedKVStore…
private Map<Long, Session> clientSessions = new ConcurrentHashMap<>();
private RegisterClientResponse registerClient(WALEntry walEntry) {
		Long clientId = walEntry.getEntryId();
		//clientId 存储客户端的响应
		clientSessions.put(clientId, new Session(clock.nanoTime()));
		return new RegisterClientResponse(clientId);
}
```

服务器会创建一个会话来存储注册客户端的请求响应。它还跟踪创建会话的时间，以便可以丢弃不活跃的会话，这将在后面的小节中解释。

```java
class Session {
    long lastAccessTimestamp;
    Queue<Response> clientResponses = new ArrayDeque<>();

    public Session(long lastAccessTimestamp) {
        this.lastAccessTimestamp = lastAccessTimestamp;
    }

    public long getLastAccessTimestamp() {
        return lastAccessTimestamp;
    }

    public Optional<Response> getResponse(int requestNumber) {
        return clientResponses.stream().
                filter(r -> requestNumber == r.getRequestNumber()).findFirst();

    }

    private static final int MAX_SAVED_RESPONSES = 5;

    public void addResponse(Response response) {
        if (clientResponses.size() == MAX_SAVED_RESPONSES) {
            clientResponses.remove(); //移除过老的请求
        }
        clientResponses.add(response);
    }

    public void refresh(long nanoTime) {
        this.lastAccessTimestamp = nanoTime;
    }
}
```

对于一致的核心，客户端注册请求也被复制为共识算法的一部分。所以在 leader 失败的情况下，客户端注册也要能可用的。然后，服务器还存储发送给客户机的后续请求的响应。

> 共识（Consensus）与一致性（Consistency）是有区别的。
>
> 一致性：数据不同副本之间的差异
>
> 共识：指达成一执行的方法与过程
>
> 一些翻译资料中把 Consensus 翻译成一致性，其实是混淆了两个概念，分布式一执行算法实际是 “Distributed Consensus Algorithm”

> **幂等与非幂等请求**
>
> 值得注意的是，有些请求本身就是幂等的。例如，在 key/value 存储服务中设置一个 key 和 一个 value，这本身就是幂等的。甚至是相同的 key 和 value 被设置了多次都不会有问题。
>
> 另一方面，如创建一个[租约](Lease.md)就不是幂等的。如果一个租约已经存在了，一个重复创建租约的请求过来就会发生失败。这就有问题了。请思考下面场景：一个客户端发送一个创建租约的请求；服务器也成功响应，但是随即奔溃了，或者是在请求相应返回给客户端之前连接失败。客户端就会重新创建连接，并重新发送创建租约请求；因为这个租约已经存在了，所以它会返回错误。所以客户端就会认为它还没有这个租约。这显然不是我们预期的行为。
>
> 通过幂等接收处理，客户端将会使用一个相同的编号来发起创建租约的请求。因为服务器早已经把成功处理的请求的响应在服务器上保存起来了，所以会返回给客户端相同的响应。

对于服务器来说每个非幂等的请求（详见侧栏，这里因样式问题直接列在下面），它在成功执行后将响应存储在客户端会话中。

```java
//class ReplicatedKVStore…
private Response applyRegisterLeaseCommand(WALEntry walEntry, RegisterLeaseCommand command) {
		logger.info("Creating lease with id " + command.getName()
              + "with timeout " + command.getTimeout()
              + " on server " + getServer().getServerId());
      try {
          leaseTracker.addLease(command.getName(),
                  command.getTimeout());
          Response success = Response.success(walEntry.getEntryId());
          if (command.hasClientId()) {
              Session session = clientSessions.get(command.getClientId());
              session.addResponse(success.withRequestNumber(command.getRequestNumber()));
          }
          return success;

      } catch (DuplicateLeaseException e) {
          return Response.error(1, e.getMessage(), walEntry.getEntryId());
      }
}
```

客户端将客户端标识符和发送到服务器的每个请求一起发送。客户端会保留一个计数器来为发送到服务器的每个请求分配请求编号。

```java
// class ConsistentCoreClient…
int nextRequestNumber = 1;

public void registerLease(String name, long ttl) {
      RegisterLeaseRequest registerLeaseRequest
              = new RegisterLeaseRequest(clientId, nextRequestNumber, name, ttl);

      nextRequestNumber++; // 下一个请求递增请求编号

      var serializedRequest = serialize(registerLeaseRequest);

      logger.info("Sending RegisterLeaseRequest for " + name);
      blockingSendWithRetries(serializedRequest);

  }

  private static final int MAX_RETRIES = 3;

  private RequestOrResponse blockingSendWithRetries(RequestOrResponse request) {
      for (int i = 0; i <= MAX_RETRIES; i++) {
          try {
              //blockingSend will attempt to create a new connection is there is no connection.
              return blockingSend(request);

          } catch (NetworkException e) {
              resetConnectionToLeader();
              logger.error("Failed sending request  " + request + ". Try " + i, e);
          }
      }

      throw new NetworkException("Timed out after " + MAX_RETRIES + " retries");
  }
```

## 保存的客户端请求过期

客户端的每个请求不可能永远都会保存起来。这里有多种方式可以让请求过期。在 Raft 的[参考实现](https://github.com/logcabin/logcabin)中，客户端保留一个单独的编号来记录成功接收响应的请求编号。这个编号随后与每个请求一起发送到服务器。服务器能通过这个编号，能将小鱼这个编号的请求全部丢弃。

如果客户端保证只在收到前一个请求的响应后才发送下一个请求，那么服务器就能安全的删除前面所有请求，一旦这个请求成功处理之后。这里有一个问题就是当使用[请求管道](Request-Pipeline.md)的时候，因为可能有多个正在运行的请求，客户端可能没有收到响应。如果服务器知道客户端可以容纳正在运行的请求的最大数量，那么它只能存储这些响应，就会删除所有其他响应。例如 [kafka](https://kafka.apache.org/) 就知道生产者能容纳最大的正在运行的请求数，所以它最多存储 5 个之前的响应。

```java
// class Session…
private static final int MAX_SAVED_RESPONSES = 5;

public void addResponse(Response response) {
    if (clientResponses.size() == MAX_SAVED_RESPONSES) {
        clientResponses.remove(); //remove the oldest request
    }
    clientResponses.add(response);
}
```

## 移除已注册请求

客户端会话状态不会一直在服务器上保持。服务器对它保存的客户端会话有一个最大的生存期。客户端会定义的发送[心跳](HeartBeat.md)。如果客户端在设置的生存期内没有响应心跳，这个客户端的会话就会移除。

所以服务器会定义的开启一个任务来检查过期的客户端会话，并将它移除。

```java
// class ReplicatedKVStore…
private long heartBeatIntervalMs = TimeUnit.SECONDS.toMillis(10);
private long sessionTimeoutNanos = TimeUnit.MINUTES.toNanos(5);

private void startSessionCheckerTask() {
    scheduledTask = executor.scheduleWithFixedDelay(()->{
        removeExpiredSession();
    }, heartBeatIntervalMs, heartBeatIntervalMs, TimeUnit.MILLISECONDS);
}
private void removeExpiredSession() {
    long now = System.nanoTime();
    for (Long clientId : clientSessions.keySet()) {
        Session session = clientSessions.get(clientId);
        long elapsedNanosSinceLastAccess = now - session.getLastAccessTimestamp();
        if (elapsedNanosSinceLastAccess > sessionTimeoutNanos) {
            clientSessions.remove(clientId);
        }
    }
}
```

## 例子

-  [Raft]([Raft](https://raft.github.io/) has reference implementation to have idempotency for providing linearizable actions.) 有参考实现具有等幂，以提供可线性化的操作。
- [Kafka](https://kafka.apache.org/) 允许幂等生产者允许客户端重试请求和忽略重复的请求。
- [Zookeeper](https://zookeeper.apache.org/) 有会话以及 zxid 的概念，它能让客户端进行恢复。Hbase 有一个 [[Hbase -recoverable-zookeeper]](https://docs.cloudera.com/HDPDocuments/HDP2/HDP-2.4.0/bk_hbase_java_api/org/apache/hadoop/hbase/zookeeper/RecoverableZooKeeper.html) 包装器，它遵循[[zookeeper-error-handling]](https://cwiki.apache.org/confluence/display/ZOOKEEPER/ErrorHandling) 的指导方针实现幂等动作。

> **最多一次**，**最少一次**以及**明确一次操作**
>
> 根据客户端具体如何与服务器交互，服务器是否会执行某些操作的保证是预先确定的。如果客户端在发送请求之后、接收响应之前遇到故障，那么可能有三种情况。
>
> 如果客户端在失败的情况下没有重试请求，则服务端可能已经处理了请求，或者可能在处理请求之前失败了。因此请求在服务器上最多处理一次。
>
> 如果客户端重试请求，并且服务器在通信失败之前已经成功处理了请求，那么客户端可能会再次处理请求。**因此请求至少被处理一次，但可以在服务器上处理多次。**
>
> 使用幂等接收，即使有多个客户端重试，服务器也只处理一次请求。所以为了实现“一次”的操作，拥有幂等的接收者是很重要的。