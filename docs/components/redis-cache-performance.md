# Redis 缓存的性能与集成验证

2026-10-15，Windows 10.0.19045、.NET 10.0.13、6 个逻辑处理器，Release。服务端为测试环境 Redis 4.0.1 Cluster，3 主 3 副本；客户端使用用户原有的副本 seed 自动发现拓扑，没有改服务器配置。库版本为 HybridCache 10.5.0、RedisCache 10.0.6、StackExchange.Redis 3.3.1。

连接参数通过环境变量提供，仓库不记录真实地址或凭据。原始汇总见 [JSON](performance/redis-cache-2026-10-15.json)，复现命令见 [基准 README](../../benchmarks/MS.Microservice.Framework.Benchmarks/README.md#redis-模式)。

## 测量结果

| L1 场景 | 批次中位数 ns/次 | 线程分配 B/次（四舍五入） |
| --- | ---: | ---: |
| 原生 HybridCache 命中 | 84.0 | 0.0 |
| IGovernedCache 命中，预建键 | 104.3 | 0.0 |

两者使用同一 HybridCache 实例、相同键与策略、1 KiB 不可变字符串。治理入口增加约 20.3ns（24.1%）；未观察到逐调用额外分配，不能把这个口径扩大成整个请求零分配。实际键构建、DTO、监听和业务源成本仍需分别计算。

| Redis 读取场景 | 平均 ms/次 | P95 ms | 样本数 |
| --- | ---: | ---: | ---: |
| 原生 HybridCache 强制 L2 | 0.921 | 3.620 | 3,000 |
| 原生 IDistributedCache 读取同一 payload | 0.871 | 3.426 | 3,000 |

L2 是单线程顺序读取，禁用本地读写且源工厂一旦运行就抛错，确保没有把 L1 命中或回源算成 L2 成功。原生分布式读取返回含 HybridCache 格式信息的 1,142 字节 payload，不解码成业务值；它与 HybridCache 的缓冲接口／命令路径不同，二者差值不能归因于治理层。

## 方法与边界

先建立连接并主动写入，验证 Redis 已存入数据，再开始预热和采样。首次连接、集群发现及连接重试不计入命中数据。L1 预热后执行 5 轮、每轮每场景 500,000 次，轮换顺序，报告批次均值的中位数和当前线程分配。L2 两条路径轮换执行 3 轮、每轮 1,000 次，记录每次等待完成的时间；包含计时和取消预算检查。

这是一次当前测试网络下的热读取测量，不能用平均延迟倒算最大 TPS。没有覆盖并发容量、大 DTO、故障切换、认证／TLS 或完整 HTTP 请求。没有通过停服、改配置或清空数据制造故障。原生 L2 故障语义的依据见 [设计文档](../../src/MS.Microservice.Caching.Redis/docs/design.md#异常与降级)。

基准使用随机键前缀，只精确删除创建的一个键，清理已成功；55 秒操作预算之外最多 5 秒清理。报告在测量和清理都成功后才落盘。

## 集成验证

真实 Redis 测试使用两个独立 DI 容器及两个由 provider 管理的连接，验证 JSON 往返、类型／Scope 隔离、L2 共享、删除后二级不存在但其他 L1 暂存、本地 TTL 过期后重新回源，以及释放一个容器不影响另一个容器。该测试通过，耗时约 7 秒；仅操作并删除三个确定的自建键。

注册测试 16 项、原有核心缓存测试 5 项、Lab 注册测试 10 项通过；另通过可复用项目依赖边界检查、独立源码导出／NuGet 消费及构建布局验证。它们分别确认配置契约、既有行为、宿主组合和可独立引用，不代替生产容量验收。

~~~powershell
$env:MS_CACHE_REDIS_TEST_CONNECTION = '<Redis 连接配置>'
$env:MS_CACHE_REDIS_TEST_PREFIX = 'framework-cache-check:'
dotnet test test/MS.Microservice.Caching.Redis.Tests -c Release --filter RedisIntegrationTests
~~~

未设置连接时，实库测试明确跳过。测试保留原生连接配置与集群发现重试，总预算 40 秒、清理预算 8 秒。不要为缩短用例禁用连接重试，否则副本 seed 的初始拓扑发现可能尚未完成，测试就无法路由写入。失败后的清理不覆盖原始异常；不确定的写入仍只尝试清理确定的键，条目另设 30 秒 TTL。
