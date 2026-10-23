# 当前任务
Status: in_progress
Task: CORE-RETRY-EXECUTION
Next: 验证旧基线，补齐取消和异常传播，再验收并独立提交。

范围：src/MS.Microservice.Core/Common/Advance/Resilience/RetryExecutor.cs、该目录 README.md、Core README、test/MS.Microservice.Core.Tests/Common/Advance/Resilience/RetryExecutionContractTests.cs、docs/status.md、本文件、docs/validation/CORE-RETRY-EXECUTION/。
验收：dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~Retry --verbosity quiet。
基线 b4b9394；保持旧接口、次数、策略/条件扩展、WithTimeout预算语义和结果耗尽异常。仅修复取消、异常堆栈、同步上下文与尝试上下文。
后续片：F.Retry共用循环；延迟边界。HTTP、AI、Networking 均排除。按用户要求本地提交，不推送。
