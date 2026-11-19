# AzureCloud 接入验收

基线为 `f2729bc`，验收使用 .NET SDK 11.0.100。既有 Azure 接入指南的未提交修订归入第一片。

变更覆盖 Azure Provider、相关 AI Core 传输、Azure 测试和 AI 模块文档。聊天和图像按模型部署选择 API，TTS 使用独立的 Speech REST 适配器。原有 `AI:Providers:Azure` 仍可用于 Azure OpenAI 部署。

| 分片 | `dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release` |
| --- | --- |
| AzureCloud 聊天配置 | 209 项通过，其中 Azure 25 项，失败 0，跳过 0。 |
| 按部署选择聊天 API | 218 项通过，其中 Azure 34 项，失败 0，跳过 0。 |
| Azure AI Speech 合成 | 231 项通过，其中 Azure 47 项，失败 0，跳过 0。 |
| 按部署选择图像 API | 241 项通过，其中 Azure 57 项，失败 0，跳过 0。 |

Azure 测试使用本地 HTTP handler 检查实际构造的请求。真实 Azure 调用尚未联调。

[接入指南](../../../MS.Microservice.AI/docs/azure.md)的 JSON 示例均能解析，聊天和图像模型引用的资源均存在。`dotnet pack MS.Microservice.AI/src/MS.Microservice.AI.Azure/MS.Microservice.AI.Azure.csproj --no-build -c Release` 成功，包内 `azure.md` 与指南一致。各片暂存差异通过 `git diff --cached --check`。
