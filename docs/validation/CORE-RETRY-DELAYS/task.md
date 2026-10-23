# 当前任务
Status: in_progress
Task: CORE-RETRY-DELAYS
Next: 复现延迟边界，修复封顶及长等待，验收后独立提交。

范围：src/MS.Microservice.Core/Common/Advance/Resilience/RetryStrategy/ExponentialBackoffRetryStrategy.cs、RetryExecutor.cs、该目录README.md、src/MS.Microservice.Core/Functional/FAsync.cs、test/MS.Microservice.Core.Tests/Common/Advance/Resilience/RetryDelayBoundaryTests.cs、docs/status.md、本文件、docs/validation/CORE-RETRY-DELAYS/。
验收：dotnet test test/MS.Microservice.Core.Tests -c Release --no-restore --filter FullyQualifiedName~Retry --verbosity quiet。
基线3df005f；保留正常次数和默认延迟。指数结果先封顶避免溢出，零base正确为0；合法长等待分段并可取消；F.Retry负delayMillis明确拒绝（非法参数修正）。不扩展重试策略/超时协议，不改HTTP/AI/Networking；本地提交不推送。
