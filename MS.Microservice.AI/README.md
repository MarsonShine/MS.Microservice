教育图像场景已迁至 samples/AI/MS.Microservice.Samples.EducationalImages。使用该场景时引用场景类库，调用 services.AddImagePromptPipeline()；Qwen 参考图适配还需 services.AddQwenEducationalImages()。通用 AddQwen() 不注册场景适配器。

## MS.Microservice.AI

Independent AI module family for MS.Microservice. It provides provider-neutral Chat, structured JSON output, TTS, ASR, image generation, image edit, educational image prompt planning, and a bounded question-generation Harness with concrete providers for OpenAI, DeepSeek, and Qwen.

### Packages

| Package | Purpose |
|---|---|
| `MS.Microservice.AI.Abstractions` | Stable business-facing contracts such as `IAIChatClient`, `IAITtsClient`, `IAIAsrClient`, `IAIImageGenerationClient`, `IAIImageEditClient`, request/response DTOs, and unified exceptions. |
| `MS.Microservice.AI.Core` | Options binding, model resolution, routing clients, DI entry point, shared resilience, OpenAI-compatible HTTP/SSE implementations for chat, audio, and image capabilities. Also includes **`Images/`** — educational flashcard image prompt planning, scene grouping, structured edit delta, and batch generation pipeline. |
| `MS.Microservice.AI.QuestionGeneration` | Business-neutral question contracts and a bounded Draft → Validate → Review → Repair Harness, strict JSON Schema integration, Attempt auditing, budgets, repair allowlists, and pluggable question definitions. |
| `MS.Microservice.AI.OpenAI` | OpenAI chat, TTS, ASR, image generation, and image edit provider registration and validation. |
| `MS.Microservice.AI.DeepSeek` | DeepSeek chat provider registration and validation. DeepSeek is currently enforced as chat-only. |
| `MS.Microservice.AI.Qwen` | Qwen chat, TTS, ASR, image generation, image edit, and **reference-image edit** (via `IQwenImageReferenceEditClient` + `QwenReferenceImageEditAdapter`) provider registration and validation through compatible-mode endpoints. |

### Quick Start — Image Prompt Pipeline

```csharp
// Program.cs
builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddOpenAI()
    .AddQwen()   // for reference-image editing
    .Services
    .AddImagePromptPipeline(); // Requires the samples/AI education scenario library

// Usage: one-step text → image
var orchestrator = provider.GetRequiredService<ImageGenerationOrchestrator>();
var result = await orchestrator.GenerateFromTextAsync("Be careful! Don't run in the classroom.");

// Usage: structured reference-image edit
var delta = member.EditDelta; // from SentenceEditDeltaAgent
var editResult = await orchestrator.GenerateFromReferenceEditDeltaAsync(delta, referenceImageUrl);

// Usage: batch sentence images (full flow)
var agent = provider.GetRequiredService<ISceneGroupingAgent>();
var grouping = await agent.GroupAsync(excelRows);

var deltaAgent = provider.GetRequiredService<SentenceEditDeltaAgent>();
foreach (var group in grouping.Groups) { await deltaAgent.EnrichAsync(group, excelRows); }

var batchOrchestrator = provider.GetRequiredService<SentenceImageBatchOrchestrator>();
var results = await batchOrchestrator.GenerateBatchAsync(excelRows);
```

> See `../samples/AI/MS.Microservice.Samples.EducationalImages/README.md` for full documentation on the image prompt pipeline, scene grouping, structured edit delta, anti-clutter design, and dual-prompt architecture.

### Quick Start — Question Generation Harness

```csharp
builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddOpenAI();

builder.Services
    .AddQuestionGeneration(options =>
    {
        options.DraftScenario = "QuestionGenerationDraft";
        options.ReviewScenario = "QuestionGenerationReview";
        options.RepairScenario = "QuestionGenerationRepair";
    })
    .AddDefinition<MyQuestionDefinition>();

var harness = serviceProvider.GetRequiredService<IQuestionGenerationHarness>();
var result = await harness.GenerateAsync(contextSnapshot, blueprint, cancellationToken: cancellationToken);

if (result.Accepted)
{
    await repository.SaveAsync(result.Candidate!, cancellationToken);
    harness.CommitAccepted(result); // only after the host transaction commits
}
```

The framework contains no fixed educational question codes or database projection. Applications provide `QuestionCandidate`, `IQuestionDefinition`, `IQuestionBlueprintPlanner`, and versioned prompts. See [QuestionGeneration Code Guide](src/MS.Microservice.AI.QuestionGeneration/README.md) and [architecture deep dive](src/MS.Microservice.AI.QuestionGeneration/docs/ARCHITECTURE.md).

### Capability Matrix

| Provider | Chat | JSON Object | JSON Schema request | TTS | ASR | Image Generation | Image Edit | Reference Image Edit |
|---|---|---|---|---|---|---|---|---|
| OpenAI | Yes | Yes | Yes, model-dependent | Yes | Yes | Yes | Yes | No |
| DeepSeek | Yes | Yes, model-dependent | Requested with automatic fallback | No | No | No | No | No |
| Qwen | Yes | Yes, model-dependent | Requested with automatic fallback | Yes | Yes | Yes | Yes | Yes |

`AIChatRequest.ResponseFormat` supports `Text`, `JsonObject`, and `JsonSchema`. Structured formats are non-streaming only. A Provider must explicitly return `unsupported_response_format` before QuestionGeneration downgrades `JsonSchema → JsonObject → Text`; authentication, rate-limit, timeout, safety, and unrelated request errors never trigger format fallback.

> **架构说明**：
> - `OpenAICompatible*ProviderBase` 是 provider HTTP 复用层（chat/completions, images/generations 等），不是参考图编辑通道。
> - 参考图编辑使用独立的 `IReferenceImageEditClient` (Samples.EducationalImages) → `IQwenImageReferenceEditClient` (Qwen) → `QwenReferenceImageEditAdapter`。
> - `AIImageEditRequest` 保持二进制编辑语义（inpainting / background removal），没有 `ReferenceImageUrl`。
> - `MS.Microservice.Core` 是允许依赖的核心层。

### Quick Start

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AI.Abstractions;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddMicroserviceAI(configuration)
    .AddOpenAI()
    .AddDeepSeek()
    .AddQwen();

using var provider = services.BuildServiceProvider();
var chatClient = provider.GetRequiredService<IAIChatClient>();
var ttsClient = provider.GetRequiredService<IAITtsClient>();
var asrClient = provider.GetRequiredService<IAIAsrClient>();
var imageClient = provider.GetRequiredService<IAIImageGenerationClient>();

var response = await chatClient.GetResponseAsync(new AIChatRequest
{
    Messages = [new AIChatMessage("user", "Hello")],
    Scenario = "Default",
});

var speech = await ttsClient.SynthesizeAsync(new AITtsRequest
{
  Input = response.Text,
  Scenario = "Speech",
});

var transcription = await asrClient.RecognizeAsync(new AIAsrRequest
{
  Audio = new AIBinaryContent
  {
    Content = speech.Audio.Content,
    ContentType = speech.Audio.ContentType,
    FileName = speech.Audio.FileName,
  },
  Scenario = "Transcription",
});

var image = await imageClient.GenerateAsync(new AIImageGenerationRequest
{
  Prompt = transcription.Text,
  Scenario = "Poster",
});
```

### Configuration

```json
{
  "AI": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "ApiKeySecretName": "AI_OPENAI_API_KEY",
        "BaseAddress": "https://api.openai.com/v1/",
        "TimeoutSeconds": 100,
        "MaxRetryAttempts": 2,
        "ConcurrencyLimit": 8
      },
      "DeepSeek": {
        "ApiKeySecretName": "AI_DEEPSEEK_API_KEY",
        "BaseAddress": "https://api.deepseek.com/",
        "TimeoutSeconds": 80,
        "MaxRetryAttempts": 2,
        "ConcurrencyLimit": 4
      },
      "Qwen": {
        "ApiKeySecretName": "AI_QWEN_API_KEY",
        "BaseAddress": "https://dashscope.aliyuncs.com/compatible-mode/v1/",
        "TimeoutSeconds": 120,
        "MaxRetryAttempts": 2,
        "ConcurrencyLimit": 4
      }
    },
    "Models": {
      "Chat": {
        "Default": {
          "Provider": "OpenAI",
          "Model": "gpt-4.1-mini",
          "Temperature": 0.2
        },
        "Coding": {
          "Provider": "DeepSeek",
          "Model": "deepseek-v4-pro",
          "Temperature": 0.1,
          "TimeoutSeconds": 60
        },
        "ChineseAssistant": {
          "Provider": "Qwen",
          "Model": "qwen-plus",
          "Temperature": 0.3
        },
        "QuestionGenerationDraft": {
          "Provider": "OpenAI",
          "Model": "draft-model",
          "Temperature": 0.4,
          "MaxOutputTokens": 3000
        },
        "QuestionGenerationReview": {
          "Provider": "OpenAI",
          "Model": "review-model",
          "Temperature": 0,
          "MaxOutputTokens": 1800
        },
        "QuestionGenerationRepair": {
          "Provider": "OpenAI",
          "Model": "repair-model",
          "Temperature": 0.2,
          "MaxOutputTokens": 3000
        }
      },
      "Tts": {
        "Speech": {
          "Provider": "OpenAI",
          "Model": "gpt-4o-mini-tts",
          "Voice": "alloy",
          "ResponseFormat": "mp3",
          "TimeoutSeconds": 30
        }
      },
      "Asr": {
        "Transcription": {
          "Provider": "OpenAI",
          "Model": "whisper-1",
          "ResponseFormat": "verbose_json",
          "TimeoutSeconds": 60
        }
      },
      "ImageGeneration": {
        "Poster": {
          "Provider": "Qwen",
          "Model": "qwen-image",
          "Count": 1,
          "Size": "1024x1024",
          "ResponseFormat": "b64_json",
          "TimeoutSeconds": 90
        }
      },
      "ImageEdit": {
        "Cleanup": {
          "Provider": "OpenAI",
          "Model": "gpt-image-1",
          "Count": 1,
          "ResponseFormat": "url",
          "TimeoutSeconds": 90
        }
      }
    },
    "RateLimiting": {
      "Enabled": false,
      "RequestsPerWindow": 600,
      "WindowSeconds": 60
    },
    "CircuitBreaker": {
      "Enabled": false,
      "FailureThreshold": 5,
      "BreakDurationSeconds": 30
    },
    "PayloadLimits": {
      "MaxChatCharacters": 200000,
      "MaxStreamingChatCharacters": 200000,
      "MaxTextCharacters": 100000,
      "MaxAudioBytes": 26214400,
      "MaxImagePromptCharacters": 20000,
      "MaxImageBytes": 20971520,
      "MaxImageMaskBytes": 20971520
    }
  }
}
```

### Environment Variables

- `AI__PROVIDERS__OpenAI__ApiKey`
- `AI__PROVIDERS__DeepSeek__ApiKey`
- `AI__PROVIDERS__Qwen__ApiKey`
- `AI_OPENAI_API_KEY`, `AI_DEEPSEEK_API_KEY`, `AI_QWEN_API_KEY` when the corresponding `ApiKeySecretName` is configured
- `AI__Models__Tts__Speech__Voice`
- `AI__Models__ImageGeneration__Poster__ResponseFormat`

### Current Scope

- Implemented: chat/completion, provider-neutral JSON Object/JSON Schema response formats, streaming SSE parsing, TTS, ASR, image generation, image edit, and the business-neutral QuestionGeneration Harness.
- Production readiness already covered: `HttpClientFactory`, provider/model timeout, exponential retry with `Retry-After`, provider concurrency limit, streaming cancellation, token usage mapping, provider-neutral error classification, Activity tracing, provider capability validation, rate limiter abstraction, circuit breaker abstraction, log sanitizer, provider-neutral secret lookup, payload limits, and cost accounting hooks.
- Production extension points: `IAIRateLimiter`, `IAICircuitBreaker`, `IAILogSanitizer`, `IAISecretProvider`, and `IAICostReporter` can be replaced by application hosts without changing provider code.
- Current constraint: DeepSeek remains chat-only and is explicitly blocked for TTS, ASR, image generation, and image edit configuration.
- QuestionGeneration deliberately excludes distributed Attempt storage, task scheduling, persistence, business question definitions, resources, and human-review APIs; application hosts own those responsibilities.
- Planned next: distributed quota/circuit state, real cost sinks, richer observability, and optional host-side durable QuestionGeneration execution. See `../docs/framework-optimization-roadmap.md`.

### 调用结果与成本报告

AIProductionPipeline 以模型调用和解析结果判断成功。调用方取消、主动结束流不会增加
熔断失败计数；已观察到的流用量仍交给成本报告器。报告器或熔断结果记录器失败只记录
策略名称和异常类型，不覆盖模型结果或原始异常。准入检查失败仍会阻止调用。
成本报告是尽力而为的观测接口，不能用作需要事务保证的计费账本。

Chat 与 Media 共用 AIHttpExecution 的每次尝试超时、瞬时错误重试和取消分类，
并共用 AIProviderErrors 解析 OpenAI 及顶层错误信封。每次重试重建请求和正文；
调用方取消立即结束。流式数据开始返回后不自动重试，避免重复输出。

流式 Chat 的 Timeout 覆盖请求建立及后续流读取。仅收到 [DONE] 或显式 finish_reason
才产生最终成功块；提前断连返回 ResponseInvalid，已经交付的文本不会自动重试。
调用方取消仍保留取消异常，超时返回 AITimeoutException。
