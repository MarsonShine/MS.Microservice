# Swagger 启动配置与 Threading 调度的性能验收

2026 年 11 月 23 日，在同一台 Windows 10 x64、Intel Core i5-9400、.NET SDK 11.0.100 的机器上运行 Release 基准。旧 DLL 来自 `4ba9bd9`，之后的 Notifications 与 Search 两个提交没有修改 Swagger 或 Threading。耗时是一次公开调用或一组明确标出的调用的墙钟时间；B 是进程测量期间的托管分配，包含工作线程。[Swagger 代码点评](../../../src/MS.Microservice.Swagger/docs/performance-review.md) · [Threading 代码点评](../../../src/MS.Microservice.Threading/docs/performance-review.md)

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project benchmarks/MS.Microservice.Swagger.Benchmarks
dotnet run -c Release --project benchmarks/MS.Microservice.Threading.Benchmarks
```

同一份基准程序分别装载待比较的模块 DLL。Swagger 和批处理按旧新、新旧、旧新顺序启动六个独立进程。Singleflight 的最后一轮比较旧版与两种保留取消语义的实现，每版三个进程，轮换先后顺序。每个场景预热一批，再测七批；先取进程内中位数，再取同版本三个进程的中位数。每批都在计时外校验末次输出。这些数据描述给定输入和机器，不能换算为真实服务吞吐量、HTTP 请求延迟或峰值内存。

## Swagger

| 启动操作，每批 512 次 | 旧 µs/次 | 新 µs/次 | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 在新的集合注册一次 | 2.960 | 2.851 | 2752 | 2688 |
| 在同一新集合注册 16 次，保留 16 个配置委托 | 44.965 | 8.655 | 36016 | 9464 |
| 配置管道，前缀为 `swagger` | 4.203 | 4.242 | 4376 | 4376 |
| 配置管道，前缀为 `/tools/private/api-docs/` | 4.004 | 4.065 | 4656 | 4584 |

注册场景在计时后解析选项，确认全部委托按顺序生效。管道场景先创建宿主，计时包括创建 `ApplicationBuilder` 和执行 `UsePlatformSwagger`，随后在计时外请求 UI 和 JSON 检查状态及内容类型。路由候选曾尝试把前缀提到两个委托外捕获，默认前缀多出 8 B；最终版在 UI 回调内部共用一次计算，默认分配持平。路由配置两行耗时都略有上升。[Swagger 基准](../../../benchmarks/MS.Microservice.Swagger.Benchmarks/Program.cs)

同一条回归测试在旧 DLL 上复现了三次平台注册得到三个 MVC Swagger 约定，新版得到一个。还通过实际中间件验证了宿主自行注册的文档、平台文档、UI 页面和脚本里的 JSON URL。[注册与路由测试](../../../test/MS.Microservice.Swagger.Tests/SwaggerServiceCollectionExtensionsTests.cs)

## Threading

| 公开调用或调用组 | 旧 µs/次 | 新 µs/次 | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 空批 | 1.315 | 0.064 | 624 | 120 |
| 单个同步完成项 | 1.943 | 1.425 | 733 | 600 |
| 512 个同步完成项，并发上限 4 | 292.016 | 63.092 | 64523 | 21138 |
| 64 个实际执行 `Task.Yield()` 的项，并发上限 4 | 73.492 | 50.655 | 23662 | 19391 |
| 一个同步完成的工厂，无跟随者 | 0.277 | 0.283 | 248 | 224 |
| 一个在途工厂和 64 个默认令牌的跟随者 | 32.892 | 6.450 | 18218 | 1146 |
| 同上，64 个跟随者都传入可取消但未触发的令牌 | 44.564 | 34.573 | 26924 | 12068 |
| 同上，只有一个跟随者的等待被独立取消 | 44.608 | 7.383 | 19260 | 1608 |

空批和单项每批测 256 次，512 个同步项测 128 次，64 个异步项测 64 次。同步工厂每批 4096 次，其余同键调用组每批 64 次。单键场景先让工厂等待，再让跟随者逐个加入，最后释放并核对每个结果或取消状态。全令牌场景共用同一个未触发的令牌源，在计时外构建；组内的门闩、任务数组和等待计入总量。线程池调度会影响耗时，特别是涉及独立取消时，表内数字是这组测量的中位数。[Threading 基准](../../../benchmarks/MS.Microservice.Threading.Benchmarks/Program.cs)

候选比较中，只让默认令牌调用直接返回任务，会使同步工厂降至 0.263 µs、152 B，但 64 个可取消跟随者一组要分配 19580 B。最终版让有效的命中调用都直接得到已有任务的等待结果，在相同场景降至 12068 B。预先取消、缺失参数和第一次进入的调用仍走异步验证，保留校验次序及取消异常的具体类型。

## 回归与兼容性验证

| Release 命令 | 通过 | 失败或跳过 |
| --- | ---: | ---: |
| `dotnet test test/MS.Microservice.Swagger.Tests -c Release` | 13 | 0 |
| `dotnet test test/MS.Microservice.Threading.Tests -c Release` | 23 | 0 |
| `dotnet test test/MS.Microservice.Core.Tests -c Release --filter FullyQualifiedName~Concurrent` | 9 | 0 |
| `dotnet test test/MS.Microservice.Lab.Tests -c Release --filter FullyQualifiedName~ToolDemoSwaggerTests` | 3 | 0 |

Threading 的回归覆盖同键不同结果类型、同步与异步工厂故障、成功后能立即开始下一轮，以及拥有者和跟随者分别取消。空批测试覆盖读取调用方的 `Count` 期间才触发取消。Lab 测试经真实应用管道取得 OpenAPI JSON，并检查 Lab 和生产配置下暴露的操作。最终实测程序集的 SHA-256 为 Swagger `7C68F78BAB11631D51F488699E67CBBADA217FA45D40BD30FFFFC1BD7E784FF7`、Threading `1D39A15736104120997C88FFCF1EFEAA8A60C2B1754A940CF8195CE810CDFA61`。

分别从 `4ba9bd9` 源码打出两个基线包，用 .NET SDK 的包验证（`EnablePackageValidation`、`RunApiCompat`、参数名检查）与当前两个包对比，均通过。仓库另有 Threading 跨历史基线 `883b907` 的检查；运行 `build/validate-api-compatibility.ps1 -Modules MS.Microservice.Threading` 时，历史检出要求精确的 SDK 10.0.401，本机缺少该版本，脚本停在旧包构建阶段。因此此处的通过结果只覆盖本轮修改前后的包。
