# 消息元数据的长度约束

## 问题

消息经过业务入队、Outbox、RabbitMQ、Inbox 四处。原来的检查不一致：RabbitMQ 发送端只限制 `CorrelationId` 为 255 个 UTF-8 字节，接收端没有检查这三个字段；Inbox 和 Outbox 的数据库列分别限制 `CorrelationId`、`TraceParent`、`TraceState` 为 200、128、512 个字符。执行列宽约束的数据库会拒绝过长的值，但 RabbitMQ 投递处理器把数据库异常归为暂时性失败并重新入队。

例如，201 个 ASCII 字符的 `CorrelationId` 只有 201 个 UTF-8 字节，发送端原本允许它；它却超过 Inbox 的 200 字符列。反过来，86 个汉字只有 86 个 .NET 字符，但编码后是 258 个 UTF-8 字节，超过 RabbitMQ `CorrelationId` 的 255 字节限制。

## 约束与处理位置

| 字段 | .NET 字符数上限 | UTF-8 字节数上限 | 原因 |
| --- | ---: | ---: | --- |
| `CorrelationId` | 200 | 255 | Inbox/Outbox 列宽与 AMQP short string |
| `TraceParent` | 128 | 无额外上限 | Inbox/Outbox 列宽 |
| `TraceState` | 512 | 无额外上限 | Inbox/Outbox 列宽 |

`.NET 字符数` 指 `string.Length`，即 UTF-16 代码单元数。数据库提供程序对补充字符的计数可能不同，因此这里选用能满足现有列宽的保守上限。三个字段都要求能够无损编码为 UTF-8；孤立代理字符会被拒绝。`null` 和空字符串仍然允许。本次只统一长度及编码约束，不改变追踪字段的格式解析规则。

`MessageMetadataLimits` 定义上限并执行共同校验。`MessageContractRegistry` 在序列化和反序列化时校验；RabbitMQ 在发送前校验，接收时在进入业务接收器之前校验；`InboxStore` 在直接调用该入口时也在数据库操作之前校验。EF 模型使用同一组字符数常量。SelfManaged 的 RabbitMQ 外部非法投递会得到 `MessageContractException`，投递处理器将其拒绝而不重新入队；本地非法发送在入队或传输前失败。数据库连接故障等暂时性错误仍按原有逻辑重试。

## 验证与边界

单元测试覆盖三个字段的合法上限、超出上限、ASCII 和汉字的字节边界、孤立代理字符，以及 RabbitMQ 的拒绝决定和直接 Inbox 入口不写入数据库。测试中的 Inbox 使用 SQLite；SQLite 不执行 `varchar(n)` 长度限制，因此这些测试验证的是应用校验，并非数据库列本身的拒绝行为。实际 PostgreSQL/RabbitMQ 容器矩阵在可用的 Docker 环境中仍需运行。

旧数据若已经含有超限值，发布或重放时会按永久性契约错误处理；本次不修改已有数据。若未来调整数据库列宽或更换传输协议，应同时调整共享上限、迁移和边界测试。
