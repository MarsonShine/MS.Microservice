# MS.Microservice

面向 .NET 10 的可复用组件与教学仓库。通用组件可以独立引用或复制；生产参考服务展示完整接入，Lab 保留本地登录、事件溯源、函数式、文件和其他实验。

| 入口 | 用途 |
| --- | --- |
| [总体架构与设计理由](docs/Architecture-Overview.md) | 先理解 Core、模块、Reference 和 Lab 各自解决什么问题。 |
| [快速启动](docs/Getting-Started.md) | 一个脚本准备 Reference 或 Lab，并演练消息恢复。 |
| [六个递进 Lab](docs/labs/README.md) | 分层、事务、可靠消息、故障、诊断与组件替换。 |
| [源码复制与包消费](docs/components/consumption.md) | 依赖闭包导出和仓库外接入验证。 |
| [生产参考服务](samples/Reference/MS.Microservice.Reference.Web/README.md) | 外部 JWT/OIDC 身份、用户档案、业务角色、审计和可靠消息。 |
| [可选 Redis 就绪探针](src/MS.Microservice.HealthChecks.Redis/README.md) | 使用现有 Redis 连接执行 PING；未接入 Redis 的宿主无需引用。 |
| [消息契约](MS.Microservice.Messaging/src/MS.Microservice.Messaging.Abstractions/README.md) | 业务层依赖的入队、工作单元、处理器和运维接口。 |
| [默认 Inbox/Outbox](MS.Microservice.Messaging/src/MS.Microservice.Messaging.SelfManaged.EFCore/README.md) | 与业务 EF Core 事务共享的自研实现。 |
| [RabbitMQ 传输](MS.Microservice.Messaging/src/MS.Microservice.Messaging.RabbitMQ/README.md) | 持久消息、mandatory、publisher confirms、手动 ACK。 |
| [Wolverine 替换实现](MS.Microservice.Messaging/src/MS.Microservice.Messaging.Wolverine/README.md) | 业务契约不变，使用框架原生存储、事务与恢复。 |
| [参考数据库迁移](samples/Reference/MS.Microservice.Reference.DatabaseMigrator/README.md) | 默认导出 SQL 和校验清单，显式选择应用迁移。 |
| [理论与专题资料](docs/README.md) | DDD、消息、数据库、部署等资料。 |

## 按模块独立开发

多项目模块把 docs、src、test 和独立解决方案放在同一目录；小型独立组件暂留根 src/test。
根解决方案覆盖全仓库，日常维护可以只打开对应模块：

| 模块 | 独立解决方案 | 模块说明 |
|---|---|---|
| AI | [MS.Microservice.AI.slnx](MS.Microservice.AI/MS.Microservice.AI.slnx) | [说明](MS.Microservice.AI/docs/design.md) |
| Logging | [MS.Microservice.Logging.slnx](MS.Microservice.Logging/MS.Microservice.Logging.slnx) | [说明](MS.Microservice.Logging/docs/design.md) |
| Messaging | [MS.Microservice.Messaging.slnx](MS.Microservice.Messaging/MS.Microservice.Messaging.slnx) | [说明](MS.Microservice.Messaging/docs/design.md) |
| Persistence | [MS.Microservice.Persistence.slnx](MS.Microservice.Persistence/MS.Microservice.Persistence.slnx) | [说明](MS.Microservice.Persistence/docs/design.md) |
| Idempotency | [MS.Microservice.Idempotency.slnx](MS.Microservice.Idempotency/MS.Microservice.Idempotency.slnx) | [说明](MS.Microservice.Idempotency/README.md) |
| Media | [MS.Microservice.Media.slnx](MS.Microservice.Media/MS.Microservice.Media.slnx) | [说明](MS.Microservice.Media/README.md) |

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

Core、Domain.Primitives、Observability、Swagger、Logging、HTTP Resilience、Idempotency、EFCore、SqlSugar、EventSourcing、Audio、Excel、Documents、Notifications、Media 和 AI 按需接入。S3、媒体推理和 HTML 排版作为可选组件引用；具体依赖与平台限制以模块文档为准。参考业务模型不作为通用组件的依赖。

| 工具组件 | 使用说明 |
| --- | --- |
| 图片、视频、二维码和水印 | [Media](MS.Microservice.Media/README.md)；图片／视频自动去水印、OCR、表格、抠图和增强使用可选 Inference |
| PDF、Word 模板与 HTML／DOCX 排版 | [Documents](src/MS.Microservice.Documents/README.md)；HTML／DOCX 转 PDF 使用可选 Windows [Rendering](src/MS.Microservice.Documents.Rendering/README.md) |
| 文件存储、哈希与 ZIP | [Core](src/MS.Microservice.Core/README.md)；对象存储使用可选 [S3](src/MS.Microservice.Storage.S3/README.md) |
| 音频 | [Audio](src/MS.Microservice.Audio/README.md)：剪辑、音量、淡入淡出、合并和转换 |
| Excel 与 CSV | [Excel](src/MS.Microservice.Excel/README.md)：导入、导出和定位错误报告 |
| 文本、编码与 JSON | [Core](src/MS.Microservice.Core/README.md) |
| 邮件、短信、模板与站内通知 | [Notifications](src/MS.Microservice.Notifications/README.md) |
| 后台图片批处理 | [Lab](samples/Lab/MS.Microservice.Lab/README.md#图片后台批处理)：上传、进度、取消、重试和私有下载 |
| 统一任务中心 | [Tasks](MS.Microservice.Tasks/README.md)：持久提交、进度、取消与执行历史；[Coravel](MS.Microservice.Tasks/src/MS.Microservice.Tasks.Coravel/README.md) 接入分组执行与单实例恢复；[Dashboard](MS.Microservice.Tasks/src/MS.Microservice.Tasks.Dashboard/README.md) 提供可挂载 UI 与注册计划查看 |
| 文件与正文检索 | [Search](src/MS.Microservice.Search/README.md)：持久文件名／路径和中英文正文索引，默认快速返回前 50 条、按需精确计数，路径排序优先响应、相关度排序可选 |

数据库、Broker 和容器故障验证属于集成测试。没有对应环境时不要把跳过这些测试视为生产验证通过；快速的宿主与业务验证使用 TestServer/SQLite。
