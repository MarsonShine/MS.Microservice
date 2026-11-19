# 接入 Azure AI 服务

本例使用两个 Azure Foundry 资源和一个 Azure AI Speech 资源。East US 2 上的同一个资源承载 `gpt-6.1-sol` 和 `gpt-image-2.5-flare`，`fztest001` 承载 `gpt-6.1-terra`。`Endpoint` 从 Azure 资源的 **Keys and Endpoint** 获取，`Deployment` 填 Foundry **Build → Models** 里的部署名。模型能否部署取决于区域、版本和部署类型。[查看 Azure 部署](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/deploy-foundry-models) · [区域可用性](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)

宿主引用 `MS.Microservice.AI.Azure`，传入包含根节点 `AI` 和 `AzureCloud` 的配置。

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMicroserviceAI(builder.Configuration)
    .AddAzure();
```

如果宿主把独立的配置段传给 `AddMicroserviceAI`，可以用 `.AddAzure(configuration.GetSection("AzureCloud"))` 显式指定 AzureCloud 配置段。

## 完整配置

下面是 Azure Lab 使用的 [appsettings.json](../../samples/Lab/MS.Microservice.Lab.Azure/appsettings.json)。`Resources` 的键是配置内的资源名，`Models` 的键是业务传给 `Model` 的别名。这里让别名和部署名保持一致。更换资源时可以修改别名对应的 `Resource` 和 `Deployment`，业务仍使用同一个别名。

```json
{
  "AI": {
    "Providers": {
      "Azure": {
        "TimeoutSeconds": 330,
        "MaxRetryAttempts": 0,
        "ConcurrencyLimit": 8,
        "MaxResponseBytes": 67108864,
        "MaxErrorResponseBytes": 65536,
        "MaxStreamLineCharacters": 1048576
      }
    }
  },
  "AzureCloud": {
    "CHAT": {
      "Resources": {
        "east2": { "Endpoint": "https://admin-m9ks5c5n-eastus2.openai.azure.com/" },
        "terra": { "Endpoint": "https://fztest001.openai.azure.com/" }
      },
      "Models": {
        "gpt-6.1-sol": { "Resource": "east2", "Deployment": "gpt-6.1-sol", "Api": "Responses" },
        "gpt-6.1-terra": { "Resource": "terra", "Deployment": "gpt-6.1-terra", "Api": "Responses" }
      }
    },
    "TTS": {
      "Region": "eastus",
      "Endpoint": "https://eastus.api.cognitive.microsoft.com/",
      "Voice": "zh-CN-XiaoxiaoNeural"
    },
    "IMAGE": {
      "Resources": {
        "east2": { "Endpoint": "https://admin-m9ks5c5n-eastus2.openai.azure.com/" }
      },
      "Models": {
        "gpt-image-2.5-flare": { "Resource": "east2", "Deployment": "gpt-image-2.5-flare", "Api": "OpenAIV1" }
      }
    }
  }
}
```

两个聊天部署走 Azure 资源级 Responses API。文生图走资源级 v1 Images API，复用 `east2` 的终结点和密钥。`CHAT.Resources` 和 `IMAGE.Resources` 各自查找资源，因此示例在两个节点中都声明 `east2`。`TTS` 使用独立的 Speech 密钥和区域，模块将区域地址解析为 `https://eastus.tts.speech.microsoft.com/cognitiveservices/v1`，用 SSML 发起合成。[Azure Responses API](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses) · [Azure 图像生成 API](https://learn.microsoft.com/en-us/azure/foundry/reference/rest/images-create-image) · [Azure Speech REST](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)

默认的 Lab 直接指定 `Provider` 和 `Model`，可以省略 `AI:Models:Chat:Assistant`。如果应用通过 `Scenario = "Assistant"` 选择模型，保留该场景并让它的 `Model` 指向 `AzureCloud.CHAT.Models` 中的键。[Lab 场景示例](../../samples/Lab/MS.Microservice.Lab.Azure/README.md#用-assistant-场景切换模型) · [场景参数参考](azure-configuration.md)

`AI:Providers:Azure` 中的超时、重试、并发量和响应预算适用于整个 Azure Provider。示例给图像生成留出 330 秒，设置 `MaxRetryAttempts: 0` 让每次 Lab 操作只发出一个请求。Lab 由模块管理请求超时。生产宿主如需超过 `HttpClient` 的默认超时，也要配置命名客户端 `MS.Microservice.AI.Azure` 的 `HttpClient.Timeout`。完整默认值、单位和覆盖规则见 [Azure 配置参数参考](azure-configuration.md)。

## 通过环境变量注入密钥

上面的 JSON 只保存终结点和部署。宿主加载环境变量配置源后，双下划线 `__` 对应配置路径里的冒号。下表列出实际使用的变量及其注入位置。Lab 识别表中的原有变量名，并将变量值注入对应节点。

| 原有环境变量 | 注入位置 |
| --- | --- |
| `AZURE_ESAT2_CHAT_API_KEY` | `AzureCloud__CHAT__Resources__east2__Secret` 和 `AzureCloud__IMAGE__Resources__east2__Secret` |
| `AZURE_FZTEST001_CHAT_API_KEY` | `AzureCloud__CHAT__Resources__terra__Secret` |
| `AZURE_SPEECH_KEY` | `AzureCloud__TTS__Secret` |

普通宿主可以直接设置右列变量。已有左列变量且当前 PowerShell 进程能读取它们时，可在启动宿主前映射：

```powershell
$env:AzureCloud__CHAT__Resources__east2__Secret = $env:AZURE_ESAT2_CHAT_API_KEY
$env:AzureCloud__IMAGE__Resources__east2__Secret = $env:AZURE_ESAT2_CHAT_API_KEY
$env:AzureCloud__CHAT__Resources__terra__Secret = $env:AZURE_FZTEST001_CHAT_API_KEY
$env:AzureCloud__TTS__Secret = $env:AZURE_SPEECH_KEY
```

`Secret` 接收的是密钥值。给两个 `east2` 节点注入同一个环境变量的值即可。

## 运行真实调用

从仓库根目录运行 [Azure Lab](../../samples/Lab/MS.Microservice.Lab.Azure/README.md)。它用两次完全相同的合成输入验证每个聊天部署的缓存，然后保存回复、Speech MP3、文生图 PNG 和用量报告。缓存 CSV 读取 Azure Responses 返回的 `input_tokens_details.cached_tokens` 和 `cache_write_tokens`。通用 `AIUsage` 提供输入、输出和总 token；Lab 从服务响应补充缓存字段。[Azure 提示词缓存](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/prompt-caching)

遇到 `DeploymentNotFound`，在请求实际选中的资源下核对部署名。认证失败时检查该资源对应的环境变量，以及宿主是否加载了环境变量配置源。
