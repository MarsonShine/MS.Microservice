# 当前状态
更新：2026-11-20
Status: idle
Task: none
Next: EF Core 升级仍等待 PostgreSQL 提供程序支持。Azure 部署变更继续用 Lab 验证。
Baseline: 4ae3623

Core 已完成全模块性能检查，函数式链表保留不可变结构、惰性投影与左折叠。见[源码导读](../src/MS.Microservice.Core/docs/performance-review.md)和[验收记录](validation/CORE-PERFORMANCE/summary.md)。

Audio 的 WAV 混音、单声道转立体声和多段合并已降低耗时与托管分配，见[优化笔记](../src/MS.Microservice.Audio/docs/wav-processing-performance.md)和[性能验收](validation/AUDIO-PROCESSING-PERF/summary.md)。

文件格式探测与 WAV 元数据入口的耗时和分配对照，以及其余路径的复现结果，见[Audio 检查验收](validation/AUDIO-INSPECTION-PERF/summary.md)。

Audio 的 MP3 样本格式、跨容器直拷、PCM8 静音和演示流生命周期问题均已修复，见[回归验收](validation/AUDIO-ENCODING-REGRESSIONS/summary.md)。

AspNetCore、Caching 和 Documents 已按模块组织源码和测试，详见[目录整理验收](validation/PROJECT-DIRECTORY-LAYOUT/summary.md)。

.NET 11 官方包已对齐，EF Core 与 CLI 保持兼容的 `10.0.15`，详见[验收记录](validation/NET11-OFFICIAL-PACKAGES/summary.md)。Azure Lab 已验证同一个 `Assistant` 场景可切换两个真实 Chat 部署，见[Azure 验收记录](validation/AZURE-ASSISTANT-LAB/summary.md)。
