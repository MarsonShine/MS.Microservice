# 当前任务
Status: idle
Task: none
Next: EF Core 升级仍等待 PostgreSQL 提供程序支持。Azure 部署变更继续用 Lab 验证。

Caching 建键的耗时、分配及兼容性检查已完成，见[验收记录](../validation/CACHING-KEY-PERF/summary.md)。

Idempotency 请求指纹、回放和冲突查询的性能优化见[验收记录](../validation/IDEMPOTENCY-LOOKUP-PERF/summary.md)。

Persistence 和 Tasks 的首批优化已完成。[审计缓冲验收](../validation/PERSISTENCE-AUDIT-BUFFERS/summary.md) · [任务终态查询验收](../validation/TASKS-FINALIZATION-PERF/summary.md)

Messaging 的死信摘要、路由键与正文编码已完成第一轮优化，见[同机基准与回归记录](../validation/MESSAGING-PAYLOAD-COSTS/summary.md)。

Media 条码识别和 BiRefNet 抠图的第一批优化见[新旧基准、输出及回归结果](../validation/MEDIA-PIXEL-BUFFERS/summary.md)。

Logging 四个项目的耗时、分配和宿主回归结果见[验收记录](../validation/LOGGING-HOT-PATHS/summary.md)。

最近完成的文档处理与渲染性能检查见[基准与回归记录](../validation/DOCUMENTS-RENDERING-PERF/summary.md)。

ArrayBufferWriter 指南见[示例验证记录](../validation/ARRAY-BUFFER-WRITER-GUIDE/summary.md)。

收件箱 JSON 读取调整见[回归与复测](../validation/INBOX-JSON-BUFFER/summary.md)。

Swagger 与 Threading 性能检查见[代码、基准与回归记录](../validation/SWAGGER-THREADING-PERF/summary.md)。

Notifications 与 Search 性能检查见[基准与回归记录](../validation/NOTIFICATIONS-SEARCH-PERF/summary.md)。

CSV 写入优化见[性能笔记](../../src/MS.Microservice.Excel/docs/csv-performance.md)和[验收记录](../validation/CSV-WRITE-BLOCKS/summary.md)。

此前的 EventSourcing 与 Excel 性能检查见[验收记录](../validation/EVENTSOURCING-EXCEL-PERF/summary.md)。

此前的 EventBus 进程内消费实现见[验收记录](../validation/EVENTBUS-DISPATCH/summary.md)。
