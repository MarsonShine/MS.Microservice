# HTTP 写入幂等记录

## 问题

客户端可能在业务提交成功后、收到 HTTP 响应前断线。它使用同一个请求键重试时，业务唯一约束可以阻止第二次写入，却无法返回第一次的状态码、`Location` 和正文。若在端点执行完毕后才缓存响应，业务事务和响应记录之间仍有一个断电窗口。

## 实现边界

`MS.Microservice.Idempotency.EFCore` 将幂等记录加入业务 `DbContext`。调用方先开启自己拥有的事务，再调用 `ClaimAndExecuteAsync`。该方法先插入 `(操作与认证身份的哈希, 请求键哈希)` 唯一记录并执行 `SaveChangesAsync`，然后调用业务回调，最后保存请求指纹与可重放响应。所有写入仍在调用方事务内；本组件不提交事务，也不接管消息 Outbox。

先保存占位记录使同键并发请求在执行第二次业务操作之前发生数据库写冲突。赢家提交后，输家必须退出并回滚自己的事务，再用新的上下文调用 `FindAsync`：相同指纹返回已保存响应；不同指纹返回 `DifferentRequest`。如果查不到记录，应报告原数据库错误或按照调用方明确的重试策略处理，不能假定任意数据库异常都是幂等键冲突。赢家失败并回滚时，占位记录也回滚，后续请求可以执行。

`IdempotencyRequest.Create` 接收稳定的操作名、已认证身份、一个请求键和调用方选定的请求表示字节。键最多 128 个可见 ASCII 字符；请求表示最多 1 MiB。库仅保存三项 SHA-256 哈希，不保存原始键或请求。调用方须先拒绝多个 `Idempotency-Key` 请求头，并确定“同一请求”的比较规则。Reference 执行器把 Method、Path、QueryString、完整的 Content-Type 和 Body 表示纳入指纹：UTF-8 JSON 直接解析，标为 `charset=utf-16` 的 JSON 先解码；随后递归按对象字段名排序并忽略字符串之外的空白，数组保持原顺序。非 JSON 使用原始 Body 字节。JSON 同一对象内的字段名重复，包括只差大小写的字段名，会在绑定前被拒绝。此规则由 Reference 实现，EFCore 存储组件不解析 HTTP 或 JSON。身份必须来自已经验证的认证信息，重放前仍须执行授权。

可保存响应的状态码是 200–499，正文最多 64 KiB。`Content-Type` 可为空，例如 `204 No Content` 可以保存空正文，之后按原状态和空正文重放。调用方应让初次响应与重放响应都使用 `IdempotencyResponse` 中的状态码、可为空的内容类型、`Location` 和正文；动态响应头和服务器错误不属于重放契约。Reference 的 HTTP 执行器进一步限定只保存 `2xx`，并在响应写入暂存流时执行 64 KiB 上限，超过时回滚事务。存储原响应正文意味着它可能包含个人信息，应按业务数据控制访问与保留期限。`PruneExpiredAsync` 删除到期记录，但不会自动运行：宿主必须安排清理。到期后、实际删除前继续重放；删除后同键可以重新执行。客户端应在最早可能清理记录之前完成重试，并确保服务端仍保留该记录。

本组件只约束同一数据库事务内的业务记录和响应记录。外部网络调用、邮件和没有参与该事务的消息发送不获得 exactly-once 保证。要与现有消息单元配合，幂等执行应是最外层事务，业务服务的嵌套 `IUnitOfWork` 参加同一个 `DbContext` 事务。

## 最小接入顺序

1. 在业务 `DbContext.OnModelCreating` 中调用 `model.AddHttpIdempotency(schema)`，为实际数据库生成迁移。
2. 在已经鉴权的端点校验单个请求键，按该接口明确的比较规则生成请求指纹和有界响应。
3. 在该 `DbContext` 的最外层业务事务中调用 `ClaimAndExecuteAsync`。提交失败后先回滚，再查询赢家结果。
4. 安排过期记录清理；用真实数据库验证两次并发请求及故障重试行为。

本项目的 SQLite 测试覆盖唯一键、回滚、取消、保留期和重放契约。实际 PostgreSQL 部署还需执行并发与故障集成测试。此功能改变写入正确性，不宣称性能收益，也没有做性能优化基准测试。

## EF 编译模型与 AOT 边界

业务 `DbContext` 位于使用本组件的项目中，因此 EF 生成的编译模型也位于使用方程序集。原先 `IdempotencyRecord` 是组件内部类型；普通 EF 模型能注册它，但 `dotnet ef dbcontext optimize --nativeaot` 生成的代码会直接引用这个类型，使用方编译时出现 `CS0122`。`IdempotencyRecord` 现在公开，仅为让跨程序集的生成代码能够引用它。表名、主键、列和事务行为没有改变，数据库不需要新迁移。

运行 `./build/validate-idempotency-compiled-model.ps1` 会创建临时使用方 `DbContext`，生成 EF Native AOT 编译模型并再次编译使用方。它验证组件不会因实体可见性阻断编译模型生成。EF Core 的 `DbContext` 构造函数仍标注了 `IL2026` 和 `IL3050`；这项检查不等于 Reference.Web 已完成 Native AOT 发布，也不验证 PostgreSQL 驱动或 Wolverine 的原生运行。未来若要原生发布整个宿主，需要单独处理这些依赖和运行路径。

## MVC Action 接入

MVC 可以在单个 Action 上使用 `[RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.create")]`。Attribute 只通过 DI 选择 Filter，并携带稳定操作名；Reference 的多个测试 Action 共用这个 `IAsyncResourceFilter` 和 `ReferenceHttpIdempotencyExecutor`，不再为每个 Action 编写业务 Filter。共用执行器仍在 Reference.Web，其他宿主需要提供自己的事务和身份接入。当前 MVC 示例只在 Reference.Web.Tests 的非 AOT TestServer 中映射，详见 [MVC Action 如何接入 HTTP 幂等](src/MS.Microservice.Idempotency.Mvc/README.md)。
