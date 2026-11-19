# Azure 配置参数

[Azure 接入指南](azure.md)提供一份包含两个聊天部署、语音合成和文生图的完整配置。部署映射集中在 `AzureCloud`，场景与调用策略放在 `AI`。宿主可以从两组配置中分别调整部署目标、业务默认值和请求预算。

## `AzureCloud` 的资源与部署

`AzureCloud.CHAT` 和 `AzureCloud.IMAGE` 各有自己的 `Resources` 和 `Models`，资源名和模型键按不区分大小写的方式查找。配置了 `CHAT`，聊天就从该组解析部署。配置了 `IMAGE`，图像生成和编辑都会从该组解析部署。这两组路由独立查找资源。同一 Azure 资源可用于多种能力，宿主可将同一个密钥注入相应的配置路径。

| 路径（相对于 `AzureCloud.CHAT` 或 `AzureCloud.IMAGE`） | 默认值 | 含义与限制 |
| --- | --- | --- |
| `Resources:<资源键>:Endpoint` | 无 | 启用的资源必须提供绝对 HTTPS 地址。填写资源根地址或 `/openai/v1/` 基础地址，可省略末尾斜杠。地址须省略用户名密码、查询和片段。 |
| `Resources:<资源键>:Secret` | 无 | 启用的资源必须提供实际 API 密钥，值不能含空白或控制字符。通过宿主配置注入，例如将密钥值绑定到 `AzureCloud__CHAT__Resources__east2__Secret`。 |
| `Resources:<资源键>:ApiKey` | 无 | 与 `Secret` 对应同一个密钥值，二者任选其一。 |
| `Resources:<资源键>:ApiKeySecretName` | 无 | 复用资源类型时可以绑定此字段。密钥名称解析仅在下文的旧式资源配置中生效，`AzureCloud` 资源使用宿主已注入的密钥值。 |
| `Resources:<资源键>:Enabled` | `true` | 设置为 `false` 时，该资源不会成为调用目标，也会跳过它的地址和密钥校验。 |
| `Models:<模型键>:Resource` | 无 | 启用的部署必须引用本组 `Resources` 中已定义的键。调用这个模型需要对应资源也处于启用状态。 |
| `Models:<模型键>:Deployment` | 无 | 启用的部署必须填 Azure 门户中的部署名。部署级图像接口会把它放入 URL 路径，`.` 和 `..` 不可作为部署名。 |
| `Models:<模型键>:Api` | Chat 为 `ChatCompletions`，Image 为 `Deployment` | Chat 支持 `Responses` 和 `ChatCompletions`。Image 支持 `Deployment` 和 `OpenAIV1`，分别使用部署级与资源级接口。 |
| `Models:<模型键>:ApiVersion` | 图像 `Deployment` 接口为 `2025-04-01-preview` | 仅适用于部署级图像接口，可填写 `yyyy-MM-dd` 或 `yyyy-MM-dd-preview`。资源级图像接口使用固定的 `?api-version=preview`。 |
| `Models:<模型键>:MaxTokenParameter` | Chat Completions 为 `max_completion_tokens` | 仅用于 Chat Completions，可按该部署要求设为 `max_tokens`。Responses 将输出上限发送为 `max_output_tokens`。 |
| `Models:<模型键>:Enabled` | `true` | 设置为 `false` 时跳过该部署及其校验。关联的业务场景必须改用另一个可调用的模型键。 |

启用 Provider 时，已配置的每组 `Resources` 和 `Models` 都要有条目。路由挑选的部署决定实际接口及资源密钥，聊天在资源级 `/openai/v1/` 接口使用 `api-key`，图像生成与编辑也使用所选资源的 `api-key`。地址及密钥在宿主启动后由选项缓存，修改后通过重启宿主更新调用目标。

`Responses` 调用 `/openai/v1/responses`，`ChatCompletions` 调用 `/openai/v1/chat/completions`。两者都把 Azure 部署名写入请求的 `model`，并支持非流式响应和 SSE。

图像 `Deployment` 调用 `/openai/deployments/<部署名>/images/generations` 或 `/openai/deployments/<部署名>/images/edits`，查询参数 `api-version` 使用 `ApiVersion`。编辑通过 multipart 字段 `image` 上传源图。`OpenAIV1` 调用 `/openai/v1/images/generations` 或 `/openai/v1/images/edits`，把部署名写入 `model`，编辑通过 multipart 字段 `image[]` 上传源图。两种编辑接口都可传 `mask`。适配器将 Azure 的 Base64 图像解码为 PNG。[部署级图像 API](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview) · [资源级图像 API](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

## `AzureCloud.TTS` 的语音合成

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `Secret` | 无 | Speech 资源密钥，通过 `AzureCloud__TTS__Secret` 等配置路径注入实际值。值不能含空白或控制字符。适配器发送 `Ocp-Apim-Subscription-Key`。 |
| `Region` | 无 | Speech 资源的区域标识，例如 `eastus`。使用 ASCII 字母开头的字母数字标识。匹配区域地址时会忽略大小写。 |
| `Endpoint` | `https://<region>.tts.speech.microsoft.com/cognitiveservices/v1` | 可填写 Speech 资源的 HTTPS 根地址或 `/cognitiveservices/v1` 路径。与 `Region` 一致的 `<region>.api.cognitive.microsoft.com` 根地址或 `/sts/v1.0/issueToken` 地址会转换为区域合成地址。地址须省略用户名密码、查询和片段。 |
| `Voice` | 无 | Speech 音色，如 `zh-CN-XiaoxiaoNeural`。优先采用请求的 `Voice`，其次是场景的 `Voice`，最后才读取这里的值。调用前需有一个有效音色。 |

使用 `AZURE_SPEECH_KEY` 这种自定义环境变量名时，由宿主把它的值映射到 `AzureCloud:TTS:Secret`。合成使用 `azure-speech` 别名。启用 Azure 且未配置 `AI:Models:Tts:Default` 时，模块自动建立这个默认场景。已有的默认场景继续使用它配置的模型。配置了 `AzureCloud.TTS` 后，旧式 TTS 部署中的可用别名须避开 `azure-speech`。

`ResponseFormat` 和 `Speed` 通过请求或 `AI:Models:Tts` 设置。Speech 的音频格式支持 `mp3`、`wav`、`opus`、`pcm`，默认采用 `mp3`。设置速度时传入 0.5 到 2 的倍率。

## `AI` 的 Provider 策略

`AI:DefaultProvider` 可选。当请求显式给出 `Model` 且省略 `Provider` 时，框架读取这个默认 Provider。值须引用已配置的 Provider。使用有效的 `AzureCloud` 时，`AddAzure()` 会补注册 `Azure`。宿主可只配置下表所需的开关或策略。

| 路径（相对于 `AI:Providers:Azure`） | 默认值 | 含义与限制 |
| --- | --- | --- |
| `Enabled` | `true` | 控制 Azure 的可用性。Azure 被场景引用或用作 `DefaultProvider` 时需要启用。 |
| `TimeoutSeconds` | `100` | 每次 HTTP 尝试的默认超时时间，单位为秒，必须大于 0。聊天流还用该值作为建立连接并读取流的总时限。 |
| `MaxRetryAttempts` | `2` | 在首次尝试之外允许的重试次数，必须为非负整数。瞬时故障可重试，`0` 让每次调用只尝试一次。 |
| `ConcurrencyLimit` | `8` | 同一 DI 容器内 Azure 内置能力共享的并发上限，必须大于 0。同一 Provider 下的资源、部署和能力共用这组名额。 |
| `MaxResponseBytes` | `67108864`（64 MiB） | 成功的非流式 HTTP 响应体上限，单位为字节，取值 1 至 `Int32.MaxValue`。包含图像 JSON 中的 Base64 数据和音频响应体。 |
| `MaxErrorResponseBytes` | `65536`（64 KiB） | 错误 HTTP 响应体上限，单位为字节，取值 1 至 `Int32.MaxValue`。流式请求的握手错误也使用这个预算。 |
| `MaxStreamLineCharacters` | `1048576` | SSE 单行允许的 UTF-16 字符数，必须大于 0。Responses 还用该值限制一个事件的合并数据长度。 |
| `Headers` | 空字典 | 额外的 HTTP 请求头。`api-key`、`Authorization` 和 `Host` 由 Azure 适配处理，配置中须省略。Speech 另外自行设置 `Ocp-Apim-Subscription-Key` 和 `X-Microsoft-OutputFormat`。 |
| `BaseAddress`、`Endpoints` | 空 | 通用 Provider 字段。本模块的 Azure 终结点由资源和部署映射确定。 |
| `ApiKey`、`ApiKeySecretName` | 无 | 通用 Provider 凭据字段。填写 `ApiKeySecretName` 时须为非空白字符串。本模块从所选资源或 `AzureCloud.TTS` 获取 Azure 凭据。 |
| `Resources`、`Deployments` | 空字典 | 旧式 Azure 部署映射，仍用于尚未迁移的能力。详见下节。 |

并发名额覆盖同一调用的重试过程，也覆盖流的消费期间。等待名额受调用方取消控制。网络错误、请求超时、HTTP 429 和大部分 HTTP 5xx 响应会进入重试判断。框架在重试前优先采用服务端给出的重试等待时间，缺省时按 500 毫秒、1 秒、2 秒退避，随后保持 2 秒。同一次调用的重试沿用选中的部署。流式调用只在建立流时重试，成功建立后由调用方处理流读取失败。

底层 `HttpClient.Timeout` 默认 100 秒，也会限制建立请求和接收响应头的时间。只提高 `TimeoutSeconds` 时，底层 100 秒时限仍可能先触发。需要由 AI 请求时限统一管理时，可在宿主配置名为 `MS.Microservice.AI.Azure` 的 HTTP 客户端，将 `HttpClient.Timeout` 设为 `Timeout.InfiniteTimeSpan`。

### 旧式资源与部署映射

`AI:Providers:Azure:Resources:<资源键>` 支持 `Endpoint`、`Enabled`、`ApiKey`、`ApiKeySecretName`。地址、启用条件和密钥有效性与上表一致。只有旧式资源会在启用且 `ApiKey` 为空时，调用 `IAISecretProvider` 按名称解析密钥。凭据的查找顺序见[通用 AI 策略](#通用-ai-策略)。

`AI:Providers:Azure:Deployments:<能力>:<模型键>` 是部署候选数组。能力键为 `Chat`、`Tts`、`Asr`、`ImageGeneration` 或 `ImageEdit`。数组元素支持前述部署字段，路由按顺序选择第一个启用且资源也启用的候选。旧式 Chat 默认使用 `ChatCompletions`，旧式图像默认使用 `OpenAIV1`。旧式 TTS 和 ASR 使用各自的 Azure OpenAI v1 音频接口，配置时省略 `Api`、`ApiVersion` 和 `MaxTokenParameter`。语音转录通过旧式 `Deployments:Asr` 绑定部署。

旧式 Azure OpenAI 语音合成向 `/openai/v1/audio/speech?api-version=preview` 发送 JSON。转录向 `/openai/v1/audio/transcriptions?api-version=preview` 发送 multipart 请求，音频放在 `file` 字段。按部署能力选择音色、音频格式和转录返回格式。[Azure 音频 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

如已由 `AzureCloud` 接管相关能力，可删除对应的旧式部署和仅被它们引用的资源。保留旧式映射时，`Resources` 和 `Deployments` 需同时有条目。即使 AzureCloud 已接管某种能力，仍会校验留在旧配置中的启用资源和部署。共用的超时、重试和并发策略可以单独保留。

## `AI:Models` 的场景参数

`AI:Models:<能力>:<场景名>` 适用于需要让业务按场景发起请求的调用方。场景名由宿主定义。请求没有指定 `Model` 时，先查找 `Scenario`，再尝试能力下名为 `Default` 的场景。显式给出 `Model` 会跳过场景解析和场景默认值，此时应提供 `Provider` 或设置 `AI:DefaultProvider`。显式 `Provider` 与 `Model` 成对使用。所有 Azure 场景在选项校验时都必须能解析到启用目标。

| 通用场景参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `Provider` | 无 | 必填。引用已经配置的 Provider，Azure 为 `Azure`。 |
| `Model` | 无 | 必填。引用当前能力下可解析的模型键。AzureCloud 模型键可与 Azure 部署名分别设置。 |
| `TimeoutSeconds` | Provider 的值 | 可选。大于 0 的秒数。请求的 `Timeout` 优先于场景，场景优先于 Provider。 |
| `MaxRetryAttempts` | Provider 的值 | 可选。非负整数，场景设置优先于 Provider。 |

下表中的可选参数由同名请求属性优先覆盖。请求和场景都省略时，使用表中描述的 Provider 行为。

| 场景能力 | 可选参数 | 含义及 Azure 行为 |
| --- | --- | --- |
| `Chat` | `Temperature`、`TopP`、`MaxOutputTokens` | 分别表示采样温度、核采样概率和输出 token 上限。值省略时由所选接口和部署决定。显式请求的温度须在 `[0, 2)`，`TopP` 须在 `(0, 1]`，输出上限须大于 0。配置前核对部署接受的采样参数。`ResponseFormat` 由请求指定，结构化格式适用于非流式聊天。 |
| `Tts` | `Voice`、`ResponseFormat`、`Speed` | 分别指定音色、音频格式和语速。Speech 的回退音色来自 `AzureCloud.TTS.Voice`，Azure OpenAI 的音色来自场景或请求。两种实现的音频格式均默认 `mp3`。Speech 速度采用上文的 0.5 至 2 倍范围。 |
| `Asr` | `Language`、`Prompt`、`ResponseFormat` | 分别指定语言提示、转录提示和返回格式。Azure 默认 `json`，也支持部署接受的 `verbose_json`。省略语言和提示时由部署处理。 |
| `ImageGeneration`、`ImageEdit` | `Count`、`Size`、`Quality`、`ResponseFormat` | `Count` 默认 1，提供时须大于 0。尺寸与质量按部署支持的取值填写，例如 `1024x1024` 和 `low`，省略时由接口决定。Azure GPT Image 使用 Base64 图像，省略 `ResponseFormat` 或指定 `b64_json`。 |

## 通用 AI 策略

这些参数与 `AzureCloud` 路由一起使用，路径均相对于 `AI`。限流和熔断按 Provider 名称与解析后的模型键计数，状态保存在当前 DI 容器内。

| 路径（相对于 `AI`） | 默认值 | 含义与限制 |
| --- | --- | --- |
| `RateLimiting:Enabled` | `false` | 开启内置固定窗口限流。超过窗口额度时立即返回本地限流异常。 |
| `RateLimiting:RequestsPerWindow` | 无 | 每个 Provider 与模型在一个窗口内允许的逻辑调用次数。设置时须大于 0，留空时限流器直接放行。 |
| `RateLimiting:WindowSeconds` | `60` | 窗口长度，单位为秒，须大于 0。 |
| `CircuitBreaker:Enabled` | `false` | 开启内存熔断。单个逻辑调用完成后记录结果，成功时清除该模型的失败状态。调用方取消及主动结束流会跳过失败计数。 |
| `CircuitBreaker:FailureThreshold` | `5` | 打开熔断器所需的连续失败次数，须大于 0。 |
| `CircuitBreaker:BreakDurationSeconds` | `30` | 熔断期间拒绝调用的秒数，须大于 0。 |
| `LogSanitizer:RedactionText` | `[REDACTED]` | 默认 `IAILogSanitizer` 使用的替换文本，须为非空白字符串。 |
| `LogSanitizer:SensitiveFields` | `api_key`、`apikey`、`authorization`、`access_token`、`token`、`secret`、`password`、`prompt`、`response` | 默认脱敏器匹配的字段名数组，忽略大小写，字段名不能是空白字符串。自定义日志可调用此服务处理文本和元数据。Azure HTTP 客户端的请求头日志有独立的脱敏设置。 |
| `SecretProvider:PreferEnvironment` | `true` | 默认名称解析优先尝试环境变量，设为 `false` 则优先读配置。适用于旧式资源和通用 Provider 的密钥解析。 |
| `SecretProvider:EnvironmentVariablePrefix` | `AI__PROVIDERS__` | 推导密钥环境变量名称的前缀，须为非空白字符串。旧式 Azure 资源的默认变量格式为 `AI__PROVIDERS__Azure__Resources__<资源键>__ApiKey`。 |
| `PayloadLimits:MaxChatCharacters` | `200000` | 非流式聊天所有消息正文的合计字符数上限，须大于 0。 |
| `PayloadLimits:MaxStreamingChatCharacters` | `200000` | 流式聊天所有消息正文的合计字符数上限，须大于 0。 |
| `PayloadLimits:MaxTextCharacters` | `100000` | TTS 输入文本字符数上限，须大于 0。 |
| `PayloadLimits:MaxAudioBytes` | `26214400`（25 MiB） | 转录请求的音频文件字节数上限，须大于 0。 |
| `PayloadLimits:MaxImagePromptCharacters` | `20000` | 图像生成或编辑提示词的字符数上限，须大于 0。 |
| `PayloadLimits:MaxImageBytes` | `20971520`（20 MiB） | 编辑请求的源图像字节数上限，须大于 0。 |
| `PayloadLimits:MaxImageMaskBytes` | `20971520`（20 MiB） | 编辑请求的遮罩字节数上限，须大于 0。 |
| `CostAccounting:Enabled` | `true` | 通知 `IAICostReporter` 一次逻辑调用的结果、总用量与耗时。宿主可注册自己的报告器。内置默认报告器丢弃这些记录。 |

名称解析器先检查资源是否已有实际 `ApiKey`。需要解析时，环境分支先查 `ApiKeySecretName` 指定的同名进程环境变量，再查推导出的变量。配置分支用 `ApiKeySecretName` 查询宿主的 `IConfiguration`，两分支的先后顺序由 `PreferEnvironment` 决定。`AzureCloud` 的密钥由宿主把环境变量或别的配置源绑定到 `Secret`，不经该名称解析器。

字符预算按 .NET 字符串的 UTF-16 长度计算。请求载荷超出预算会在发送前失败，响应超出预算会作为响应无效错误返回。框架通用 `AIUsage` 记录输入、输出、总 token，若需保存 Azure Responses 的缓存用量，应从原始响应的 `usage.input_tokens_details` 读取。
