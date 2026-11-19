# 接入 Azure AI 服务

先在 Azure 中准备所用服务的资源。使用 Foundry 模型时，在资源上部署需要的模型。可用的模型因区域、版本和部署类型而异。更换区域时，可以保留应用中的模型别名，调整它指向的资源和部署。[Microsoft Foundry 区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)

宿主引用 `MS.Microservice.AI.Azure`，通过现有 AI 注册入口启用 Azure。

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddAzure();
```

将应用用到的 `CHAT`、`TTS` 和 `IMAGE` 放在同一个 `AzureCloud` 节点下。`AI` 仍可设置业务场景、重试和响应预算。

## 当前支持的接口

`AddAzure()` 的聊天适配访问 Azure 资源级 `/openai/v1/`，通过资源密钥 `api-key` 认证。每个部署选择 Responses 或 Chat Completions。使用这些接口和认证方式的 Foundry 第三方模型也能用同一套资源和部署配置。在部署详情核对所需接口和参数。[Foundry v1 模型支持](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle#model-support) · [Foundry 模型终结点](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/endpoints)

Foundry 项目级终结点和 Microsoft Entra ID 认证需要相应的调用适配。`AzureCloud.TTS` 使用独立的 Azure AI Speech REST 接口。基于 Azure OpenAI 模型部署的语音配置也可继续使用。[Foundry 项目级聊天示例](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-chat-reasoning) · [Azure AI Speech REST](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)

## 使用 `AzureCloud.CHAT` 配置聊天

如果调用方已经知道要用的模型，在宿主配置根节点保留 `AzureCloud.CHAT`。每个 `Models` 条目直接指向一个资源和部署。下面的 `main`、`terra` 是配置内的资源名。替换示例终结点，并从 Foundry 复制相应资源中的部署名。

```json
{
  "AzureCloud": {
    "CHAT": {
      "Resources": {
        "main": { "Endpoint": "https://aoai-contoso-main.openai.azure.com/" },
        "terra": { "Endpoint": "https://aoai-contoso-terra.openai.azure.com/" }
      },
      "Models": {
        "gpt-5.4": { "Resource": "main", "Deployment": "gpt-5.4", "Api": "Responses" },
        "DeepSeek-V4.1-Flash-1017": { "Resource": "terra", "Deployment": "DeepSeek-V4.1-Flash-1017", "Api": "ChatCompletions" }
      }
    }
  }
}
```

启用环境变量配置源后，分别设置 `AzureCloud__CHAT__Resources__main__Secret` 和 `AzureCloud__CHAT__Resources__terra__Secret`，值是对应资源的 API 密钥。调用 `IAIChatClient` 时传 `Provider = "Azure"` 和 `Model = "DeepSeek-V4.1-Flash-1017"`，模块选出 `terra` 并把部署名写入请求。也可以在 `AI:Models:Chat` 中配置业务场景，让场景的 `Model` 指向 `AzureCloud.CHAT.Models` 的键。

`Api: "Responses"` 使用 `/openai/v1/responses`，并把 `MaxOutputTokens` 映射为 `max_output_tokens`。对支持 Responses 的新部署，可显式选择这个接口。原配置省略 `Api` 时使用 `ChatCompletions`，也可显式填写。它调用 `/openai/v1/chat/completions`，默认使用 `max_completion_tokens`。有的兼容部署要求 `max_tokens`，这时在该部署上加 `"MaxTokenParameter": "max_tokens"`。支持的参数以部署的调用示例为准。[Azure Responses API](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses) · [DeepSeek 参数示例](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/tutorials/get-started-deepseek-r1#parameters)

设置 `AzureCloud.CHAT` 后，Azure 聊天统一从这一组资源和部署取值。其他能力仍可读取 `AI:Providers:Azure`。通过根配置调用 `AddMicroserviceAI(configuration).AddAzure()` 会读取根节点的 `AzureCloud`。宿主如果传的是独立的 AI 配置段，可以用 `.AddAzure(configuration.GetSection("AzureCloud"))` 显式绑定。

## 使用 `AI` 场景与部署候选配置聊天

在 Microsoft Foundry 的模型目录选择 `gpt-6.2-astra`（版本 `2026-11-17`）。这个例子使用两个支持 Global Standard 部署的资源，分别位于 East US（`eastus`）和 Sweden Central（`swedencentral`）。资源名和部署名是示例值，配置时请复制自己订阅中对应的值。部署前可在 Foundry 确认可用区域和配额。Global Standard 请求可由 Azure 在其他支持该模型的区域处理。[Azure 模型目录](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure?pivots=azure-openai#gpt-62) · [Global Standard 区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability?pivots=standard#global-standard)

| 从哪里取值 | East US 示例 | Sweden Central 示例 |
| --- | --- | --- |
| Azure 门户的 Azure OpenAI 资源名 | `aoai-contoso-eastus` | `aoai-contoso-swedencentral` |
| 资源的 **Keys and Endpoint** 中的 **Endpoint** | `https://aoai-contoso-eastus.openai.azure.com/` | `https://aoai-contoso-swedencentral.openai.azure.com/` |
| Foundry **Build → Models** 中的 **Deployment name** | `assistant-astra-us` | `assistant-astra-se` |
| 在 Foundry 创建两个部署时选择的模型 ID | `gpt-6.2-astra` | `gpt-6.2-astra` |

创建部署时，Foundry 默认用模型 ID 作为部署名，也允许你自定义。这个例子给同一个模型在两个资源中的部署分别取名 `assistant-astra-us` 和 `assistant-astra-se`。在 Azure 门户的 **Keys and Endpoint** 中，复制各资源自己的 **KEY 1** 或 **KEY 2**。[创建和查看模型部署](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/deploy-foundry-models) · [查找资源终结点和密钥](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/gpt-with-vision#retrieve-key-and-endpoint)

```json
{
  "AI": {
    "Providers": {
      "Azure": {
        "Resources": {
          "east-chat": {
            "Endpoint": "https://aoai-contoso-eastus.openai.azure.com/",
            "ApiKeySecretName": "AZURE_EAST_CHAT_API_KEY"
          },
          "sweden-chat": {
            "Endpoint": "https://aoai-contoso-swedencentral.openai.azure.com/",
            "ApiKeySecretName": "AZURE_SWEDEN_CHAT_API_KEY"
          }
        },
        "Deployments": {
          "Chat": {
            "assistant": [
              { "Resource": "east-chat", "Deployment": "assistant-astra-us" },
              { "Resource": "sweden-chat", "Deployment": "assistant-astra-se" }
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
      }
    }
  }
}
```

| 示例值 | 在配置里的含义 |
| --- | --- |
| `Assistant` | 应用的场景名。业务代码设置 `Scenario = "Assistant"`，框架读取 `AI:Models:Chat:Assistant`。 |
| `Provider: Azure` | 使用 `.AddAzure()` 注册的提供方。 |
| `Model: assistant` | 应用自己取的模型别名，指向 `AI:Providers:Azure:Deployments:Chat:assistant` 这组候选。更换 Azure 模型或部署时，可以沿用这个别名。 |
| `Resource: east-chat` | 引用 `Resources` 里的 `east-chat` 节点。`east-chat` 是应用内取的资源别名。框架从这里读取终结点和该资源的密钥。`Endpoint` 可填资源根地址或以 `/openai/v1/` 结尾的基础地址。 |
| `Deployment: assistant-astra-us` | 东部资源中实际创建的 **Deployment name**。框架把它作为 Azure 请求体中的 `model`。Azure 通过这个部署调用底层模型 `gpt-6.2-astra`。 |
| `MaxOutputTokens: 1024` | 单次生成的 token 上限，映射到 Azure 的 `max_completion_tokens`，包含可见输出和推理 token。 |

### 为每个启用的资源提供密钥

`ApiKeySecretName` 是密钥的查找名称。默认实现可以读取同名环境变量或配置值。示例要求在应用进程的环境中分别设置下面两个变量，变量值填对应资源的 **KEY 1** 或 **KEY 2**。

| 环境变量 | 值来自 |
| --- | --- |
| `AZURE_EAST_CHAT_API_KEY` | `aoai-contoso-eastus` 的密钥 |
| `AZURE_SWEDEN_CHAT_API_KEY` | `aoai-contoso-swedencentral` 的密钥 |

另一种方式是通过宿主配置提供资源的 `ApiKey`。例如，使用环境变量配置源时，可设置 `AI__Providers__Azure__Resources__east-chat__ApiKey`，并省略 `east-chat` 的 `ApiKeySecretName`。启动时，每个启用的资源都需要解析出自己的密钥，包括排在后面的候选资源。

### 看一次实际的路由选择

候选按数组顺序检查。上例两个资源默认启用，`Scenario = "Assistant"` 经由别名 `assistant` 选中 `east-chat`。模块发送 `POST https://aoai-contoso-eastus.openai.azure.com/openai/v1/chat/completions`，请求头 `api-key` 使用东部资源的密钥，请求体包含 `"model": "assistant-astra-us"`。

在 `Resources:east-chat` 中加上 `"Enabled": false` 并重启宿主后，模块跳过整个东部资源，改用 `sweden-chat` 的终结点、密钥和部署 `assistant-astra-se`。停用的资源可以暂时省略密钥，重新启用前要配好。若一个资源上的其他模型仍需使用，只把对应的部署候选设为 `"Enabled": false`。请求失败后的重试继续使用同一目标。

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

普通响应和 SSE 流式响应都使用部署所选的 API。使用 JSON Object 或 JSON Schema 时，选择支持所需格式的部署并使用非流式调用。设置采样参数前，确认所选模型支持这些参数。[Azure Chat REST 参考](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/chat) · [Azure v1 API](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) · [推理模型参数](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)

## 使用 `AzureCloud.IMAGE` 生成和编辑图像

`IMAGE` 与 `CHAT` 使用相同的 `Resources` 和 `Models` 结构。两组资源独立配置。部署名从承载该模型的 Azure 资源中复制。在下面的示例中，`poster` 使用支持部署级接口的 GPT Image 1.5 部署，`retouch` 使用资源级 v1 接口的 GPT Image 2 部署。

```json
{
  "AzureCloud": {
    "IMAGE": {
      "Resources": {
        "main": { "Endpoint": "https://aoai-contoso-image.openai.azure.com/" },
        "v1": { "Endpoint": "https://aoai-contoso-image-v1.openai.azure.com/" }
      },
      "Models": {
        "poster": { "Resource": "main", "Deployment": "gpt-image-1.5", "Api": "Deployment" },
        "retouch": { "Resource": "v1", "Deployment": "gpt-image-2", "Api": "OpenAIV1" }
      }
    }
  }
}
```

通过 `AzureCloud__IMAGE__Resources__main__Secret` 和 `AzureCloud__IMAGE__Resources__v1__Secret` 注入对应资源的密钥。调用 `IAIImageGenerationClient` 或 `IAIImageEditClient` 时传 `Provider = "Azure"` 和相应的 `Model`，例如 `poster` 或 `retouch`。也可以在 `AI:Models:ImageGeneration` 或 `AI:Models:ImageEdit` 中用场景引用这些别名。设置 `AzureCloud.IMAGE` 后，两种图像能力都从这里查找部署。

| `Api` | 生成和编辑的请求格式 |
| --- | --- |
| `Deployment` | `AzureCloud.IMAGE` 的默认值。请求走 `/openai/deployments/<部署名>/images/generations` 或 `/openai/deployments/<部署名>/images/edits`，查询参数 `api-version` 默认是 `2025-04-01-preview`。可在模型条目中用 `ApiVersion` 指定部署支持的版本。编辑请求使用 multipart 的 `image` 字段。 |
| `OpenAIV1` | 请求走 `/openai/v1/images/generations?api-version=preview` 或 `/openai/v1/images/edits?api-version=preview`，部署名写入 `model`，编辑请求使用 multipart 的 `image[]` 字段。既有 `AI:Providers:Azure` 图像部署省略 `Api` 时沿用此接口。 |

两种编辑接口都接受 `mask`。应用可以省略 `ResponseFormat` 或指定 `b64_json`，适配器将返回的 base64 图像解码为 PNG。指定 `url` 会在发送请求前报错。[Azure 图像指南](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/dall-e) · [部署级图像 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview) · [资源级图像 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

## 使用 `AzureCloud.TTS` 合成语音

`TTS` 使用 Speech 资源的密钥和区域。以下配置可和 `AzureCloud.CHAT` 放在同一个根节点。用 `AzureCloud__TTS__Secret` 向宿主提供 Speech 资源的密钥，对应原配置里的 `Secret`。

```json
{
  "AzureCloud": {
    "TTS": {
      "Region": "eastus",
      "Endpoint": "https://eastus.api.cognitive.microsoft.com/",
      "Voice": "zh-CN-XiaoxiaoNeural"
    }
  }
}
```

East US 的区域 Cognitive Services 地址可用于获取 Speech 令牌。把它作为 `Endpoint` 时，模块根据 `Region` 选取 `https://eastus.tts.speech.microsoft.com/cognitiveservices/v1`，直接以 `Ocp-Apim-Subscription-Key` 认证并发送 SSML。省略 `Endpoint` 也使用区域合成地址。有自定义终结点时，填 Speech 资源的 HTTPS 根地址或合成地址。配置里的区域应与 Speech 资源匹配。[Speech 认证和标准合成终结点](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)

启用 `AzureCloud.TTS` 时，`azure-speech` 是为 Speech 保留的模型别名。应用没有配置 `AI:Models:Tts:Default` 时，模块把默认 TTS 场景指向该别名。业务可直接调用 `IAITtsClient.SynthesizeAsync(new AITtsRequest { Input = "你好" })`，也可以显式传 `Provider = "Azure"`、`Model = "azure-speech"`。在配置或请求中提供 `Voice`，两处都有值时以请求为准。输出默认 `mp3`，也支持 `wav`、`opus`（Ogg）和 `pcm`，`Speed` 范围为 `0.5` 到 `2`。

## 使用 Azure OpenAI 合成和转录语音

在 `AI:Providers:Azure:Deployments:Tts` 或 `AI:Providers:Azure:Deployments:Asr` 配置部署后，可分别通过 `IAITtsClient` 和 `IAIAsrClient` 调用。需要场景时，可在 `AI:Models:Tts` 或 `AI:Models:Asr` 中用 `Provider: Azure` 和对应部署别名配置 `Narration` 或 `Transcript`。两个接口使用 Azure OpenAI v1 预览终结点和所选资源的 `api-key`。[Azure 媒体 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

语音合成向 `/openai/v1/audio/speech?api-version=preview` 发送 JSON，从 `AITtsResponse.Audio.Content` 读取音频字节。调用需要提供 `Voice`，也可以在 `AI:Models:Tts:Narration` 中设置默认值，例如 `alloy`。按部署选择支持的 `ResponseFormat` 和 `Speed`。[语音合成 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/audio-create-speech)

语音转录向 `/openai/v1/audio/transcriptions?api-version=preview` 发送 multipart 请求，音频放在 `file` 字段。默认使用 `json`，适用于 GPT-4o 转录模型。部署支持 `verbose_json` 时（例如 Whisper），可通过 `ResponseFormat` 选择详细结果。其他格式会在发送请求前报错。[语音转录 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/audio-create-transcription)

## 排查调用问题

排查 `DeploymentNotFound` 时，到实际选中的资源下核对部署名。遇到认证错误时，检查该资源的密钥来源。Azure HTTP 客户端禁用自动重定向，密钥随请求发送到配置的终结点。
