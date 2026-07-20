# Framework Optimization Roadmap

This document tracks the review items that are intentionally staged instead of completed as a large breaking rewrite.

## P3: Split Infrastructure

Current state:

- `src/MS.Microservice.Persistence.EFCore` now owns Activation EF Core persistence: `ActivationDbContext`, EF Core entity configurations, EF Core repositories, EF Core query/soft-delete helpers, DbContext settings, and EF Core persistence registration.
- `src/MS.Microservice.Persistence.SqlSugar` now owns SqlSugar persistence: SqlSugar clients/scopes, generic SqlSugar repository bases, `UserDemoRepository`, options, converters, sharding helpers, query extensions, and SqlSugar persistence registration.
- `src/MS.Microservice.Infrastructure` no longer owns the main Activation EF Core or SqlSugar implementations. It still owns event sourcing, telemetry, Excel, audio, cache, health checks, and Wolverine helpers.
- The Web Host still consumes the stable facade through `services.AddInfrastructure(configuration)`.
- Infrastructure temporarily references the new Persistence projects so the existing Web startup facade remains stable during the staged split.

This change set:

- [x] Created `MS.Microservice.Persistence.EFCore` and moved Activation EF Core DbContext, mappings, repositories, options, query helpers, transaction helpers, and EF Core registration into it.
- [x] Created `MS.Microservice.Persistence.SqlSugar` and moved SqlSugar clients/scopes, repositories, options, converters, sharding helpers, query helpers, and SqlSugar registration into it.
- [x] Added `services.AddMicroserviceEfCorePersistence(configuration)`.
- [x] Added `services.AddMicroserviceSqlSugarPersistence(configuration)`.
- [x] Kept `services.AddInfrastructure(configuration)` as the stable facade and changed it to call both new persistence registration methods.
- [x] Moved `SqlSugarCore`, EF Core design/tools/relational package ownership to the new Persistence projects where applicable.
- [x] Kept EF Core and Npgsql packages in Infrastructure only because the unchanged Event Sourcing module still uses `EventStoreDbContext` and `UseNpgsql`.
- [x] Updated Web Host references for `ActivationDbContext` and EF Core repositories to the new EFCore persistence project.
- [x] Added registration tests for EF Core persistence, SqlSugar persistence, and the Infrastructure facade.
- [x] Updated architecture tests for the new Persistence projects and Domain dependency rules.

Target split order:

1. `MS.Microservice.Persistence.EFCore` - completed in this change set.
2. `MS.Microservice.Persistence.SqlSugar` - completed in this change set.
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

- EF Core and SqlSugar persistence have been split in this change set.
- Observability, Excel, Audio, Cache, Health Checks, Wolverine Messaging, Event Sourcing, Logging, AI, Swagger, and EventBus remain out of scope for this change to avoid mixing persistence boundaries with unrelated runtime behavior.
- Infrastructure still references the new Persistence projects as a transition facade so existing Web startup code can keep calling `AddInfrastructure(configuration)`.
- Event Sourcing still lives in Infrastructure and still uses EF Core/Npgsql. Those package references cannot be removed from Infrastructure until Event Sourcing is split or redesigned.

Remaining TODO:

- [ ] Decide whether Web hosts should continue to call `AddInfrastructure(configuration)` long term or switch to direct module registrations once more Infrastructure slices are split.
- [ ] Split Event Sourcing separately before removing the remaining EF Core/Npgsql package references from Infrastructure.
- [ ] Revisit the legacy Wolverine `DispatchDomainEventsAsync(ActivationDbContext ctx)` helper; it now depends on the EFCore persistence project only for compatibility.
- [ ] Reassess SqlSugar client lifetime: `AddSqlSugarClient<TSqlSugarClient>` still builds one client instance during registration and returns it from a scoped registration, matching the previous behavior but worth reviewing.
- [ ] Add EF Core mappings/migrations for `OutboxMessage` and `InboxMessage` in `MS.Microservice.Persistence.EFCore`.
- [ ] Add SqlSugar table mapping/index compatibility for `OutboxMessage` and `InboxMessage` in `MS.Microservice.Persistence.SqlSugar`.
- [ ] Implement Outbox writes and Inbox receipts inside the same ORM transaction boundary for both EF Core and SqlSugar.
- [ ] Continue the next split candidates only in separate changes: Observability, Excel, Audio, Wolverine Messaging, and any later Cache/Health Checks decisions.

New findings:

- `ActivationDbContext` previously exposed a Wolverine `IMessageBus` constructor and directly created `WolverineDomainEventDispatcher`. The EFCore persistence project now depends only on the Domain `IDomainEventDispatcher`; Infrastructure registers the Wolverine-backed dispatcher before calling the persistence facade.
- Infrastructure still contains EF Core usage through Event Sourcing (`EventStoreDbContext`, event store repositories, projection stores, and migration host helpers), so EF Core/Npgsql cannot be fully removed from Infrastructure yet.
- SqlSugar options are now bound from the `SqlSugarOptions` and `ShardingOptions` sections in the SqlSugar persistence registration. The previous registration configured options from the root configuration while separately reading the sections for immediate setup.
- The SqlSugar sharding factory depends on `IOptions<ShardingOptions>.Value.Count`; empty or missing sharding connection strings still need a product decision before runtime hardening.

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
- P3 dependency: the EF Core / SqlSugar persistence split is now complete, so the next Outbox / Inbox slice can add ORM-specific mappings and transaction boundaries in `MS.Microservice.Persistence.EFCore` and `MS.Microservice.Persistence.SqlSugar`.

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
- Provider-neutral non-streaming `Text`, `JsonObject`, and strict `JsonSchema` response formats for OpenAI-compatible chat providers.
- `MS.Microservice.AI.QuestionGeneration` with pluggable question definitions, deterministic validation, independent review, bounded repair, Attempt observers, budgets, exact duplicate detection, and strict structured-output fallback.

Completed next steps:

- [x] `IAIRateLimiter`
- [x] `IAICircuitBreaker`
- [x] `IAILogSanitizer`
- [x] Secret Provider integration
- [x] Payload limit options for audio/image requests, plus chat/streaming text limits
- [x] Cost accounting hooks by provider/model/usage
- [x] Provider-neutral JSON Object and JSON Schema chat response formats
- [x] Business-neutral QuestionGeneration Harness and AI gateway adapter

Remaining production TODO:

- [ ] Replace or decorate the in-memory rate limiter with a distributed quota implementation for multi-instance deployments.
- [ ] Replace or decorate the in-memory circuit breaker with durable/shared state if provider protection must span multiple service instances.
- [ ] Add cloud-specific secret-provider adapters only in consuming services or optional packages; the framework remains provider-neutral.
- [ ] Connect `IAICostReporter` to a real metrics, billing, or audit sink in application hosts.
- [ ] Decide whether prompt/response tracing should be fully disabled by default in host observability; the sanitizer is available, but host logging policy remains product-specific.
- [ ] Add an optional distributed QuestionGeneration Attempt Store and invocation-reservation implementation for multi-instance crash recovery.
- [ ] Add optional host packages for task scheduling, persistence, human review, and resource orchestration; these remain outside the reusable Harness.
- [ ] Build a versioned offline evaluation runner and application-owned golden datasets for question definitions.
