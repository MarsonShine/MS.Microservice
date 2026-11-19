# AI 模块的设计思路

## 为什么区分 Abstractions、Core 和 Provider

业务应表达“聊天、语音、图像”等能力，而不是请求某个 SDK 的私有对象。
Abstractions 定义中立输入输出；Core 解析模型配置并组织策略；Provider 处理端点与数据格式差异。
这让业务可以替换 Provider，但不意味着所有模型能力相同：不支持的能力必须明确失败，不能静默降级。

Core 不是新的业务 Domain。它是 AI 调用基础设施。
QuestionGeneration 则在中立模型客户端之上组织生成、检查、修复与预算，是可选的上层 Harness。

## Azure 的资源与模型部署

使用 `AzureCloud.CHAT` 时，每个模型键直接引用本组资源和部署。直接指定模型的业务可以使用这个键发起调用。需要按业务场景配置默认参数时，Core 场景仍可引用同一个键。选定部署后，Azure 聊天适配器根据 `Api` 构建 Responses 或 Chat Completions 请求。Responses 每次发送完整消息并设置 `store: false`，从终止事件取得完成状态和用量。

同一个 Azure OpenAI 模型在不同地区的可用性可能不同，部署名称也由各资源独立设置。业务场景用逻辑别名调用 Core。Azure Provider 再按配置顺序选中首个启用且所属资源也启用的部署，用该资源的终结点和密钥发送请求，并把部署名称写入请求的 `model` 字段。[区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)

部署选择在启动时校验。聊天、图像生成、图像编辑、语音合成和语音转录各自配置候选列表，因此同一个资源可以服务多种能力，也可以按能力选择不同地区。每次调用及其重试使用同一部署。需要调整目标资源时，更新候选项并重启宿主。Azure Provider 复用 Core 的 HTTP、SSE 和媒体响应解析。资源寻址、认证和 Azure 请求格式由 Azure 模块适配。[Azure 配置](azure.md)

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

## 响应读取的资源边界

[AIResponseReader](../src/MS.Microservice.AI.Core/AIResponseReader.cs) 在公共 Chat/Media 传输层读取正文。请求使用 `ResponseHeadersRead`，收到状态后分别应用成功正文或错误正文预算，再交给原有解析器。正文仍使用 .NET `HttpContent.LoadIntoBufferAsync` 的有界缓冲和取消能力；由于它会跳过已缓冲内容的新限额，随后将已有缓冲写入只计数字节的 sink，核对实际长度而不再分配一份正文。`Content-Length` 不作为实际长度的证明。

成功 SSE 响应不整包缓冲。行读取器向 `StreamReader` 每次请求一个字符，在追加字符前检查上限；以延迟消费 LF 的状态保留 CRLF 语义，遇到 CR 即交付已完成的行，不等待下一次网络输入。`StreamReader` 继续负责解码和 BOM，行读取器只负责边界，现有单 `data:` 行解析协议不变。

正文和行超限都在进入重试分类前翻译成不可重试的 `ResponseInvalid`，异常保留请求上下文但不携带响应正文。读取继续使用既有尝试或整个流的 linked token；响应与并发租约仍由原有作用域释放。配置和默认值见[响应读取预算](../README.md#响应读取预算)。

## 为什么教育图像不放在 AI.Core

场景分组、视觉计划、提示词元素和参考图连续性是具体业务规则。
把它们放进 Core，会让普通聊天调用也依赖教育场景，并使 Qwen 通用注册携带场景适配。
因此它们共同位于 [samples/AI](../../samples/AI/MS.Microservice.Samples.EducationalImages/README.md)；
共享 QuestionGeneration 仍留在模块内。

阅读顺序：Abstractions → 模型解析/路由 → AIProductionPipeline → AIHttpExecution → 具体 Provider。
测试对应这些边界。新增 Provider 时复用共同策略，保留能力和解析差异，避免复制整套失败处理。
