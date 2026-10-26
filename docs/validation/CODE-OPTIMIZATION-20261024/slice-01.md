# Slice 01：AI Provider 共享并发

Status: complete

范围：MS.Microservice.AI/src 中 AI.Core 与内置 Provider 构造链、相关 csproj 友元声明；AI/test；AI 使用说明；本任务状态/证据文档。范围外：响应大小限制、重试策略变更、动态配置、多实例全局配额。

根因：ConcurrencyLimit 属于 Provider，而 Chat/Media 基类按实例各自建 SemaphoreSlim。修复由容器内 AIProviderConcurrency 单例统一持有按 Provider 名隔离的名额，保持现有 AcquireAsync 租约位置及取消/流枚举生命周期。

构造链超过通常 8 个生产文件的审查信号，但都是 internal 类型传递同一依赖，不能拆成行为不一致或不能构建的中间片。公开接口保持不变。

验收：`dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx --no-restore -c Release`。重点是 DI 跨能力预算、Provider/容器隔离、取消等待和流释放；先确认新增回归能在旧实现失败。

结果：新增公共 DI 回归在旧实现 7 项中 5 失败、2 通过（跨能力请求期望 1，实际 2；流占用和取消等待同样失败）。实现后先 7/7 通过，再加入 Provider 隔离用例后运行完整验收：157/157 通过、0 失败、0 跳过，退出码 0。分项：Core 82、OpenAI 31、Qwen 13、DeepSeek 10、QuestionGeneration 21。

复核：共享注册表为容器 singleton、名称不区分大小写，惰性读取实际使用的配置；17 个内部构造链转发依赖，公开接口不变；没有移动租约取得和释放位置。已核对 DI、跨能力、流释放、取消及容器隔离回归。git diff --check 通过。

边界：未调用真实 AI 服务；名额不跨进程共享，不支持动态调整已创建并发门。响应大小治理属于 Slice 02。
