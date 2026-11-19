# Azure OpenAI 模块验收

会话基线为 `d0e8bac`。验收运行于 2026-11-19，使用 .NET SDK 11.0.100。

范围包括 AI Core 请求构造、Azure Provider、两处解决方案入口、定向测试和接入文档。各能力按逻辑模型别名选择配置中首个启用的资源和部署，重试使用同一目标。

| 分片 | `dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release` |
| --- | --- |
| Chat 和部署路由 | 194 项通过，其中 Azure 10 项，失败 0，跳过 0。 |
| 图像生成和编辑 | 200 项通过，其中 Azure 16 项，失败 0，跳过 0。 |
| 语音合成和转录 | 204 项通过，其中 Azure 20 项，失败 0，跳过 0。 |

Azure 测试从宿主 DI 入口调用 AI 客户端，由测试 HTTP handler 捕获终结点、凭据和请求正文。区域选择验证使用本地配置，尚未调用真实 Azure 资源。

同时核对了两个解决方案的项目入口与[接入指南](../../../MS.Microservice.AI/docs/azure.md)示例中的五条路由。`dotnet pack MS.Microservice.AI/src/MS.Microservice.AI.Azure/MS.Microservice.AI.Azure.csproj --no-build -c Release` 成功生成 NuGet 包，包内包含同一份接入指南。
