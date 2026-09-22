# AOT 改造记录

Reference.Web 已经承载可运行的业务流程。直接在它上面替换持久化或消息机制，会把 AOT 探索与原站点的可用性绑在一起。
因此新增 `MS.Microservice.Reference.AotWeb`，每接入一项能力，就在原生产物中验证它，同时保留原宿主。

这份记录区分三种结论：代码已经实现并有运行证据；上游明确存在限制；项目尚未验证。
“尚未验证”不写成“不支持”。后续调整需要更新同一能力条目，保留原做法、新做法、原因和证据。

## 独立宿主改了什么

| 部分 | Reference.Web 的做法 | AotWeb 的做法 | 为什么这样处理 |
|---|---|---|---|
| 宿主入口 | 调用公共 `ServiceHost.CreateBuilder`，内部使用 `WebApplication.CreateBuilder` | 新宿主单独调用 `CreateSlimBuilder` | 缩小默认功能范围；公共 Builder 保持原状，不改变原站点和 Lab 的启动语义。 |
| 项目依赖 | 引用 Reference.Persistence、Observability 和公共 HTTP、日志模块 | 暂只引用公共 HTTP 和请求日志模块 | 让宿主基线的原生验证不依赖 EF、PostgreSQL、Wolverine 或消息后台任务。它们仍是待接入能力，不是被配置静默跳过。 |
| 发布设置 | 普通托管 Web 项目 | 启用 `PublishAot`、请求委托生成器，关闭 JSON 默认反射，发布警告按错误处理 | 编译和运行都必须遵守 AOT 限制，不能靠警告抑制获得通过。 |
| 存活响应 | `Results.Ok(new { status = "healthy" })` | `TypedResults.Ok(LivenessResponse)`，由 JSON 上下文生成元数据 | 匿名类型改为可声明的协议类型，仍保留 `status` 字段和 200 状态。 |
| JSON | 由原宿主及调用路径的配置处理 | 注册 `AotWebJsonContext`，显式包含存活响应和 ProblemDetails 类型 | 运行时不靠扫描模型属性来补齐协议。 |
| 通用 HTTP 行为 | 使用 `AddPlatformHttp` / `UsePlatformHttp` | 继续使用同一组公共组件 | 复用 CORS、代理配置和错误处理；本片不为 AOT 复制第二套中间件。 |
| 日志 | JSON 控制台日志与请求日志中间件 | 保留相同设置和公共请求日志组件 | 日志仍可用于诊断启动和请求行为，不引入额外日志提供器。 |
| 就绪检查 | 检查数据库迁移、消息存储与 broker | 暂不映射 `/health/ready` | 尚未接入这些依赖时，不能返回一个含义不同的“ready”。 |
| HTTP 测试 | 原站点测试使用 TestServer、SQLite 和测试身份元数据 | 在同一测试项目增加 `AotHostTests`，支持 TestServer 或外部原生进程 | 不增加测试入口项目；原生验收通过真实 HTTP 访问已发布进程。 |

原站点、Application、Persistence、数据库迁移和现有部署入口均未因这个宿主改写。
两个宿主被同一个解决方案收录，但不存在宿主之间的项目引用。

## 能力现在走到哪里

| 能力 | 当前判断 | 继续接入前需要解决的事 |
|---|---|---|
| Minimal API、命名 JSON 响应、CORS、请求日志 | 已实现独立宿主基线 | 验收范围见下节；不能从一个存活接口推断全部业务类型都兼容。 |
| JWT / 外部身份 | 尚未迁入；ASP.NET Core 官方列出了 JWT 的 AOT 支持 | 验证项目自己的 Authority 配置、令牌验证、权限策略与错误响应。没有认证的基线站点不提供业务接口。 |
| 档案、角色、审计接口 | 尚未迁入 | 显式登记请求、响应、集合和错误协议，再与实际业务依赖组合验证。 |
| EF Core / Npgsql | 现有持久化路径尚未进行本站点的原生验证；EF 官方仍把 NativeAOT 与查询预编译列为实验性功能，不建议用于生产 | 核实 Provider、模型、查询预编译和事务能力。原审计仓储按条件追加 Where 的写法属于需要审查的动态查询。不能只打开 PublishAot。 |
| SelfManaged 消息 | 尚未验证；仍依赖 EF Core | 验证业务事务与 Outbox 的原子性、Inbox 和后台派发；不能仅切到默认 Provider 就认为绕开 EF 限制。 |
| Wolverine | 尚未验证，不能凭依赖名称判定不支持 | 确认实际版本的生成代码、处理器发现、消息持久化与原生发布路径。 |
| RabbitMQ、OpenTelemetry | 尚未在此宿主中验证 | 使用具体版本和真实接入路径发布、运行，检查启动、停止和故障行为。 |
| MVC 与 Lab 实验 | 不迁入这个站点；当前 ASP.NET Core 官方支持表将 MVC 列为不支持 Native AOT | Reference 本来使用 Minimal APIs，无需为它迁移 Lab 的 Controllers、模型绑定和本地登录实验。 |

上游依据是 [ASP.NET Core Native AOT 支持范围](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)
和 [EF Core NativeAOT 与预编译查询](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)。
这些资料用于判断风险，不替代仓库当前版本的执行结果。以后上游能力变化时，应重新发布验证并更新结论。

## 验收怎样留证据

宿主基线需要同时具备三项证据：原站点回归通过，新站点的 HTTP 断言通过，以及新站点的原生发布和独立运行通过。
原生运行时只把可执行文件放进独立目录，配置从命令行传入，不携带托管 DLL 或 runtimeconfig。
这能避免把关闭了动态代码开关的托管程序误当成原生程序。

本地发布日志保存于 `artifacts/reference-aot-web/win-x64.publish.log`，原生进程和 HTTP 测试结果保存在同一目录。
这些文件属于构建产物，不提交二进制。复查命令见 [README](README.md)。

宿主基线已在 Windows x64、.NET SDK 10.0.401 上验证：原生发布零警告、零错误；
Reference Web 测试共 14 项通过，其中原站点 6 项、新站点 8 项。新站点的 8 项又在独立启动的原生进程上运行通过，
覆盖存活 JSON、404/405 ProblemDetails 及 traceId、CORS 允许/拒绝分支。
发布依赖图不含 EF Core、Npgsql 或 Wolverine，原生进程也实际输出了 JSON 请求完成日志。
这些结果证明的是宿主基线，不是尚未接入的业务能力。

Linux 原生验证和本站点的原生 CI 尚未接入；Core 已有的 CI 结果不能作为这个 Web 宿主的验收结果。
通用异常处理已注册，但当前原生 HTTP 场景验证的是 404、405 响应，尚未覆盖业务异常、身份错误或数据库冲突。

## 后续切片

| 顺序 | 内容 | 验收条件 | 拟定提交 |
|---|---|---|---|
| A1，已完成 | 独立宿主和能力记录 | Windows 原生站点启动，HTTP 场景及原站点回归通过 | `feat(reference-aot): add isolated native web host` |
| A2，依赖 A1 | 外部身份、权限与协议 | 原生进程中的认证、拒绝访问、JSON 响应有实际测试 | `feat(reference-aot): add explicit identity and HTTP contracts` |
| A3，依赖 A1 | 持久化与消息可行性 | 得到具体依赖组合的发布、事务和故障验证结果，再确定实现取舍 | `test(reference-aot): verify persistence and messaging viability` |
| A4，依赖 A2、A3 | 业务流程和持续验证 | 实际数据与消息场景通过，Windows/Linux CI 留存结果 | `feat(reference-aot): integrate verified application paths` |

A2 至 A4 尚未实施。若某个依赖无法满足约定，记录具体失败路径与上游限制，再决定替换或调整边界，
不把关闭功能或换成内存替身算作业务迁移成功。
