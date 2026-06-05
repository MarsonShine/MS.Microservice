## MS.Microservice.AI

Independent AI module family for MS.Microservice. The current MVP focuses on provider-neutral chat access with concrete providers for OpenAI, DeepSeek, and Qwen.

### Packages

| Package | Purpose |
|---|---|
| `MS.Microservice.AI.Abstractions` | Stable business-facing contracts such as `IAIChatClient`, request/response DTOs, and unified exceptions. |
| `MS.Microservice.AI.Core` | Options binding, model resolution, routing client, DI entry point, shared resilience, and OpenAI-compatible HTTP/SSE implementation. |
| `MS.Microservice.AI.OpenAI` | OpenAI chat provider registration and validation. |
| `MS.Microservice.AI.DeepSeek` | DeepSeek chat provider registration and validation. |
| `MS.Microservice.AI.Qwen` | Qwen chat provider registration and validation. |

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

var response = await chatClient.GetResponseAsync(new AIChatRequest
{
    Messages = [new AIChatMessage("user", "Hello")],
    Scenario = "Default",
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
      }
    }
  }
}
```

### Environment Variables

- `AI__Providers__OpenAI__ApiKey`
- `AI__Providers__DeepSeek__ApiKey`
- `AI__Providers__Qwen__ApiKey`

### Current Scope

- Implemented: chat/completion, streaming SSE parsing, timeout, retry, provider-neutral errors, model routing, provider validation, DI, and unit tests.
- Planned next: TTS, ASR, image generation, image edit, richer observability, and optional Agent Framework integration.