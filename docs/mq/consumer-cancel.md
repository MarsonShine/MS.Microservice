# 消费者取消通知

当 channel 从一个队列中消费消息的时候，会有很多原因导致消费停止。其中一个最明显的就是客户端在相同的 channel 上调用方法 `basic.cancel`，这会引起消费者取消处理，并使用 `basic.cancel-ok` 来回复服务器。其它因素比如队列被删除，或在集群中的节点分配队列失败，这些都会导致消费请求取消，但是客户端对此毫不知情，这通常是没有帮助的。

为了接这个问题，我们引入了一个 broke 的拓展，在那些不在预期的消费取消的情况下，给客户端发送 `basic.cancel`。broker 在没有发送的情况下从客户端接收到一个 `basic.cancel`。AMQP 0-9-1 客户端默认情况下不接收从 broker 异步的方法 `basic.cancel`，所以为了开启这个特性，客户端必须提供一个在 `client-properties` 中的 `capabilities ` 表。其中包含一个关键的consumer_cancel_notify和一个布尔值true。有关详细信息，请参阅 [section on capabilities](https://www.rabbitmq.com/consumer-cancel.html#capabilities)。

我们支持的客户端默认给 broker 提供一个能力，因此将会发送一个 `basic.cancel` 方法，它们会提供给消费者一个回调。例如，在 Java 客户端有一个接口 `Consumer` 有个 `handleCancel` 方法，它能被子类 `DefaultConsumer` 覆写：

```c#
channel.queueDeclare(queue, false, true, false, null);
Consumer consumer = new DefaultConsumer(channel) {
    @Override
    public void handleCancel(String consumerTag) throws IOException {
        // consumer has been cancelled unexpectedly
    }
};
channel.basicConsume(queue, consumer);
```

客户端给消费者发送一个 `basic.cancel` ，这是未在预期的取消，这不是一个错误（例如由于删掉队列）。当然，这里客户端发起一个 `basic.cancel` 和 broker 发送一个异步通知这两个之间存在竞争。在这种情况下，broker 当接收到 `basic.cancel` 不会发生错误并且会正常的用 `basic.cnacel-ok` 回复。

# 消费取消和镜像队列

客户端支持消费取消通知机制，客户端会在队列不可用或被删除的时候会发送一个取消请求。消费者会在镜像队列不可用的时候发送一个取消请求（关于镜像队列如何做到的详见 [mirrored queues](https://www.rabbitmq.com/ha.html)）。

# 客户端和服务端能力

AMQP 0-9-1 规格定义了一种方式让客户端和服务端能够展示它们的能力，就是使用 `connection.open` 方法的字段 `capabilities` 。这个字段是申明在规格 AMQP 0-9-1 中，不受 broke 视察。正如 AMQP 0-8中也一样，`shortstr` 也是短字符串:不超过256个字符的字符串。

客户端和服务器能够提供它们支持的扩展和功能是有充分理由的，因此，我们引入了另一种形式的能力。`connection.start` 的字段 `server-properties` 以及 `connection.start-ok` 的字段 `client-properties`，字段值（一个 `peer-properties` 表）可选择的包含一个名为 `capabilities` 的键，它的值是另一个表，其中的键命名了所支持的功能。这些功能键的值通常是布尔值，指示是否支持该功能，但可能根据功能的性质而变化。

例如 RabbitMQ broker 提供了 `server-properties` 给客户端，就像下面：

```java
{ "product"      = (longstr) "RabbitMQ",
  "platform"     = (longstr) "Erlang/OTP",
  "information"  = (longstr) "Licensed under the MPL.  See https://www.rabbitmq.com/",
  "copyright"    = (longstr) "Copyright (c) 2007-2020 VMware, Inc. or its affiliates.",
  "capabilities" = (table)   { "exchange_exchange_bindings" = (bool) true,
                               "consumer_cancel_notify"     = (bool) true,
                               "basic.nack"                 = (bool) true,
                               "publisher_confirms"         = (bool) true },
  "version"      = (longstr) "3.7.15" }
```

注意，对于客户端来说，将这个 `capabilities` 表作为 `client-properties` 表的一部分是可选的：未能显示这样的表并不妨碍客户端能够使用扩展(如[exchange to exchange绑定](https://www.rabbitmq.com/e2e.html))。然而，大多数情况下如作为消费者取消通知，客户端必须提供一个相关的能力，否则 broker 无法知道客户端有能力接收额外的通知。