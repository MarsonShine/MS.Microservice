# 消息 TTL（Time-To-Live）和过期

RabbitMQ 可以给队列和消息两个都设置 TTL 时间。通过使用队列参数或策略就能做到这个（推荐用策略配置）。

TTL 消息可以应用在单队列，一组队列或是应用在基于消息的逐个应用。TTL 设置可以通过策略操作强制设置。

# 队列中的每个消息的 TTL

TTL 是在队列中，通过使用策略将 `message-ttl` 复制代表给消息添加 TTL，不用策略也可以在队列申明的时候添加相同的参数来指定 TTL。

在队列中的消息的存在的时间如果设置的 TTL 要长的话，就会过期。请注意，路由到多个队列的消息可能在不同时间死亡，或者在其所在的每个队列中根本没有死亡。在一个队列中存在一个死信消息对队列中的其它消息没有影响。

服务器保证了死信队列不会通过 `basic.delivery` 推送消息或者是通过 `basi.get-ok` 这种响应（用于一次性获取操作）。另外，服务器还会将超过 TTL 的消息快速移除。

TTL 参数或策略的值必须是非负整数(0 <= n)，用毫秒来描述间隔。假设 TTL 的值是 1000，那么在这个消息推送给消费者之前存活的时间就是 1 秒。参数类型格式是  AMQP 0-9-1 中的 `short-short-int`, `short-int`, `long-int`, 或者 `long-long-int`。

# 队列使用策略定义 TTL

在策略中指定 TTL，只需要添加 `message-ttl` 

| rabbitmqctl           | `rabbitmqctl set_policy TTL ".*" '{"message-ttl":60000}' --apply-to queues` |
| --------------------- | ------------------------------------------------------------ |
| rabbitmqctl (Windows) | `rabbitmqctl set_policy TTL ".*" "{""message-ttl"":60000}" --apply-to queues` |

上面的配置是说给所有队列设置 TTL 时间为 1 分钟

# 使用 x-argument 在队列申明期间消息设置 TTL

下面是 Java 客户端例子，创建一个队列让消息存留 60 秒

```java
Map<String, Object> args = new HashMap<String, Object>();
args.put("x-message-ttl", 60000);
channel.queueDeclare("myqueue", false, false, false, args);
```

.NET 客户端

```c#
var args = new Dictionary<string, object>();
args.Add("x-message-ttl", 60000);
model.QueueDeclare("myqueue", false, false, false, args);
```

给已经有的消息的队列应用 TTL，但是有一些注意事项（后面会提到）。

如果它重入队列了（例如由于使用了 AMQP 方法并传递了参数 `requeue`，或者是由于 channel 关闭），就会提供消息的原始过期时间。

将 TTL 设置为 0 将导致消息在到达队列时就过期，除非它们能够立即交付给消费者。因此它们提供了另一个推送标签 `immediate`，RabbitMQ 服务端是不支持这个参数。不像那个标签，没有发出 `basic.return`，并且如果设置了一个死信交换机，那么消息都将成为死信消息。

# 发布者中每个消息的 TTL

当发送一个 `basic.publish` 时，TTL 可以通过设置在 AMQP 0-9-1 的类 `basic` 的 `expiration` 字段，给每个消息指定。

`expiration` 字段值描述的时 TTL 时间间隔。受 `x-message-ttl` 约束的一样。由于 `expiration` 必须是字符串，broker 将一个字符串表示数字。

当给每个队列和每个消息指定 TTL 时，会选择其中最低的值。

下面是 JAVA 客户端的例子，存留时间为 60 秒：

```java
byte[] messageBodyBytes = "Hello, world!".getBytes();
AMQP.BasicProperties properties = new AMQP.BasicProperties.Builder()
                                   .expiration("60000")
                                   .build();
channel.basicPublish("my-exchange", "routing-key", properties, messageBodyBytes);
```

C# ：

```c#
byte[] messageBodyBytes = System.Text.Encoding.UTF8.GetBytes("Hello, world!");

IBasicProperties props = model.CreateBasicProperties();
props.ContentType = "text/plain";
props.DeliveryMode = 2;
props.Expiration = "36000000"

model.BasicPublish(exchangeName,
                   routingKey, props,
                   messageBodyBytes);
```

# 警告

具有每个消息 TTL 的队列(当它们已经有消息时)将在发生特定事件时丢弃消息。只有当过期的消息到达队列头部时，它们才会真正被丢弃(或被死信)。消费者不会收到已过期的消息。请记住，在消息过期和使用者交付之间可能存在竞态条件，例如在消息写进 Socket 之后，在过期之前消费者又接收到了这个消息。

在设置每条消息的TTL时，过期的消息会排在未过期的消息之后，知道这个消息被消费或过期。因此这些过期的消息所占用的资源不会被释放，并且它们还会再队列中计数统计（比如再队列中的消息数量）。

当我们追溯给每个消息应用 TTL 时，它推荐这些消费能确保快速的丢弃。

考虑到现有队列上的每个消息 TTL 设置的这种行为，当需要删除消息以释放资源时，应该使用队列 TTL(或队列清除，或队列删除)。

# 队列 TTL

TTL 可以设置在队列上，并不仅时队列上的内容。队列只有它们没有使用一段时间之后就会过期（并不是被消费）。这个特征能同[自动删除队列属性](https://www.rabbitmq.com/queues.html)一起使用。

能通过给 `queue.declare` 上设置 `x-arguments` 来给队列设置过期时间，或者通过设置策略的 `expires`。它控制了一个队列在自动删除之前，能使用多长时间。未使用代表还没有消费者，最近队列还没有重新申明（重新申明新的租约），并且在过期这段时间内还没有调用 `basic.get`。例如这还能用在对于 RPC 风格的回复队列，这里有很多队列创建永不耗尽的队列。

如果在过期间隔内队列没有被使用，那么服务器就会保证队列将会被删除。对于过期后如何迅速删除队列，没有给出任何保证。持久化队列的租约（lease）在服务器重启的时候启动。

`x=arguments` 参数的值或者 `expires` 策略描述了以毫秒未单位的过期时间间隔。它必须时一个正整数（不像 TTL 它可以是 0）。因此 1000 表示的这个队列在 1 秒内没有使用则将会被删除。

# 队列使用策略定义队列 TTL

以下策略使所有队列在上次使用后 30 分钟后过期:

| rabbitmqctl            | rabbitmqctl set_policy expiry ".*" '{"expires":1800000}' --apply-to queues |
| ---------------------- | ------------------------------------------------------------ |
| rabbitmqctl（windows） | rabbitmqctl.bat set_policy expiry ".*" "{""expires"":1800000}" --apply-to queues |

# 申明队列期间使用 x-arguments 定义队列 TTL

下面这个例子是 Java 创建一个队列，并设置了未使用时间位 30 分钟后过期

```java
Map<String, Object> args = new HashMap<String, Object>();
args.put("x-expires", 1800000);
channel.queueDeclare("myqueue", false, false, false, args);
```

