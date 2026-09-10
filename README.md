# MS.Microservice

面向 .NET 10 的可复用组件与教学仓库。通用组件可以独立引用或复制；生产参考服务展示完整接入，Lab 保留本地登录、事件溯源、函数式、文件和其他实验。

| 入口 | 用途 |
| --- | --- |
| [生产参考服务](samples/Reference/MS.Microservice.Reference.Web/README.md) | 外部 JWT/OIDC 身份、用户档案、业务角色、审计和可靠消息。 |
| [消息契约](src/MS.Microservice.Messaging.Abstractions/README.md) | 业务层依赖的入队、工作单元、处理器和运维接口。 |
| [默认 Inbox/Outbox](src/MS.Microservice.Messaging.SelfManaged.EFCore/README.md) | 与业务 EF Core 事务共享的自研实现。 |
| [RabbitMQ 传输](src/MS.Microservice.Messaging.RabbitMQ/README.md) | 持久消息、mandatory、publisher confirms、手动 ACK。 |
| [Wolverine 替换实现](src/MS.Microservice.Messaging.Wolverine/README.md) | 业务契约不变，使用框架原生存储、事务与恢复。 |
| [参考数据库迁移](samples/Reference/MS.Microservice.Reference.DatabaseMigrator/README.md) | 默认导出 SQL 和校验清单，显式选择应用迁移。 |
| [理论与专题资料](docs/README.md) | DDD、消息、数据库、部署等资料。 |

## 构建

SDK 基线为 `10.0.401`，由 `global.json` 定义；包版本见 `Directory.Packages.props`。依赖安全维护见 [依赖基线](docs/Dependency-Baseline.md)。

```powershell
dotnet restore --configfile nuget.config
dotnet build --no-restore -c Release
```

## 生产参考服务

正式服务不提供账号密码登录或令牌签发。配置外部身份 Authority/Audience，并通过环境变量提供数据库和 Broker 连接信息：

```text
ConnectionStrings__ReferenceDatabase=Host=...;Database=ms_reference_self;Username=...;Password=...
Messaging__RabbitMQ__ConnectionString=amqp://...
Authentication__Authority=https://.../realms/...
Authentication__Audience=ms-reference
```

默认 `Messaging:Provider=SelfManaged`；设为 `Wolverine` 可选择替换实现。两者不能同时接管同一事务。首次使用应先导出、审查并应用对应迁移；Web 不自动执行 DDL。

```powershell
dotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -- --provider SelfManaged --output artifacts/migrations/reference-self
dotnet run --project samples/Reference/MS.Microservice.Reference.Web
```

`/health/live` 检查进程，`/health/ready` 检查迁移、消息存储和 Broker。Broker 中断可降级，数据库或迁移未准备就绪则返回 503。档案管理需要 `profiles.manage`，消息运维需要 `messaging.manage`。

## Lab

```powershell
dotnet run --project samples/Lab/MS.Microservice.Lab
```

Lab 与正式 Host 是两个独立程序集。实验 Controller 只在 Lab 中，正式镜像不携带它们。旧 Host 的设置与历史接入资料保存在 [历史快照](docs/history/legacy-host-readme.txt)，其中的旧命令不适用于当前生产参考服务。

## 可选组件

Core、Domain.Primitives、Observability、Swagger、Logging、EFCore、SqlSugar、EventSourcing、Audio、Excel 和 AI 按需接入。具体依赖与平台限制以模块文档为准；参考业务模型不作为通用组件的依赖。

数据库、Broker 和容器故障验证属于集成测试。没有对应环境时不要把跳过这些测试视为生产验证通过；快速的宿主与业务验证使用 TestServer/SQLite。
