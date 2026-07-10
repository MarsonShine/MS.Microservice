## MS.Microservice.AI

Independent AI module family for MS.Microservice. It now provides provider-neutral Chat, TTS, ASR, image generation, image edit, and **educational image prompt planning with scene grouping** entry points with concrete providers for OpenAI, DeepSeek, and Qwen.

### Packages

| Package | Purpose |
|---|---|
| `MS.Microservice.AI.Abstractions` | Stable business-facing contracts such as `IAIChatClient`, `IAITtsClient`, `IAIAsrClient`, `IAIImageGenerationClient`, `IAIImageEditClient`, request/response DTOs, and unified exceptions. |
| `MS.Microservice.AI.Core` | Options binding, model resolution, routing clients, DI entry point, shared resilience, OpenAI-compatible HTTP/SSE implementations for chat, audio, and image capabilities. Also includes **`Images/`** — educational flashcard image prompt planning, scene grouping, structured edit delta, and batch generation pipeline. |
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
    .AddImagePromptPipeline(); // Registers IPlanGeneratorClient, SceneGroupingAgent, orchestrator, SentenceEditDeltaAgent

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

> See `src/MS.Microservice.AI.Core/Images/README.md` for full documentation on the image prompt pipeline, scene grouping, structured edit delta, anti-clutter design, and dual-prompt architecture.

### Capability Matrix

| Provider | Chat | TTS | ASR | Image Generation | Image Edit | Reference Image Edit |
|---|---|---|---|---|---|---|
| OpenAI | Yes | Yes | Yes | Yes | Yes | No |
| DeepSeek | Yes | No | No | No | No | No |
| Qwen | Yes | Yes | Yes | Yes | Yes | Yes |

> **架构说明**：
> - `OpenAICompatible*ProviderBase` 是 provider HTTP 复用层（chat/completions, images/generations 等），不是参考图编辑通道。
> - 参考图编辑使用独立的 `IReferenceImageEditClient` (Core.Images) → `IQwenImageReferenceEditClient` (Qwen) → `QwenReferenceImageEditAdapter`。
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
        "ApiKey": "",
        "ApiKeySecretName": "AI_OPENAI_API_KEY",
        "BaseAddress": "https://api.openai.com/v1/",
        "TimeoutSeconds": 100,
        "MaxRetryAttempts": 2,
        "ConcurrencyLimit": 8
      },
      "DeepSeek": {
        "ApiKey": "",
        "BaseAddress": "https://api.deepseek.com/",
        "TimeoutSeconds": 80,
        "MaxRetryAttempts": 2,
        "ConcurrencyLimit": 4
      },
      "Qwen": {
        "ApiKey": "",
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

- `AI__Providers__OpenAI__ApiKey`
- `AI__Providers__DeepSeek__ApiKey`
- `AI__Providers__Qwen__ApiKey`
- `AI__Models__Tts__Speech__Voice`
- `AI__Models__ImageGeneration__Poster__ResponseFormat`

### Current Scope

- Implemented: chat/completion, streaming SSE parsing, TTS, ASR, image generation, image edit, timeout, retry, provider-neutral errors, model routing, provider validation, DI, and offline unit tests.
- Production readiness already covered: `HttpClientFactory`, provider/model timeout, exponential retry with `Retry-After`, provider concurrency limit, streaming cancellation, token usage mapping, provider-neutral error classification, Activity tracing, provider capability validation, rate limiter abstraction, circuit breaker abstraction, log sanitizer, provider-neutral secret lookup, payload limits, and cost accounting hooks.
- Production extension points: `IAIRateLimiter`, `IAICircuitBreaker`, `IAILogSanitizer`, `IAISecretProvider`, and `IAICostReporter` can be replaced by application hosts without changing provider code.
- Current constraint: DeepSeek remains chat-only and is explicitly blocked for TTS, ASR, image generation, and image edit configuration.
- Planned next: distributed quota/circuit state, real cost sinks, richer observability, provider-specific advanced parameters, and optional Agent Framework integration on top of this provider gateway layer. See `../docs/framework-optimization-roadmap.md`.
