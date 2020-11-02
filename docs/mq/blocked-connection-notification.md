# 连接阻塞通知

有时，当代理的资源（内存或磁盘）不足导致连接被阻塞时，客户机希望收到通知。

我们已经引入了一个 AMQP 0-9-1 协议拓展，这个 broker 会发送给客户端的 `connection.blocked` 方法时，连接就会发生阻塞。当为`connection.unblocked` 就是还没有阻塞。

为了接收到这些通知，客户端必须在 `client-properties` 中提供一个 `capabilities` 表，这里面还有一个 `connection-blocked` 的key ，是个值为 `true` 的布尔类型。

查看 [capabilities](https://www.rabbitmq.com/connection-blocked.html#capabilities) 查看详情。我们默认支持客户端表示的这些能力，以及提供一个方式来注册 `connection.blocked` 和 `connection.unblocked` 处理程序。

## 何时发送通知

RabbitMQ 第一个发布一个 `connection.blocked` 通知时，在资源上非常少。例如，当 RabbitMQ 节点检测到 RAM 不足时，它会发送 `connection.blocked` 给所有支持这个特性的已经连接发布的客户端。如果在连接在是非阻塞（unblicked）之前，节点的磁盘空间也开始不足，那么其他的 `connection.blocked` 也不会发送。

一个 `connection.unblocked` 被发送之时，就是清除了所有的资源警报以及这个连接完全解除阻塞。

## Java 客户端使用连接阻塞通知

阻塞连接通知是通过实现接口 `BlockedListener` 来处理的。它们能在 channel 上 使用 `Connection.addBlockedListener` 方法注册

```java
ConnectionFactory factory = new ConnectionFactory();
Connection connection = factory.newConnection();
connection.addBlockedListener(new BlockedListener() {
    public void handleBlocked(String reason) throws IOException {
        // Connection is now blocked
    }

    public void handleUnblocked() throws IOException {
        // Connection is now unblocked
    }
});
```

## .NET 客户端使用连接阻塞通知

这个是通过 `RabbitMQ.Client.Events.ConnectionBlockedEventHandler` 的委托来实现的。`IConnection` 提供一个 `IConnection.ConnectionBlocked` 和 `IConnection.ConnectionUnblocked` 事件。

```csharp
public void HandleBlocked(object sender, ConnectionBlockedEventArgs args)
{
    // Connection is now blocked
}

public void HandleUnblocked(object sender, EventArgs args)
{
    // Connection is now unblocked
}

Conn.ConnectionBlocked   += HandleBlocked;
Conn.ConnectionUnblocked += HandleUnblocked;
```