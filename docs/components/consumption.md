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

此验证使用 SQLite，不访问 Broker，也不替代 PostgreSQL/RabbitMQ 故障验收。
报告默认保存在 artifacts/validation/module-consumption.json；临时目录保留供检查。
使用 -Modules 可选择子集，使用 -ReportPath 指定报告位置。
[已执行的 Windows 验证记录](../validation/module-consumption.json)列出了源版本和范围。

## 接入边界

| 组件 | 宿主需要负责的内容 |
|---|---|
| Messaging.Abstractions | 注册稳定消息名称/版本，定义 Handler，使用最外层工作单元 |
| Messaging.SelfManaged.EFCore | 业务 DbContext、AddSelfManagedMessaging 模型、迁移、后台生命周期 |
| Messaging.RabbitMQ | Secret、Broker 拓扑显式初始化、连接和确认超时 |
| Messaging.Wolverine | 独立具体 DbContext、原生存储初始化、原生恢复配置 |
| Persistence.EFCore / SqlSugar | 数据库 Provider、业务映射、事务入口、数据库结构 |
| AspNetCore / Logging / Observability | 身份 Authority/Audience、权限、日志后端、遥测导出器 |
| AI.* | Provider 凭据、模型能力、超时、配额与成本报告 |
| Audio / Excel / EventSourcing | 平台限制、输入文件、资源所有权；事件溯源存储示例在 Lab |

教育图像场景的源码复制需包含 samples/AI 中的场景类库及其共享依赖；
它不是 AI.Core 的默认功能。QuestionGeneration Harness 可以单独引用。

## Excel 的兼容包和 AOT 包

Excel 保留唯一项目，`src/MS.Microservice.Excel.Aot` 是同项目编译的独立源码目录。源码导出会同时携带两个目录；通用 pack-modules 脚本继续打兼容包。单独的 AOT 包使用：

```powershell
dotnet pack src/MS.Microservice.Excel/MS.Microservice.Excel.csproj -c Release -p:ExcelVariant=Aot -p:PackageVersion=1.0.0-local.1 -o artifacts/excel-packages
```

需要原包时将 ExcelVariant 改为 Legacy。两种包的程序集和命名空间不同，可以同时引用；默认 All 构建只用于开发和测试，打包时必须明确选择。完整调用和验证边界见 [Excel AOT 说明](../../src/MS.Microservice.Excel.Aot/README.md)。
