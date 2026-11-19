# Azure OpenAI 模块验收

会话基线为 `d0e8bac`。验收运行于 2026-11-19，使用 .NET SDK 11.0.100。

| 分片 | `dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release` |
| --- | --- |
| Chat 和部署路由 | 194 项通过，其中 Azure 10 项，失败 0，跳过 0。 |
| 图像生成和编辑 | 200 项通过，其中 Azure 16 项，失败 0，跳过 0。 |

Azure 测试从宿主 DI 入口调用 AI 客户端，由测试 HTTP handler 捕获终结点、凭据和请求正文。区域选择验证使用本地配置，尚未调用真实 Azure 资源。
