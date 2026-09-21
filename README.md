# MS.Microservice

面向 .NET 10 的可复用组件与教学仓库。通用组件可以独立引用或复制；生产参考服务展示完整接入，Lab 保留本地登录、事件溯源、函数式、文件和其他实验。

| 入口 | 用途 |
| --- | --- |
| [总体架构与设计理由](docs/Architecture-Overview.md) | 先理解 Core、模块、Reference 和 Lab 各自解决什么问题。 |
| [快速启动](docs/Getting-Started.md) | 一个脚本准备 Reference 或 Lab，并演练消息恢复。 |
| [六个递进 Lab](docs/labs/README.md) | 分层、事务、可靠消息、故障、诊断与组件替换。 |
| [源码复制与包消费](docs/components/consumption.md) | 依赖闭包导出和仓库外接入验证。 |
| [性能与 AOT 迁移](docs/Performance-Aot-Migration.md) | 静态接口迁移表、独立提交和实际验证边界。 |
| [生产参考服务](samples/Reference/MS.Microservice.Reference.Web/README.md) | 外部 JWT/OIDC 身份、用户档案、业务角色、审计和可靠消息。 |
| [消息契约](MS.Microservice.Messaging/src/MS.Microservice.Messaging.Abstractions/README.md) | 业务层依赖的入队、工作单元、处理器和运维接口。 |
| [默认 Inbox/Outbox](MS.Microservice.Messaging/src/MS.Microservice.Messaging.SelfManaged.EFCore/README.md) | 与业务 EF Core 事务共享的自研实现。 |
| [RabbitMQ 传输](MS.Microservice.Messaging/src/MS.Microservice.Messaging.RabbitMQ/README.md) | 持久消息、mandatory、publisher confirms、手动 ACK。 |
| [Wolverine 替换实现](MS.Microservice.Messaging/src/MS.Microservice.Messaging.Wolverine/README.md) | 业务契约不变，使用框架原生存储、事务与恢复。 |
| [参考数据库迁移](samples/Reference/MS.Microservice.Reference.DatabaseMigrator/README.md) | 默认导出 SQL 和校验清单，显式选择应用迁移。 |
| [理论与专题资料](docs/README.md) | DDD、消息、数据库、部署等资料。 |

## 按模块独立开发

多项目模块把 docs、src、test 和独立解决方案放在同一目录；小型独立组件暂留根 src/test。
根解决方案覆盖全仓库，日常维护可以只打开对应模块：

| 模块 | 独立解决方案 | 设计入口 |
|---|---|---|
| AI | [MS.Microservice.AI.slnx](MS.Microservice.AI/MS.Microservice.AI.slnx) | [说明](MS.Microservice.AI/docs/design.md) |
| Logging | [MS.Microservice.Logging.slnx](MS.Microservice.Logging/MS.Microservice.Logging.slnx) | [说明](MS.Microservice.Logging/docs/design.md) |
| Messaging | [MS.Microservice.Messaging.slnx](MS.Microservice.Messaging/MS.Microservice.Messaging.slnx) | [说明](MS.Microservice.Messaging/docs/design.md) |
| Persistence | [MS.Microservice.Persistence.slnx](MS.Microservice.Persistence/MS.Microservice.Persistence.slnx) | [说明](MS.Microservice.Persistence/docs/design.md) |

Reference 是完整接入的参考应用，不是新的底层框架；使用模块不需要引用或启动它。
项目拆分理由和代价见[Reference 说明](samples/Reference/README.md)。
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
./build/start-local.ps1 -Lab
```

Lab 与正式 Host 是两个独立程序集。实验 Controller 只在 Lab 中，正式镜像不携带它们。旧 Host 的设置与历史接入资料保存在 [历史快照](docs/history/legacy-host-readme.txt)，其中的旧命令不适用于当前生产参考服务。

## 可选组件

Core、Domain.Primitives、Observability、Swagger、Logging、EFCore、SqlSugar、EventSourcing、Audio、Excel 和 AI 按需接入。具体依赖与平台限制以模块文档为准；参考业务模型不作为通用组件的依赖。

数据库、Broker 和容器故障验证属于集成测试。没有对应环境时不要把跳过这些测试视为生产验证通过；快速的宿主与业务验证使用 TestServer/SQLite。
