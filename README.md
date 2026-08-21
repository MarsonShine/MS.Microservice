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

### 实验端点环境

详细原理与扩展方式见 [Development/Lab Controller 发现机制](docs/Lab-Only-Controller-Discovery.md)。

`DemoController`、`ImageController`、`OrdersController` 和 `FeatureManagerController` 仅在 `Development` 或显式的 `Lab` 环境中参与 MVC Controller discovery。其他环境中这些 Controller 不会生成路由，也不会出现在 Swagger 文档中。

本地使用 Development：

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/MS.Microservice.Web/MS.Microservice.Web.csproj
```

独立实验部署使用 Lab：

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Lab"
dotnet run --project src/MS.Microservice.Web/MS.Microservice.Web.csproj
```

Docker 未设置 `ASPNETCORE_ENVIRONMENT` 时默认为 Production，因此实验端点默认不可见。正式 Controller 不受该规则影响。

默认配置位于：

- `src/MS.Microservice.Web/appsettings.json`
- `src/MS.Microservice.Web/appsettings.Development.json`

关键配置启动期校验：

- `CorsOptions`
- `IdentityOptions:JwtBearerOption`
- `FzPlatformDbContextSettings`
- `ConnectionStrings:ActivationConnection`
- `ConnectionStrings:ActivationReaderConnection`
- `ConnectionStrings:EventStoreConnection`（Sample/Event Sourcing）

Production 正式数据访问统一使用 PostgreSQL：EF Core 使用 `ActivationConnection`，Dapper 查询使用 `ActivationReaderConnection`。Sample Profile 的 Event Sourcing 使用独立的 `EventStoreConnection`。部署环境通过 Secret 注入完整连接串，例如：

```text
ConnectionStrings__ActivationConnection=Host=...;Database=...;Username=...;Password=...
ConnectionStrings__ActivationReaderConnection=Host=...;Database=...;Username=...;Password=...
ConnectionStrings__EventStoreConnection=Host=...;Database=...;Username=...;Password=...
```

仓库配置不保存数据库密码。SqlSugar/Sharding 仍属于 Sample Profile，可以继续使用其独立配置和数据库类型。

### Infrastructure Profile

Web Host 保留统一的 `AddInfrastructure` 门面，并通过 `Infrastructure:Profile` 显式选择模块组合：

| Profile | 模块 |
| --- | --- |
| `Production` | Wolverine Messaging、EF Core Persistence、OpenTelemetry |
| `Sample` | Wolverine Messaging、EF Core、SqlSugar、Event Sourcing、OpenTelemetry |

基础 `appsettings.json` 使用 `Production`；`appsettings.Development.json` 使用 `Sample`。`Lab` 环境如需完整实验模块，应通过环境变量设置：

```text
Infrastructure__Profile=Sample
```

消费方也可以跳过预设，显式选择模块：

```csharp
services.AddInfrastructure(configuration, options =>
{
    options.UseMessaging();
    options.UseEfCorePersistence();
    options.UseTelemetry();
});
```

### 缓存注册与生产部署

Web Host 同时注册两种缓存抽象，但它们服务于不同的调用方式：

- `HybridCache` 是新代码优先使用的高层缓存 API，负责组合本地缓存与可选的二级分布式缓存，并提供并发请求合并等能力。
- `IDistributedCache` 是现有 Controller、应用服务和授权处理器使用的兼容契约。当前默认实现是 `DistributedMemoryCache`，便于单实例开发和启动验证，也可以由 Redis 等 Provider 替换。

当前默认的 `HybridCache` 本地层和 `IDistributedCache` 兼容实现都只保存在当前进程内。应用重启后数据会丢失，多副本之间也不会共享，因此不能把它们用于跨实例权限失效、分布式会话或其他要求全局一致的场景。生产环境部署多个实例时，应注册 Redis 等共享 `IDistributedCache` Provider；`HybridCache` 会在存在分布式实现时将其作为二级缓存使用。

缓存配置使用与 `CacheOptions` 属性一致的秒数键名：

```json
"CacheOptions": {
  "KeyPrefix": "",
  "AbsoluteExpirationSecond": 7200,
  "SlidingExpirationSecond": 7200
}
```

`SlidingExpirationSecond` 必须大于零；配置了 `AbsoluteExpirationSecond` 时也必须大于零。当前 commit 只建立可替换的注册边界，不引入 Redis，也不改变现有缓存扩展方法的过期与并发语义。

### JWT 本地密钥

JWT 签名密钥不存放在 `appsettings*.json` 中。每个密钥必须是至少 32 个 ASCII 字符；建议使用 32 字节密码学随机数的 Base64 文本。

Development 环境使用 .NET User Secrets。先生成两个不同的随机值，再分别写入当前兼容配置所需的两个密钥槽位：

```powershell
$jwtKey0 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$jwtKey1 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "IdentityOptions:JwtBearerOption:SecurityKeys:0" $jwtKey0 --project src/MS.Microservice.Web/MS.Microservice.Web.csproj
dotnet user-secrets set "IdentityOptions:JwtBearerOption:SecurityKeys:1" $jwtKey1 --project src/MS.Microservice.Web/MS.Microservice.Web.csproj
```

容器和其他非 Development 环境通过环境变量注入：

```text
IdentityOptions__JwtBearerOption__SecurityKeys__0=<至少 32 个 ASCII 字符的随机密钥>
IdentityOptions__JwtBearerOption__SecurityKeys__1=<另一个至少 32 个 ASCII 字符的随机密钥>
```

缺少密钥或任一密钥长度不足时，Web Host 会在启动阶段失败。

### JWT 生产部署

`dotnet publish` 和 Docker 镜像构建阶段不注入 JWT 密钥。发布产物保持无密钥，部署平台在应用启动时通过环境变量或 Secret Store 提供配置。ASP.NET Core 会把环境变量名中的双下划线 `__` 映射为配置层级，并覆盖 `appsettings.json`。

直接运行发布产物时，可以在进程环境中设置密钥：

```powershell
$env:IdentityOptions__JwtBearerOption__SecurityKeys__0 = $env:JWT_KEY_0
$env:IdentityOptions__JwtBearerOption__SecurityKeys__1 = $env:JWT_KEY_1
dotnet MS.Microservice.Web.dll
```

Docker 部署时由宿主机或 CI/CD 变量传入，不要把密钥直接写进 Dockerfile：

```powershell
docker run --rm -p 8080:8080 `
  -e IdentityOptions__JwtBearerOption__SecurityKeys__0="$env:JWT_KEY_0" `
  -e IdentityOptions__JwtBearerOption__SecurityKeys__1="$env:JWT_KEY_1" `
  ms-microservice-web
```

Docker Compose 可以把部署环境中的变量映射到容器：

```yaml
services:
  web:
    image: ms-microservice-web
    environment:
      IdentityOptions__JwtBearerOption__SecurityKeys__0: ${JWT_KEY_0:?JWT_KEY_0 is required}
      IdentityOptions__JwtBearerOption__SecurityKeys__1: ${JWT_KEY_1:?JWT_KEY_1 is required}
```

`.env` 文件只能保存在部署服务器并设置严格访问权限，不得提交到 Git。CI/CD 场景应把 `JWT_KEY_0`、`JWT_KEY_1` 保存为受保护的 Secret 变量，部署任务只负责映射，日志中不得输出其值。

Kubernetes 使用 Secret 引用，不把实际密钥写入 Deployment：

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: ms-microservice-jwt
type: Opaque
stringData:
  key-0: "<由部署系统提供>"
  key-1: "<由部署系统提供>"
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ms-microservice-web
spec:
  template:
    spec:
      containers:
        - name: web
          image: ms-microservice-web
          env:
            - name: IdentityOptions__JwtBearerOption__SecurityKeys__0
              valueFrom:
                secretKeyRef:
                  name: ms-microservice-jwt
                  key: key-0
            - name: IdentityOptions__JwtBearerOption__SecurityKeys__1
              valueFrom:
                secretKeyRef:
                  name: ms-microservice-jwt
                  key: key-1
```

普通 Kubernetes Secret 的 Base64 只是编码，不是加密。生产集群应结合 External Secrets、Vault 或云厂商 Secret Manager 管理实际值，示例清单不得携带真实密钥。

> 当前兼容代码需要两个密钥槽位，并使用索引 `1` 的密钥签发 Token。部署时两个值都必须提供且不能相同；后续会单独重构为显式的当前签发密钥与历史验证密钥配置。

## Docker

```bash
docker build -t ms-microservice-web .
docker run --rm -p 8080:8080 ms-microservice-web
```

容器默认监听 `http://+:8080`。Dockerfile 使用 .NET 10 SDK/Runtime，并按当前仓库结构 restore/publish `src/MS.Microservice.Web`。

健康检查端点：

| 路径 | 用途 | 检查内容 | 失败状态 |
| --- | --- | --- | --- |
| `/health/live` | liveness | 进程自身 | 503 |
| `/health/ready` | readiness | PostgreSQL `ActivationConnection` | 503 |
| `/hc` | 兼容旧探针 | 与 readiness 相同 | 503 |

健康响应不会返回异常、连接串或数据库错误详情。Kubernetes 应将 livenessProbe 指向 `/health/live`，readinessProbe 指向 `/health/ready`。

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
- **图像 Prompt 规划管线**：文本 → LLM 视觉规划 → Safe/Rich Prompt → 图片生成
- **场景分组与批量生图**：句子语义分组 → 结构化 EditDelta → 参考图编辑保持组内视觉连续性
- **Qwen 参考图编辑**：通过 `IQwenImageReferenceEditClient` / `IReferenceImageEditClient` + `QwenReferenceImageEditAdapter`，不是 `AIImageEditRequest`

> **架构说明**：
> - `OpenAICompatible*ProviderBase` 是 provider HTTP 复用层（chat/completions, images/generations 等），不是参考图编辑通道。
> - 参考图编辑有独立的 `IReferenceImageEditClient` 接口和 provider adapter（当前仅 Qwen 支持）。
> - `MS.Microservice.Core` 是允许依赖的核心层，`AI.Core` 已引用它（`DefaultSerializeSetting` 等）。

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

- [文档中心总览](docs/)
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
