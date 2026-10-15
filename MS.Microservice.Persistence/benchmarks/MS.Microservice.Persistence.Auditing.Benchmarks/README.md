# PostgreSQL 自动审计性能比较

比较普通 EF DbContext、关闭审计、开启审计三种模式。业务模型、索引、读取、显式事务与保存流程一致；开启时校验实际审计条数，并在测量前验证多表提交／回滚的原子性。

## 运行

准备专用 PostgreSQL 测试库，通过运行环境提供 `AUDIT_BENCHMARK_CONNECTION`。连接串不写入源码、日志或结果文件。不需要 Docker。

```powershell
dotnet run --project MS.Microservice.Persistence/benchmarks/MS.Microservice.Persistence.Auditing.Benchmarks -c Release -- --probe
dotnet run --project MS.Microservice.Persistence/benchmarks/MS.Microservice.Persistence.Auditing.Benchmarks -c Release -- --output artifacts/audit-postgresql.json
```

| 参数 | 行为 |
| --- | --- |
| `--probe` | 只连接并读取版本与持久化设置，不建 schema |
| `--initialize` | 显式连接维护库 `postgres`，仅在指定数据库不存在时创建；需建库权限，不删除数据库 |
| `--output path` | JSON 结果文件，默认当前目录 `audit-benchmark.json`；父目录须已存在 |
| `--scenarios Single,MultiTable` | 限定场景；默认下表全部场景 |
| `--concurrency 4` | 限定工作线程数，可用逗号分隔；支持 1–4，默认 1 和 4 |

运行账户须允许在测试库建／删 schema。每次测量创建 `ms_audit_bench_<随机GUID>`，所有表均映射到这个 schema，退出时删除本次 schema。不会删除数据库或既有表；异常强制终止可能留下 schema，按控制台打印的完整名称核对后再清理。默认运行不会自动创建数据库。

| 场景 | 每次操作 | 开启时审计条数 |
| --- | --- | --- |
| `Unchanged` | 查询一个跟踪实体，无修改保存 | 0 |
| `Unselected` | 修改一个未选入审计的字段 | 0 |
| `Single` | 修改一个实体的三个标量字段 | 1 |
| `MultiTable` | 修改订单、两条库存，新增审批，共三表四实体 | 4 |
| `Tracked1000` | 查询并跟踪 1,000 个实体，只修改其中一个 | 1 |

每个工作线程有自己的业务数据，避免把同一行争用混入审计成本。每种模式预热每线程 24 次，随后测量 3 轮，每轮每线程 96 次，轮换三种模式的先后顺序。输出 P50/P95/P99、吞吐、近似分配量、EF 命令次数和采集／保存／提交阶段的平均耗时。SQL 批次内多个语句仍算一次 EF 命令；事务协议、保存点和 WAL 字节不计入该次数。

这是包含上下文／操作创建、跟踪查询、事务、保存、提交和释放的数据库工作流耗时，不是 HTTP TPS。分配量使用进程级差值，存在运行时后台分配；阶段计时不能当成纯 CPU 时间。普通 EF 在 SavingChanges 回调之后检测变化，而审计基类在回调之前检测和采集，因此 `MeanBeforeCallbackMs` 只用于观察开启模式的采集上界，不直接比较两种检测实现。

测量不修改服务器的 fsync、同步提交、WAL 或复制配置。共享测试库的磁盘、网络和其他负载会影响尾延迟；单次结果不能证明所有业务场景满足固定百分比。测试和结果解释见 [性能记录](../../docs/automatic-auditing-performance.md)，采集实现见 [技术设计](../../docs/automatic-auditing.md)。
