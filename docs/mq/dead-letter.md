# 死信问题

队列的消息是可以被 “死信” 的，什么是死信，就是以下情况就会出发死信，会将消息重新发布到交换机：

- 消息被消费者使用 `basic.cancel` 或 `basic.reject` 伴随参数 `requeue` 设置未 `true`
- 由于设置了每个消息的 TTL，那么超过了这个时间就会发生死信
- 由于队列设置容量限制，超过了这个限制会丢弃消息转而进入死信队列

注意，队列的过期不会造成死信

死信交换机（DLXs）就是普通的交换机。它们能用和申明任何类型。

对于所有队列，一个 DLSx 能够通过客户端使用[队列参数](https://www.rabbitmq.com/queues.html#optional-arguments)定义，或者通过服务器使用[策略](https://www.rabbitmq.com/parameters.html#policies)。在在策略和指定参数定义 DLXs 都存在时，其中的指定队列参数会覆盖策略指定。

推荐通过指定策略配置 DLXs，因为这个不涉及到应用程序的发布。

# 策略配置

定义一个 DLX 使用策略，是通过增加键 `dead-letter-exchange` 来定义一个策略：

| rabbitmqctl           | `rabbitmqctl set_policy DLX ".*" '{"dead-letter-exchange":"my-dlx"}' --apply-to queues` |
| --------------------- | ------------------------------------------------------------ |
| rabbitmqctl (Windows) | `rabbitmqctl set_policy DLX ".*" "{""dead-letter-exchange"":""my-dlx""}" --apply-to queues` |

上面表示给所有的队列应用 DLX `my-dlx`。这只是简单的例子，实际上是不同的队列设置不同的 DLX 配置。

类似的，一个显式的键路由能够通过添加键 `dead-letter-routing-key` 给策略来指定。

策略也能通过使用管理插件使用指定，详见 [策略文档](https://www.rabbitmq.com/parameters.html#policies) 

# 队列参数配置

若要为队列设置 DLX，请在声明队列时指定可选的参数 `x-dead-letter-exchange`。该值必须是同一虚拟主机中的交换机的名称：

```java
channel.exchangeDeclare("some.exchange.name", "direct");

Map<String, Object> args = new HashMap<String, Object>();
args.put("x-dead-letter-exchange", "some.exchange.name");
channel.queueDeclare("myqueue", false, false, false, args);
```

上面的代码声明了一个名为 `some.exchange.name` 的交换机，并为新创建的队列设置新的交换机作为 DLX。注意，交换机不必在声明队列时声明，但是在那个时候消息已经存在了，并且需要成为死信；如果丢失了它们，这些消息就会在背后丢弃掉。

当有死信消息的时候，你需要指定一个路由的键用。如果没有设置，这些消息自己的路由键就会被选择使用。

```java
args.put("x-dead-letter-routing-key", "some-routing-key");
```

当一个 DLX 被申明了，那么一般下还会在其申明的队列上配置权限，用户必须在 DLX 上经过读写许可。许可在队列申明期间验证的。

# 路由 DLX

死信消息在它们的 DLX 中路由的：

- 在消息所在的队列上指定路由键（route key）；或者不设置
- 使用与最初发布时相同的 route key

例如，如果你使用一个 `foo` 的 route key 发布消息到交换机，并且这个消息死信，那么它将会被推送到 DLX 中，route key 设置为 `foo`。如果原始队列消息到达并已经申明了 `x-dead-letter-routing-key` 的值为 `bar`，那么这个消息会被发布到 DLX 中，route key 为 `bar`。

注意，就如之前所述，当没有对队列设置 route key，那么在死信队列的消息用的是它们自己的 route key。这包括 `CC` 和 `BCC` 头添加的 route key（关于这两个头信息详见 [Sender-selected distribution](https://www.rabbitmq.com/sender-selected.html) ）

死信消息会在打开发布者确认机制内部重新发布，所以这些死信队列的消息在原始队列删除消息之前必须经过确认。换句话说，在死信队列确认收到确认消息之前，“正在发布” (消息过期) 的队列不会删除消息。要注意，在为清理的 broker 关闭事件，在原始队列和死信队列的消息可能会重复。

这可能形成死信消息的周期。例如，在没有指定死信 route key，一个队列的死信消息发生在默认的交换机中。如果在整个周期中没有发生拒绝，在这个周期的消息（例如消息到达相同的队列两次）将会丢弃。

# 死信对消息的影响

死信会修改头部信息：

- 用最新的死信交换机（DLX）替换交换机的名称
- 在队列中执行死信会替换 route key
- 如果发生上面两点，`CC` 头就会被移除
- `BBC`会被作为[Sender-selected distribution](https://www.rabbitmq.com/sender-selected.html)移除

死信处理会给每个死信消息头增加一个名为 `x-depth` 数组。这个数组包含一个关于每个死信时间的条目，通过成对的 `queue, reason` 的标识符。每个条目都是一个由以下字段组成的表：

- `queue`：在队列消息成为死信之前的名称
- `reason`：死信的原因
- `time`：死信消息的时间，格式是 AMQP 0-9-1 规格的 64 位时间戳
- `exchange`：消息发布目标交换机（注意，如果消息死信了多次，那么这个就是一个死信交换机）
- `routing-keys`：发布的消息的 route key 集合（包括 `CC` 键，但不包括 `BCC`）
- `count`：在队列中这个消息死信了的次数
- `original-expiration`（如果这个消息是因为 TTL 导致的死信）：消息原始的 `expiration` 属性。为了防止路由的消息再次过期，`expiration` 属性从死信队列的消息上移除。

新条目被前置到 `x-death` 数组的开头。在相同的队列和相同的死信理由情况下，`x-death` 早就包含了这个条目，count 字段会增长，并移动到数组的开头。

`reason` 描述的是为什么会成为死信消息，有以下原因：

- `reject`：通过被设置的参数 `requeue` 为 `true` 来拒绝消息
- `expired`：消息的 TTL 过期了
- `maxlen`：超过了队列最大容量阈值

为第一个死信事件添加了三个顶级头信息，它们分别是：

- `x-first-death-reason`
- `x-first-death-queue`
- `x-first-death-exchange`

它们与原始死信事件的 `reason`、`queue` 以及 `exchange` 字段具有相同的值。一旦添加，这些头部就不能被修改了。

注意，数组的排序顺序是就近排序的，所以总是最近的死信将会被记录在第一个条目中。

