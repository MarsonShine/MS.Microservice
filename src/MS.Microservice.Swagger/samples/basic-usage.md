## Basic Usage

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformSwagger(options =>
{
    options.DocumentTitle = "Orders API";
    options.DocumentVersion = "v1";
    options.DocumentName = "v1";
    options.Name = "Orders API Docs";
    options.RoutePrefix = "docs";
    options.EnabledSecurity = true;
    options.IsAuth = true;
    options.SwaggerXmlFile = "Orders.Api.xml";
});

var app = builder.Build();
app.UsePlatformSwagger();
app.Run();
```

If you prefer configuration-driven setup, keep the package sample file `appsettings.swagger.sample.json` next to your service configuration and map its values into `SwaggerOptions` before calling `AddPlatformSwagger`.
