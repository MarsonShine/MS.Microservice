# 项目目录整理验收

2026-11-20，基线为 `ae2c41c`，使用 Windows 和 .NET SDK `11.0.100`。

将根 `src` 和 `test` 中按模块前缀合计超过两个的项目归入模块目录。[AspNetCore](../../../MS.Microservice.AspNetCore/MS.Microservice.AspNetCore.slnx) 迁移 3 个项目，[Caching](../../../MS.Microservice.Caching/MS.Microservice.Caching.slnx) 和 [Documents](../../../MS.Microservice.Documents/MS.Microservice.Documents.slnx) 各迁移 4 个。根解决方案按模块显示。示例和基准项目继续使用原目录，并直接引用新路径。

| 验收 | 结果 |
| --- | --- |
| `./build/validate-build-layout.ps1` | 通过，覆盖 Docker 恢复阶段的 17 个项目 |
| `dotnet restore --configfile nuget.config` 与 `dotnet build --no-restore -c Release` | 通过，构建为 0 警告、0 错误 |
| `dotnet test --no-build -c Release` | 全量复跑退出码 0。49 个测试程序集，3647 通过、158 跳过、0 失败。首次运行的 Chromium 测试超时，单独复跑也通过 |
| `./build/validate-module-consumption.ps1 -Modules @('MS.Microservice.Threading', 'MS.Microservice.Caching', 'MS.Microservice.Http.Resilience', 'MS.Microservice.Messaging.SelfManaged.EFCore')` | 通过，按 CI 的模块组合验证了源码导出和包消费 |
| 对照基线校验引用与链接 | 119 个项目、179 条项目引用、11 个解决方案、96 个受影响的 Markdown 链接均通过 |

单独运行 Caching 包消费时，消费夹具缺少 `BuildServiceProvider` 所需的 DI 实现。采用 CI 的模块组合后验证通过。Docker 容器烟测受限于本机 Docker 守护进程未启动。API 兼容性脚本需要本机未安装的基线 SDK `10.0.401`。[主分支 CI](https://github.com/MarsonShine/MS.Microservice/actions/runs/44653134835) 已记录 `net10.0` 目标框架基线的 `PKV006` 失败。
