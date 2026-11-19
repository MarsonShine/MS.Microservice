# Azure 调用实验

这个控制台项目通过 `MS.Microservice.AI.Azure` 运行实际的 Azure 请求。一次运行会对 `gpt-6.1-sol` 和 `gpt-6.1-terra` 各发送两次相同的聊天输入，调用 East US Speech 合成一段中文语音，并用 `gpt-image-2.5-flare` 生成一张灯塔插画。聊天 CSV 中的缓存读取和写入数据来自 Azure Responses 的响应。

## 配置和运行

准备两个 Foundry 聊天部署、一个图像部署和一个 Speech 资源。[appsettings.json](appsettings.json) 已填入本案例用到的终结点和部署名。换资源时可以沿用示例的资源键和模型键，只修改 `Endpoint`、`Deployment` 和对应密钥。业务用的模型键由 [AzureLabScenario.cs](AzureLabScenario.cs) 指定。

从进程环境提供下面的变量。Windows 下，Lab 也可读取当前用户环境变量。使用 [Azure 接入指南](../../../MS.Microservice.AI/docs/azure.md#通过环境变量注入密钥)中的分层 `AzureCloud__...__Secret` 变量时，Lab 优先读取已绑定的值。

| 变量 | 资源 |
| --- | --- |
| `AZURE_ESAT2_CHAT_API_KEY` | East US 2 聊天和图像资源，变量名中的 `ESAT2` 沿用本案例的拼写 |
| `AZURE_FZTEST001_CHAT_API_KEY` | `fztest001` 聊天资源 |
| `AZURE_SPEECH_KEY` | East US Speech 资源 |

从仓库根目录传入输出父目录。省略时使用用户主目录下的 `Downloads`。Lab 在输出父目录下为本次运行创建单独的子目录。

```powershell
dotnet run --project samples/Lab/MS.Microservice.Lab.Azure -c Release -- "$env:USERPROFILE\Downloads"
```

结果目录保存四次聊天的文本、Speech MP3、PNG、`cache-tokens.csv` 和 `summary.json`。CSV 的 `cached_tokens` 与 `cache_write_tokens` 保留 Azure 实际报告的值。先看首轮是否写入，再看第二轮是否命中。请求失败时，程序把该步骤的错误写入摘要，其余独立步骤仍会运行，最后以非零状态退出。

## 用 Assistant 场景切换模型

`Assistant` 是应用给聊天用途起的名字。已有的 `AzureCloud.CHAT.Models` 继续保存模型到 Azure 资源和部署的映射。新增的场景只选其中一个模型键，并提供默认输出上限。[appsettings.Assistant.json](appsettings.Assistant.json)

| 配置项 | 示例值 | 作用 |
| --- | --- | --- |
| `AI:Models:Chat:Assistant:Provider` | `Azure` | 选择 Azure 适配器。 |
| `AI:Models:Chat:Assistant:Model` | `gpt-6.1-sol` | 引用 `AzureCloud.CHAT.Models` 的键。该条目指向 `east2` 上的部署。 |
| `AI:Models:Chat:Assistant:MaxOutputTokens` | `384` | 提供默认输出上限，与直接指定模型的示例相同。 |

给同一组 `messages` 发起请求时，直接调用在 C# 里写出三个值。场景调用把这三个值交给配置。两种写法使用同一个 `IAIChatClient`。[调用代码](AzureLabScenario.cs)

```csharp
// 直接调用
await chat.GetResponseAsync(new AIChatRequest
{
    Provider = "Azure",
    Model = "gpt-6.1-sol",
    MaxOutputTokens = 384,
    Messages = messages,
}, cancellationToken);

// 按场景调用
await chat.GetResponseAsync(new AIChatRequest
{
    Scenario = "Assistant",
    Messages = messages,
}, cancellationToken);
```

`--assistant` 加载场景配置，只需要两把聊天资源的密钥。它发送两轮聊天并保存回复和缓存 CSV。首次运行选 `gpt-6.1-sol`。随后在当前 PowerShell 会话覆盖场景的 `Model`，相同的调用代码就会选到 `fztest001` 上的 `gpt-6.1-terra`。

```powershell
$env:AI__Models__Chat__Assistant__Model = $null
dotnet run --project samples/Lab/MS.Microservice.Lab.Azure -c Release -- --assistant "$env:USERPROFILE\Downloads"

$env:AI__Models__Chat__Assistant__Model = 'gpt-6.1-terra'
dotnet run --project samples/Lab/MS.Microservice.Lab.Azure -c Release -- --assistant "$env:USERPROFILE\Downloads"
```

查看每次运行的 `summary.json`，对比 `Assistant` 解析到的模型和 Azure 返回的模型。显式在请求里传 `Model` 会跳过场景解析。超时、并发和响应预算的默认值见 [Azure 接入指南](../../../MS.Microservice.AI/docs/azure.md)和[参数参考](../../../MS.Microservice.AI/docs/azure-configuration.md)。
