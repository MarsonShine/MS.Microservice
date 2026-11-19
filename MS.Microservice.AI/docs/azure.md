# 接入 Azure OpenAI

先在 Azure 中创建资源并部署所需模型。模型是否可部署取决于区域、版本和部署类型。为同一个业务场景更换区域时，可以保留应用中的模型别名，只调整它指向的部署配置。[Microsoft Foundry 区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)

宿主引用 `MS.Microservice.AI.Azure`，通过现有 AI 注册入口启用 Azure。

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddAzure();
```

## 配置资源和部署

下面的 `Assistant` 场景使用 `assistant` 作为逻辑模型别名。候选列表的第一项所属资源已停用，因此请求会发送给第二项 `sweden-chat` 资源上的 `assistant-b` 部署。将示例中的资源终结点和部署名称替换为 Azure 门户中对应的值。

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
          }
        },
        "Deployments": {
          "Chat": {
            "assistant": [
              { "Resource": "east-chat", "Deployment": "assistant-a" },
              { "Resource": "sweden-chat", "Deployment": "assistant-b" }
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

| 配置项 | 用法 |
| --- | --- |
| `Resources:<资源名>:Endpoint` | HTTPS 资源根地址，例如 `https://example-sweden.openai.azure.com/`，也可以填到 `/openai/v1/` 为止的地址。 |
| `Resources:<资源名>:ApiKey` | 该资源的密钥。启用环境变量配置源后，可通过 `AI__Providers__Azure__Resources__sweden-chat__ApiKey` 注入示例所需的密钥。 |
| `Resources:<资源名>:ApiKeySecretName` | `ApiKey` 为空时使用的密钥名称，由 `IAISecretProvider` 解析。默认实现可读取同名环境变量或配置值。为各资源使用各自的密钥名称。 |
| `Resources:<资源名>:Enabled` | 资源开关，默认 `true`。 |
| `Deployments:Chat:<别名>` | 按优先级排列的部署候选。`Resource` 引用资源名，`Deployment` 使用 Azure 中的部署名称。每项的 `Enabled` 默认为 `true`。 |

配置加载时校验场景引用和部署映射。运行时调用采用首个启用且所属资源已启用的候选。重试继续使用同一目标。要切换区域，调整候选顺序或开关并重启宿主。

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

排查 `DeploymentNotFound` 时，到实际选中的资源下核对部署名。遇到认证错误时，检查该资源的密钥来源。Azure HTTP 客户端禁用自动重定向，密钥随请求发送到配置的终结点。
