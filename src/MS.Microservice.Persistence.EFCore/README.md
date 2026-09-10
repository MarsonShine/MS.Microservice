# EF Core query components

This package contains reusable specification/include evaluation and soft-delete query helpers. It depends on Core and EF Core relational APIs, not on sample domain models, a host, a broker, or a specific database context.

Applications own their DbContext, model mappings and migrations. The old Activation identity/log database is maintained under `samples/Lab/MS.Microservice.Lab.Persistence`; the production reference has its own provider-specific contexts under `samples/Reference`.

Copy this project with Core and Domain.Primitives, or consume their packages together. Choose and configure the database provider in the application.

AuditTimestampExtensions.UpdateAuditTimestamps(context, timeProvider) 可在保存钩子中显式调用。
新增实体设置其实现的 ICreatedAt / IUpdatedAt；修改实体仅设置 IUpdatedAt；未修改或删除实体
不变。一次调用使用同一个 UTC 时刻。它不提交事务，也不改动业务领域事件。
