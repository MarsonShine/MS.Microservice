## MS.Microservice.Swagger

Reusable Swagger and OpenAPI integration for ASP.NET Core services.

### What This Package Solves

This package extracts Swagger registration and Swagger UI wiring into a standalone reusable component.

Compared with the old in-app implementation, this version fixes several design problems:

- It no longer reads `IConfiguration` inside `UsePlatformSwagger()`.
- It no longer hardcodes `Assembly.GetExecutingAssembly()` for XML comments.
- It no longer hardcodes a custom JavaScript path.
- SwaggerGen configuration is moved into a dedicated configurator class, which makes it testable.
- Route template and JSON endpoint generation are isolated into a small helper with unit tests.

### Packages

```bash
dotnet add package MS.Microservice.Swagger
```

### Basic Usage

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformSwagger(options =>
{
    options.IsEnabled = true;
    options.DocumentTitle = "Order API";
    options.DocumentVersion = "v1";
    options.DocumentName = "v1";
    options.Name = "Order API Docs";
    options.RoutePrefix = "swagger";
    options.EnabledSecurity = true;
    options.IsAuth = true;
    options.SwaggerXmlFile = "MyService.xml";
});

var app = builder.Build();
app.UsePlatformSwagger();
app.Run();
```

For a standalone sample file, see `samples/basic-usage.md` in this package.

### appsettings Example

See `appsettings.swagger.sample.json` in this package.

### Route Behavior

- `RoutePrefix = null` -> UI route is `/swagger`, JSON route is `/swagger/{documentName}/swagger.json`
- `RoutePrefix = "docs"` -> UI route is `/docs`, JSON route is `/docs/{documentName}/swagger.json`
- `RoutePrefix = ""` -> UI route is `/`, JSON route is `/{documentName}/swagger.json`

### Security

When `EnabledSecurity = true`, the package registers a bearer security definition in SwaggerGen.

### XML Comments

If `SwaggerXmlFile` is set, the package tries to load that file from `AppContext.BaseDirectory` unless you pass an absolute path.
If `SwaggerXmlFile` is omitted, the package tries to load `{EntryAssemblyName}.xml` from `AppContext.BaseDirectory`.
