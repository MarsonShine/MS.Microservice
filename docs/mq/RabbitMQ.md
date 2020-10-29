# RabbitMQ

## 推送

- [发布确认](ack.md)（又叫发布ACK（Acknowledgements））是一种知道 RabbitMQ 响应消息的一种轻量级的方式。
- 阻塞连接通知（Block Connection Notification）当连接阻断和接触阻断时允许通知客户端。

## 订阅

- 取消订阅通知（Consumer Cancellation Notifactions）让订阅者（消费者）知道服务器是否被取消。
- `basic.nack` 拓展了 `basic.reject` 以支持一次取消多个消息。
- 消费者优先级（Consumer Priorities）允许将消息发送给优先级更高的消费者。
- 直接回复（Direct reply-to）是允许 RPC 客户端可以不需要生命临时的队列就可以直接接收它们查询的回复

## 消息路由

- 交换机间的绑定（[Exchange to Exchange Bindings](https://www.rabbitmq.com/e2e.html)），为了让路由更加灵活，允许消息传递给多个交换机。
- 备用交换机（Alternate Exchanges）对那些无法路由的消息路由。
- 发送已选择的发送器（[Sender-selected Distribution](https://www.rabbitmq.com/sender-selected.html)），它允许一个发布者决定这些路由的消息直接路由到指定地方。

## 消息生命周期

- Per-Queue Message TTL 表示确定未消费的消息在自动删除之前能多久生存在队列中。
- Per-Message TTL 确定在每条消息的基础上的 TTL。
- Queue TTL 确定在未使用的队列在自动删除之前能存活多长时间。
- Dead Letter Exchange（死信）确保消息当被拒绝或过期时能重新路由。
- Queue Length Limit 允许队列设置的最大容量。
- Priority Queues 支持消息优先级字段（以略微不同的方式）。

## 验证与身份

- User-ID 是经过服务器验证的消息属性
- 发布适当功能的客户机可能会收到来自 broker 的显式的验证失败通知。
- `update-secret` 当那些凭证过期的时候，能够为活动的连接重新生成一个凭证。



# 参考资料

- https://www.rabbitmq.com/extensions.html