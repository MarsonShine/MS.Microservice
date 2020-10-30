# 消费确认和发布确认（ACK）

这篇文章主要阐述下面几个观点（注意以下 ACK 都指 Acknowledgement ）

- 为什么需要 ACK 的存在
- 手动与自动确认的模式
- ACK API，包括“多ACK”以及重入队列
- 在连接丢失或 channel 关闭之上自动重入队列
- Channel 预先加载与高吞吐的影响
- 常见的客户端错误
- 发布确认和与发布者发布数据安全相关的话题

还有很多，总之在应用程序中的使用消息， ACK 是对消费者和发布者两者的数据安全都非常重要的。

## 基础

像 RabbitMQ 这样使用一个消息 broker 是被定义为分布式系统。因为这个协议方法不能保证每个消息请求都能成功处理，所以消费者和发布者两个都需要一个派送和处理确认的机制。RabbitMQ 在很多消息协议上已经提供支持这些特性了。

从客户机到 RabbitMQ 的派送处理确认在消息协议里被称为确认。对发布者的代理确认（broker ack）是一个协议拓展，被称为发布者确认（publisher confirms）。它们的特征都是受到了 TCP 的启发。

对于从发布者到 RabbitMQ 节点的可靠交付以及从 RabbitMQ 到消费者的可靠交付都是非常必要的。换句话说，它们对数据安全至关重要，而应用程序对数据安全的责任与 RabbitMQ 节点的责任是一样的。

## （消费者）交付确认

当 RabbitMQ 递送一个消息给消费者时，它需要知道这个消息何时发送成功了。什么样的逻辑最优这是取决于系统。这主要是由应用程序决定。在 AMPQ 0-9-1 中就是由一个消费者通过使用 `basic.consume` 方法或使用 `basic.get` 方法按需提取一个消息时生成的。

## 递送标识符：递送的标签

在我们着手讨论其他话题之前，去解释是如何识别递送的（确认表示它们各自的推送）。当消费者（订阅者）注册时，消息就会被 RabbitMQ 通过方法 `basic.deliver` 推送过去。这个方法就会被标记上推送标签（deliver tag），它在 channel 上表示唯一的标识。因此推送标识作用在每个 channel 内。

推送标签是单调增长的正整数，并由客户端库表示。客户端库方法会接受一个推送标签作为参数确认推送。

由于推送标签只作用在每个 channel 内，推送必须在接收它们的同一通道上得到确认。在不同的 channel 上确认会得到“未知的推送标签”协议异常并会关闭 channel。

## 消费者确认模式和数据安全考虑

当一个节点推送给消费者，它必须决定是否将消息交给使用者处理(或至少是接收)。因为有很多因素（客户端连接，消费者应用程序等）都能导致失败，这个决定是出于数据安全的考虑。消息协议一般会提供一个确认机制来允许消费者确认向其连接的节点推送。是否使用该机制是在用户订阅时决定的。

根据使用的确认模式，RabbitMQ 认为在消息发出之后就是消息成功推送（写到 Socket TCP 中）或者是当显式（手动）接收到一个客户端确认时成功推送。手动发送确认可以是正也可以是负，使用以下协议中的一个：

- `basic.ack` 是用来肯定确认
- `basic.nack` 失败确认（注意：[RabbitMQ 将其拓展到 AMQP 0-9-1](https://www.rabbitmq.com/nack.html)）
- `basic.reject` 失败确认，但与 `basic.nack` 相比有一个限制

这些方法都暴露在下面要讨论的客户端库 API 中。

肯定确认只是指示 RabbitMQ 在发送时记录一条消息，这个消息并可以丢弃。使用 `basic.reject` 失败确认有相同的效果。主要不同的地方是：肯定确认假使一个消息被成功的处理了，而失败确认对应的是这个推送还没被处理，但是可以删除。

在自动确认模式里面，在发送消息之后就会被认为这个消息是被成功推送的。这个模式牺牲了高吞吐（只要消费者能跟上），以降低推送和消费者处理的安全性。

> 这个模式牺牲了高吞吐（只要消费者能跟上），以降低推送和消费者处理的安全性：这句话的意思是自动确认机制消息已推送到 RabbitMQ 就认为是成功的，但是如果消费者发生异常，那么这条消息就无法再次被重新消费，而并不是每次发送成功就真的被消费者处理了，所以这还不是最高的吞吐量）但是要比手动确认模式要高效

这种模式被称为 “一次性” 模式。不像手动模式那样，如果消息在推送成功之后，消费者的 TCP 连接或是 channel 被关闭了，这条消息就会丢失。因此消息自动确认是不安全的，不适于所有的工作负载。

在使用消息自动确认模式还有一个重要的事要考虑，那就是消费者超载的问题。手动确认模式通常与有界通道预取（prefetch）一起使用，它限制了通道上未完成（进行中）的数量。然而，自动确认在定义上就没有这种限制。因此消费者能被推送的速度压垮， 可能会因为队列中消息越来越多，会挤压内存，耗尽堆或者被操作系统中止进程。一些客户机库将应用 TCP回压（TCP back pressure）(直到在未处理的交付的积压量超过某个限制，那么就会停止会从 Sockt 读取数据)。因此自动确认模型只推荐用在高效平稳处理的消费者上。

## 推送成功确认

那些用来推送确认的方法 API 作为在客户端库中的 channel 上公开出来的操作。java 客户端用户会使用 `Channel#basicAck` 和 `Channel#basicNack` 来执行 `basic.ack` 和 `basic.nack`。下面是一个关于成功确认的 java 客户端的例子

```java
boolean autuAck = false;
channel.basicConsume(queueName, autoAck, "a-consumer-tag", new DefaultConsumer(channel) {
	@Override
	public void handleDelivery(String consumerTag, Envelope envelope, AMQP.BasicProperties properties, byte[] body)
		throw IOException 
	{
		long deliveryTag = envelope.getDeliveryTag();
		// 推送消息成功，消息将会被丢弃
         channel.basicAck(deliveryTag, false);
	}
});
```

在 .NET 客户端的方法就是 `IModel#BasicAck` 和 `IModel#BasicNack`。

```c#
var consumer = new EventingBasicConsumer(channel);
consumer += (ch, ea) => {
	var body = ea.Body.ToArray();
	// 推送消息成功，消息将会被丢弃
	channel.BasicAck(ea.DeliveryTag, false);
};
string consumerTag = channel.BasicConsume(queueName, false, consumer);
```

## 一次确认多个消息

可以批量处理手动确认以减少网络流量。这是通过设置 ack 的方法（下面可以看到这个方法）字段 `multipe` 为 `true`。注意 `basic.reject` 由于历史原因没有这个字段，所以 RabbitMQ 才将 `basic.nack` 作为一个协议拓展引入进来。

当设置 `multipe` 字段为 `true` 时，RabbitMQ 会将所有未完成的推送确认标记，包括那些已经在确认中的指定标记。在每个 channel 范围内，其它所有相关的都会确认。例如，在通道 `ch` 中，这里有一些未确认的消息被标记了 5，6，7，8 。当一个确认栈到达的时候，channel 会用 `delivery_tag` 复制为 8，并且 `multipe` 设置为 `true`。从 5-8 所有的标记都会被确认。如果 `multipe` 设置为 `false`，那么推送的消息 5，6，7 都不会被确认。

下面是一个用 RabbitMQ java 客户端确认多个推送的例子，传递参数 `true` 给 `Channel#basicAck`

```java
boolean autoAck = false;
channel.basicConsume(queueName, autoAck, "a-consumer-tag", 
	new DefaultConsumer(channel) {
		@Override
		public void handleDelivery(String consumerTag, Envelope envelope, AMQP.BasicProperties properties, byte[] body)
			throws IOException
		{
			long deliveryTag = envelope.getDeliveryTag();
            //成功确认所有推送消息设置成 deliveryTag 标签
            channel.basicAck(deliveryTag, true);
		}
	});
```

.NET 客户端：

```c#
var consumer = new EventingBasicConsumer(channel);
consumer.Received += (ch, ea) =>
{
    var body = ea.Body.ToArray();
    //成功确认所有推送消息设置成 deliveryTag 标签
    channel.BasicAck(ea.DeliveryTag, true);
};
String consumerTag = channel.BasicConsume(queueName, false, consumer);
```

## 错误确认和推送重试

有些时候消费者无法立即处理一个推送，但是其它节点实例是可以的。在这种情况下，它期望会入队列并让其它消费者实例来接收它并处理。`basic.reject` 和 `basic.nack` 是用来实现它的两个协议方法。

这些方法通常用于推送的否定确认。想这种的推送会被 broker 丢弃或者是重新安排入队列。这个行为是由 `requeue` 字段控制的。当设置为 `true`时，broker 会将其重新推送（或者多推送情况，下面会简短的解释）通过指定特殊的推送标签（delivery tag）。

这些方法都是在客户端库 channel 上以操作公开的。Java 客户端是使用 `Channel#basicReject` 和 `Channel#basicNack` 来执行 `basic.reject` 和 `basic.nack`。

```java
boolean autoAck = false;
channel.basicConsume(queueName, autoAck, "a-consumer-tag",
     new DefaultConsumer(channel) {
         @Override
         public void handleDelivery(String consumerTag,
                                    Envelope envelope,
                                    AMQP.BasicProperties properties,
                                    byte[] body)
             throws IOException
         {
             long deliveryTag = envelope.getDeliveryTag();
             // negatively acknowledge, the message will
             // be discarded
             channel.basicReject(deliveryTag, false);
         }
     });
```

```java
boolean autoAck = false;
channel.basicConsume(queueName, autoAck, "a-consumer-tag",
     new DefaultConsumer(channel) {
         @Override
         public void handleDelivery(String consumerTag,
                                    Envelope envelope,
                                    AMQP.BasicProperties properties,
                                    byte[] body)
             throws IOException
         {
             long deliveryTag = envelope.getDeliveryTag();
             // requeue the delivery
             channel.basicReject(deliveryTag, true);
         }
     });
```

.NET 是用 `IModel#BasicReject` 和 `IModel#BasicNack`

```c#
var consumer = new EventingBasicConsumer(channel);
consumer.Received += (ch, ea) =>
                {
                    var body = ea.Body.ToArray();
                    // negatively acknowledge, the message will
                    // be discarded
                    channel.BasicReject(ea.DeliveryTag, false);
                };
String consumerTag = channel.BasicConsume(queueName, false, consumer);
```

```csharp
var consumer = new EventingBasicConsumer(channel);
consumer.Received += (ch, ea) =>
                {
                    var body = ea.Body.ToArray();
                    // requeue the delivery
                    channel.BasicReject(ea.DeliveryTag, true);
                };
String consumerTag = channel.BasicConsume(queueName, false, consumer);
```

根据在其队列中的位置以及活动的消费者使用的预载值，重新入队列的消息已经准备马上推送。这就是说，所有的消费者重新入队列，由于这个临时的条件导致它们都无法处理这个推送，它们将创建一个重入队列/重试推送的循环。这些循环会消耗网络宽带和 CPU 资源。消费者实现了能够跟踪重试推送的数量，并拒绝（或丢弃）或者稍后调度重新入队列。

使用 `basic.nack` 方法可以一次拒绝或重试多个消息。这就是它与 `basic.reject` 的不同之处。它接收一个额外的 `multiple` 参数。下面是 Java 例子

```java
boolean autoAck = false;
channel.basicConsume(queueName, autoAck, "a-consumer-tag",
     new DefaultConsumer(channel) {
         @Override
         public void handleDelivery(String consumerTag,
                                    Envelope envelope,
                                    AMQP.BasicProperties properties,
                                    byte[] body)
             throws IOException
         {
             long deliveryTag = envelope.getDeliveryTag();
             // requeue all unacknowledged deliveries up to
             // this delivery tag
             channel.basicNack(deliveryTag, true, true);
         }
     });
```

.NET 例子

```c#
// this example assumes an existing channel (IModel) instance

var consumer = new EventingBasicConsumer(channel);
consumer.Received += (ch, ea) =>
                {
                    var body = ea.Body.ToArray();
                    // requeue all unacknowledged deliveries up to
                    // this delivery tag
                    channel.BasicNack(ea.DeliveryTag, true, true);
                };
String consumerTag = channel.BasicConsume(queueName, false, consumer);
```

## Channel 预载设置（Qos）

由于消息发送到客户端是异步的，因此经常会有超过一个消息是在任何时刻的 channel 上是处于“飞行中”。另外，一般情况下客户端手动确认也是异步的。所有一个滑动窗口的推送标签是未确认的。开发者经常优先限制这个窗口的大小，以避免使用者端的无边界缓冲区问题。这是通过 `basic.qos` 方法设置 `prefetch count` 值就是这样做的。这个值定义了在 channel 上未确定的推送数量最大值。一旦这个值到达了设置的这个阈值，RabbitMQ 就会停止推送消息，知道 channel 上有消息被确认了。

例如，在通道 `Ch` 给定一些推送消息 5，6，7 和 8，它们都是未确认的。并且 `Ch` 预载值设置为 4，这个时候 RabbitMQ 就会停止推送任何消息给 `Ch`，直到 `Ch` 通道上至少有一个被确认了。当确认栈帧到达，并设置 5（6，7 或 8）的时候，RabbitMQ 就会接收通知并继续推送消息给 `Ch`。一次确认多消息会至少让一条消息确认推送。

再次重申，推送以及手动客户端确认整个都是异步的。因此如果预载值发生改变的同时已经有很多推送在途中了，这里自然就会触发一个竞态条件，并且在通道上可能临时存在超过预载值的未确认的消息。

### pre-channel，Per-consumer  以及 Global Prefetch

Qos 设置能在指定的 channel 或消费者上配置。[消费者预载](https://www.rabbitmq.com/consumer-prefetch.html)指引就解释了这个影响的范围。

### Prefetch 和 消费者轮询

Qos 预载设置在使用 `basic.get`（即 pull API）获得的消息上没有影响，在手动确认模式也是如此。

## 消费者确认模式，Prefetch 和 吞吐量

消息确认模式和 Qos 预载值对消费者的吞吐量有直接的影响。通常，提高预载值会提高消费者消息推送的效率。推送消息在自动确认模式下收益最好。然而，在这种情况，那些推送的消息还没有及时处理，并且数量一直上升，那么就会一直消耗消费者 RAM。

在无限制预载下自动确认模式或手动确认模式要小心使用。消费者在不经过确认消费大量消息，将会导致它们所连接的节点的内存消耗增长。要不断实验才能找到其合适的预载值，因为不同的工作环境负载不同。值在 100-300 范围通常提供了最佳的吞吐量，并且不会让消费者有重大风险。设置更高的值往往会使[收益递减](https://www.rabbitmq.com/blog/2014/04/14/finding-bottlenecks-with-rabbitmq-3-3/)。

预载值设置为 1 是最保守的。它降低了吞吐，特别是在消费者连接延迟非常高的环境。对大多数应用程序，更高的值更好更合适。

## 当消费者失败或丢失连接：自动重试

当使用手动确认模式时，当推送的 channel（或连接）发生关闭时，那些未确认的推送将会自动进入重试。这包括客户端 TCP 连接丢失，消费者应用程序处理失败以及通道级别协议异常（下面会覆盖到）。

注意，检测不可用的客户端需要一段时间。

由于这种行为，消费者必须要准备好处理重新推送的消息，要实现消息处理的幂等性。重新推送的消息有一个 bool 类型的属性，`redelivery`。RabbitMQ 会设置为 `true`。第一次推送的消息会被设置为 `false`。注意，消费者接收的消息可能是其它消费者已经消费过的消息。

## 客户端错误：重复确认和未知标签

客户端确认一次会确认多个相同的推送标签，RabbitMQ 将会返回一个通道错误，如 `PRECONDITION_FAILED - unknown delivery tag 100`。如果是一个未知的推送标签也会抛出相同的错误。

在与接收推送的通道不同的通常上确认这个消息，这种场景中，broker 也会解释为 “未知推送标签”，当一个消息被确认，无论是成功还是失败。推送确认必须要在相同的 channel。

## 推送确认

网络可能会不经意间出现故障，而且检测某些故障需要时间。因此，向 Socket 编写协议帧（protocol frame）或一组帧（例如发布的消息）的客户端不能假定消息已经到达服务器并已成功处理。它可能会丢失或推送发生延时。

使用 AMPQ 0-9-1 标准，只有一种方式来保证消息不会丢失，那就是事务 —— 让 channel 然后对每个消息或每组消息发布，提交  事务化。在这种情况下，事务是不必要的，过于重量级，并将吞吐量降低了 250 倍。要记住，引入确认机制，就不必要开启事务了。它模仿了协议中已经存在的消费者确认机制。

为了开启确认，客户端发送 `confirm.select` 方法。这取决于 `no-wait` 是否设置，broker 可能会使用 `confirm.select-ok` 来回应。一旦在 channel 上使用了 `confirm.select`，它就是说要进入确认模式了。一个事务 channel 无法进入确认模式，并且一旦 channel 进入确认模式，那它就无法事务了。

一旦 channel 进入确认模式，broker 和 客户端统计消息（第一次调用 `confirm.select` 从 1 开始计数）。broker 在相同的 channel 上发送 `basic.ack`，然后处理并确认消息。`delivery-tag` 字段包含已经确认的消息序列。broker 可能会在 `basic.ack` 中设置 `multipe` 字段来表明所有消息包括那些被处理的消息序列。

## 发布者消息失败确认

当 broker 无法成功处理消息这种情况列外，broker 将会发送一个 `basic.nack` 而不是 `basic.ack`。在这种情境下，字段 `basic.nack` 与作为 `basic.ack` 的职责有相同的意思，并且 `requeue` 字段会被忽略。否定一个或多个消息，broker 表示这个消息无法被处理以及拒绝响应它们；到那时，客户端会选择重新发送消息。

当 channel 进入确认模式时，所有已经发布的消息序列都会被确认或拒绝一次。无法保证消息合适被确认。没有消息将被确认或拒绝。

`basic.nack` 只会在 Erlang 队列的响应进程中出现了内部错误时确认推送。

## 发布的消息 Broker 何时确认？



