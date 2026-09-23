# Logging 模块的设计思路

日志是一项横切能力，不应让业务依赖 NLog 或 Serilog 的专用类型。
Core 提供请求上下文；AspNetCore 在请求进入和结束时维护它；
后端适配器把同一份上下文映射到日志系统。

## 上下文与生命周期

请求上下文用于串联 requestId、请求耗时等诊断数据，不等于经过身份验证的用户声明。
调用方传来的诊断头不能用于授权。上下文在请求结束时必须恢复或释放，避免并发请求互相污染。

中间件位于 HTTP 层，Worker 不应为了写日志引入 Web Host。
NLog/Serilog 配置由宿主决定，Core 不默认选择后端。
适配器负责字段映射和宿主接入，不负责业务事务。

## 日志与可观测性组件的分工

Logging 关注日志字段、输出与后端；根 src 下的 Observability 负责 Trace/Metrics 采集与导出。
两者可以组合，也可以按需使用。消息 Id 适合日志定位，不适合成为无限增长的指标标签。

默认诊断使用标识、类型、耗时和结果，不依靠消息正文、密码、密钥或提示词排错。
异常文本也可能包含上游响应，因此不能以“异常日志”作为绕过内容边界的理由。

阅读顺序：Core 上下文 → AspNetCore 中间件 → 选择的后端适配器 → 相应测试。
详细配置见[模块 README](../README.md)。

## 请求上下文的分配成本

`MsRequestLoggingMiddleware` 每次请求都会调用 `RequestLogScope.Push`，并在请求结束时释放作用域。原实现把 `RequestLogContext` 放进单独的 `ScopeState`，再创建一个 `PopWhenDisposed` 保存先前的作用域。`ScopeState` 没有附加行为，因此每次进入作用域都会多分配一个对象；嵌套作用域也有同样成本。

现在 `AsyncLocal` 直接保存 `RequestLogContext`，释放对象仍保存先前的引用并负责恢复。这保留了异步流隔离、嵌套恢复及重复释放的行为。没有引入反射或动态代码。这里选择删除多余的包装对象，而没有改变上下文或日志后端的接口。

在 Windows x64、.NET 10.0.12、Release 下，从模块根目录运行 `dotnet run --project benchmarks/MS.Microservice.Logging.Core.Benchmarks -c Release`。该[基准程序](../benchmarks/MS.Microservice.Logging.Core.Benchmarks/Program.cs)对复用的上下文执行每场景 7 轮、每轮 20 万次，取中位数：

| 场景 | 修改前 | 修改后 |
| --- | ---: | ---: |
| 普通 Push/Dispose | 117.0 ns、128 B/次 | 100.8 ns、104 B/次 |
| 嵌套 Push/Dispose | 130.7 ns、200 B/次 | 122.0 ns、176 B/次 |
| 读取 Current | 19.5 ns、0 B/次 | 17.4 ns、0 B/次 |

分配差值在独立重复运行中保持 24 B/次；耗时受运行环境影响，嵌套场景的多次结果有波动。这些数字仅覆盖作用域操作，不代表完整 HTTP 请求或 NLog/Serilog 输出的吞吐量。后续若要继续处理请求路径，应先用包含真实后端的负载确定成本分布。NLog 的 XML 配置和 Serilog 的配置读取没有纳入此次改动，使用这些路径发布 Native AOT 前仍需单独验证。
