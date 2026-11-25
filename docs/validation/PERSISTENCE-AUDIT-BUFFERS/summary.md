# EF Core 审计 JSON 缓冲区验收

2026-11-25，Windows 10 x64（10.0.19045），Intel Core i5-9400（6 个逻辑处理器），.NET SDK 11.0.100、运行时 11.0.0、EF Core SQLite 10.0.15，Release。生产代码基线为 `157880b7`。同步和异步保存共用的 `Prepare` 在一次采集内复用 UTF-8 缓冲区和写入器。[代码原理与字节示例](../../../MS.Microservice.Persistence/docs/automatic-auditing.md#一次保存中的两条-json)

## SQLite 内存库的采集成本

| 每次保存的审计记录 | 旧版 µs/次 | 新版 µs/次 | 旧版 B/次 | 新版 B/次 |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 50.25 | 53.37 | 11,840 | 11,840 |
| 32 | 417.07 | 372.49 | 319,784 | 170,240 |

32 条记录时，每次少分配 149,544 B（46.8%）。耗时中位数降了 10.7%，各进程的结果有波动。1 条记录时只有一次写入，没有可供下一条复用的缓冲区，分配量持平，本次耗时中位数略高。B/次是调用线程在采集窗口内的托管分配总量，进程常驻内存与 SQLite 原生分配另计。

每个样本通过公开的 `SaveChanges()` 更新已跟踪实体，在两段各 512 个 ASCII 字符的备注间切换。开始时间位于保存调用前，结束时间位于 EF 的 `SavingChanges` 回调中，包含变更检测、JSON 生成与审计实体附加。数据查询、SQL、提交和结果校验位于计时窗口外。每次仍正常落库，基准校验保存的行数，并在结束时核对实际的 300 或 9,600 条审计记录。[基准源码](../../../MS.Microservice.Persistence/benchmarks/MS.Microservice.Persistence.Auditing.Benchmarks/SqliteCapture.cs)

对照使用同一份基准驱动。在基线代码上构建后保留 Release 输出目录，再用修改后的采集实现重新构建。关闭分层编译，按旧、新、新、旧、旧、新启动六个独立进程。每个进程中，两个场景均预热 20 次、测量 7 轮，每轮 40 次，取各轮均值的中位数。上表再取同版本三个进程结果的中位数。按各自运行顺序，旧版单行耗时为 53.29、50.25、50.18，新版为 53.37、56.52、50.97 µs/次。旧版 32 行耗时为 470.00、417.07、406.42，新版为 368.77、372.49、439.27 µs/次。同版本的分配值三次相同。[历史 PostgreSQL 提交耗时](../../../MS.Microservice.Persistence/docs/automatic-auditing-performance.md)

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet build MS.Microservice.Persistence/MS.Microservice.Persistence.slnx -c Release
dotnet run --no-build -c Release --project MS.Microservice.Persistence/benchmarks/MS.Microservice.Persistence.Auditing.Benchmarks -- --sqlite-capture
```

## 行为验证

`dotnet build MS.Microservice.Persistence/MS.Microservice.Persistence.slnx -c Release --no-restore` 通过，0 个警告，0 个错误。`dotnet test MS.Microservice.Persistence/MS.Microservice.Persistence.slnx -c Release` 通过，EF Core 22 项、SqlSugar 20 项，共 42 项，0 跳过。

新增 SQLite 用例将两条含转义文本和数值的记录分别与预期 JSON 字符串逐字比较。单条和批次预算恰好等于有效字节时保存成功。任一上限少 1 B 时，审计表为空，跟踪状态恢复，缩短内容后可以在同一上下文重试。已有用例覆盖敏感字段掩码、未知原值、同步增删改、异步事务回滚、取消、数据库和保存回调异常。[回归测试](../../../MS.Microservice.Persistence/test/MS.Microservice.Persistence.EFCore.Tests/AutomaticAuditingTests.cs)
