# ASP.NET Core host components

Provides HTTP error handling, trace IDs, proxy/CORS configuration, optional rate limits, request deadlines, health checks, external JWT validation and upload screening.

| Capability | Entry point | Reference |
| --- | --- | --- |
| HTTP defaults | `AddPlatformHttp` / `UsePlatformHttp` | ProblemDetails and trace-correlated errors |
| Rate limits | `AddPlatformRateLimiting` / `UsePlatformRateLimiting` | [Policies and limits](docs/rate-limiting.md) |
| Request deadlines | `AddPlatformRequestTimeouts` / `UsePlatformRequestTimeouts` | [Cancellation behavior](docs/request-timeouts.md) |
| Health checks | `AddPlatformHealthChecks` / `MapPlatformHealthChecks` | [Liveness and readiness](docs/health-checks.md) |
| Database readiness | `AddDbConnectionCheck` | [Connection probe](docs/db-health-checks.md) |
| Application errors | `ApplicationErrorResults.ToProblem` | [Error-code mapping](docs/application-errors.md) |
| Request validation | .NET 10 generated validation | [Binding limitations](docs/minimal-api-validation.md) |
| Upload screening | `FileUploadValidator` | [Implementation and tests](docs/uploads.md) |
| External identity | `AddExternalIdentity` | [Permission semantics](docs/permission-check-design.md) |

## Host setup

```csharp
using MS.Microservice.AspNetCore;
using MS.Microservice.AspNetCore.Uploads;

var builder = ServiceHost.CreateBuilder(args);
builder.Services.AddPlatformHttp(builder.Configuration);
builder.Services.AddScoped<FileUploadValidator>();
builder.Services.AddControllers();
var app = builder.Build();
app.UsePlatformHttp();
app.MapControllers();
app.Run();
```

| Configuration | Meaning |
| --- | --- |
| Application settings | Loaded from the deployed application directory |
| `Http:KnownProxies` | Explicit trusted proxy IP addresses |
| `Cors:Origins` | Origin allowlist; an empty list does not grant cross-origin access |
| Optional policies | Hosts supply rate-limit and timeout policies, then enable their middleware |

Unhandled 500 responses hide internal details. Default exception logs contain metadata rather than request bodies or exception messages. Readiness probes use the `ready` tag; explicitly allow anonymous access only to endpoints intended to be public.

## External JWT validation

Configure `Authentication:Authority` and `Authentication:Audience`, then register and enable authentication before protected endpoints:

```csharp
builder.Services.AddExternalIdentity(builder.Configuration, builder.Environment);
// After app.UsePlatformHttp():
app.UseAuthentication();
app.UseAuthorization();
```

| Parameter / policy | Default or requirement |
| --- | --- |
| `Authority`, `Audience` | Required; production Authority uses HTTPS |
| `SubjectClaimType` | `sub`; exactly one distinct, non-empty subject is required |
| `RoleClaimType`, `PermissionClaimType` | `roles`, `scope` |
| `Manage` policy | Whole scope token `profiles.manage` |
| `MessagingOperations` policy | Whole scope token `messaging.manage` |
| Permission matching | Ordinal and case-sensitive; claims can contain space-separated scopes |

The component validates external tokens and does not issue tokens or store passwords. Its fallback policy protects endpoints unless they explicitly allow anonymous access. Development may use an HTTP identity provider.

## Upload validation

Use this action in a host configured with MVC and authentication as above:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;

[ApiController, Route("uploads"), Authorize]
public sealed class UploadsController(FileUploadValidator validator) : ControllerBase
{
    [HttpPost("image"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ValidateImage(IFormFile file, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(file, UploadFileKind.Image,
            maximumBytes: 5 * 1024 * 1024, cancellationToken: cancellationToken);
        if (!result.IsValid) return StatusCode(result.StatusCode, result.Message);
        return Ok(new { result.Extension });
    }
}
```

| Input / result | Rule |
| --- | --- |
| `kind` | `Image` or `Excel` |
| `maximumBytes` | Host-selected positive per-file limit; the exact boundary is allowed |
| Missing / empty / oversized | 400 / 400 / 413 |
| Type or signature mismatch | 415 |
| Success | 200, empty message and lowercase extension |
| Stream | Validator disposes the stream it opens; reopen the file for later processing |

| Kind | Extension | Content-Type |
| --- | --- | --- |
| Image | `.jpg`, `.jpeg` | `image/jpeg` |
| Image | `.png` | `image/png` |
| Image | `.webp` | `image/webp` |
| Excel | `.xls` | `application/vnd.ms-excel` |
| Excel | `.xlsx` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |

Extension and MIME comparisons ignore case and must agree. This is a header check, not complete image/workbook validation: an ordinary ZIP can pass the XLSX signature. Later processors must decode or parse the content. The size check trusts `IFormFile.Length`; request-body limits must separately allow multipart overhead while bounding the whole request. The host owns authorization, storage paths, file names and retention. Cancellation and I/O errors propagate.

The separate [MVC encryption adapter](../MS.Microservice.AspNetCore.Encryption/README.md) handles explicitly registered request envelopes and is an optional dependency.
