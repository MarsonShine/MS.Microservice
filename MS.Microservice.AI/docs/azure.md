# 接入 Azure OpenAI

先在 Azure 中创建资源并部署所需模型。模型是否可部署取决于区域、版本和部署类型。为同一个业务场景更换区域时，可以保留应用中的模型别名，只调整它指向的部署配置。[Microsoft Foundry 区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)

宿主引用 `MS.Microservice.AI.Azure`，通过现有 AI 注册入口启用 Azure。

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddAzure();
```

## 配置资源和部署

下面的 `Assistant` 场景使用 `assistant` 作为逻辑模型别名。候选列表的第一项所属资源已停用，因此聊天请求会发送给第二项 `sweden-chat` 资源上的 `assistant-b` 部署。图像和音频场景按各自能力选择部署。将示例中的资源终结点和部署名称替换为 Azure 门户中对应的值。

```json
{
  "AI": {
    "Providers": {
      "Azure": {
        "Resources": {
          "east-chat": {
            "Endpoint": "https://example-east.openai.azure.com/",
            "Enabled": false
          },
          "sweden-chat": {
            "Endpoint": "https://example-sweden.openai.azure.com/",
            "ApiKeySecretName": "AZURE_SWEDEN_CHAT_API_KEY"
          },
          "image-west": {
            "Endpoint": "https://example-west.openai.azure.com/",
            "ApiKeySecretName": "AZURE_WEST_IMAGE_API_KEY"
          }
        },
        "Deployments": {
          "Chat": {
            "assistant": [
              { "Resource": "east-chat", "Deployment": "assistant-a" },
              { "Resource": "sweden-chat", "Deployment": "assistant-b" }
            ]
          },
          "ImageGeneration": {
            "poster": [
              { "Resource": "image-west", "Deployment": "gpt-image-poster" }
            ]
          },
          "ImageEdit": {
            "retouch": [
              { "Resource": "sweden-chat", "Deployment": "gpt-image-retouch" }
            ]
          },
          "Tts": {
            "narration": [
              { "Resource": "image-west", "Deployment": "tts-narration" }
            ]
          },
          "Asr": {
            "transcriber": [
              { "Resource": "sweden-chat", "Deployment": "audio-transcriber" }
            ]
          }
        }
      }
    },
    "Models": {
      "Chat": {
        "Assistant": {
          "Provider": "Azure",
          "Model": "assistant",
          "MaxOutputTokens": 1024
        }
      },
      "ImageGeneration": {
        "Poster": { "Provider": "Azure", "Model": "poster" }
      },
      "ImageEdit": {
        "Retouch": { "Provider": "Azure", "Model": "retouch" }
      },
      "Tts": {
        "Narration": { "Provider": "Azure", "Model": "narration", "Voice": "alloy", "ResponseFormat": "mp3" }
      },
      "Asr": {
        "Transcript": { "Provider": "Azure", "Model": "transcriber" }
      }
    }
  }
}
```

| 配置项 | 用法 |
| --- | --- |
| `Resources:<资源名>:Endpoint` | HTTPS 资源根地址，例如 `https://example-sweden.openai.azure.com/`，也可以填到 `/openai/v1/` 为止的地址。 |
| `Resources:<资源名>:ApiKey` | 该资源的密钥。启用环境变量配置源后，可通过 `AI__Providers__Azure__Resources__sweden-chat__ApiKey` 注入示例所需的密钥。 |
| `Resources:<资源名>:ApiKeySecretName` | `ApiKey` 为空时使用的密钥名称，由 `IAISecretProvider` 解析。默认实现可读取同名环境变量或配置值。为各资源使用各自的密钥名称。 |
| `Resources:<资源名>:Enabled` | 资源开关，默认 `true`。 |
| `Deployments:<能力>:<别名>` | 在 `Chat`、`ImageGeneration`、`ImageEdit`、`Tts` 或 `Asr` 下按优先级排列候选。`Resource` 引用资源名，`Deployment` 使用 Azure 中的部署名称。每项的 `Enabled` 默认为 `true`。 |

配置加载时校验场景引用和部署映射。运行时调用采用首个启用且所属资源已启用的候选。要切换区域，调整候选顺序或开关并重启宿主。请求失败后的重试继续使用同一目标。

`AI:Providers:Azure` 下的 `TimeoutSeconds`、`MaxRetryAttempts`、`ConcurrencyLimit` 和响应预算沿用 AI 模块的通用配置。部署候选共用 Azure Provider 的并发上限。终结点和密钥分别在所属资源下配置。

## 调用聊天

业务继续使用 `IAIChatClient`。场景名称由 `AI:Models:Chat` 定义。

```csharp
using Microsoft.Extensions.DependencyInjection;
using MS.Microservice.AI.Abstractions;

var chat = serviceProvider.GetRequiredService<IAIChatClient>();
var response = await chat.GetResponseAsync(new AIChatRequest
{
    Scenario = "Assistant",
    Messages = [new AIChatMessage("user", "用一句话解释消息队列。")],
}, cancellationToken);
```

普通响应和 SSE 流式响应都向 `/openai/v1/chat/completions` 发送 `POST`。Azure 请求使用所选资源的 `api-key`，`model` 取部署名称，`MaxOutputTokens` 映射为 `max_completion_tokens`。使用 JSON Object 或 JSON Schema 时，选择支持所需格式的部署并使用非流式调用。设置采样参数前，确认所选模型支持这些参数。[Azure Chat REST 参考](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/chat) · [Azure v1 API](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) · [推理模型参数](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)

## 生成和编辑图像

在 Azure 中部署 GPT Image 模型后，通过 `IAIImageGenerationClient` 调用 `Poster` 场景，通过 `IAIImageEditClient` 调用 `Retouch` 场景。两个场景各自在 `AI:Models` 的对应能力下配置，示例中的生成请求发往 `image-west`，编辑请求发往 `sweden-chat`。

生成和编辑分别调用 `/openai/v1/images/generations?api-version=preview` 和 `/openai/v1/images/edits?api-version=preview`。编辑请求以 multipart 的 `image[]` 字段上传图像，也可带一个 `mask`。应用可以省略 `ResponseFormat` 或指定 `b64_json`。适配器省略 Azure 请求中的 `response_format` 字段，将返回的 base64 图像解码为 `AIImageResponse.Images` 中的 PNG 内容。配置或调用中指定 `url` 会在发送请求前报错。[Azure 图像指南](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/dall-e) · [Azure 媒体 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

## 合成和转录语音

通过 `IAITtsClient` 调用 `Narration` 场景，通过 `IAIAsrClient` 调用 `Transcript` 场景。示例的合成和转录分别使用 `image-west` 和 `sweden-chat` 中的部署，请求携带所选资源的 `api-key`。两个接口使用 Azure OpenAI v1 预览终结点。[Azure 媒体 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

语音合成向 `/openai/v1/audio/speech?api-version=preview` 发送 JSON，从 `AITtsResponse.Audio.Content` 读取音频字节。按部署选择支持的 `Voice`、`ResponseFormat` 和 `Speed`。场景可以设置默认音色，调用也可以指定音色。[语音合成 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/audio-create-speech)

语音转录向 `/openai/v1/audio/transcriptions?api-version=preview` 发送 multipart 请求，音频放在 `file` 字段。默认使用 `json`，适用于 GPT-4o 转录模型。部署支持 `verbose_json` 时（例如 Whisper），可通过 `ResponseFormat` 选择详细结果。其他格式会在发送请求前报错。[语音转录 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/audio-create-transcription)

## 排查调用问题

排查 `DeploymentNotFound` 时，到实际选中的资源下核对部署名。遇到认证错误时，检查该资源的密钥来源。Azure HTTP 客户端禁用自动重定向，密钥随请求发送到配置的终结点。
