# Domain.Primitives

这个小程序集提供通用实体、审计与领域事件相关契约，不包含业务模型、数据库或宿主注册。
Core 复用它，而业务 Domain 可以按需直接引用它。

单独存在的原因是避免“只需要一个小契约，却必须引用完整 Core 或基础设施”。
部分类型保留 MS.Microservice.Core.Domain.Entity 等历史命名空间；
程序集归属与命名空间不是同一概念，项目引用才是依赖依据。

这些接口只描述形状，不自动提供业务校验、保存、审计更新或消息传播。
例如 ICreatedAt/IUpdatedAt 是否在保存时更新，由应用接入的持久化策略决定；
领域事件需要显式映射，不能因为实现一个接口就自动跨进程广播。

[总体架构](../../docs/Architecture-Overview.md)说明它与 Core、模块和 Reference 的关系。
