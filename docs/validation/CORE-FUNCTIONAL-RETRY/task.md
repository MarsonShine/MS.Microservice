# 当前任务
Status: in_progress
Task: CORE-FUNCTIONAL-RETRY
Next: 让 F.Retry 复用执行器与指数策略，验收后独立提交。

范围：src/MS.Microservice.Core/Functional/FAsync.cs、src/MS.Microservice.Core/Common/Advance/Resilience/README.md、test/MS.Microservice.Core.Tests/Functional/FunctionalRetryCompatibilityTests.cs、docs/status.md、本文件、docs/validation/CORE-FUNCTIONAL-RETRY/。
验收：dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~Retry --verbosity quiet。
基线2a222a8；保持F.Retry原签名、N额外尝试、二倍间隔、无五分钟封顶、独立取消异常仍受旧条件处理。排除HTTP/AI/Networking、新公开策略与Builder。后续第三片处理延迟边界。不推送。
