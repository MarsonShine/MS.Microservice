# 接入 Azure AI 服务

先在 Azure 中创建资源并部署所需模型。模型是否可部署取决于区域、版本和部署类型。为同一个业务场景更换区域时，可以保留应用中的模型别名，只调整它指向的部署配置。[Microsoft Foundry 区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)

宿主引用 `MS.Microservice.AI.Azure`，通过现有 AI 注册入口启用 Azure。

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddAzure();
```

## 当前支持的接口

`AddAzure()` 访问 Azure 资源级 `/openai/v1/` 接口，通过资源密钥 `api-key` 认证。聊天调用 Chat Completions。Foundry 中提供同一接口和认证方式的非 OpenAI 部署，例如部分 DeepSeek 和 Grok 模型，也可以使用这里的资源和部署配置。在 Foundry 的部署详情核对调用路径、认证方式和所需参数。配置里的 `Deployment` 填 Azure 部署名，模块按配置查找部署。[Foundry v1 模型支持](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle#model-support) · [Foundry 模型终结点](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/endpoints)

部署如果使用项目级终结点、Responses API 或 Microsoft Entra ID，需要相应的调用适配。语音合成示例调用 Azure OpenAI 的音频接口。使用 Azure AI Speech 的应用按 Speech SDK 或 Speech REST 的终结点和认证方式接入。[Foundry 项目级聊天示例](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-chat-reasoning) · [Azure AI Speech REST](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)

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
        "gpt-5.4": { "Resource": "main", "Deployment": "gpt-5.4" },
        "DeepSeek-V4.1-Flash-1017": { "Resource": "terra", "Deployment": "DeepSeek-V4.1-Flash-1017" }
      }
    }
  }
}
```

分别为宿主提供 `AzureCloud__CHAT__Resources__main__Secret` 和 `AzureCloud__CHAT__Resources__terra__Secret`，值是对应资源的 API 密钥。调用 `IAIChatClient` 时传 `Provider = "Azure"` 和 `Model = "DeepSeek-V4.1-Flash-1017"`，模块选出 `terra` 并把部署名写入请求。也可以在 `AI:Models:Chat` 中配置业务场景，让场景的 `Model` 指向 `AzureCloud.CHAT.Models` 的键。

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

普通响应和 SSE 流式响应共用 `/openai/v1/chat/completions`。使用 JSON Object 或 JSON Schema 时，选择支持所需格式的部署并使用非流式调用。设置采样参数前，确认所选模型支持这些参数。[Azure Chat REST 参考](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/chat) · [Azure v1 API](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) · [推理模型参数](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)

## 按能力添加图像和语音场景

媒体能力也用同一套映射。把部署候选加在 `AI:Providers:Azure:Deployments:<能力>:<别名>`，把场景加在 `AI:Models:<能力>:<场景名>`，场景里的 `Provider` 填 `Azure`，`Model` 填表中的应用别名。候选的 `Resource` 指向实际承载该部署的资源。

| 能力 | 应用场景名 | 应用 `Model` 别名 | Azure 模型 ID 示例 | Azure `Deployment` 示例 |
| --- | --- | --- | --- | --- |
| `ImageGeneration` | `Poster` | `poster` | `gpt-image-2` | `gpt-image-poster` |
| `ImageEdit` | `Retouch` | `retouch` | `gpt-image-2` | `gpt-image-retouch` |
| `Tts` | `Narration` | `narration` | `gpt-4o-mini-tts` | `tts-narration` |
| `Asr` | `Transcript` | `transcriber` | `gpt-4o-mini-transcribe` | `audio-transcriber` |

## 生成和编辑图像

在 Azure 中部署 GPT Image 模型后，通过 `IAIImageGenerationClient` 调用 `Poster` 场景，通过 `IAIImageEditClient` 调用 `Retouch` 场景。两种能力可以指向同一个部署，也可以选择各自的资源和部署。

生成和编辑分别调用 `/openai/v1/images/generations?api-version=preview` 和 `/openai/v1/images/edits?api-version=preview`。编辑请求以 multipart 的 `image[]` 字段上传图像，也可带一个 `mask`。应用可以省略 `ResponseFormat` 或指定 `b64_json`。适配器省略 Azure 请求中的 `response_format` 字段，将返回的 base64 图像解码为 `AIImageResponse.Images` 中的 PNG 内容。配置或调用中指定 `url` 会在发送请求前报错。[Azure 图像指南](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/dall-e) · [Azure 媒体 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

## 合成和转录语音

通过 `IAITtsClient` 调用 `Narration` 场景，通过 `IAIAsrClient` 调用 `Transcript` 场景。请求携带所选资源的 `api-key`。两个接口使用 Azure OpenAI v1 预览终结点。[Azure 媒体 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/openai/reference-preview-latest)

语音合成向 `/openai/v1/audio/speech?api-version=preview` 发送 JSON，从 `AITtsResponse.Audio.Content` 读取音频字节。调用需要提供 `Voice`，也可以在 `AI:Models:Tts:Narration` 中设置默认值，例如 `alloy`。按部署选择支持的 `ResponseFormat` 和 `Speed`。[语音合成 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/audio-create-speech)

语音转录向 `/openai/v1/audio/transcriptions?api-version=preview` 发送 multipart 请求，音频放在 `file` 字段。默认使用 `json`，适用于 GPT-4o 转录模型。部署支持 `verbose_json` 时（例如 Whisper），可通过 `ResponseFormat` 选择详细结果。其他格式会在发送请求前报错。[语音转录 REST 参考](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/audio-create-transcription)

## 排查调用问题

排查 `DeploymentNotFound` 时，到实际选中的资源下核对部署名。遇到认证错误时，检查该资源的密钥来源。Azure HTTP 客户端禁用自动重定向，密钥随请求发送到配置的终结点。
