# .NET 11 官方包对齐验收

日期：2026-11-19。基线为 `d6fc973`（已合并原 .NET 11 升级）。本次范围为中央包清单、EF CLI 工具清单、[升级对照](../../NET11-Optimization-Comparison.md)和验收记录；业务代码与数据库迁移不在范围内。

| 命令 | 结果 |
| --- | --- |
| `dotnet restore --configfile nuget.config` | 全解决方案还原通过。初次下载遇到 NuGet 传输中断，用临时本地源缓存官方 NuGet 下载后，以仓库原配置重跑通过。 |
| `dotnet build --no-restore -c Release` | 退出码 0，0 警告、0 错误。 |
| `dotnet test --no-build -c Release` | 退出码 0，49 个测试程序集，3,644 通过、158 跳过、0 失败。 |
| `./build/validate-build-layout.ps1` | SDK 11.0.100 与 Docker 还原依赖图的 17 个项目通过。 |
| `dotnet ef --version`；`./build/validate-idempotency-compiled-model.ps1` | CLI 输出 10.0.15；幂等 SQLite 消费项目生成编译模型并重新编译通过。 |
| `./build/validate-module-consumption.ps1 -Modules @('MS.Microservice.Threading','MS.Microservice.Caching','MS.Microservice.Http.Resilience','MS.Microservice.Messaging.SelfManaged.EFCore')` | 源码导出、NuGet 打包与消费应用运行通过。 |
| `dotnet publish samples/Reference/MS.Microservice.Reference.Web/MS.Microservice.Reference.Web.csproj --no-build -c Release -o artifacts/publish/net11-official-packages` | Reference Web 发布产物生成成功。 |

本机 Docker daemon 不可用，未运行容器启动冒烟及需要真实 PostgreSQL/RabbitMQ 的消息集成测试。这两项由 PR 的 `dotnet-ci` 验证。EF Core 11 的后续升级条件见[升级对照](../../NET11-Optimization-Comparison.md#nuget-依赖对齐)。
