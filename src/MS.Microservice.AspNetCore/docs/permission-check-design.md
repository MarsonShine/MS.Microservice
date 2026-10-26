# Permission-check implementation

Host registration and claim mappings are described in the [README](../README.md#external-jwt-validation). The implementation is [ExternalIdentity.cs](../ExternalIdentity.cs).

`HasPermission` scans claim values with `ReadOnlySpan<char>` and compares whole space-delimited tokens using ordinal case sensitivity. It retains `ClaimsPrincipal.FindAll`, preserving claim-type matching and enumeration across identities. A direct identity walk or parsed-scope cache would require separate semantic and workload evidence.

The subject requirement collects distinct subject values with ordinal comparison and accepts exactly one non-whitespace value. Missing, empty or conflicting subjects fail protected policies. JWT validation checks issuer, audience, lifetime and signing key; the component issues no credentials.

`AddExternalJwtIdentity` registers JWT validation and subject-based default/fallback policies. Hosts own named application policies and use `RequireExternalScope` to include authentication, subject validation and an explicit scope requirement together. Pure JWT registration does not inspect permission settings. Reference binds one identity options instance and registers `Manage` and `MessagingOperations` in its composition root.

The original `AddExternalIdentity` entry point is retained in [the compatibility adapter](../ExternalIdentity.Compatibility.cs). It still validates all legacy settings and registers both original policies. Their permission and claim mappings are read from the registered options during authorization, preserving the original closure behavior. `HasPermission` remains the common matching implementation for both paths.

## Measurement boundary

The [microbenchmark](../../../benchmarks/MS.Microservice.AspNetCore.Benchmarks/Program.cs) measures the public permission method on prebuilt principals, not HTTP throughput. The earlier implementation used `FindAll`, LINQ and `Split`; the revised scanner avoids token arrays and substrings. The comparison baseline was commit `7b1c78a`.

Median values from five rounds of 200,000 calls, Windows x64, .NET 10.0.12 Release:

| Claims | Earlier | Scanner | Allocation earlier | Scanner allocation |
| --- | ---: | ---: | ---: | ---: |
| One, match | 342.8 ns/op | 259.9 ns/op | 344 B/op | 216 B/op |
| Two, last matches | 620.3 ns/op | 315.4 ns/op | 592 B/op | 216 B/op |
| Two, no match | 313.3 ns/op | 130.3 ns/op | 584 B/op | 216 B/op |

Reproduce with `dotnet run --project benchmarks/MS.Microservice.AspNetCore.Benchmarks/MS.Microservice.AspNetCore.Benchmarks.csproj -c Release`. Remaining allocation includes `FindAll`; its exact sources were not profiled. These measurements do not justify changing identity enumeration or caching claims without production evidence.
