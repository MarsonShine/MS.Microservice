# EventBus 从发布到业务处理器的验收

2026-11-20，Windows x64、.NET 11 Release。起点为 `39a704c` 加工作区中已验收的订阅索引优化。范围包括本包的进程内总线、订阅表、容器注册、测试与示例。

旧入口能按短名找到事件类型，尚未保存调用哪个 `Handle` 的信息。本次将注册时的事件类型、处理器类型和强类型委托保存在同一份注册关系中。类型查询沿用短名的首次绑定，消费则以发布对象真实 CLR 类型筛选。发布取得有序快照后在锁外建立作用域，按注册顺序解析并等待处理器。首个失败会终止该次发布。[从业务示例跟到 Handle](../../../src/MS.Microservice.EventBus/samples/basic-usage.md)

| 验收 | 结果 |
| --- | --- |
| `dotnet test test/MS.Microservice.EventBus.Tests -c Release` | 29 项通过，0 失败、0 跳过。覆盖注册、退订、清空、同名事件隔离、逆变及显式多接口实现、快照、异步等待和作用域释放 |
| 同步入口与启动兼容 | 同步、异步发布均传播处理器的原始异常并停止后续处理。旧适配器只实现 `Publish` 即可继续工作。旧方式注册的内置管理器与 DI 暴露的总线共享状态 |
| 真实业务示例 | 发布订单 42 输出审计与收据。退订收据后发布订单 43，仅输出审计。同一笔订单的两个处理器共享 scoped 依赖，下一笔取得新的实例 |
| NuGet 消费 | 将本模块及其仓库依赖打为本地包，由独立控制台项目通过 PackageReference 还原，直接执行完整示例，三行输出及顺序与文档一致。已核对包内 README、示例和配图的路径 |

[端到端消费测试](../../../test/MS.Microservice.EventBus.Tests/InMemoryEventBusTests.cs)和[同步适配器兼容测试](../../../test/MS.Microservice.EventBus.Tests/EventBusContractTests.cs)可单独运行。

此前的 15 个订阅基准场景也在新实现上完成结果校验。写入现在需要维护有序节点和失效快照，例如在已有首处理器时增删第二个处理器，单次复测分配约 48 B/轮。此前数据属于仅管理订阅的实现，不能直接用作消费吞吐指标。[此前的测量范围](../EVENTBUS-SUBSCRIPTIONS/summary.md)
