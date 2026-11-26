# 缓存键性能与兼容性验收

2026-11-26，以 `469a76a` 为基线，同一驱动对比短 ASCII 输入、上限 Unicode 输入、泛型类型、预建键命中及即时建键命中。逐场景耗时、托管分配与复现命令见[技术说明](../../../MS.Microservice.Caching/src/MS.Microservice.Caching/docs/key-performance.md)。

| Release 验收 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Caching 核心 | 8 | 0 | 0 |
| Caching.Redis，含本机 Redis 实库 | 17 | 0 | 0 |
| Lab 的缓存 API、配置与 OpenTelemetry 指标 | 12 | 0 | 0 |

```powershell
# 将 MS_CACHE_REDIS_TEST_CONNECTION 指向可用于集成测试的 Redis 后执行
dotnet test MS.Microservice.Caching/MS.Microservice.Caching.slnx -c Release
dotnet test test/MS.Microservice.Lab.Tests -c Release --filter "FullyQualifiedName~CacheToolsApiTests|FullyQualifiedName~CacheMetricsIntegrationTests|FullyQualifiedName~CacheRegistrationTests"
```

测试预先用旧物理键写入 HybridCache，再用新版建键读取、删除并确认重新加载。另核对嵌套泛型名称、UTF-16 长度边界、字段中的冒号及零字符、非法代理字符。既有测试继续覆盖空值命中、同键合并、并发回源预算和异常释放。[核心回归](../../../MS.Microservice.Caching/test/MS.Microservice.Caching.Tests/GovernedCacheTests.cs)

Redis 集成使用回环地址上的独立 Redis 7.0.15 进程。两个服务实例共用 L2，验证了跨实例读取、本地副本过期、删除后的重新加载、类型和作用域隔离。测试结束后已核实键数为零并关闭临时实例。[实库测试](../../../MS.Microservice.Caching/test/MS.Microservice.Caching.Redis.Tests/RedisIntegrationTests.cs)
