# MS.Microservice

MS.Microservice 是一个面向 .NET 10 的微服务基础框架仓库，当前主线包含 Web Host、DDD Domain/Core、Infrastructure、Swagger、EventBus、Logging 和 AI Provider 模块。

首页文档只描述当前可运行状态和接入方式；DDD、微服务、消息队列、Kubernetes 等长期资料保留在 `docs/`。

## 版本矩阵

| 项 | 当前值 |
| --- | --- |
| TargetFramework | `net10.0` |
| SDK | `10.0.x`，见 `global.json` |
| 主解决方案 | `MS.Microservice.slnx` |
| Web Host | `src/MS.Microservice.Web` |
| Docker Runtime | `mcr.microsoft.com/dotnet/aspnet:10.0` |

## 模块

| 模块 | 说明 |
| --- | --- |
| `src/MS.Microservice.Core` | 通用领域接口、函数式类型、规范模式、缓存、序列化、安全工具。 |
| `src/MS.Microservice.Domain` | 当前示例业务领域模型、聚合、领域服务和领域事件暂存。 |
| `src/MS.Microservice.Infrastructure` | EF Core、SqlSugar、事件溯源、健康检查、OpenTelemetry 等基础设施实现。 |
| `src/MS.Microservice.Web` | ASP.NET Core Host、API 入口、认证授权、Swagger、Wolverine 接入。 |
| `MS.Microservice.Logging` | Provider-agnostic request logging，支持 NLog 和 Serilog。 |
| `MS.Microservice.Swagger` | Swagger 注册与 UI 封装。 |
| `MS.Microservice.EventBus` | 事件总线抽象与内存订阅管理。 |
| `MS.Microservice.AI` | Provider-neutral AI Gateway，支持 OpenAI、DeepSeek、Qwen。 |

## 本地开发

```bash
dotnet restore
dotnet build
dotnet test
```

运行 Web Host：

```bash
dotnet run --project src/MS.Microservice.Web/MS.Microservice.Web.csproj
```

默认配置位于：

- `src/MS.Microservice.Web/appsettings.json`
- `src/MS.Microservice.Web/appsettings.Development.json`

关键配置启动期校验：

- `CorsOptions`
- `IdentityOptions:JwtBearerOption`
- `FzPlatformDbContextSettings`
- `ConnectionStrings:ActivationConnection`

## Docker

```bash
docker build -t ms-microservice-web .
docker run --rm -p 8080:8080 ms-microservice-web
```

容器默认监听 `http://+:8080`。Dockerfile 使用 .NET 10 SDK/Runtime，并按当前仓库结构 restore/publish `src/MS.Microservice.Web`。

## Logging

Web Host 已接入新 Logging 模块：

```csharp
builder.ConfigureMsNLog();
builder.Services.AddMsRequestLogging();
app.UseMsRequestLogging();
```

请求日志上下文由 `MS.Microservice.Logging.AspNetCore` 写入，NLog/Serilog Provider 只负责渲染或结构化 enrich。旧 Web 内部 NLog 工具保留用于兼容测试，不再作为默认启动路径。

## Swagger

Swagger 通过 `MS.Microservice.Swagger` 接入：

```csharp
builder.Services.AddPlatformSwagger(options =>
{
    configuration.GetSection(SwaggerOptions.SectionName).Bind(options);
});

app.UsePlatformSwagger();
```

配置节：`SwaggerOptions`。

## EventBus 与领域事件

实体只负责暂存内存态 `DomainEvents`。完整生产链路仍需 Outbox/Inbox、事件版本、trace/correlationId、失败重试和死信记录。

当前已修正：

- `DomainEvents` 默认返回空集合。
- `AddDomainEvent`/`RemoveDomainEvent` 拒绝 null。
- `Id` setter 从公共写入收敛为 `protected set`。
- `EntityBase<TId>` 不再缓存 HashCode。

长期事件路线见 `docs/framework-optimization-roadmap.md`。

## AI

`MS.Microservice.AI` 当前已具备：

- `HttpClientFactory`
- Provider/model 级超时
- 指数退避重试
- Provider 并发限制
- 流式 SSE cancellation
- Token usage 解析
- Provider-neutral 错误分类
- Activity tracing
- Provider capability validation

仍计划补齐：更细粒度限流策略、熔断策略、prompt/response 脱敏日志、Secret Provider、payload 限制和成本统计。路线见 `docs/framework-optimization-roadmap.md`。

## CI

GitHub Actions 工作流位于 `.github/workflows/dotnet-ci.yml`，执行：

```bash
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release
dotnet publish src/MS.Microservice.Web/MS.Microservice.Web.csproj --no-build -c Release
dotnet list package --vulnerable --include-transitive
```

## 架构边界

架构测试位于 `test/MS.Microservice.Core.Tests/Architecture`，当前守护：

- Domain 不依赖 Infrastructure/Web/EF Core/SqlSugar。
- Infrastructure 不依赖 Web。
- Controller 不直接依赖具体 Repository、Dapper 或 `System.Data`。

## 文档入口

- [上下文边界](docs/Context-Bounded.md)
- [领域命令模式处理程序](docs/Domain-Command-Patterns-Handlers.md)
- [领域命令验证](docs/Domain-Command-Validation.md)
- [值对象](docs/ValueObject.md)
- [CQRS](docs/CQRS.md)
- [事件溯源模式](docs/Event-Source-Pattern.md)
- [最终一致性](docs/Eventual-Consistency.md)
- [持久化透明](docs/Persistence-Ignorance.md)
- [服务网格](docs/service-mesh/README.md)
- [分布式系统模式](docs/patterns-of-distributed-systems/README.md)
