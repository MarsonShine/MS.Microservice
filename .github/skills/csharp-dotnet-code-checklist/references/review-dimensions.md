# Review Dimensions for C# / .NET 10 PRs

Use this file when you need a deeper checklist than the main `SKILL.md` provides. The point is not to mention every item every time; it is to help you spot high-signal risks quickly and explain them clearly.

## 1. Correctness and domain behavior

Look for:

- missing validation at the application boundary
- changed defaults that alter business behavior
- silent catches, success-shaped fallbacks, or dropped failure paths
- partial updates that can leave the system in an invalid state
- idempotency problems in retryable or externally triggered flows

High-signal examples:

- a bug fix changed the happy path but left rollback or cleanup logic inconsistent
- a new enum value is accepted by input models but not handled downstream
- a handler updates state before an external call and cannot recover safely on failure

## 2. API and contract design

Look for:

- controllers or endpoints exposing EF entities directly
- methods returning `null` where the surrounding design expects explicit results
- raw exceptions, stack traces, or infrastructure details leaking through API responses
- missing validation attributes, guard clauses, or typed failure results

High-signal examples:

- a minimal API route returns `Results.BadRequest(ex.ToString())`
- a service accepts `string?` but immediately dereferences it
- new public API behavior is incompatible with existing consumers

## 3. Async, concurrency, and cancellation

Look for:

- `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`
- `async void` outside true event handlers
- fire-and-forget tasks without ownership or error handling
- missing `CancellationToken` on database, HTTP, storage, or delay operations
- shared mutable state accessed across requests or worker iterations
- `Task.Run` added to hide synchronous work inside ASP.NET request handling

High-signal examples:

- a request handler blocks on an HTTP call and risks thread-pool starvation
- a background loop ignores shutdown cancellation and delays process exit
- a singleton caches mutable state that is updated without synchronization

## 4. Nullability and contracts

Look for:

- new `!` suppressions without clear proof
- APIs that ignore nullable reference types in a project with `Nullable` enabled
- missing nullability attributes on `Try*` patterns or guard helpers
- nullable values passed into JSON, logging, or mapping layers without checks

High-signal examples:

- `JsonSerializer.Deserialize<T>(...)!` used in production code without failure handling
- a new public property is marked non-nullable but is not initialized across all constructors
- `TryGet...` methods do not tell callers when an out value is guaranteed non-null

## 5. Resource lifetime and disposal

Look for:

- per-call or per-loop `new HttpClient()`
- missing `using` / `await using`
- streams, readers, timers, or linked token sources not disposed
- scoped services captured in singleton objects, callbacks, or background tasks
- `DbContext` reused across threads or long-lived caches

High-signal examples:

- a worker creates `HttpClient` on every poll iteration
- a service stores `DbContext` outside its intended request scope
- an `IAsyncDisposable` dependency is used without `await using`

## 6. DI, configuration, and options

Look for:

- injecting `IConfiguration` deep into business logic instead of a typed options object
- magic string lookups for critical configuration values
- singleton services depending on scoped services
- missing options validation for required settings
- service locator patterns through `IServiceProvider`

High-signal examples:

- a singleton uses a scoped repository
- configuration values are parsed manually in multiple locations
- critical external endpoint settings can be missing and fail only at runtime

## 7. EF Core and data access

Look for:

- `ToList()` or `ToListAsync()` before filtering, paging, or projection
- missing `AsNoTracking()` on clearly read-only queries
- `Include()` graphs larger than needed
- N+1 query patterns
- `SaveChanges()` inside loops
- missing transactions where multiple writes must succeed together
- omitted `CancellationToken` on async database work

High-signal examples:

- all rows are loaded into memory and filtered afterward
- writes are persisted before audit or outbox work without a consistency story
- a query returns full aggregates when only a small DTO is needed

## 8. ASP.NET Core web concerns

Look for:

- auth or authorization gaps
- missing `ProblemDetails`, typed results, or consistent failure contracts
- unbounded list endpoints without pagination or streaming
- sync or blocking work on request paths
- weak logging around failures and retries
- direct `HttpContext` dependencies in layers that should stay framework-agnostic

High-signal examples:

- a new endpoint reads external services with `.Result`
- a route exposes internal exception messages to clients
- the request pipeline lacks validation before command execution

## 9. Security and secrets

Look for:

- secrets or credentials committed into code or default config
- interpolated SQL or unsafe raw SQL
- user-controlled file paths, URLs, or identifiers without validation
- insufficient ownership or tenant checks
- PII or secrets written to logs
- overly broad exception data returned to callers

High-signal examples:

- access checks rely on client-provided IDs without server-side ownership validation
- a logging statement records tokens, emails, or raw payloads
- raw SQL is built with string interpolation instead of safe parameters

## 10. Performance and allocation

Look for:

- repeated allocations or serializer setup in hot paths
- `Count() > 0` instead of `Any()` when the query cost matters
- materialization before filtering
- regex or parsing objects recreated per request
- unbounded parallelism or fan-out
- reflection-heavy patterns in projects signaling trimming or NativeAOT concerns

High-signal examples:

- repeated `JsonSerializerOptions` construction inside request handlers
- large collections returned without paging
- CPU-heavy mapping or LINQ chains inside high-volume endpoints

## 11. Diagnostics, resilience, and operations

Look for:

- swallowed exceptions
- no timeout, retry, or circuit-breaking strategy around remote calls where one is expected
- retry logic that ignores idempotency
- background jobs that cannot stop promptly
- logs that are unstructured or lack enough context to diagnose failures

High-signal examples:

- `catch (Exception)` that logs and continues without surfacing failure
- remote calls without timeout or cancellation
- failure paths with no correlation or structured fields in logs

## 12. Tests and change safety

Look for:

- behavior changes without tests
- tests that cover only the happy path
- missing regression coverage for bug fixes
- time-dependent tests that could use `TimeProvider`
- tests coupled to internal implementation instead of behavior

High-signal examples:

- a bug fix changes branching logic but no test captures the bug
- auth or serialization changes ship without integration coverage
- retry or cancellation behavior changes with no dedicated tests

## .NET 10 alignment notes

Use these as tie-breakers, not rigid law:

- prefer analyzers and platform defaults over local reinvention
- prefer `System.Text.Json`, `ProblemDetails`, `IOptions<T>`, `CancellationToken`, and `await using` when they improve correctness or readability
- consider trimming and `NativeAOT` friendliness if the project clearly targets them
- use modern `C#` features only when they reduce noise or make the contract clearer

## Do not over-report

Avoid low-value comments unless the user explicitly asked for exhaustive review:

- formatter-only differences
- naming debates that do not affect meaning
- "could be cleaner" feedback without a concrete maintenance cost
- speculative optimizations outside likely hot paths
- framework migrations unrelated to the change under review
