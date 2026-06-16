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

- Entities expose in-memory `DomainEvents` through `IHasDomainEvents`.
- `ActivationDbContext.SaveEntitiesAsync` now saves EF changes before dispatching collected domain events, then clears events after successful dispatch.
- Existing Wolverine dispatch remains compatible through `WolverineDomainEventDispatcher` and the legacy `IMessageBus.DispatchDomainEventsAsync` extension.
- Domain contains provider-neutral Outbox / Inbox records that keep public setters for EF Core and SqlSugar materialization.

This change set:

- [x] Added `IHasDomainEvents`, `IDomainEventDispatcher`, and `IIntegrationEventPublisher` abstractions in the Domain layer.
- [x] Added `OutboxMessage` with status, retry count, max retry count, next/last attempt, last error, trace id, and correlation id metadata.
- [x] Added `InboxMessage` with receipt, consumer, deduplication key, duplicate count, processing status, trace id, and correlation id metadata.
- [x] Added no-op Domain dispatchers for design-time and tests.
- [x] Added Wolverine-backed Domain and Integration event implementations in Infrastructure.
- [x] Updated Infrastructure DI to register the new abstractions without splitting the Infrastructure project.
- [x] Updated `ActivationDbContext.SaveEntitiesAsync` from dispatch-before-save to save-before-dispatch.
- [x] Added tests for DomainEvents collection/clearing, dispatcher behavior, Outbox status transitions, Inbox deduplication, and `SaveEntitiesAsync` behavior.

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

- [x] `OutboxMessage`
- [x] `InboxMessage`

Remaining gap:

- [ ] Full transactional Outbox / Inbox persistence is not complete yet. The current implementation establishes the Domain model, interfaces, default no-op path, Wolverine-backed path, and tests, but does not write Outbox records in the same EF Core / SqlSugar transaction as the business data.
- Reason: the current repository still hosts EF Core and SqlSugar in the same Infrastructure module. A durable Outbox needs concrete table mappings, migration strategy, unique Inbox indexes, and one chosen unit-of-work transaction boundary per ORM.
- Current mitigation: `SaveEntitiesAsync` no longer dispatches before `SaveChangesAsync`, so failed persistence no longer publishes events. This is safer than the previous flow, but it is not atomic; if dispatch fails after save, retry still depends on the caller or future Outbox worker.
- Next TODO: add EF Core mapping and migration for `OutboxMessage` / `InboxMessage`, add SqlSugar mapping compatibility, write Outbox records inside the same transaction, add a background publisher with retry/dead-letter handling, and add Inbox unique-key enforcement.
- P3 dependency: not strictly required for the next thin vertical slice, but the full EF Core / SqlSugar split would reduce risk before productionizing both ORMs.

New findings:

- `CommitTransactionAsync` still calls `SaveChangesAsync` directly. Domain event dispatch for explicit transaction flows should be revisited when Outbox persistence is wired in, otherwise event dispatch timing can differ between `SaveEntitiesAsync` and manual transaction flows.

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
- `IAIRateLimiter` with a default in-memory fixed-window implementation and `AddAIRateLimiter(...)`.
- `IAICircuitBreaker` with a default in-memory circuit breaker and `AddAICircuitBreaker(...)`.
- `IAILogSanitizer` with default redaction for API keys, Authorization headers, Bearer tokens, and configurable sensitive fields such as prompt/response.
- Provider-neutral secret lookup through `IAISecretProvider`, `EnvironmentAISecretProvider`, `ConfigurationAISecretProvider`, and AI options post-configuration.
- Payload limit options for chat, streaming chat, TTS text, ASR audio, image prompt, image edit source image, and image edit mask.
- Cost accounting hooks through `IAICostReporter`, emitted from routing clients by provider/model/usage/duration/success/failure/category.
- Production readiness registrations exposed through `AddMicroserviceAI(...)`, `AddAIRateLimiter(...)`, `AddAICircuitBreaker(...)`, `AddAILogSanitizer(...)`, `AddAISecretProvider(...)`, `AddAIPayloadLimits(...)`, and `AddAICostAccounting(...)`.

Completed next steps:

- [x] `IAIRateLimiter`
- [x] `IAICircuitBreaker`
- [x] `IAILogSanitizer`
- [x] Secret Provider integration
- [x] Payload limit options for audio/image requests, plus chat/streaming text limits
- [x] Cost accounting hooks by provider/model/usage

Remaining production TODO:

- [ ] Replace or decorate the in-memory rate limiter with a distributed quota implementation for multi-instance deployments.
- [ ] Replace or decorate the in-memory circuit breaker with durable/shared state if provider protection must span multiple service instances.
- [ ] Add cloud-specific secret-provider adapters only in consuming services or optional packages; the framework remains provider-neutral.
- [ ] Connect `IAICostReporter` to a real metrics, billing, or audit sink in application hosts.
- [ ] Decide whether prompt/response tracing should be fully disabled by default in host observability; the sanitizer is available, but host logging policy remains product-specific.
