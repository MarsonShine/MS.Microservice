# Messaging 模块的设计思路

## 契约为何不能直接等于某个框架的 API

如果业务 Handler 接收原生 Envelope，或者业务自己操作 Outbox 表，
“替换组件”就会变成修改所有业务代码。这里用稳定事件 Id、显式类型名/版本、
工作单元、业务 Handler 和最小失败操作作为边界；表结构、租约与调度属于实现。

[Contracts.cs](../src/MS.Microservice.Messaging.Abstractions/Contracts.cs)定义公开行为。
类型注册是持久化协议的一部分，不能随着程序集搬家自动变化。
EnqueueAsync 保存序列化快照，是为了避免调用者随后修改对象，改变即将提交的消息。

## 默认实现的事务与所有权

[SelfManagedUnitOfWork](../src/MS.Microservice.Messaging.SelfManaged.EFCore/SelfManagedUnitOfWork.cs)
在同一个业务 DbContext 中保存业务和 Outbox，最外层统一提交。
内部操作失败后，即使调用方捕获异常，事务也被标记为只能回滚，防止提交部分业务结果。

[InboxStore](../src/MS.Microservice.Messaging.SelfManaged.EFCore/InboxStore.cs)区分 Acquired、
AlreadyProcessed 与 Busy。Busy 不是“处理过”，不能因此 ACK。完成标记和业务效果必须一起提交。

[LeaseGuard](../src/MS.Microservice.Messaging.SelfManaged.EFCore/LeaseGuard.cs)续租使用独立作用域：
EF DbContext 不支持并发操作，后台续租不能与业务共享一个实例。
续租失败意味着无法证明所有权，取消当前工作比允许旧 worker 继续确认更安全。
完成、失败、续租本身仍必须有 token/status 条件，取消令牌不是数据库所有权锁。

RabbitMQ 发送确认与数据库提交是两个系统的动作，无法合并成一个本地原子事务。
确认丢失可能导致重发，因此实现按至少一次传输设计，并以 Inbox 与业务唯一约束防重复。
这也是为什么不能把 EnqueueAsync 的返回值当作 Broker 已接收。

## Wolverine 替换了哪些职责

Wolverine 接管可靠存储、事务中间件、原生收发与恢复；不会复用自研租约表。
适配器在持久化前映射事件 Id，通过原生 DbContext outbox 与事务桥接共同契约。

中立 Handler 必须从原生约定扫描中排除，否则命名为 Handler/Consumer 的业务类型可能与适配桥一起被执行。
普通本地命令仍可使用原生发现机制；业务集成事件只有适配桥这一条入口。

固定版本的原生 RabbitMQ 发送路径未满足 mandatory 要求，因此使用发送扩展补足路由确认，
同时保留原生持久存储和恢复所有权。它不是第二套 Outbox。
具体差异见 [Wolverine README](../src/MS.Microservice.Messaging.Wolverine/README.md)。

## 接入和阅读顺序

1. 先读公共契约与消息注册，明确 Id、时间、版本和取消行为。
2. 看 [Reference.Application](../../samples/Reference/MS.Microservice.Reference.Application/README.md)如何使用中立接口。
3. 看所选 Provider 的工作单元、消费路径与失败操作。
4. 用模块 test 中的事务、并发、重放与进程恢复用例核对不变量。

两种实现可以有不同内部状态、失败标识和 Broker ACK 时序。
共同要求是已经承诺的持久化与业务恢复行为，而不是强行统一内部表。
切换前需要停写、排空和处置死信；配置不会自动迁移在途消息。

## Consumer 查找成本

`SelfManagedReceiver.ReceiveAsync` 每收到一条消息都会按 consumer 名称查找订阅。原实现对只读订阅列表调用
`SingleOrDefault`。它每次构造 LINQ 查找状态，并遍历列表；即使只有一个订阅，查找仍分配 120 B。
构造 `MessageTopology` 时已经检查名称唯一，因此接收时不需要再次扫描以证明唯一性。

现在构造拓扑时用同一个 `Dictionary<string, MessageSubscription>` 完成唯一性检查和后续查找，
字符串比较仍为 `Ordinal`，公开的 `Subscriptions` 仍保持注册顺序和只读快照。
字典占用随订阅数量增长的一次性内存，但代替了原本用于去重的 `HashSet`。查找路径没有反射或动态代码生成。

在 `MS.Microservice.Messaging` 目录下，以 Windows、.NET 10、Release 运行 `dotnet run --project benchmarks/MS.Microservice.Messaging.Benchmarks -c Release`，
每个场景预热 2 万次，随后测 5 轮、每轮 20 万次，表中为各轮中位数；分配使用
`GC.GetAllocatedBytesForCurrentThread` 测量：

| 查找场景 | 修改前 | 修改后 |
| --- | ---: | ---: |
| 1 个订阅，首项 | 67.8 ns、120 B/次 | 16.6 ns、0 B/次 |
| 2 个订阅，末项 | 85.2 ns、120 B/次 | 16.7 ns、0 B/次 |
| 8 个订阅，末项 | 115.5 ns、120 B/次 | 16.8 ns、0 B/次 |

这是内存中的订阅查找微基准；没有测量数据库、Broker 或完整消费吞吐量。
`codex/performance-aot` 后续已将消息 JSON 和 Wolverine 桥接改为显式登记：`MessageContract.For<T>` 接收 `JsonTypeInfo<T>`，Registry 按 `Type` 查找已登记的序列化委托；`WolverineMessageRegistration<TContext>.For<TEvent>()` 在配置时列出需要的事件类型。这些改动没有包含在上表的订阅查找基准中。Wolverine、EF 与传输依赖的完整 Native AOT 发布兼容性仍需单独验证，详见[迁移说明](../../docs/Performance-Aot-Migration.md)。
