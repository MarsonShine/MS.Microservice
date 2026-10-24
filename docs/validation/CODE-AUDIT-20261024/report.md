# 代码级优化与技术模块评估

日期：2026-10-24。源码基线：`883b907c733d75093f79a0fa20639023ac3167fd`。

本报告检查共享组件、参考宿主及相关调用和测试，不扩展业务功能。采用模块巡检与重点路径追踪，不声称逐行覆盖全仓库；运行行为与静态推导分别标注。实现、依赖和部署均未修改。

项目的技术能力已经较完整。下一轮投入应先修正并发预算、资源所有权、消息租约和请求完成语义，再补可复用组件的兼容性验证。没有证据支持立即增加一批通用基础框架。

优先级含义：P1 为受影响组件启用时优先处理的正确性或资源风险；P2 为近期可靠性、维护性改善；条件性能力只有满足部署或消费前提才升级。优先级不表示所有问题都已在真实环境复现。

## 现有代码的主要优化项

### 1. P1：AI 的 Provider 并发预算没有跨能力共享

**证据。** [AIOptions.cs](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/AIOptions.cs) 第 77–78 行将 ConcurrencyLimit 定义为每个 Provider 的最大并发，默认 8；[Chat 基类](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/OpenAICompatibleChatProviderBase.cs) 第 48 行和 [Media 基类](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/OpenAICompatibleMediaProviderBase.cs) 第 47 行各自创建实例私有 SemaphoreSlim。[OpenAI 注册](../../../MS.Microservice.AI/src/MS.Microservice.AI.OpenAI/OpenAIBuilderExtensions.cs) 第 16–20 行为 Chat、TTS、ASR、生成图片、编辑图片注册五个不同的单例。

**影响与根因。** 配置归属是 Provider，执行预算归属却是能力实例。同时使用上述五种能力时，代码结构允许合计占用 40 个名额，而非 Provider 总计 8；实际并发还受外部限制影响。窗口限流和熔断不能替代这个并发契约。

**建议。** 在 AI.Core 内按规范化 Provider 身份共享租约，复用已有 Threading 租约工具。明确流式枚举、重试等待、调用取消时的名额生命周期。保留不同 Provider 的隔离，不需要新建一套限流框架。

**验收缺口。** 现有 [AIConcurrencyLeaseTests](../../../MS.Microservice.AI/test/MS.Microservice.AI.Core.Tests/AIConcurrencyLeaseTests.cs) 覆盖单实例释放。新增测试应从同一 DI 容器发起 Chat 和 TTS，设置上限 1，用阻塞 HTTP handler 验证总活跃数，再覆盖不同 Provider、等待取消及提前结束流枚举。本次未执行该跨能力复现。

### 2. P1：AI 成功正文、错误正文和 SSE 缺少应用级读取预算

**证据。** Chat 基类第 78 行、Media 基类第 239 行使用 ResponseContentRead，解析前已经整包缓冲。[TTS 基类](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/OpenAICompatibleTtsProviderBase.cs) 第 51 行再获取 byte[]；[AIProviderErrors](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/AIProviderErrors.cs) 第 19 行完整读取错误字符串。Chat 基类第 313 行以 ReadLineAsync 解析 SSE，没有应用级单行限制。[现有 PayloadLimit](../../../MS.Microservice.AI/src/MS.Microservice.AI.Core/AIProductionOptions.cs) 第 79 行起约束的是输入。

**影响与根因。** 外部响应是独立的资源边界。大响应、错误代理返回的大页面、没有换行的 SSE 都可能在超时前显著消耗内存；并发会放大影响。输入预算、token 数和框架默认缓冲上限不能代替合理的响应字节预算。

**建议。** 增加小型有界读取能力，分别配置正常正文、错误正文、SSE 行/事件的预算。先收响应头，再在实际读取过程中计数，不能只信任 Content-Length。超限应终止读取、释放响应并分类为不可重试的无效响应。TTS 可另外提供流式输出入口，但需要保留当前 byte[] API 的所有权和兼容语义。

使用 ResponseHeadersRead 后必须继续让取消和超时覆盖正文读取；Microsoft 文档明确说明，此模式下 HttpClient.Timeout 和 MaxResponseContentBufferSize 不会自动覆盖后续正文读取。[官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)

**验收缺口。** 无 Content-Length 的分块超限正文、虚假长度、巨大错误正文、无换行 SSE；验证在预算附近停止读取、名额释放且不重试。现有流式路径已经有超时与取消，不应重新实现这些能力。本次未进行内存压力测试。

### 3. P1（慢数据库或长批次触发）：Outbox 在发布前可能整批租约过期

**证据。** [OutboxStore](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.SelfManaged.EFCore/OutboxStore.cs) 第 13 行只取一次 now；第 14–33 行先选候选，再逐条 UPDATE 和 SELECT，成功取得 N 条约需 1+2N 次数据库往返。全部到期时间均为同一个 now + PublishingLease。[SelfManagedPublisher](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.SelfManaged.EFCore/SelfManagedPublisher.cs) 第 16 行等待整批领取完成，第 21 行续租失败就跳过；续租条件拒绝已经过期的所有者。[选项](../../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.SelfManaged.EFCore/SelfManagedOptions.cs) 允许批次 1000、租约最短 1 秒。

**影响与根因。** 认领开销和队列等待消耗了尚未开始发布的租约。若整批认领耗时超过租约，可能反复领取又全部跳过，无法取得发布进展；正常路径也有较多数据库往返。后续逐条发布的等待同样需要纳入租约生命周期。

**建议。** 在存储认领与发布流水线处理：按可立即处理的容量取得小批，或采用 Provider 支持的原子批量认领并返回记录。控制未开始工作的持有量；保留确认发布、token 校验、过期所有者保护及不同数据库支持。仅增大租约不是根因修复，不能为减少 SQL 破坏所有权隔离。

**验收缺口。** 模拟认领过程本身跨越租约，再验证仍能推进、旧所有者不能提交、多发布者不越权。现有 [OutboxStoreTests](../../../MS.Microservice.Messaging/test/MS.Microservice.Messaging.SelfManaged.Tests/OutboxStoreTests.cs) 覆盖领取后的过期。本项为代码条件推导，未执行慢 PostgreSQL 或吞吐测试，不给出性能提升比例。

### 4. P2：旧音频入口的资源所有权不完整

**证据。** [AudioProcessor](../../../src/MS.Microservice.Audio/AudioProcessor.cs) 第 232 行创建文件 reader 而无 using/finally；第 651 行的 WriteConvertedAudio 只释放 writer。同格式 WAV 转 WAV 的成功路径也不会确定性释放输入 reader。第 194 行的混音，以及 [AudioProcessorExtensions](../../../src/MS.Microservice.Audio/AudioProcessorExtensions.cs) 第 100 行的文件转流，都在进入 try 前批量打开资源，中间失败时前面已经打开的资源不受清理保护。

**建议。** 在取得资源的层次明确所有权，逐项打开与部分失败清理处于同一保护范围；成功、转换失败和构造失败都释放输入。不需要再建一个音频组件，新的 AudioEditor 已有较完整生命周期管理。

**验收缺口。** 同格式转换后立即以 FileShare.None 打开输入；输出写入失败后也能打开；第二个输入损坏时第一个可独占打开。现有 [AudioProcessorTests](../../../test/MS.Microservice.Audio.Tests/AudioProcessorTests.cs) 第 286 行只检查输出，第 43 行附近吞掉清理异常，不能证明没有句柄泄漏。本次未跑音频原生依赖和句柄复现。

### 5. P2：HTTP 完成日志可能记录异常处理前的 200

**证据。** [ReferenceHost](../../../samples/Reference/MS.Microservice.Reference.Web/ReferenceHost.cs) 第 122–123 行先 UsePlatformHttp，后 UseMsRequestLogging；[ServiceHost](../../../src/MS.Microservice.AspNetCore/ServiceHost.cs) 第 54 行在外层安装异常处理器。[日志中间件](../../../MS.Microservice.Logging/src/MS.Microservice.Logging.AspNetCore/MsRequestLoggingMiddleware.cs) 第 35–42 行在 finally 中立即读取 Response.StatusCode。异常返回时，内层日志先执行，外层才生成最终错误响应。

**影响与建议。** 部分最终 409/500 请求可能记录为 200，排障和错误率判断受影响。应让完成日志覆盖异常处理后的结果，并明确未处理异常、取消、响应已开始的行为。适当调整宿主顺序与组件契约，不能对所有异常直接硬编码 500；需要保留请求作用域清理。

**已有验证。** [MsRequestLoggingMiddlewareTests](../../../MS.Microservice.Logging/test/MS.Microservice.Logging.AspNetCore.Tests/MsRequestLoggingMiddlewareTests.cs) 第 105 行明确断言抛异常后记录 200。当前源码重建后该类 4/4 通过，说明已有测试固定了这一语义，不表示问题已修复。缺少包含异常处理器的真实管线测试，应核对最终 HTTP 状态与日志一致。

### 6. P2：Tasks 终态落库失败只能等下次启动恢复

**证据。** [JobExecutor](../../../MS.Microservice.Tasks/src/MS.Microservice.Tasks/JobExecutor.cs) 第 145–169 行在工作结束后写终态；失败只记日志，随后释放容量、移出活动集合。第 45 行的 Running 扫描只在启动时执行，日常 Tick 只取 Queued。[EfCoreJobStore](../../../MS.Microservice.Tasks/src/MS.Microservice.Tasks.EFCore/EfCoreJobStore.cs) 第 242 行人工重试也不接受 Running。[设计文档](../../../MS.Microservice.Tasks/docs/execution-design.md) 明确保留了等待启动恢复的限制，因此这是可用性增强，不是违反现有承诺的实现错误。

**建议。** 为已经结束的执行增加有界终态写入重试和在线补偿，按 AttemptToken 确认提交结果；只重试状态持久化，不重新执行 handler。持久化结果不确定时不得伪造成功。补偿队列自身也要有容量和失败观测。

**验收缺口。** handler 成功、终态写入短暂失败、同一宿主数据库恢复后终态收敛，且 handler 只执行一次。已有 [JobExecutorTests](../../../MS.Microservice.Tasks/test/MS.Microservice.Tasks.Tests/JobExecutorTests.cs) 覆盖停机与启动恢复，没有覆盖这一在线恢复路径。

### 7. P2：收敛两套 HTTP 辅助入口的公共契约

**证据。** [HttpClientExtensions](../../../src/MS.Microservice.Core/Http/HttpClientExtensions.cs) 第 27–45 行不接受 CancellationToken，泛型入口消费响应但没有释放；URL 直接追加问号，无法正确合并已有查询和 fragment。第 63 行强行解引用可能缺失的 Content-Type，第 70 行把 JSON 错误转为 default。相比之下，[LogHttpClient](../../../src/MS.Microservice.Core/Net/Http/LogHttpClient.cs) 已处理请求隔离、响应释放、取消传播、错误分类和 URL 合并。

**建议。** 优先复用一套 URL 合并、读取和资源释放机制，给公共旧接口增加兼容迁移路径。旧接口遇到无效 JSON 返回 null 已被 [HttpClientExtensionsTests](../../../test/MS.Microservice.Core.Tests/Net/Http/HttpClientExtensionsTests.cs) 和 [JsonReuseRegressionTests](../../../test/MS.Microservice.Core.Tests/Serialization/JsonReuseRegressionTests.cs) 固定，不能直接改成抛异常而称为无行为变化的重构。返回原始 HttpResponseMessage 的重载仍由调用方释放。

**消费者与收益。** 仓库搜索发现旧扩展的直接调用集中在测试，Lab 注册了 LogHttpClient；但旧扩展为公共 API，外部消费者未知，不能据此删除。合理收益是去掉一份重复 URL/响应处理逻辑，保留薄兼容入口；具体净删除量应在兼容方案确定后计算。

**验证。** 当前源码重建后 HTTP 与 JSON 复用相关 24/24 通过。新验收需要覆盖已有查询、fragment、缺 Content-Type、取消、泛型读取后的响应释放，并保留旧 null 契约或明确版本迁移。

### 8. P2：通用认证组件移出示例宿主的具体权限策略

**证据。** [ExternalIdentity](../../../src/MS.Microservice.AspNetCore/ExternalIdentity.cs) 第 18–19 行内置 profiles.manage、messaging.manage，第 59–62 行固定注册 Manage、MessagingOperations。JWT 验证、通用 scope 判定与 Reference 的策略定义被放在一个注册入口。

**建议。** 通用包保留身份验证、subject/scope 规则；策略名称和对应权限移到 Reference 组合根。必要时复用 requirement/handler，但无需引入业务 RBAC 或立即拆新程序集。这里收益是减少消费耦合，不预期显著减少行数；现有宿主的授权效果必须保持。

**验收。** 保持 issuer、audience、签名、subject、scope 测试；新增仅配置通用认证的最小消费者，确认无需携带 Reference 权限名即可使用。本次未执行相关身份测试。

## 值得新增的代码级能力

| 建议 | 紧迫度与最小落点 | 契约与验收重点 |
| --- | --- | --- |
| 有界 HTTP 响应读取 | 优先；先服务 AI，多消费者成立后再成为独立 HTTP 小组件 | 实际字节、错误正文、SSE 行/事件限制，取消、超时及资源释放；不要求 AI 依赖整个通用 Core |
| 公共 API 兼容性与包消费者契约校验 | 优先于继续扩张公共接口；放 build/测试层 | 与受支持版本基线比较；刻意删除公开成员时检查失败；从打出的包编译并运行关键消费者 |
| Tasks 数据保留与分批清理 | 任务中心持续运行前补；属于现有 Tasks 扩展 | 按终态、完成时间、Scope 清理；不删除 Queued/Running；与 Retry 竞争安全；明确删除后相同 JobId 的去重期限 |
| Tasks 执行归属与持久租约 | 多执行宿主或滚动发布前必需；单实例时无需全面建设 | owner、续租、失联与 fencing；第二宿主不能恢复第一宿主仍在运行的任务；不等同“再引入调度器” |
| 缓存跨实例失效通知 | 多实例且本地 TTL 无法满足新鲜度要求时 | 通知丢失、重连和旧回源回填；必要时加版本协议；强一致读取仍可绕过缓存 |
| 密钥 ID 与密钥来源适配 | 持久密文需要不停机轮换时 | 新封套版本、旧密文可读期、未知 key ID 与撤销；不自建加密算法 |

**兼容性校验的缺口。** [消费验证脚本](../../../build/validate-module-consumption.ps1) 已做源码导出、打包、程序集加载，Messaging 还有实际消费者；这些值得保留。但它们主要验证当前版本能否消费，不比较历史公共 API。[PublicNamingCompatibilityTests](../../../test/MS.Microservice.Architecture.Tests/PublicNamingCompatibilityTests.cs) 只覆盖局部兼容名称，仓库尚未配置 ApiCompat/PackageValidation。[官方 Package Validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview) 可比较受支持的包基线；尚无发布包时先建立明确的程序集/API 基线。签名兼容检查不能替代错误、取消、序列化行为的消费者测试。

**Tasks 生命周期的缺口。** [IJobStore](../../../MS.Microservice.Tasks/src/MS.Microservice.Tasks/JobContracts.cs) 没有清理契约，Job 持久保存 PayloadJson 和结果引用；[模型](../../../MS.Microservice.Tasks/src/MS.Microservice.Tasks.EFCore/JobModelBuilderExtensions.cs) 已有 Attempt 级联删除，可以复用。清理必须有批次上限，结果文件只能经过宿主明确提供的生命周期接口处理，不能根据任意引用删除。多实例方面，启动扫描当前会处理所有 Running，文档明确限定单执行宿主；继续单实例时可先增加第二宿主拒绝启动的保护。

**已声明的边界不是遗漏缺陷。** [缓存设计](../../../src/MS.Microservice.Caching/docs/design.md) 已明确其他实例 L1 不会立即失效，以及旧加载回填窗口；[AES 格式说明](../../../src/MS.Microservice.Core/Security/Cryptology/authenticated-encryption.md) 已说明 v1 是格式版本而非 key ID。两项应按实际需求启动，不列为无条件紧急工作。

## 次级观察与不建议扩张的方向

- [ArchitectureTests](../../../test/MS.Microservice.Architecture.Tests/ArchitectureTests.cs) 第 253 行的 ProductionWeb_ShouldNot_DependOn_LabHost 取的是 Lab 程序集，检查对象不正确。应修正或删除这个空护栏；[ComponentBoundaryTests](../../../test/MS.Microservice.Architecture.Tests/ComponentBoundaryTests.cs) 已有有效 Reference 依赖图检查，不能说仓库没有架构边界验证。
- [RetryContext](../../../src/MS.Microservice.Core/Common/Advance/Resilience/RetryContext.cs) 第 10 行仍用 UTC 墙钟差计算持续时间；时钟校正会影响超时策略。后续可使用已有 TimeProvider 模式的单调时间戳，不需要新造时钟模块。保留公开 StartTime 的兼容语义；测试墙钟跳变和长延迟取消。[TimeProvider 官方说明](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview) 区分了 UTC 时间与用于计时的 timestamp。
- Lab 的 [事件投影](../../../samples/Lab/MS.Microservice.Lab.Infrastructure/EventSourcing/Orders/OrderReadModelProjector.cs) 第 37–52 行逐事件查库、整批末尾保存；同一新聚合积压多个事件时，数据库查询看不到之前 Added 的实体，存在重复跟踪风险。[ReadAllAsync](../../../src/MS.Microservice.EventSourcing/EventSourcingAbstractions.cs) 也没有批上限。可补批内身份缓存和分页契约，但这属于 Lab 路径，不是 Reference 生产链路缺陷；本次未运行持久化投影复现。
- 不建议因为文件大或抽象只有一个实现就拆包、删除接口。Core 的教学和公共兼容路径必须分别核对用途，不能仅凭仓库内搜索结果判断为死代码。
- 不建议重复引入消息总线、Outbox/Inbox、HTTP 幂等、任务调度、缓存框架、日志框架、限流/熔断、统一进程执行或 AI 配置管理。仓库已实现这些能力。
- 本次没有证据要求立即引入 Elasticsearch、通用分布式锁平台、Saga、多租户、服务注册中心或全仓 Native AOT 改造。Search 的本地索引、版本与分页，S3 的条件写/预算，Documents/Media 的资源预算，以及 Threading 的 Singleflight/租约均应保留并按实际需求演进。

## 建议执行顺序

1. 按实际启用模块修复 AI 共享并发、响应读取预算和 Outbox 租约进展性；音频资源释放与日志最终状态可作为各自独立的小修复。
2. 补 API 基线与关键消费者契约，再收敛 HTTP 公共入口、认证注册职责；完善 Tasks 在线终态收敛及保留清理。
3. 确认多实例、缓存新鲜度、密钥轮换需求后，再做对应扩展。每次只维护一个实现任务及其独立验收。

## 验证与边界

当前源码执行（Windows，实际解析 SDK 10.0.402）：

```powershell
dotnet test test/MS.Microservice.Core.Tests/MS.Microservice.Core.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~HttpClientExtensionsTests|FullyQualifiedName~LogHttpClientTests|FullyQualifiedName~JsonReuseRegressionTests' --verbosity minimal
dotnet test MS.Microservice.Logging/test/MS.Microservice.Logging.AspNetCore.Tests/MS.Microservice.Logging.AspNetCore.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~MsRequestLoggingMiddlewareTests' --verbosity minimal
```

输出摘要：Core 24 通过、0 失败、0 跳过；Logging 4 通过、0 失败、0 跳过。两个命令均重建相关源码后执行。它们证明既有回归用例通过，不覆盖新识别的全部故障场景，更不表示上述问题已经修复。

本次没有运行全解决方案测试、真实 PostgreSQL/RabbitMQ 故障矩阵、完整音频原生测试、性能压测、部署或漏洞审计。原状态中的远端 CI 失败未在本次调查或解决。不声明性能提升数值、全仓安全性或生产就绪。

交付结构检查及额外既有产物测试记录见 [任务与验收](task.md)。
