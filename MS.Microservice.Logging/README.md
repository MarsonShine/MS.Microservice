# MS.Microservice.Logging

Logging 负责应用日志上下文与后端接入，不负责业务身份验证，也不替代 Trace/Metrics。

打开 [MS.Microservice.Logging.slnx](MS.Microservice.Logging.slnx)：
源码在 src、对应测试在 test，[设计说明](docs/design.md)在 docs。
在本目录执行 dotnet test MS.Microservice.Logging.slnx -c Release。

Core 不依赖 ASP.NET Core 或具体日志后端，AspNetCore 负责请求边界，
NLog/Serilog 是可选后端。这样 Worker 可以只使用 Core，Web 可以按需选择中间件和后端，
无需为切换日志后端修改业务代码。

以下保留各包的详细注册和配置说明。

---
## MS.Microservice.Logging

Provider-agnostic structured request logging for .NET, with first-class NLog and Serilog support.

### Packages

| Package | Purpose |
|---|---|
| `MS.Microservice.Logging.Core` | Ambient request log context, high-performance log extensions. No ASP.NET / NLog / Serilog dependency. |
| `MS.Microservice.Logging.AspNetCore` | ASP.NET Core middleware that captures headers, timing, and status into the ambient context. Optional — skip for Worker/Console hosts. |
| `MS.Microservice.Logging.NLog` | NLog provider with custom layout renderers (`${requestId}`, `${platformId}`, `${userflag}`, `${RequestDuration}`, …). Supports `nlog.config` and automatic fallback. |
| `MS.Microservice.Logging.Serilog` | Serilog provider with a `RequestLogContextEnricher` that pushes ambient context as structured properties. Supports `appsettings.json` and code configuration. |

### Architecture

```
┌─────────────────────────────────────────────────────┐
│                  Your Application                   │
├─────────────────────────────────────────────────────┤
│  MS.Microservice.Logging.AspNetCore                 │
│    - MsRequestLoggingMiddleware                     │
│    - Captures headers, timing, status               │
│    - Pushes RequestLogContext into AsyncLocal        │
├─────────────────────────────────────────────────────┤
│  MS.Microservice.Logging.Core                        │
│    - RequestLogContext (POCO)                        │
│    - RequestLogScope (AsyncLocal ambient context)    │
│    - LoggerExtensions (high-perf logging helpers)    │
├──────────────────┬──────────────────────────────────┤
│  NLog Provider   │  Serilog Provider                │
│  LayoutRenderers │  RequestLogContextEnricher         │
│  read from       │  reads from                       │
│  RequestLogScope │  RequestLogScope                   │
└──────────────────┴──────────────────────────────────┘
```

**Key design: provider-agnostic ambient context**

The middleware pushes a `RequestLogContext` onto an `AsyncLocal` stack. Both NLog layout renderers and the Serilog enricher read from `RequestLogScope.Current` — they don't know or care whether the data came from HTTP headers, gRPC metadata, or a message bus envelope. This keeps each provider focused on its single responsibility (rendering / enriching) and open for extension (new data sources don't require provider changes).

### Quick Start — ASP.NET Core + NLog

1. Install packages:

```bash
dotnet add package MS.Microservice.Logging.Core
dotnet add package MS.Microservice.Logging.AspNetCore
dotnet add package MS.Microservice.Logging.NLog
```

2. In `Program.cs`:

```csharp
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Logging.NLog;

var builder = WebApplication.CreateBuilder(args);

// Step 1 — register request logging middleware services
builder.Services.AddMsRequestLogging();

// Step 2 — configure NLog
builder.ConfigureMsNLog(options =>
{
    // options.ConfigurationFilePath = "nlog.config";   // default
    // options.MinimumLevel = LogLevel.Information;      // default
});

var app = builder.Build();

// Step 3 — add middleware (order: before endpoints, after auth if needed)
app.UseMsRequestLogging();

app.MapGet("/", () => "Hello");
app.Run();
```

3. Place `nlog.config` in your project root (set `CopyToOutputDirectory`). See the sample config in `src/MS.Microservice.Logging.NLog/nlog.sample.config`.

4. Send a request with headers:

```bash
curl -H "requestId: req-001" -H "platformId: mobile" -H "userflag: internal" http://localhost:5000/orders/42
```

NLog output includes `requestId=req-001 platformId=mobile userflag=internal dur=12ms`.

### Quick Start — Worker/Console + NLog (no ASP.NET)

```csharp
using MS.Microservice.Logging.Core;
using MS.Microservice.Logging.NLog;

var builder = Host.CreateApplicationBuilder(args);
builder.ConfigureMsNLog();

// Manually push context for background jobs
using (RequestLogScope.Push(new RequestLogContext
{
    RequestId = Guid.NewGuid().ToString("N"),
    Action = "process-order",
}))
{
    // All log events inside this block carry the context
}
```

### Quick Start — ASP.NET Core + Serilog

```csharp
using MS.Microservice.Logging.AspNetCore;
using MS.Microservice.Logging.Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMsRequestLogging();
builder.ConfigureMsSerilog(options =>
{
    // options.ReadFromConfiguration = true;  // default, reads Serilog section from appsettings.json
    // options.UseConsoleSink = true;         // default
});

var app = builder.Build();
app.UseMsRequestLogging();
app.MapGet("/", () => "Hello");
app.Run();
```

### Customizing Header Names

```csharp
builder.Services.AddMsRequestLogging(options =>
{
    options.RequestIdHeaderName = "x-request-id";
    options.PlatformIdHeaderName = "x-platform-id";
    options.UserFlagHeaderName = "x-user-flag";
    options.EmitCompletionLog = true; // set false to suppress "HTTP GET /path -> 200 in 5ms"
});
```

### Testing

The ambient context is designed for testability:

```csharp
// Unit test — no HttpContext needed
using (RequestLogScope.Push(new RequestLogContext
{
    RequestId = "test-001",
    ElapsedMilliseconds = 42,
}))
{
    var renderer = new RequestDurationLayoutRenderer();
    var sb = new StringBuilder();
    renderer.Append(sb, LogEventInfo.CreateNullEvent());
    sb.ToString().Should().Be("42ms");
}
```

From this module directory, test projects exist for all four packages under `test/`. Run with:

```bash
dotnet test test/MS.Microservice.Logging.Core.Tests
dotnet test test/MS.Microservice.Logging.AspNetCore.Tests
dotnet test test/MS.Microservice.Logging.NLog.Tests
dotnet test test/MS.Microservice.Logging.Serilog.Tests
```

### Package Dependencies (Consumer View)

| Host Type | Required Packages |
|---|---|
| ASP.NET Core | `Core` + `AspNetCore` + one of `NLog` / `Serilog` |
| Worker / Console | `Core` + one of `NLog` / `Serilog` (no `AspNetCore`) |
| Library / Shared | `Core` only (for `LoggerExtensions` and `RequestLogScope`) |
