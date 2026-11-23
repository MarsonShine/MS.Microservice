# 当前状态
更新：2026-11-20
Status: idle
Task: none
Next: EF Core 升级仍等待 PostgreSQL 提供程序支持。Azure 部署变更继续用 Lab 验证。
Baseline: 39a704c

EventSourcing 的 Lab 读取适配器和 Excel 的 CSV、工作簿、校验及颜色表已完成性能检查。见[前后基准与验收](validation/EVENTSOURCING-EXCEL-PERF/summary.md)。

EventBus 已优化订阅注册和并发名称查询，保留同名事件的首次类型绑定与处理器去重语义。见[源码图解](../src/MS.Microservice.EventBus/docs/subscription-performance.md)和[验收记录](validation/EVENTBUS-SUBSCRIPTIONS/summary.md)。

EventBus 的进程内发布已接通本包处理器的 `Handle`，见[可运行示例](../src/MS.Microservice.EventBus/samples/basic-usage.md)和[消费验收](validation/EVENTBUS-DISPATCH/summary.md)。

Core 已完成全模块性能检查，函数式链表保留不可变结构、惰性投影与左折叠。见[源码导读](../src/MS.Microservice.Core/docs/performance-review.md)和[验收记录](validation/CORE-PERFORMANCE/summary.md)。

Audio 的 WAV 混音、单声道转立体声和多段合并已降低耗时与托管分配，见[优化笔记](../src/MS.Microservice.Audio/docs/wav-processing-performance.md)和[性能验收](validation/AUDIO-PROCESSING-PERF/summary.md)。

文件格式探测与 WAV 元数据入口的耗时和分配对照，以及其余路径的复现结果，见[Audio 检查验收](validation/AUDIO-INSPECTION-PERF/summary.md)。

Audio 的 MP3 样本格式、跨容器直拷、PCM8 静音和演示流生命周期问题均已修复，见[回归验收](validation/AUDIO-ENCODING-REGRESSIONS/summary.md)。

AspNetCore、Caching 和 Documents 已按模块组织源码和测试，详见[目录整理验收](validation/PROJECT-DIRECTORY-LAYOUT/summary.md)。

.NET 11 官方包已对齐，EF Core 与 CLI 保持兼容的 `10.0.15`，详见[验收记录](validation/NET11-OFFICIAL-PACKAGES/summary.md)。Azure Lab 已验证同一个 `Assistant` 场景可切换两个真实 Chat 部署，见[Azure 验收记录](validation/AZURE-ASSISTANT-LAB/summary.md)。
