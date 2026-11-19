# .NET 11 升级验收

日期：2026-11-16；分支 `codex/dotnet-11-upgrade`；基线 `936c6f1512a2100a0b60405dd04fb56799a9822b`，验收对象为该基线加本任务未提交改动。SDK 11.0.100，Windows。

## 范围与结果

统一目标框架与独立基准、容器及 SDK 基线升级至 .NET 11。CI 从 global.json 读取 SDK。EF Core、PostgreSQL 提供程序和 dotnet-ef 保持 10.x；C# 语言版本仍为 14。移除 .NET 11 框架自带的冗余引用；中央传递版本将 MySqlConnector 从 2.2.5 升至 2.6.1。Reference Web 使用程序集别名规避验证生成器与公开命名空间冲突，保留请求验证；渲染 NuGet 元数据路径随目标框架更新。Excel 数值与公式的 decimal 导入恢复原有 15 位有效数字语义；Lab 认证端点的重复 200 响应声明合并为实际响应类型。

## 执行证据

- `dotnet restore --configfile nuget.config --verbosity quiet`：通过，退出码 0。
- `dotnet build --no-restore -c Release --verbosity quiet`：通过，0 警告、0 错误。
- `dotnet test --no-build -c Release -m:1 --logger 'console;verbosity=normal'`：通过，48 个测试项目，3,554 通过、158 跳过、0 失败，退出码 0。输出保存在本机 `artifacts/validation/net11-full-test.log`（不纳入版本库）。与初始验收命令相比增加 `-m:1` 顺序执行测试项目，以降低并发资源竞争。
- 首次并发 `dotnet test --no-build -c Release --verbosity quiet` 失败：Excel 转换 1 项、Lab 5 项、渲染 1 项。修复 Excel 和重复 Swagger 响应后，对剩余后台任务和渲染两项分别重跑通过；最终顺序全量重跑通过。不能据此确定首轮这两项失败的原因。
- Excel 测试：134 通过、19 跳过；Swagger 定向测试：3 通过；启动验证定向测试：27 通过；压缩定向测试：61 通过。
- `pwsh -NoProfile -File build/validate-idempotency-compiled-model.ps1`：通过。
- `build/validate-module-consumption.ps1 -Modules @('MS.Microservice.Documents.Rendering')`：源码构建和包消费者通过；检查包及消费者包含 net11.0 渲染运行元数据。未通过该检查启动浏览器。
- `python "$HOME/.agents/harness/harness.py" check` 和 `git diff --check`：通过。

## 限制

Docker 引擎不可连接，未构建或运行容器镜像；真实 MySQL、PostgreSQL、RabbitMQ 集成及部署未验证。SqlSugar 声明允许 MySqlConnector 2.6.1，但真实 MySQL 运行兼容性未验证。外部消费者使用 Core 和 .NET 11 `AddValidation` 时也可能遇到命名空间冲突。默认宿主行为下后台服务未处理异常在 .NET 11 会传递给 `RunAsync`，部署方应检查进程退出与重启策略。历史 API 基线检查依赖本机未安装的 SDK 10.0.401，本次未运行；目标框架升级改变消费者最低运行环境。
