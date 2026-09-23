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

## 请求作用域为什么使用 Holder

`MsRequestLoggingMiddleware` 每次请求调用 `RequestLogScope.Push`，请求结束时释放作用域。只在当前执行流恢复 `AsyncLocal.Value`，不能使此前继承该值的子任务失效；子任务可能在请求结束后继续把旧的 RequestId 等字段写入日志。现在每次 Push 创建可清空的 Holder，释放时先清空当前 Holder，再恢复外层 Holder。详细的执行流示例、三种写法的区别和使用条件见[技术文章](async-local-holder-lifetime.md)。

这一生命周期保证有分配成本：Windows x64、.NET 10.0.12、Release 下，[基准程序](../benchmarks/MS.Microservice.Logging.Core.Benchmarks/Program.cs)按每场景 7 轮、每轮 20 万次测得普通 Push/Dispose 从直接存值时的 100.8 ns、104 B/次变为 117.3 ns、136 B/次。耗时有波动，分配差值在重复运行中稳定。这不是完整请求或日志后端的吞吐数据。NLog XML 与 Serilog 配置的 Native AOT 发布兼容性仍需单独验证。
