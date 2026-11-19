# 当前任务
Status: in_progress
Task: AZURE-AI
Next: 按活动任务交付 Azure Chat、图像和语音适配。

范围为 AI Core 公共请求构造、Azure Provider 及测试、AI 和根解决方案入口、AI 模块说明及任务记录。部署 Azure 资源和远程推送在范围之外。

每片运行 `dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx -c Release`，并运行 `git diff --check`。

1. Azure Chat 选择资源、密钥和部署，验证配置和普通及流式请求。
2. GPT Image 生成和编辑复用部署路由，验证 Azure 请求字段和响应解析。
3. 语音合成和转录复用部署路由，验证 Azure 请求、响应和现有能力回归。
