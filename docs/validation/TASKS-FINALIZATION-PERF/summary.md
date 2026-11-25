# 任务终态事务验收

2026-11-25，基线 `157880b`。此次修改覆盖 EF 任务存储的执行所有权查询与终态事务。[实现说明和流程图](../../../MS.Microservice.Tasks/docs/persistence-design.md#终态写入要从-jobs-读哪些列)

## SQLite 端到端对照

Windows x64，Intel Core i5-9400，.NET SDK 11.0.100，运行时 11.0.0，Release。两份代码使用同一份[基准程序](../../../MS.Microservice.Tasks/benchmarks/MS.Microservice.Tasks.Benchmarks/Program.cs)，分别在独立进程运行，固定 `DOTNET_TieredCompilation=0`。按旧版、新版、新版、旧版顺序串行运行。每种输入先预热两轮，再取七轮中位数，每轮 32 个任务。

每轮使用新的 SQLite 内存数据库。任务有固定的 Scope、JobId 序列和已知字节数的 ASCII JSON。提交和领取在计时前完成，计时覆盖公共 `IJobStore.CompleteAsync` 的独立 DI 作用域、数据库事务及提交。每次调用须返回成功，计时后逐条核对原始正文、任务与唯一尝试的成功状态、100% 进度和结果引用。B/次使用 `GC.GetTotalAllocatedBytes(true)` 的进程托管分配差额，本次未测量 SQLite 原生缓存及进程峰值内存。

| 顺序 | 版本 | 32 B JSON，µs/次 | 32 B JSON，B/次 | 128 KiB JSON，µs/次 | 128 KiB JSON，B/次 |
| --- | --- | ---: | ---: | ---: | ---: |
| 1 | 基线 | 929.9 | 148,092 | 1,239.8 | 411,082 |
| 2 | 优化后 | 868.6 | 149,270 | 1,015.1 | 149,270 |
| 3 | 优化后 | 850.5 | 149,626 | 880.7 | 149,626 |
| 4 | 基线 | 843.2 | 147,596 | 1,021.7 | 410,568 |

128 KiB 正文场景，两次中位数取平均后，每次完成的托管分配下降约 255 KiB（63.6%），与少构造一份 UTF-16 正文的量级相符。32 B 场景的分配增加约 1.6 KiB。小正文耗时区间重叠，大正文两次新版耗时均低于基线。该测量覆盖本地 SQLite 的完整终态事务，生产数据库的网络、锁竞争及磁盘写入会影响端到端结果。

在仓库根目录复跑。比较基线时，把当前 `MS.Microservice.Tasks/benchmarks/MS.Microservice.Tasks.Benchmarks` 下的项目文件和 `Program.cs` 放到 `157880b` 检出的同一相对位置；各自编译、分别运行。

```powershell
dotnet build MS.Microservice.Tasks/benchmarks/MS.Microservice.Tasks.Benchmarks -c Release
$env:DOTNET_TieredCompilation = '0'
dotnet MS.Microservice.Tasks/benchmarks/MS.Microservice.Tasks.Benchmarks/bin/Release/net11.0/MS.Microservice.Tasks.Benchmarks.dll
```

## 兼容性与 SQL

`dotnet test MS.Microservice.Tasks/MS.Microservice.Tasks.slnx -c Release` 退出码 0。存储与执行测试 30 项、Dashboard 4 项、Messaging 事务 3 项，全部通过。

新增回归通过 SQLite 命令拦截器观察 `CompleteAsync`，断言终态事务的 SELECT 读取 `ProgressMessage` 并排除 `PayloadJson`。原有测试继续覆盖取消与成功的提交顺序、旧执行令牌、进度单调性、历史与重试、停止时恢复和终态写入故障。[查询和状态测试](../../../MS.Microservice.Tasks/test/MS.Microservice.Tasks.Tests/JobStoreTests.cs) · [终态故障测试](../../../MS.Microservice.Tasks/test/MS.Microservice.Tasks.Tests/JobFinalizationTests.cs)
