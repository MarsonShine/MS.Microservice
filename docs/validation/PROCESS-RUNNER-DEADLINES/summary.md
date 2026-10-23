# ProcessRunner 超时参数修复

基线：cfecf51c2e6cbc99ebd1e9749581511237f76812。日期：2026-10-23。
范围与原验收约定见 [任务契约](task.md)。本次为 f4483c5 审查后的独立修复，不改写原提交，不推送。

## 根因与改动

执行期限的正数校验拒绝了媒体旧接口支持的 Timeout.InfiniteTimeSpan；清理期限只检查正数，超出计时器范围的配置会在异常清理阶段才抛错，覆盖原始异常并跳过后续清理。

共享 ProcessRunner 在枚举参数、启动进程前校验两种期限：执行允许无限或有效正期限；清理只允许有效正的有限期限。有限期限沿用 CancellationTokenSource 的整毫秒截取和 UInt32.MaxValue - 1 上限，保留合法小数毫秒边界。原方法签名、媒体适配与非零退出异常转换保持不变。

## 验收证据

先增加回归测试，在未修复实现上执行：

- Processes：20 项中 14 通过、6 失败，见 [修复前输出](red-process.txt)。失败覆盖无限执行、无限执行取消和超限参数的提前拒绝。
- 媒体适配：1 项失败，见 [修复前兼容输出](red-media.txt)。

修复后补充毫秒取整边界，并执行：

```powershell
dotnet test test/MS.Microservice.Processes.Tests -c Release --no-restore --verbosity quiet
dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Tests -c Release --no-restore --filter "FullyQualifiedName~Runner_InfiniteTimeoutRemainsSupported" --verbosity quiet
```

- Processes：22 通过、0 失败、0 跳过，见 [测试输出](process-tests.txt)。
- 媒体旧接口：1 通过、0 失败、0 跳过，见 [兼容测试输出](media-tests.txt)。测试使用 dotnet 子进程，无需 FFmpeg。
- `git diff --check` 与 `harness.py check` 通过。

媒体测试依赖恢复使用本机包缓存（RestoreSources=$env:USERPROFILE/.nuget/packages，NuGetAudit=false），未修改项目依赖或仓库源配置。原始日志含编译警告及路径，只表示所列命令的结果。

## 限制

本次验证运行于 Windows/.NET 10；未执行漏洞审计、真实 FFmpeg/Python 工作负载或跨平台进程树终止验证。本次修复恢复参数与兼容契约，不改变进程终止策略，也不宣称终止所有后代进程。
