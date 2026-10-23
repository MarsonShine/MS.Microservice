# 当前任务
Status: in_progress
Task: THREADING-SINGLEFLIGHT
Next: 先复现旧入口异常挂起，再迁移并验证；验收通过后提交第一片。

范围：src/MS.Microservice.Threading/SingleflightManager.cs、Threading README、src/MS.Microservice.Core/Concurrent/SingleflightManager.cs、Core.csproj、Core README、test/MS.Microservice.Threading.Tests/SingleflightTests.cs、test/MS.Microservice.Core.Tests/Concurrent/SingleflightManagerTests.cs、根 README、docs/status.md、本文件、docs/validation/THREADING-SINGLEFLIGHT/。
验收：dotnet test test/MS.Microservice.Threading.Tests -c Release；dotnet test test/MS.Microservice.Core.Tests -c Release --filter FullyQualifiedName~Concurrent。
依赖使用本机缓存恢复（RestoreSources=$env:USERPROFILE/.nuget/packages，NuGetAudit=false）。不修改依赖源。
排除：异步名额租约、AI 接入、缓存实现、分布式协调。基线 6d02fbf。提交本地供审查，不推送。
