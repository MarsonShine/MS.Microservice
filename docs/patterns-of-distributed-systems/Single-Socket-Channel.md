# 单 Socket 通道(Single Socket Channel)

通过一个 Socket 通道连接，维护发送给服务器的请求顺序。

## 问题

当我们使用 [Leader And Followers](https://martinfowler.com/articles/patterns-of-distributed-systems/leader-follower.html)，对于消息丢失重试机制，我们需要确保消息在 leader 和 followers 的有序性。我们需要在保持低新连接成本的同时做到这一点，以至于打开一个新连接不会增加系统的延时。

## 解决方案

幸运的是，长时间并广泛使用的 [TCP](https://en.wikipedia.org/wiki/Transmission_Control_Protocol) 机制提供了所有这些必要的特征。我们需要确保 followers 和 leader 之间的所有通信都是通过一个 socket 通道来获取我们需要的通信。followers 然后会序列化从 leader 使用一个[单更新队列](https://martinfowler.com/articles/patterns-of-distributed-systems/singular-update-queue.html)的更新

![](../asserts/single-socket-channel.png)

节点一旦打开连接并连续读取新的请求就从不会关闭。每一个连接节点都会用一个专用线程来读写请求。如果使用的是[非阻塞的 io](https://en.wikipedia.org/wiki/Non-blocking_I/O_(Java))，都不需要每个连接一个线程。

一个简单基于线程的实现：

class SocketHandlerThread…

```java
  @Override
  public void run() {
      try {
          //Continues to read/write to the socket connection till it is closed.
          while (true) {
              handleRequest();
          }
      } catch (Exception e) {
          getLogger().debug(e);
      }
  }

   private void handleRequest() {
      RequestOrResponse request = readRequestFrom(clientSocket);
      RequestId requestId = RequestId.valueOf(request.getRequestId());
      requestConsumer.accept(new Message<>(request, requestId, clientSocket));
    }
```

节点读取请求并提交它们到一个[单更新队列](https://martinfowler.com/articles/patterns-of-distributed-systems/singular-update-queue.html)中来处理。一个节点处理这个请求，它会向 socket 回写一个响应。

每当节点建立通信时，它就会打开一个 socket 通道连接，用于连接与另一方的所有请求。

class SingleSocketChannel…

```java
  public class SingleSocketChannel implements Closeable {
      final InetAddressAndPort address;
      final int heartbeatIntervalMs;
      private Socket clientSocket;
      private final OutputStream socketOutputStream;
      private final InputStream inputStream;
  
      public SingleSocketChannel(InetAddressAndPort address, int heartbeatIntervalMs) throws IOException {
          this.address = address;
          this.heartbeatIntervalMs = heartbeatIntervalMs;
          clientSocket = new Socket();
          clientSocket.connect(new InetSocketAddress(address.getAddress(), address.getPort()), heartbeatIntervalMs);
          clientSocket.setSoTimeout(heartbeatIntervalMs * 10); //set socket read timeout to be more than heartbeat.
          socketOutputStream = clientSocket.getOutputStream();
          inputStream = clientSocket.getInputStream();
      }
  
      public synchronized RequestOrResponse blockingSend(RequestOrResponse request) throws IOException {
          writeRequest(request);
          byte[] responseBytes = readResponse();
          return deserialize(responseBytes);
      }
  
      private void writeRequest(RequestOrResponse request) throws IOException {
          var dataStream = new DataOutputStream(socketOutputStream);
          byte[] messageBytes = serialize(request);
          dataStream.writeInt(messageBytes.length);
          dataStream.write(messageBytes);
      }
```

在连接上设置超时是非常重要的，在出错的场景下，它不会无线等待。我们在 socket 通道上使用[心跳检查](https://martinfowler.com/articles/patterns-of-distributed-systems/heartbeat.html)定时发送请求保持存活。这个超时通常设置为心跳间隔的倍数，来允许网络往返的时间和可能出现的一些延迟。将连接超时保持为心跳间隔的10倍是合理的。

class SocketListener…

```java
  private void setReadTimeout(Socket clientSocket) throws SocketException {
      clientSocket.setSoTimeout(config.getHeartBeatIntervalMs() * 10);
  }
```

在 socket 通道上发送请求会产生一个[行头阻塞(head of line blocking)](https://en.wikipedia.org/wiki/Head-of-line_blocking)的问题。为了避免这个，我们可以使用[请求管道](https://martinfowler.com/articles/patterns-of-distributed-systems/request-pipeline.html)。

## 例子

- [Zookeeper](https://zookeeper.apache.org/doc/r3.4.13/zookeeperInternals.html) 使用单个 socket 通道以及每个 follower 一个线程与所有的通信。
- [Kafka](https://kafka.apache.org/protocol) 在 leader 和 followers 分区之间使用单个 socket 通道，备份信息。
- 参考 [Raft](https://raft.github.io/) 的一致性算法实现，[LogCabin](https://github.com/logcabin/logcabin) 使用单个 Socket 通道在 leader 和 followers 之间通信。

原文连接：https://martinfowler.com/articles/patterns-of-distributed-systems/single-socket-channel.html