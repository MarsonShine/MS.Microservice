# AI.Core

AI.Core 是调用基础设施：模型配置解析、按能力路由、限流/熔断/报告管线、
公共 HTTP 执行及错误翻译。它依赖 AI.Abstractions、轻量 Threading 和必要的 Microsoft.Extensions 包，
不依赖教育图像场景、Reference、Lab 或通用 Core。

宿主通过 AddMicroserviceAI(configuration) 注册，再选择 AddOpenAI/AddDeepSeek/AddQwen 等 Provider。
凭据、模型、超时和配额由宿主提供；不支持的能力会明确失败。
按能力使用 IAIChatClient、IAITtsClient 等接口，不把业务规则塞进通用 Provider。

`ConcurrencyLimit` 是同一 DI 容器内同名内置 Provider 的总并发上限，普通聊天、流式聊天、TTS、ASR 和图像能力共享名额。Provider 名称不区分大小写，不同 Provider 和不同根容器各自计数；此限制不跨进程共享，配置也不会动态调整已创建的名额。

请求使用 [信号量租约](../../../src/MS.Microservice.Threading/README.md#异步名额租约)管理名额：等待取消不消耗名额，初始化、日志或响应清理抛出异常也归还名额；重试期间仍持有名额，流式响应在枚举器释放后才归还。调用方提前停止读取流时应释放枚举器（`await foreach` 自动处理）。

[模块设计](../../docs/design.md)解释共享重试与独立解析、取消分类、成本报告隔离，
以及为什么教育图像迁到 samples。详细配置见[模块 README](../../README.md)。
