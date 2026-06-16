# Framework Optimization Roadmap

This document tracks the review items that are intentionally staged instead of completed as a large breaking rewrite.

## P3: Split Infrastructure

Current state:

- `src/MS.Microservice.Infrastructure` still contains EF Core, SqlSugar, event sourcing, telemetry, Excel, audio, cache, health checks, and Wolverine helpers.
- The Web Host consumes the module through `services.AddInfrastructure(configuration)`.

This change set:

- Keeps `AddInfrastructure(configuration)` as the stable facade.
- Adds startup validation for `ConnectionStrings:ActivationConnection` and `FzPlatformDbContextSettings`.
- Documents the intended split order.

Target split order:

1. `MS.Microservice.Persistence.EFCore`
2. `MS.Microservice.Persistence.SqlSugar`
3. `MS.Microservice.Observability`
4. `MS.Microservice.Excel`
5. `MS.Microservice.Audio`
6. `MS.Microservice.Messaging.Wolverine`

Expected extension methods:

```csharp
services.AddMicroserviceEfCorePersistence(configuration);
services.AddMicroserviceSqlSugarPersistence(configuration);
services.AddMicroserviceObservability(configuration);
services.AddMicroserviceExcel();
services.AddMicroserviceAudio();
services.AddMicroserviceWolverineMessaging(configuration);
```

Reason not completed in this change:

- The current Infrastructure project owns runtime registrations and tests across multiple concerns. Splitting it now would require moving project references, namespaces, test projects, and package boundaries at once.
- The safer staged approach is to first protect boundaries with architecture tests, then split one submodule per change.

## P4: Domain Events, Outbox, and Inbox

Current state:

- Entities expose in-memory `DomainEvents`.
- `ActivationDbContext.SaveEntitiesAsync` dispatches domain events through Wolverine before `SaveChangesAsync`.

This change set:

- Makes `DomainEvents` non-null and read-only.
- Removes public `Id` mutation and HashCode caching risk.
- Keeps existing Wolverine dispatch behavior compatible.

Target production flow:

```text
Aggregate produces DomainEvent
Application Service calls Repository
UnitOfWork SaveChanges
Collect DomainEvents
Write Outbox in the same transaction
Background worker publishes IntegrationEvent
Consumer writes Inbox receipt
Retry / dead-letter / trace correlation
```

Planned abstractions:

```csharp
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default);
}
```

Planned persistence records:

- `OutboxMessage`
- `InboxMessage`

Reason not completed in this change:

- Outbox must be implemented inside the same database transaction as the business write. The repository currently supports both EF Core and SqlSugar concerns inside one Infrastructure project, so the first safe step is to split persistence modules or choose a primary transaction boundary.

## P5: Samples and Documentation

Current state:

- Module-level docs exist for Logging, AI, Swagger, and EventBus.
- Root README now documents the current repository state.

Planned samples:

1. `samples/Sample.TodoService`
   - Minimal CRUD
   - EF Core
   - Swagger
   - Logging

2. `samples/Sample.OrderService`
   - DDD aggregate
   - DomainEvent
   - Outbox
   - EventBus

3. `samples/Sample.AIService`
   - Chat
   - TTS
   - ASR
   - Provider switching
   - Usage reporting

Reason not completed in this change:

- Samples should be built after the Infrastructure split and Outbox contract stabilize; otherwise samples become another historical compatibility burden.

## AI Production Readiness

Implemented:

- `HttpClientFactory`
- Provider and model timeout
- Exponential retry with Retry-After support
- Provider concurrency limit
- Streaming cancellation
- Token usage mapping
- Provider-neutral exception hierarchy
- Activity tracing
- Provider capability validation

Next steps:

- `IAIRateLimiter` for token/request quotas beyond local concurrency.
- `IAICircuitBreaker` abstraction or documented integration with a resilience library.
- `IAILogSanitizer` for prompt/response redaction before logs or traces.
- Secret Provider integration so API keys do not have to bind directly from plain configuration.
- Payload limit options for audio/image requests.
- Cost accounting hooks by provider/model/usage.

Reason not completed in this change:

- The existing retry/timeout/concurrency behavior is already in place and covered by tests. The remaining items need product-specific policies and should be added as extension points without forcing a new dependency on every consumer.
