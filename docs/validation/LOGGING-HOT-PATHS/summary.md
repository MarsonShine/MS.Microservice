# 请求日志的耗时、分配与回归验收

2026-11-24，在 Windows 10 x64、Intel Core i5-9400、.NET SDK 11.0.100、.NET 11.0.0 上，以 Git `ac6b66a` 为基线，比较 Core、AspNetCore、NLog 与 Serilog 的 Release 构建。NLog 为 6.1.3，Serilog 为 4.3.0。[优化后的代码解析](../../../MS.Microservice.Logging/docs/request-logging-performance.md) · [基准驱动](../../../MS.Microservice.Logging/benchmarks/MS.Microservice.Logging.Core.Benchmarks/Program.cs)

## 同机结果

表中耗时为每次操作的纳秒数，括号内是三个进程各自中位数的最小值和最大值。每次操作的线程托管分配在同版本三个进程中一致。

| 每次操作 | 旧 ns/次（范围） | 新 ns/次（范围） | 旧 B/次 | 新 B/次 |
| --- | ---: | ---: | ---: | ---: |
| Core 普通 Push、读 Current、Dispose | 77.2（70.9–86.1） | 73.9（65.1–81.1） | 136 | 104 |
| Core 在外层作用域中执行相同操作 | 110.9（97.1–114.7） | 97.0（91.0–110.1） | 208 | 176 |
| Core 仅读取 Current | 8.2（7.8–8.3） | 7.3（7.3–7.4） | 0 | 0 |
| AspNetCore 捕获三个请求头、格式化一条成功完成日志 | 655.5（592.0–694.1） | 651.3（578.9–674.1） | 408 | 376 |
| NLog 渲染三个非空请求头字段 | 148.0（144.6–163.0） | 131.3（127.4–144.1） | 128 | 128 |
| Serilog 写一条七字段日志，进入 enricher 时属性表为空 | 677.3（663.7–904.4） | 705.5（672.5–788.3） | 984 | 760 |
| Serilog 写一条七字段日志，模板已有 requestId | 758.9（739.8–950.7） | 795.0（789.7–901.5） | 1048 | 1048 |
| Serilog 配置 None，调用 Fatal，带七个环境字段 | 681.0（660.8–761.8） | 8.5（8.5–8.5） | 984 | 0 |

每个版本独立启动三个进程，运行顺序为旧新、新旧、旧新。每个进程对每个场景预热 2 万次，再测 7 轮，每轮 20 万次。先取每个进程的中位数，再按版本取中位数。测量期间关闭分层编译，Windows 测量进程固定在同一逻辑处理器。每批用 `Stopwatch` 计时，用 `GC.GetAllocatedBytesForCurrentThread()` 的差值计算分配。

HTTP 基准复用 `DefaultHttpContext`，下游同步完成，记录器实际格式化成功事件并只保留最近一条消息。NLog 复用同一事件和三个渲染器，每次分别返回三个字符串。Serilog 通过宿主注册取得原生 `Serilog.ILogger`，复用同一个作用域，sink 只计数；每轮在计时外检查七个标量的类型和值、重名覆盖或关闭输出。基准覆盖内存内调用，每次分配量与服务的峰值工作集是不同指标。真实网络请求、输出设备和并发负载下的尾延迟需另行测量。

## 复现

在仓库根目录运行当前版本。若需严格复现上表的 CPU 绑定，可以在执行以下命令的独立 PowerShell 进程中先设置 `[Diagnostics.Process]::GetCurrentProcess().ProcessorAffinity = [IntPtr]4`。

```powershell
$bench = 'MS.Microservice.Logging/benchmarks/MS.Microservice.Logging.Core.Benchmarks'
dotnet build $bench -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet "$bench/bin/Release/net11.0/MS.Microservice.Logging.Core.Benchmarks.dll"
```

旧版从 `git archive ac6b66a` 导出 `MS.Microservice.Logging/`、`global.json`、`Directory.Build.props`、`Directory.Packages.props` 和 `nuget.config` 到单独目录。仅将当前基准的 `Program.cs` 和项目文件复制到导出目录下的相同位置，在那里运行同一组命令。两个版本使用各自构建的四个生产程序集，输入、计时及输出检查相同。

## 回归测试

```powershell
dotnet test MS.Microservice.Logging/MS.Microservice.Logging.slnx -c Release
dotnet test test/MS.Microservice.Reference.Web.Tests -c Release --filter "FullyQualifiedName~ReferenceHostTests.CompletionLogUses"
```

| 测试项目或筛选结果 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Logging.Core.Tests | 15 | 0 | 0 |
| Logging.AspNetCore.Tests | 18 | 0 | 0 |
| Logging.NLog.Tests | 5 | 0 | 0 |
| Logging.Serilog.Tests | 5 | 0 | 0 |
| Reference 宿主完成日志 | 3 | 0 | 0 |

Core 复用已有的嵌套、跨 await、分叉子任务、并发与幂等释放测试。中间件补测成功响应的状态码读取次数，以及关闭完成日志后的状态和耗时。NLog 在同一后端实例上依次写入完整字段、空白或缺失字段和作用域外事件。Serilog 检查模板冲突、上下文值更新、数值类型，以及根级别关闭但类别阈值被显式调低时的最终过滤。Reference 宿主验证异常处理器写出的 409、500 和超时处理写出的 504 都进入正常完成事件。
