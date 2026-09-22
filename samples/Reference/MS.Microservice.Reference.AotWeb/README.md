# Reference AotWeb

这个站点用于逐步验证 Reference 的 Native AOT 接入方式。原来的 `MS.Microservice.Reference.Web`
继续提供档案、审计和可靠消息功能；两个宿主互不引用，AOT 的依赖选择不会改变原站点的注册过程。

当前只提供宿主基线：Kestrel、存活检查、JSON 响应、CORS、公共错误响应和请求日志。
它尚不是原站点的替代品，也没有用内存仓储模拟业务功能。

| 路由 | 当前行为 |
|---|---|
| `GET /health/live` | 返回 `200 {"status":"healthy"}`，只说明进程能够处理 HTTP 请求。 |
| `/health/ready` | 尚未接入数据库和消息检查，返回 404。不能把 live 当成业务就绪检查。 |
| 档案、角色、审计、消息运维接口 | 尚未迁入，返回 404。 |

改了哪些地方、为什么这样改、哪些能力还不能下结论，见 [AOT 改造记录](AotMigration.md)。

## 本地启动

从仓库根目录运行，当前不需要数据库、RabbitMQ 或身份服务配置：

```powershell
dotnet run --project samples/Reference/MS.Microservice.Reference.AotWeb -- --urls http://127.0.0.1:5276
```

普通 `dotnet run` 仍是托管运行，只适合开发调试。验证原生程序需要在目标系统上发布。
Windows x64 的命令如下，机器需要 C++ x64 工具链和 Windows SDK：

```powershell
dotnet publish samples/Reference/MS.Microservice.Reference.AotWeb -c Release -r win-x64 -o artifacts/reference-aot-web/publish-win-x64
./artifacts/reference-aot-web/publish-win-x64/MS.Microservice.Reference.AotWeb.exe --urls http://127.0.0.1:5276
```

原生发布过程中的裁剪或 AOT 警告不能忽略。Linux 必须在 Linux 主机上另行发布和运行，
不能把 Windows 的结果当作 Linux 通过。

站点使用 SlimBuilder，默认按 HTTP 入口验证。HTTPS、HTTP/3、IIS 集成尚未纳入这个站点的验证范围。
允许的跨域来源通过 `Cors:Origins` 配置，例如 `Cors__Origins__0=https://aot-client.example`；未配置时不放行跨域来源。
其他公共 HTTP 配置仍由 `MS.Microservice.AspNetCore` 处理，已知代理配置为 `Http:KnownProxies`。
请求正文大小上限为 1 MiB。

## 如何复查

HTTP 测试在现有的 `MS.Microservice.Reference.Web.Tests` 中，未新增测试项目。
不设置环境变量时，新站点测试使用 TestServer；同一项目中的原站点测试继续验证既有功能。

```powershell
dotnet test test/MS.Microservice.Reference.Web.Tests -c Release
```

验证原生站点时，先启动发布的可执行文件，并将 `Cors:Origins:0` 配置为 `https://aot-client.example`。
然后在另一个终端运行同一组 HTTP 断言：

```powershell
$env:REFERENCE_AOT_WEB_URL = 'http://127.0.0.1:5276'
dotnet test test/MS.Microservice.Reference.Web.Tests -c Release --filter FullyQualifiedName~AotHostTests
Remove-Item Env:REFERENCE_AOT_WEB_URL
```

这个变量只影响测试程序，且只接受回环 HTTP 地址。它不会切换站点内部的实现。
测试覆盖存活响应、尚未实现的路由、405 错误和 CORS 的允许/拒绝分支。
原生验收还需确认实际启动的是发布产物；仅用 TestServer 或托管 DLL 跑过断言不算完成。
