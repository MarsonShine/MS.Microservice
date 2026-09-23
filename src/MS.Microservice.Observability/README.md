# 可观测性组件

AddMsOpenTelemetry(configuration) 注册 HTTP/runtime 观测，并采集应用 ActivitySource、
MS.Microservice.Messaging、Wolverine、MS.Microservice.AI。默认不启用任何导出器。
宿主通过 OpenTelemetry:ConsoleExporterEnabled 或 OtlpExporterEnabled 显式开启；
OTLP 地址和凭据使用标准 OTEL_EXPORTER_OTLP_* 环境变量注入。

自研实现提供 messaging.operations、messaging.operation.duration 和 messaging.stored_messages。
结果标签区分 published、consumed、duplicate、busy、dead_letter、lease_lost 等；
stored_messages 按 provider/queue/state 标识未完成与死信记录，默认 30 秒采样。
指标不以 MessageId 为标签，避免无限增加时间序列。日志/Trace 可用 MessageId、CorrelationId 定位单条消息。
数据库中断时积压指标保留上次采样值，结合 readiness 和存储错误日志判断，不能将旧值视作实时状态。

Wolverine 使用原生恢复和执行指标。采集 Meter 使用 Wolverine*，ActivitySource 使用 Wolverine；
详见 [官方观测说明](https://wolverinefx.net/guide/logging)。
不能把 Handler 返回视作原生事务已经提交，自定义业务成功计数以数据库结果为准。

## 时长指标的标签分配

`PlatformMetrics` 为 outbox 发布和 inbox 处理记录时长时，会附带一个固定的 `outcome` 标签。原实现每次 `Histogram.Record` 都创建单元素 `KeyValuePair[]`，即使没有指标监听器也会分配。四种标签值由代码中的固定分支决定，因此现在每种值只创建一次标签数组，记录时复用；`failed` 可同时用于 outbox 和 inbox。指标名、计数器、时长值和标签内容不变，也没有增加反射或动态代码。

[微基准](../../benchmarks/MS.Microservice.Observability.Benchmarks/Program.cs)在 Windows x64、.NET 10.0.12、Release 下对同一 `PlatformMetrics` 实例执行五轮、每轮 200,000 次，取中位数。优化前源码是提交 `6dec9ab`。监听器场景只启用 `MeterListener` 并接收测量，不包含实际导出器的序列化或 I/O。

| 场景 | 修改前 | 修改后 | 分配修改前 | 分配修改后 |
| --- | ---: | ---: | ---: | ---: |
| 无监听器，inbox processed | 60.2 ns/次 | 50.0 ns/次 | 40 B/次 | 0 B/次 |
| 无监听器，outbox dead letter | 121.9 ns/次 | 72.9 ns/次 | 40 B/次 | 0 B/次 |
| 有监听器，inbox processed | 112.7 ns/次 | 69.4 ns/次 | 40 B/次 | 0 B/次 |
| 有监听器，outbox dead letter | 138.2 ns/次 | 107.0 ns/次 | 40 B/次 | 0 B/次 |

可运行 `dotnet run --project benchmarks/MS.Microservice.Observability.Benchmarks/MS.Microservice.Observability.Benchmarks.csproj -c Release` 复测。固定标签的数组只在类型初始化时分配；若未来标签改成按请求生成，应重新评估缓存键和基数，不应把动态值加到这些共享数组。
