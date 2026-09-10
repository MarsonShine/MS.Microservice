# Self-managed EF Core messaging

The application owns the business DbContext and its migrations. Add `modelBuilder.AddSelfManagedMessaging()` to that context's model; the component does not depend on Activation, User, Role, or Log entities.

Use one scoped `SelfManagedUnitOfWork<TContext>` for both `IUnitOfWork` and `IIntegrationEventPublisher`. Business operations run through `ExecuteAsync`; `EnqueueAsync` captures the serialized payload in that operation. The outer operation commits business data and Outbox rows in one database transaction. Nested operations join the transaction and a nested failure makes it rollback-only even if application code catches the exception.

The unit of work cannot join an independently opened transaction and must not be used concurrently. Each consumer needs its own service scope. Business handlers must not manually save or commit the context. On failure the tracked changes and pending messages are discarded; retry the operation with stable event identities.

Published messages and completed receipts have configurable retention. Unfinished and dead-letter records must not be removed by normal cleanup. Transport delivery remains at least once; consumers protect business effects with the Inbox and any permanent business uniqueness requirements.

Failed-message operations expose metadata rather than payloads. Inbox replay resets the failed receipt and schedules its original event atomically. Replaying an event may redeliver to other subscriptions; their completed receipts prevent repeated effects within the retention window. An already completed receipt cannot be replayed. Invalid JSON or an unsupported contract must be repaired through an explicit migration or application update before replay is accepted. Replay never edits the payload or creates a new logical event Id.
