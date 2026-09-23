# MS.Microservice.Messaging

完整可靠消息模块，默认是自研 EFCore Inbox/Outbox；Wolverine 是可替换的完整实现。
业务项目只依赖中立契约，不引用两种实现的内部表或原生 Envelope。

| 项目 | 职责 |
|---|---|
| [Abstractions](src/MS.Microservice.Messaging.Abstractions/README.md) | 事件、工作单元、入队、Handler、失败操作和传输扩展契约 |
| [SelfManaged.EFCore](src/MS.Microservice.Messaging.SelfManaged.EFCore/README.md) | 同事务持久化、幂等、租约、重试、死信与保留 |
| [RabbitMQ](src/MS.Microservice.Messaging.RabbitMQ/README.md) | 持久发布、mandatory、确认跟踪及手动 ACK |
| [Wolverine](src/MS.Microservice.Messaging.Wolverine/README.md) | 原生可靠存储、事务与恢复的契约适配 |

打开 [MS.Microservice.Messaging.slnx](MS.Microservice.Messaging.slnx)，阅读 [设计理由与调用链](docs/design.md)。
[消息元数据长度契约](docs/message-metadata-limits.md)说明入队、传输和持久化共用的上限。
Dependencies 中的 Reference 类库用于真实业务测试，不是消息组件的运行依赖。

在本目录构建：

    dotnet build MS.Microservice.Messaging.slnx -c Release

不需要外部依赖的测试可分别运行 test 下的 Abstractions、SelfManaged、RabbitMQ 和 Wolverine 测试项目。
IntegrationTests 中的真实依赖测试需要显式开启，见[测试说明](test/MS.Microservice.Messaging.IntegrationTests/README.md)；
不能把跳过这些用例当作验证通过。

接入时由宿主选择一个 Provider、配置数据库和 Broker，并显式执行迁移与拓扑初始化。
[Reference](../samples/Reference/README.md)展示完整接入，[Lab](../docs/labs/README.md)提供对照实验。
