## MS.Microservice.AI

Independent AI module family for MS.Microservice. It now provides provider-neutral Chat, TTS, ASR, image generation, and image edit entry points with concrete providers for OpenAI, DeepSeek, and Qwen.

### Packages

| Package | Purpose |
|---|---|
| `MS.Microservice.AI.Abstractions` | Stable business-facing contracts such as `IAIChatClient`, `IAITtsClient`, `IAIAsrClient`, `IAIImageGenerationClient`, `IAIImageEditClient`, request/response DTOs, and unified exceptions. |
| `MS.Microservice.AI.Core` | Options binding, model resolution, routing clients, DI entry point, shared resilience, and OpenAI-compatible HTTP/SSE implementations for chat, audio, and image capabilities. |
| `MS.Microservice.AI.OpenAI` | OpenAI chat, TTS, ASR, image generation, and image edit provider registration and validation. |
| `MS.Microservice.AI.DeepSeek` | DeepSeek chat provider registration and validation. DeepSeek is currently enforced as chat-only. |
| `MS.Microservice.AI.Qwen` | Qwen chat, TTS, ASR, image generation, and image edit provider registration and validation through compatible-mode endpoints. |

### Capability Matrix

| Provider | Chat | TTS | ASR | Image Generation | Image Edit |
|---|---|---|---|---|---|
| OpenAI | Yes | Yes | Yes | Yes | Yes |
| DeepSeek | Yes | No | No | No | No |
| Qwen | Yes | Yes | Yes | Yes | Yes |

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
- Current constraint: DeepSeek remains chat-only and is explicitly blocked for TTS, ASR, image generation, and image edit configuration.
- Planned next: richer observability, provider-specific advanced parameters, and optional Agent Framework integration on top of this provider gateway layer.