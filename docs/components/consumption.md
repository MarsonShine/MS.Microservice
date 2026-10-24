# 组件源码复制与本地包消费

可复用项目位于根 src 或各模块的 src；模块的 docs、src、test 与独立解决方案集中在同一目录。samples 中的业务模型、Host 和适配示例不属于共享组件依赖闭包。
使用 .NET SDK（版本由 global.json 固定）、Git 和 PowerShell 7 执行以下命令。

## 导出源码

在仓库根目录执行：

    ./build/export-modules.ps1 -Modules MS.Microservice.Messaging.SelfManaged.EFCore,MS.Microservice.Messaging.RabbitMQ -OutputDirectory ../message-components

输出目录必须不存在。脚本遍历 ProjectReference，复制选中组件及其依赖的受版本管理文件，
并带上 SDK、构建属性、中央包版本和公开 NuGet 源配置。它不会复制 samples、密钥、bin、obj。
module-manifest.json 记录源提交、文件 SHA-256 和项目闭包。

在导出目录独立构建：

    Set-Location ../message-components
    dotnet restore modules.slnx --configfile nuget.config
    dotnet build modules.slnx -c Release --no-restore

宿主随后引用导出目录中的项目。自行维护 DbContext、模型映射、迁移和 Secret。
不需要原仓库的解决方案、Lab 或隐藏的父目录配置。

## 打本地包

    ./build/pack-modules.ps1 -Modules MS.Microservice.Messaging.SelfManaged.EFCore,MS.Microservice.Messaging.RabbitMQ -OutputDirectory ../local-feed -Version 1.0.0-local.1

脚本按依赖顺序打包整个闭包，并生成 packages.sha256.json。每轮使用新版本，避免本地 NuGet 缓存
复用同版本旧包。消费项目在自己的 nuget.config 中配置此目录和 nuget.org，然后引用包。
运行时不需要源码目录。

## 独立消费验证

    ./build/validate-module-consumption.ps1

默认验证全部共享组件。脚本在系统临时目录创建独立源码副本和包消费项目，完成构建、打包、
程序集类型加载。如果闭包包含 SelfManaged，还在两个独立业务 DbContext 中实际验证：

- 业务与消息同事务提交，失败事务不留下额外业务或出站消息。
- 持久化的中文、数值、消息 Id 和 UTC 时间保持一致。
- 重复投递只有一个持久化业务效果。

闭包包含对应组件时，消费项目还直接调用本轮包中的公共 API，验证以下行为：

- Threading：同键并发请求只执行一次回源，信号量租约释放后等待者取得名额。
- Caching：通过 DI 注册后并发回源只发生一次，相同资源在不同 scope 下保持隔离。
- Http.Resilience：本地 HTTP handler 模拟瞬时错误，GET 重试成功，POST 不重试；不访问外部服务。

CI 使用以下四个入口及其依赖闭包；消费控制台只使用本轮版本的 `PackageReference`，不通过项目引用访问仓库源码。NuGet source mapping 将本轮闭包的包 ID 精确限定到临时本地源，其他依赖仍使用 nuget.org，保留默认漏洞检查：

    ./build/validate-module-consumption.ps1 -Modules @('MS.Microservice.Threading', 'MS.Microservice.Caching', 'MS.Microservice.Http.Resilience', 'MS.Microservice.Messaging.SelfManaged.EFCore')

导出前会检查选定项目闭包内未跟踪的编译、资源和打包输入，发现遗漏时列出路径并失败。先审查并纳入版本管理再重试；脚本不会自动暂存或把未知源码带入导出。消费验证程序由脚本明确复制，不属于组件源码导出。

此验证使用 SQLite，不访问 Broker，也不替代 PostgreSQL/RabbitMQ 故障验收。消费宿主显式引用仓库 `Directory.Packages.props` 中既有版本的 `Microsoft.EntityFrameworkCore.Sqlite` 与 `SQLitePCLRaw.bundle_e_sqlite3`，实际版本记录在报告；它不导入中央版本文件，也不改变被测库包。此结果不代表其他 SQLite 原生组合已经验证。
报告默认保存在 artifacts/validation/module-consumption.json，记录 SDK、源提交、实际验证项和生成包的 SHA-256；临时源码、包和消费项目保留供检查。
使用 -Modules 可选择子集，使用 -ReportPath 指定报告位置。
[已执行的 Windows 验证记录](../validation/module-consumption.json)列出了源版本和范围。

公共 API 的基线比较与破坏性变更检测见 [API 兼容性校验](api-compatibility.md)。包消费验证检查实际调用行为，两项门禁分别执行。

## 接入边界

| 组件 | 宿主需要负责的内容 |
|---|---|
| Messaging.Abstractions | 注册稳定消息名称/版本，定义 Handler，使用最外层工作单元 |
| Messaging.SelfManaged.EFCore | 业务 DbContext、AddSelfManagedMessaging 模型、迁移、后台生命周期 |
| Messaging.RabbitMQ | Secret、Broker 拓扑显式初始化、连接和确认超时 |
| Messaging.Wolverine | 独立具体 DbContext、原生存储初始化、原生恢复配置 |
| Persistence.EFCore / SqlSugar | 数据库 Provider、业务映射、事务入口、数据库结构 |
| AspNetCore / Logging / Observability | 身份 Authority/Audience、权限、日志后端、遥测导出器 |
| Http.Resilience | 为选定的命名或类型化客户端设置目标地址、总超时与调用容量；明确哪些业务请求允许重试 |
| Idempotency.EFCore | 在业务 DbContext 注册模型并应用迁移；由同一工作单元提交业务和响应记录，安排过期清理 |
| AI.* | Provider 凭据、模型能力、超时、配额与成本报告 |
| Audio / Excel / EventSourcing | 平台限制、输入文件、资源所有权；事件溯源存储示例在 Lab |
| Media / Media.Inference | FFmpeg、原生库、字体及可选本地模型；输入权限、并发预算与结果发布 |
| Documents | PDF 输入密码与输出保护、文件大小和并发；目标平台 PDFium／Skia 运行时 |
| Notifications | SMTP 地址和凭据、发送任务与投递反馈；站内通知的私有目录、所属用户授权与历史保留期 |
| Tasks / Tasks.EFCore / Tasks.Coravel | 注册宿主 scoped DbContext、AddTaskCenter 模型和迁移；明确 Scope、稳定 JobId 和处理器版本；独立提交与现有事务内 Stage 分开使用，复用宿主 Coravel Scheduler；首版限单执行实例 |
| Tasks.Dashboard | 已有 IJobStore、认证、可信 Scope 回调和操作权限；用观察入口记录 Coravel 注册计划，页面资源内嵌；不注册数据库或调度器 |
| Search | 可信本地索引目录、从身份构造 Scope、稳定文档标识与递增版本；宿主提取正文并显式发布，初次大导入后按需 Optimize；首版单写者，索引明文保存 |

教育图像场景的源码复制需包含 samples/AI 中的场景类库及其共享依赖；
它不是 AI.Core 的默认功能。QuestionGeneration Harness 可以单独引用。
