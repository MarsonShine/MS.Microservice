# EventSourcing 与 Excel 性能验收

2026-11-20，Windows x64、Intel Core i5-9400、.NET SDK 11.0.100、.NET 11 Release。旧程序集保存于修改生产代码之前，对应 `39a704c` 的 Excel 和 Lab EventSourcing 源码。新旧 DLL 用同一个基准程序和依赖运行。每个场景先预热，再分七批测量，每批除以调用次数。使用进程级 `GC.GetTotalAllocatedBytes(true)` 计托管分配。旧、新、新、旧、旧、新六个独立进程依次运行，以下数字是同一版本三个进程中位数的中位数。关闭分层编译以减少短任务中的 JIT 层级切换。

## 事件溯源

| 场景 | 耗时 µs/次（旧 → 新） | 托管分配 B/次（旧 → 新） |
| --- | ---: | ---: |
| 单流从版本 257 读 512 条，元数据各异 | 2,911.64 → 2,986.47 | 695,888 → 695,760 |
| 全历史 1024 条，元数据各异 | 5,606.27 → 5,762.5 | 1,383,096 → 1,382,968 |
| 分页 1 条 | 176.37 → 175.05 | 16,712 → 16,808 |
| 分页 100 条 | 703.81 → 721.4 | 149,504 → 149,600 |
| 全历史，每 32 条共用元数据 | 5,672.25 → 3,992.05 | 1,448,504 → 1,202,360 |
| 全历史，全部默认元数据 | 5,347.77 → 3,971.93 | 1,317,432 → 1,169,992 |

在 SQLite 内存数据库上调用实际的 `PostgresEventStore`。建库、插入数据、修改元数据分布均在计量窗口外，每批最后一次结果逐条核对全部信封及顺序。连续 32 条共用元数据时，1024 条记录只需解析 32 份元数据。若每条元数据都不同，仍要逐条解析，全量读的耗时中位数约高 2.8%；小页分配也没有稳定降低。自定义序列化选项继续逐条调用转换器。[基准输入](../../../benchmarks/MS.Microservice.EventSourcing.Benchmarks/Program.cs) · [源码与配图](../../../src/MS.Microservice.EventSourcing/docs/read-performance.md)

曾验证两个省掉原始实体列表的候选。顶层 EF 投影在单条分页场景增加了约 8.8 KiB 分配。逐条异步读取并解析 JSON 在首次前后对照中，全历史 1024 条耗时约从 5.91 ms 升至 7.45 ms。最终保留 `ToListAsync` 与读完再映射的顺序。表中数据只说明这一组输入下客户端及 SQLite 的组合成本。未测 PostgreSQL 服务器、网络传输及并发连接占用。

## Excel

| 场景 | 耗时 µs/次（旧 → 新） | 托管分配 B/次（旧 → 新） |
| --- | ---: | ---: |
| 原始 CSV，单个普通字段 | 1.19 → 1.133 | 20,888 → 20,864 |
| 原始 CSV，256 行混合字段 | 178.981 → 54.984 | 33,176 → 20,864 |
| 原始 CSV，16384 字符密集引号 | 212.732 → 69.362 | 70,064 → 20,864 |
| 原始 CSV，16384 字符普通文本 | 30.735 → 3.641 | 20,888 → 20,864 |
| 类型化 CSV，256 行 | 97.133 → 44.955 | 70,216 → 27,144 |
| 模板插入 1 行 × 8 列 | 808.438 → 744.806 | 503,551 → 503,296 |
| 模板插入 128 行 × 8 列 | 1,314.675 → 1,105.875 | 1,273,574 → 1,200,654 |
| XLSX 密集导入 128 行 | 1,280.55 → 1,256.1 | 586,528 → 586,528 |
| XLSX 导入下标 1、40000、80000 | 3,161.15 → 807.3 | 1,009,734 → 369,168 |
| 创建颜色表（预热后） | 12.856 → 0.308 | 5,048.1 → 1,808 |
| CSV 注解校验，256 行有效 | 809.317 → 794.633 | 460,856 → 405,592 |
| CSV 注解校验，256 行无效 | 1,272.25 → 1,105.475 | 585,784 → 458,896 |

CSV 写出比较预建数据和预留容量的输出流，每批结束后逐字节核对输出。模板场景计入创建 NPOI 工作簿、标题和插入行，稀疏与密集导入使用预先生成的 XLSX。稀疏例子直接返回真实行号对应的值。颜色表在环境预热后反复创建；注解场景检查有效/无效行数及最后一条的位置。输入文件和预期结果均在计时外建立。完整工作簿仍由 NPOI 加载，本轮测量只计累计分配，未测进程峰值和文件系统吞吐。[CSV 输入](../../../benchmarks/MS.Microservice.Excel.Benchmarks/CsvCases.cs) · [工作簿输入](../../../benchmarks/MS.Microservice.Excel.Benchmarks/WorkbookCases.cs) · [调色板、注解与计量入口](../../../benchmarks/MS.Microservice.Excel.Benchmarks/Program.cs)

## 复测与行为验证

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/MS.Microservice.EventSourcing.Benchmarks -c Release
dotnet run --project benchmarks/MS.Microservice.Excel.Benchmarks -c Release
dotnet test test/MS.Microservice.Lab.Tests -c Release --filter "FullyQualifiedName~EventSourcing"
dotnet test test/MS.Microservice.Excel.Tests -c Release
```

复现旧版时，将同一份基准构建输出中的 `MS.Microservice.Lab.Infrastructure.dll` 或 `MS.Microservice.Excel.dll` 分别替换为旧 Release 构建，其他输入一致。EventSourcing 契约项目本身没有数据库执行循环，未改其公开接口。

- Lab 事件溯源定向测试通过 32 项。包括分页的 SQL LIMIT、严格大于游标、取消、无跟踪、投影事务、自定义转换器的执行次数及相邻/不相邻元数据。
- Excel 模块通过 156 项，失败 0 项。另有 19 项要求 `MS_MICROSERVICE_RUN_PERF_TESTS=1` 的历史性能用例按默认配置跳过。本轮运行上面的专用性能基准。
- 固定种子的 1500 组 CSV 新旧程序集差分中，1125 组有效输入的完整字节一致，375 组超出预算输入的异常类型和逻辑行列信息一致。覆盖四种分隔符、三种换行、UTF-8 有/无 BOM、UTF-16BE、内嵌引号及块边界。
- 已检查事件回放 Mermaid、CSV 及稀疏行 SVG 的渲染，数据单位、标签和箭头与源码算例对应。
