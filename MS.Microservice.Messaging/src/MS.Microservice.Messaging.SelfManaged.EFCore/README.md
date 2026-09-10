# Self-managed EF Core messaging

The application owns the business DbContext and its migrations. Add `modelBuilder.AddSelfManagedMessaging()` to that context's model; the component does not depend on Activation, User, Role, or Log entities.

Use one scoped `SelfManagedUnitOfWork<TContext>` for both `IUnitOfWork` and `IIntegrationEventPublisher`. Business operations run through `ExecuteAsync`; `EnqueueAsync` captures the serialized payload in that operation. The outer operation commits business data and Outbox rows in one database transaction. Nested operations join the transaction and a nested failure makes it rollback-only even if application code catches the exception.

The unit of work cannot join an independently opened transaction and must not be used concurrently. Each consumer needs its own service scope. Business handlers must not manually save or commit the context. On failure the tracked changes and pending messages are discarded; retry the operation with stable event identities.

Published messages and completed receipts have configurable retention. Unfinished and dead-letter records must not be removed by normal cleanup. Transport delivery remains at least once; consumers protect business effects with the Inbox and any permanent business uniqueness requirements.

Failed-message operations expose metadata rather than payloads. Inbox replay resets the failed receipt and schedules its original event atomically. Replaying an event may redeliver to other subscriptions; their completed receipts prevent repeated effects within the retention window. An already completed receipt cannot be replayed. Invalid JSON or an unsupported contract must be repaired through an explicit migration or application update before replay is accepted. Replay never edits the payload or creates a new logical event Id.

Event occurrence time is stored as .NET UTC ticks to preserve its exact serialized identity across database providers. PostgreSQL timestamp precision is one microsecond, so ordinary timestamp storage would lose sub-microsecond digits. Operational times such as lease expiry remain database timestamps and are not part of the immutable event payload.

DiagnosticsInterval 默认 30 秒，控制后台积压采样频率。未完成及死信数量通过
messaging.stored_messages 输出；消费结果仅在事务提交之后计为 consumed。
Busy 和重复投递分别记录，不计作业务失败。启用观测导出器见 Observability README。
