# 缓存与敏感字段的性能基准

用于复测治理封装、字段保护和指标监听的增量。生产组件源码位于 Caching、Core.Security.SensitiveData 和 Persistence.EFCore；默认模式不连接外部数据库或 Redis；可选 Redis 模式见下文。两种模式均不需要 Docker。

```powershell
dotnet run --project benchmarks/MS.Microservice.Framework.Benchmarks -c Release -- artifacts/framework-component-performance.json
```

| 场景 | 对照口径 |
| --- | --- |
| 缓存命中／未命中 | 相同原生 HybridCache、键、策略、不可变字符串值与同步源；未命中包含 Remove 的成本 |
| 绕过／键构建 | 单独测量实时加载入口和类型／Scope 摘要，不混入预建键命中 |
| 标量与投影 | 单独测掩码；比较显式手写受保护字典与属性驱动投影，字段和值一致 |
| JSON | 普通序列化预处理 DTO 与标记驱动序列化原 DTO，启动时断言输出字节相同 |
| 日志 | 关闭级别的普通／受保护入口，以及启用保护后提交结构化状态；不格式化或执行 sink I/O |
| 指标监听 | 单独启用空 MeterListener 回调，不代表 OpenTelemetry 导出器或网络开销 |
| 审计采集 | 比较同样三个字段的普通／受保护采集，在 SavingChanges 入口读取耗时并立即中止，不执行业务／审计 SQL |

微基准预热后执行 7 轮，每轮每场景 100,000 次，轮换场景顺序。报告批次平均 ns/次的中位数、线程分配 B/次，每 128 次采样一个带计时调用的耗时，采样 P95 包含时间戳开销。审计采集单独预热 200 次、测量 2,000 次，报告中位数；SQLite 仅用于准备真实关系模型及跟踪快照。

固定单线程、小字符串和简单 DTO 不代表真实大对象或并发请求；采集停止在 SQL 前，更不代表 PostgreSQL 同事务保存已通过验收。结果解释、冷启动成本和未验证范围见 [性能记录](../../docs/components/cache-sensitive-performance.md)。

## Redis 模式

用独立环境变量提供测试连接及键前缀；连接串遵循 StackExchange.Redis 语法，Cluster seed 可为主节点或副本。保留部署的连接发现与重试配置。

~~~powershell
$env:MS_CACHE_REDIS_TEST_CONNECTION = '<Redis 连接配置>'
$env:MS_CACHE_REDIS_TEST_PREFIX = 'framework-cache-check:'
dotnet run --project benchmarks/MS.Microservice.Framework.Benchmarks -c Release -- --redis
~~~

连接和前缀必填，前缀最多 95 字符。可在 `--redis` 后提供报告路径，默认 `artifacts/redis-component-performance.json`。基准只创建一个随机命名空间键，结束精确删除；不扫描或清空 Redis。

分别报告原生／治理 L1 命中、强制 L2 读取和原生 `IDistributedCache` 读取。55 秒操作预算及 5 秒清理预算避免无界执行；报告不含连接参数。测量方法、结果和实库验证入口见 [Redis 性能记录](../../docs/components/redis-cache-performance.md)。
