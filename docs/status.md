# 当前状态
更新：2026-11-23
Status: idle
Task: none
Next: EF Core 升级仍等待 PostgreSQL 提供程序支持。Azure 部署变更继续用 Lab 验证。
Baseline: 56f8ba6

[ArrayBufferWriter 使用指南](basic-concept/array-buffer-writer.md)收录五组可运行示例，可对照收件箱的缓冲区复用与 JSON 输出阅读。[示例验证记录](validation/ARRAY-BUFFER-WRITER-GUIDE/summary.md)

收件箱读取通知和已读回执时，在复用缓冲区内完成解析并返回值和单文件字节数。[代码对照](../src/MS.Microservice.Notifications/docs/notification-performance.md#为什么要在读取方法内把字节变成对象) · [回归与复测](validation/INBOX-JSON-BUFFER/summary.md)

Swagger 的服务注册已按容器去重，Threading 的同键等待者直接复用在途任务，批处理按整数区间分发下标。见[Swagger 评审笔记](../src/MS.Microservice.Swagger/docs/performance-review.md)、[Threading 评审笔记](../src/MS.Microservice.Threading/docs/performance-review.md)和[性能及兼容性验收](validation/SWAGGER-THREADING-PERF/summary.md)。

Notifications 的收件箱分页、回执、模板和短信参数，以及 Search 的长文档摘要、指纹和分页签名已完成优化。见[通知笔记](../src/MS.Microservice.Notifications/docs/notification-performance.md)、[搜索笔记](../src/MS.Microservice.Search/docs/search-performance.md)和[同机基准与兼容性验收](validation/NOTIFICATIONS-SEARCH-PERF/summary.md)。

CSV 写入器已支持跨行复用字符块、自定义分隔符查找表复用和按引号密度转义，行末写入触发取消后会停止枚举。见[源码与示例](../src/MS.Microservice.Excel/docs/csv-performance.md)和[基准与回归验收](validation/CSV-WRITE-BLOCKS/summary.md)。

EventSourcing 的 Lab 读取适配器和 Excel 的 CSV、工作簿、校验及颜色表已完成性能检查。见[前后基准与验收](validation/EVENTSOURCING-EXCEL-PERF/summary.md)。

EventBus 已优化订阅注册和并发名称查询，保留同名事件的首次类型绑定与处理器去重语义。见[源码图解](../src/MS.Microservice.EventBus/docs/subscription-performance.md)和[验收记录](validation/EVENTBUS-SUBSCRIPTIONS/summary.md)。

EventBus 的进程内发布已接通本包处理器的 `Handle`，见[可运行示例](../src/MS.Microservice.EventBus/samples/basic-usage.md)和[消费验收](validation/EVENTBUS-DISPATCH/summary.md)。

Core 已完成全模块性能检查，函数式链表保留不可变结构、惰性投影与左折叠。见[源码导读](../src/MS.Microservice.Core/docs/performance-review.md)和[验收记录](validation/CORE-PERFORMANCE/summary.md)。

Audio 的 WAV 混音、单声道转立体声和多段合并已降低耗时与托管分配，见[优化笔记](../src/MS.Microservice.Audio/docs/wav-processing-performance.md)和[性能验收](validation/AUDIO-PROCESSING-PERF/summary.md)。

文件格式探测与 WAV 元数据入口的耗时和分配对照，以及其余路径的复现结果，见[Audio 检查验收](validation/AUDIO-INSPECTION-PERF/summary.md)。

Audio 的 MP3 样本格式、跨容器直拷、PCM8 静音和演示流生命周期问题均已修复，见[回归验收](validation/AUDIO-ENCODING-REGRESSIONS/summary.md)。

AspNetCore、Caching 和 Documents 已按模块组织源码和测试，详见[目录整理验收](validation/PROJECT-DIRECTORY-LAYOUT/summary.md)。

.NET 11 官方包已对齐，EF Core 与 CLI 保持兼容的 `10.0.15`，详见[验收记录](validation/NET11-OFFICIAL-PACKAGES/summary.md)。Azure Lab 已验证同一个 `Assistant` 场景可切换两个真实 Chat 部署，见[Azure 验收记录](validation/AZURE-ASSISTANT-LAB/summary.md)。
