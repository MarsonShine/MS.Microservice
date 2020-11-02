# 备用 Exchanges

有时希望让客户端处理 exchange 无法路由的消息（比如由于没有绑定队列与匹配到队列绑定）。典型的例子有：

- 探测客户端合适故障或者是恶意发布无法路由的消息
- "or else" 其中一些消息被特殊处理，其余消息由通用处理程序处理的路由语义

Alternate Exchange（AE）是解决上述问题的默认特性。

# 定义备用 Exchange

对于给定的 Exchange，客户端可以使用[策略](https://www.rabbitmq.com/parameters.html#policies)或是客户端 Exchange 的可选参数（`x-args`）定义一个 AE。假设策略和参数都指定了一个 AE，用可选参数定义的 AE 会覆盖策略定义的 AE。

## 用策略定义 AE

这是定义 AE 推荐的方式。

在策略中添加键 `alternate-exchange`来定义一个 AE，确保策略匹配到这个 exchange：

```shell
rabbitmqctl set_policy AE "^my-direct$" '{"alternate-exchange":"my-ae"}'
```

windows：

```shell
rabbitmqctl.bat set_policy AE "^my-direct$" "{""alternate-exchange"":""my-ae""}"
```

这个例子应用了 AE 的 "my-ae" 到命名为的 "my-direct" 的交换机。策略定义也是可以通过 RabbitMQ 管理插件来设置，具体详见[策略文档](https://www.rabbitmq.com/parameters.html#policies)。

## 客户端参数定义 AE

不鼓励这种定义 AE 的方式。

当创建一个交换器时，AE的名称可以在 `exchange.declare` 方法的可选参数表中提供，方法是指定一个键 `alternals -exchange` 和一个包含名称的类型 'S' 的值(字符串)。

当指定一个 AE 时，除了声明的交换器上的通常配置权限之外，用户还需要对该交换机具有读权限，对 AE 具有写权限。

例如：

```java
Map<String, Object> args = new HashMap<String, Object>();
args.put("alternate-exchange", "my-ae");
channel.exchangeDeclare("my-direct", "direct", false, false, args);
channel.exchangeDeclare("my-ae", "fanout");
channel.queueDeclare("routed");
channel.queueBind("routed", "my-direct", "key1");
channel.queueDeclare("unrouted");
channel.queueBind("unrouted", "my-ae", "");
```

上面的代码片段是创建了一个名为 'my-direct' 直接交换机，它配置了一个 'my-ae' 的 AE。然后申明这个交换机为 `fanout ` 交换机。我们还绑定了一个 `routed` 队列到 `my-direct` 上，它设置了 'key1' 的绑定键以及绑定了队列 `unrouted` 到 `my-ae`。

## AE 如何工作

当一个交换机配置了一个 AE，无法将一个消息路由到任何队列时，它就会将这个消息推送到这个指定的 AE。如果 AE 不存在那么就会记录这个警告。如果 AE 不能路由消息，那么它将相应地将消息发布到它的 AE(如果它配置了一个 AE，即 AE 的 AE)。这个处理过程会一直持续到消息成功路由，或到达 AEs 链的末端或遇到一个已经尝试路由消息的 AE 为止。

例如我发布一个消息到 `my-direct`，并附带了routing key `key1`，然后这个消息被路由到了 `routed` 队列，这与标准的 AMQP 行为一致。但是当如果发布一个消息到 `my-direct `并且 routing key 为 `key2` 时，消息没有被丢弃，而是通过我们配置的 AE 路由到 `unrouted` 队列。

AE 的行为完全跟路由有关。如果一个消息是被 AE 路由的，由于 `mandatory` 标志的目的，它仍然被视为路由，并且这个消息是不变的。

