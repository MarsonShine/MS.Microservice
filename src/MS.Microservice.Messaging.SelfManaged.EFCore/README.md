# Self-managed EF Core messaging

The application owns the business DbContext and its migrations. Add `modelBuilder.AddSelfManagedMessaging()` to that context's model; the component does not depend on Activation, User, Role, or Log entities.

Use one scoped `SelfManagedUnitOfWork<TContext>` for both `IUnitOfWork` and `IIntegrationEventPublisher`. Business operations run through `ExecuteAsync`; `EnqueueAsync` captures the serialized payload in that operation. The outer operation commits business data and Outbox rows in one database transaction. Nested operations join the transaction and a nested failure makes it rollback-only even if application code catches the exception.

The unit of work cannot join an independently opened transaction and must not be used concurrently. Each consumer needs its own service scope. Business handlers must not manually save or commit the context. On failure the tracked changes and pending messages are discarded; retry the operation with stable event identities.

Published messages and completed receipts have configurable retention. Unfinished and dead-letter records must not be removed by normal cleanup. Transport delivery remains at least once; consumers protect business effects with the Inbox and any permanent business uniqueness requirements.
