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
