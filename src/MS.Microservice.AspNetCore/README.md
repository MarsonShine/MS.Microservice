# ASP.NET Core host components

`ServiceHost.CreateBuilder(args)` loads deployed settings from the application directory. `AddPlatformHttp` / `UsePlatformHttp` supply trace-correlated ProblemDetails, metadata-only exception logging, explicit proxy trust and an opt-in CORS origin allowlist.

`AddPlatformRateLimiting` / `UsePlatformRateLimiting` optionally add ASP.NET Core's built-in rate limiter. Hosts can attach named policies to endpoints or configure a global limiter. The Reference host applies one fixed-window policy to its API routes while leaving health routes outside that policy. Configuration, middleware order and operational limits are explained in [HTTP 入口限流](docs/rate-limiting.md).

`AddPlatformRequestTimeouts` / `UsePlatformRequestTimeouts` optionally add ASP.NET Core request deadlines. The Reference host can apply one policy to its API routes while keeping health routes outside it. Cancellation behavior and configuration are explained in [HTTP 请求超时](docs/request-timeouts.md).

`AddPlatformHealthChecks` / `MapPlatformHealthChecks` expose liveness and readiness endpoints backed by ASP.NET Core health checks. Hosts register dependency probes with the `ready` tag; readiness stops accepting traffic when the host begins stopping. The [health check design](docs/health-checks.md) explains status handling and the Reference host's dependency probes.

`AddDbConnectionCheck` is an optional database readiness probe for an explicitly supplied `DbConnection` factory. See [database readiness checks](docs/db-health-checks.md) for its behavior and scope.

`ApplicationErrorResults.ToProblem` maps stable application error codes to public ProblemDetails and hides unknown 500 details. The Reference host also uses .NET 10 source-generated validation for HTTP query ranges while keeping domain rules in its application layer. See [应用错误映射到 HTTP](docs/application-errors.md) for the mapping and the observed `[AsParameters]` binding limitation.

The separate [ASP.NET Core encryption adapter](../MS.Microservice.AspNetCore.Encryption/README.md) optionally binds versioned RSA-OAEP + AES-GCM request envelopes to explicitly registered MVC DTOs. The base host package does not reference this MVC adapter or Core cryptology.

`AddExternalIdentity` validates JWTs through the host's OIDC Authority and Audience. It never issues tokens or maintains passwords. Production metadata requires HTTPS. Configure subject, role and permission claim names under `Authentication`; the reference policies use case-sensitive OAuth scopes `profiles.manage` and `messaging.manage`. A missing or empty subject cannot access protected resources.

Hosts must call authentication and authorization middleware and explicitly allow anonymous access only where intended (for example, liveness). Development may use an HTTP identity provider. TestServer tests use static test signing configuration and do not require external identity services.

## Permission check cost

The `Manage` and `MessagingOperations` policies call `HasPermission` during authorization. Previously it called `FindAll`, then used LINQ and `string.Split(' ', RemoveEmptyEntries)` for every permission claim. Splitting creates an array and may create substrings even when the requested permission is absent. A claim may contain several space-separated scopes, and permissions must match a whole token with ordinal case sensitivity.

`HasPermission` now scans each claim value with `ReadOnlySpan<char>`, without creating tokens. It still uses `ClaimsPrincipal.FindAll` so claim type matching and searches across identities retain their existing semantics. No reflection or dynamic code was added. A direct walk over identities could remove more allocations, but that would need separate semantic and workload evidence.

The included [microbenchmark](../../benchmarks/MS.Microservice.AspNetCore.Benchmarks/Program.cs) calls the public method on prebuilt principals. The "before" run used commit `7b1c78a`; the "after" run used this change. Results below are median values from five rounds of 200,000 calls per case, in a Release build on Windows x64 with .NET 10.0.12. They measure the permission method, not end-to-end HTTP throughput.

| Claim scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| One claim, match | 342.8 ns/op | 259.9 ns/op | 344 B/op | 216 B/op |
| Two claims, match in last claim | 620.3 ns/op | 315.4 ns/op | 592 B/op | 216 B/op |
| Two claims, no match | 313.3 ns/op | 130.3 ns/op | 584 B/op | 216 B/op |

Run with `dotnet run --project benchmarks/MS.Microservice.AspNetCore.Benchmarks/MS.Microservice.AspNetCore.Benchmarks.csproj -c Release`. The remaining allocation includes the `FindAll` path; its exact sources were not profiled. If authorization becomes an observed production bottleneck, profile real requests before changing claim enumeration or caching parsed scopes.
