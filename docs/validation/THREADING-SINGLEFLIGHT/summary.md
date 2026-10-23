# Singleflight 迁移与修复
基线：6d02fbf；范围见 [任务契约](task.md)。独立 slice，本地提交、不推送。

根因：旧实现仅发布成功结果；失败时等待者挂起。等待者按键删除会误删下一代；仅字符串键会让不同结果类型混用。
实现：Threading 共享实现按键/结果类型隔离，只由拥有者原子删除对应条目，清理后发布成功/失败/取消；等待取消不影响共享执行。Core 原签名转发，引用方向为 Core → Threading（纯 .NET）。

## 验收
- 未修复时，Core 新增异常传播回归测试失败：等待者只收到测试保护超时，见 [红灯输出](red-core.txt)。
- `dotnet test test/MS.Microservice.Threading.Tests -c Release -p:RestoreSources="$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false --verbosity quiet`：14 passed，0 failed，0 skipped，见 [输出](threading-tests.txt)。
- `dotnet test test/MS.Microservice.Core.Tests -c Release -p:RestoreSources="$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false --filter FullyQualifiedName~Concurrent --verbosity quiet`：9 passed，0 failed，0 skipped，见 [输出](core-tests.txt)。

覆盖同键竞争、成功/失败/操作取消、类型与键隔离、首调用者/跟随者取消、旧等待者不删除新操作、Core 兼容入口。子代理只读审查未发现缺陷；无人等待的共享异常通过显式观察处理，未做依赖 GC 时机的测试。验证仅限上述范围；离线恢复未进行漏洞审计。
