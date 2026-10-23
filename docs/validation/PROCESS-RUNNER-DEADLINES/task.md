# 当前任务
Status: in_progress
Task: PROCESS-RUNNER-DEADLINES
Next: 先用回归测试复现，再修复共享参数校验并验证；完成后创建独立本地提交，不推送。

范围：src/MS.Microservice.Processes/ProcessRunner.cs、该组件 README、test/MS.Microservice.Processes.Tests/RunnerTests.cs、MS.Microservice.Media/test/MS.Microservice.Media.Tests/Video/VideoProbeTests.cs、docs/status.md、本文件、docs/validation/PROCESS-RUNNER-DEADLINES/。

排除：BatchProcessing、进程协议、终止策略和其他组件。现有接口签名及媒体异常类型保持兼容。

验收命令：
- `dotnet test test/MS.Microservice.Processes.Tests -c Release --no-restore --verbosity quiet`
- `dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Tests -c Release --no-restore --filter "FullyQualifiedName~Runner_InfiniteTimeoutRemainsSupported" --verbosity quiet`

恢复依赖如有需要，只使用本机缓存并关闭此次恢复的 NuGetAudit；不修改仓库依赖源。基线为 cfecf51，验收输出保存在上述证据目录。
