# Reference persistence

`SelfManagedReferenceDbContext` and `WolverineReferenceDbContext` share business mappings but have separate messaging models and migration histories. Both commit business and message changes in the same database. The default database is a new reference database; these migrations do not modify the older Activation teaching database.

The profile identity `(Issuer, Subject)` and optimistic `Version` are enforced by the database. Audit storage has a message key and a unique business-effect key `(ProfileId, ProfileVersion, Consumer)`. Roles use tracked child entities; unchanged role objects are retained during updates.

Business/event occurrence times that must round-trip exactly use UTC ticks. Provider-specific operational timestamps retain their native representation.

Generate migrations through the design-time factories. They build models without connecting to a database. The library emits a runtime configuration so it can serve as the EF CLI startup project:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations script --idempotent --context SelfManagedReferenceDbContext --project samples/Reference/MS.Microservice.Reference.Persistence --startup-project samples/Reference/MS.Microservice.Reference.Persistence --output artifacts/reference-self.sql
```

Use the corresponding context for Wolverine. Its native envelope mappings are excluded from EF migrations: the separate migrator must also export/apply Wolverine's native message-store schema, including node and dead-letter tables. Do not consider the business EF script sufficient for that provider.

## Order 数据

`OrderService` 通过已有 `IUnitOfWork` 写入订单，仓储与 HTTP 幂等记录共用请求作用域的 `ReferenceDbContext`。带键请求由 HTTP 执行器开启外层事务时，服务的内层工作单元只参加该事务；无键请求则由服务自己提交。`Order` 保存 SKU、数量、创建时间及创建者的 issuer/subject。按 ID 读取时，仓储同时匹配创建者，避免其他已认证用户凭订单 ID 读取订单。此示例没有产品目录、库存或支付规则。

SelfManaged 和 Wolverine 各有一条 `AddOrders` 迁移并更新各自的模型快照。两套迁移历史中，`AddOrders` 都排在 `AddHttpIdempotency` 后面：标准完整迁移即使关闭 HTTP 幂等，也会创建幂等表；关闭开关只表示运行时不会使用该表。部署前应审查所选提供者的迁移脚本。

## HTTP 幂等记录

两种提供者共用 `ReferenceDbContext` 的业务映射，但使用不同的 EF 迁移历史。幂等记录必须和用户档案、出站消息参加同一个数据库事务；因此 `reference.HttpIdempotency` 在共享业务模型中注册，并在 SelfManaged 与 Wolverine 各有一条新增迁移。只更新其中一套迁移，会导致切换到另一提供者后运行模型与数据库结构不一致。

新增表只保存操作身份、请求键和请求内容的哈希，以及有界响应快照；`(ScopeHash, KeyHash)` 是主键，过期时间有索引。占用键、业务写入和保存响应必须由同一个最外层工作单元提交。这里仅提供模型与迁移，不会在 Web 启动时执行 DDL。先审查独立迁移器导出的对应业务 SQL，再显式应用。详见[幂等模块说明](../../../MS.Microservice.Idempotency/README.md)。

更换消息提供者不会搬运幂等记录、在途消息或档案数据；原有的停止写入、排空并迁移数据边界仍然适用。若启用带幂等键的端点，先在目标数据库应用相应提供者的迁移，再开放该端点。后续还需安排到期记录清理，避免表无限增长。
