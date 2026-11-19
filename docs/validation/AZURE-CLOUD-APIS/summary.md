# AzureCloud 接入验收

基线为 `f2729bc`，验收使用 .NET SDK 11.0.100。既有 Azure 接入指南的未提交修订归入第一片。

| 分片 | `dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release` |
| --- | --- |
| AzureCloud 聊天配置 | 209 项通过，其中 Azure 25 项，失败 0，跳过 0。 |
| 按部署选择聊天 API | 218 项通过，其中 Azure 34 项，失败 0，跳过 0。 |

Azure 测试使用本地 HTTP handler 检查实际构造的请求。在具备对应部署和密钥的环境中验证真实调用。
