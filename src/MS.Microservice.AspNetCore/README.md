# ASP.NET Core host components

`ServiceHost.CreateBuilder(args)` loads deployed settings from the application directory. `AddPlatformHttp` / `UsePlatformHttp` supply trace-correlated ProblemDetails, metadata-only exception logging, explicit proxy trust and an opt-in CORS origin allowlist.

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
