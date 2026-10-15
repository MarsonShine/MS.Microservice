# 缓存治理

在 HybridCache 上统一应用／Scope／类型隔离、固定过期策略、并发回源预算和指标。引用本组件不依赖 Lab、EF Core 或 Redis；宿主可注册自己的 `IDistributedCache` 作为共享后端，未配置时使用本地缓存。

```csharp
services.AddGovernedCaching(options =>
{
    options.ApplicationName = "Orders";
    options.ExpirationSeconds = 300;
    options.LocalExpirationSeconds = 30;
});

var key = cache.CreateKey<OrderView>("orders.v1", trustedScope, orderId);
var order = await cache.GetOrCreateAsync(key, orderId,
    static (id, token) => LoadOrderAsync(id, token), cancellationToken);
await cache.RemoveAsync(key, cancellationToken); // 数据库提交成功后
```

`cache` 为注入的 `IGovernedCache`。使用相同键定义进行读取、写入和失效。缓存 DTO，应避免共享可变业务对象或 EF 跟踪实体；加载器跨请求合并时不得依赖可能提前释放的请求 DbContext。

| 设置／参数 | 默认／要求 |
| --- | --- |
| `Enabled` | true；false 不注册 HybridCache，读取直接调用加载器 |
| `ApplicationName` / region | 1–64 个 ASCII 字母、数字、点、下划线或连字符；region 采用固定名称及数据格式版本 |
| Scope / resourceId | 可信宿主提供，非空、有效 Unicode，分别最多 256／512 字符；键中使用身份摘要，不直接输出原值 |
| `ExpirationSeconds` / `LocalExpirationSeconds` | 300／30，均正数，本地 TTL 不超过共享 TTL；绝对过期，不滑动续期 |
| `MaximumConcurrentLoads` | 32，范围 1–10,000；不同未命中键共用回源预算，耗尽抛 `CacheLoadLimitException` |
| `MaximumPayloadBytes` | 1 MiB，正数；交给 HybridCache 限制序列化项，不能代替宿主的总内存容量管理 |
| 每键策略 | `new CachePolicy(expiration, localExpiration)`；`CachePolicy.Bypass` 实时读取，不回填 |

空字符串、空集合、0、false 和 null 都是可缓存结果。可能返回不存在结果的查询应选较短的策略 TTL；本版正常值和 null 共用 TTL，不按返回值动态调整。加载器异常不缓存，取消令牌由 HybridCache 合并管理。同实例同键可以合并回源，不保证跨实例只查询一次。

配置来自 `Caching` 节，由宿主绑定到注册回调。只注册一次；配置变更后重启。需要显式／生成的 JSON 元数据时，在注册的第二个回调使用 `builder.AddGovernedJson(MyJsonContext.Default.OrderView)`。

失效会影响当前实例和共享后端，其他实例的本地副本仍可能持续至本地 TTL 到期，正在进行的旧加载也可能回填旧值。因此这是一致性窗口明确的缓存，不是强一致存储；授权等强一致查询应实时访问权威数据源。事务回滚不能清理缓存，成功提交后的失效失败由宿主处理，需要可靠通知时可组合现有 Outbox。

Meter `MS.Microservice.Caching` 提供请求数、源加载数、回源预算拒绝数和源加载耗时，仅使用固定 region 标签；不要从用户输入生成 region。后端诊断沿用 HybridCache。源加载数／请求数反映回源比例，不等同于各缓存层的命中率。

旧 `Core.Ceching` 业务缓存 API、Lab `CacheManager` 和 `CacheOptions` 配置将随存量迁移移除。实现取舍见 [设计](docs/design.md)。
