# 当前任务
Status: in_progress
Task: THREADING-SEMAPHORE-LEASE
Next: 提取名额租约并接入 AI 三个请求路径，验收后独立提交。

范围：src/MS.Microservice.Threading/SemaphoreSlimExtensions.cs、Threading README、test/MS.Microservice.Threading.Tests/SemaphoreLeaseTests.cs、MS.Microservice.AI/src/MS.Microservice.AI.Core/OpenAICompatibleChatProviderBase.cs、OpenAICompatibleMediaProviderBase.cs、AI.Core.csproj、AI Core README、MS.Microservice.AI/MS.Microservice.AI.slnx、MS.Microservice.AI/test/MS.Microservice.AI.Core.Tests/AIConcurrencyLeaseTests.cs、根 README、docs/status.md、本文件、docs/validation/THREADING-SEMAPHORE-LEASE/。
验收：Threading 测试、AI Core 测试、OpenAI/DeepSeek/Qwen Provider 测试。使用本机缓存恢复（RestoreSources=$env:USERPROFILE/.nuget/packages，NuGetAudit=false）。
排除：模型生命周期、RabbitMQ、Tasks、缓存和新调度策略。基线 22bdd68；保留旧接口与并发配置。验收后本地提交、不推送。
