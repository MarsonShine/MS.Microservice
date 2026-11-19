# 当前任务
Status: in_progress
Task: AZURE-CLOUD-APIS
Next: 依次接入 AzureCloud 聊天配置、Responses、Speech 和图像 API。

范围为 Azure Provider、相关 AI Core 传输、Azure 测试、AI 模块文档及任务记录。Azure 资源变更、其他 Provider 迁移和远程推送不在范围内。

各片运行 `dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release` 和 `git diff --check`。

1. 按 `AzureCloud.CHAT` 绑定资源和部署，保留原有 Azure 配置入口。
2. 按聊天部署选择 Azure Responses 或 Chat Completions，验证普通和流式结果。
3. 从 `AzureCloud.TTS` 接入 Azure AI Speech。
4. 从 `AzureCloud.IMAGE` 选择部署级或资源级图像 API，验证生成和编辑。
