# Azure Lab Assistant 场景实测

基线为 `1cb983a`，叠加此前未提交的 Azure Lab 和文档。2026-11-19 使用 .NET SDK 11.0.100 构建 Lab Release，0 警告、0 错误。示例请求在两个真实 Azure 部署上均通过。

两次执行的命令均为 `dotnet run --project samples/Lab/MS.Microservice.Lab.Azure -c Release --no-build -- --assistant <Downloads>`。第一次清除进程内的 `AI__Models__Chat__Assistant__Model` 覆盖，采用场景配置的默认模型。第二次将该变量设置为 `gpt-6.1-terra`。两次都使用只传 `Scenario = "Assistant"` 和消息的请求，解析出的输出上限为 384。进程退出码为 0，四次聊天均返回 HTTP 200 和 `Azure smoke test OK`。

| 场景配置 | Azure 返回的模型 | 第二次缓存命中 | Downloads 子目录 |
| --- | --- | ---: | --- |
| 默认模型 | `gpt-6.1-sol` | 2,664 | `Azure-Lab-20261119-103830Z-f8e0e24a` |
| 环境变量覆盖模型 | `gpt-6.1-terra` | 2,661 | `Azure-Lab-20261119-103800Z-dfd16441` |

参数取值和切换命令见 [Lab 场景示例](../../../samples/Lab/MS.Microservice.Lab.Azure/README.md#用-assistant-场景切换模型)。两次运行各保存一份 `summary.json`、缓存 CSV 和两条聊天文本。
