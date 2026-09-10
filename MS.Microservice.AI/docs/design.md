# AI 模块的设计思路

## 为什么区分 Abstractions、Core 和 Provider

业务应表达“聊天、语音、图像”等能力，而不是请求某个 SDK 的私有对象。
Abstractions 定义中立输入输出；Core 解析模型配置并组织策略；Provider 处理端点与数据格式差异。
这让业务可以替换 Provider，但不意味着所有模型能力相同：不支持的能力必须明确失败，不能静默降级。

Core 不是新的业务 Domain。它是 AI 调用基础设施。
QuestionGeneration 则在中立模型客户端之上组织生成、检查、修复与预算，是可选的上层 Harness。

## 为什么共享执行策略，不合并所有解析

Chat 与 Media 遇到超时、限流、网络故障时需要一致的重试规则，
因此共用 [AIHttpExecution](../src/MS.Microservice.AI.Core/AIHttpExecution.cs) 和错误翻译。
请求与正文由每次尝试重建，不能重复发送已经使用过的 HttpRequestMessage。

流式 Chat 已经交付的文本不能收回，所以收到部分输出后不能自动重试整段响应。
它保留独立的流解析、整个流的截止时间和完成标记检查。媒体响应则有二进制、URL 等不同结构。

调用方取消不代表 Provider 不可用，必须先识别调用方取消，再判断超时重试。
成本报告失败也不应把成功响应改为失败；
[AIProductionPipeline](../src/MS.Microservice.AI.Core/AIProductionPipeline.cs)将观测失败与模型调用结果分开。
报告器是尽力而为的观测扩展，不是事务性计费账本。

## 为什么教育图像不放在 AI.Core

场景分组、视觉计划、提示词元素和参考图连续性是具体业务规则。
把它们放进 Core，会让普通聊天调用也依赖教育场景，并使 Qwen 通用注册携带场景适配。
因此它们共同位于 [samples/AI](../../samples/AI/MS.Microservice.Samples.EducationalImages/README.md)；
共享 QuestionGeneration 仍留在模块内。

阅读顺序：Abstractions → 模型解析/路由 → AIProductionPipeline → AIHttpExecution → 具体 Provider。
测试对应这些边界。新增 Provider 时复用共同策略，保留能力和解析差异，避免复制整套失败处理。
