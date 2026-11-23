# Swagger 启动配置的性能验收

2026 年 11 月 23 日，在同一台 Windows 10 x64、Intel Core i5-9400、.NET SDK 11.0.100 的机器上比较基线 `4ba9bd9` 和本次修改。[Swagger 代码点评](../../../src/MS.Microservice.Swagger/docs/performance-review.md)

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project benchmarks/MS.Microservice.Swagger.Benchmarks
```

同一份基准按旧新、新旧、旧新的顺序交错启动六个独立进程，每个场景预热一批，再测七批，取各版本三个进程的批次中位数的中位数。

## Swagger

| 启动操作，每批 512 次 | 旧 µs/次 | 新 µs/次 | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 在新的集合注册一次 | 2.960 | 2.851 | 2752 | 2688 |
| 在同一新集合注册 16 次，保留 16 个配置委托 | 44.965 | 8.655 | 36016 | 9464 |
| 配置管道，前缀为 `swagger` | 4.203 | 4.242 | 4376 | 4376 |
| 配置管道，前缀为 `/tools/private/api-docs/` | 4.004 | 4.065 | 4656 | 4584 |

注册场景在计时后解析选项，确认全部委托按顺序生效。管道场景先创建宿主，计时包括创建 `ApplicationBuilder` 和执行 `UsePlatformSwagger`，随后在计时外请求 UI 和 JSON 检查状态及内容类型。路由候选曾尝试把前缀提到两个委托外捕获，默认前缀多出 8 B；最终版在 UI 回调内部共用一次计算，默认分配持平。路由配置两行耗时都略有上升。[Swagger 基准](../../../benchmarks/MS.Microservice.Swagger.Benchmarks/Program.cs)

同一条回归测试在旧 DLL 上复现了三次平台注册得到三个 MVC Swagger 约定，新版得到一个。还通过实际中间件验证了宿主自行注册的文档、平台文档、UI 页面和脚本里的 JSON URL。[注册与路由测试](../../../test/MS.Microservice.Swagger.Tests/SwaggerServiceCollectionExtensionsTests.cs)

## 回归与兼容性验证

Swagger Release 测试 13 项通过，Lab 的 `ToolDemoSwaggerTests` 3 项通过。与 `4ba9bd9` 的 NuGet 包 API 兼容性对照通过。
