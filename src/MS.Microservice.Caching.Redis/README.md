# Redis 共享缓存

`MS.Microservice.Caching.Redis` 为缓存治理接入 Redis，使不同应用实例能够读取同一份缓存数据。业务继续使用 `IGovernedCache`，无需改成 Redis 专用读写 API。

| 层级 | 存储 | 用途 |
| --- | --- | --- |
| L1 | 每个应用实例中的 MemoryCache | 减少网络访问，进程重启后丢失 |
| L2 | 通过 `IDistributedCache` 接入的 Redis | 在实例间共享序列化结果 |
| 数据源 | 调用方提供的加载器 | 两级未命中时读取权威数据 |

只有 L1 时也是可用的缓存；注册本包可为治理层补上 Redis L2。`AddDistributedMemoryCache` 的数据仍留在当前进程，不能代替 Redis 的跨进程共享能力。

## 注册

宿主引用本包，在 DI 中注册一次。Redis 连接从环境变量或宿主的秘密配置中提供，不写入源代码。

~~~powershell
$env:ConnectionStrings__CacheRedis = '<Redis 连接配置>'
~~~

~~~csharp
using MS.Microservice.Caching;
using MS.Microservice.Caching.Redis;

builder.Services.AddGovernedRedisCaching(
    configureRedis: redis =>
    {
        redis.Configuration = builder.Configuration.GetConnectionString("CacheRedis");
        redis.InstanceName = "cache:";
    },
    configureCaching: cache =>
    {
        cache.ApplicationName = "Orders";
        cache.ExpirationSeconds = 300;
        cache.LocalExpirationSeconds = 30;
        cache.MaximumConcurrentLoads = 32;
    });
~~~

此入口同时注册 Redis provider、HybridCache 和治理层，不要再调用 `AddStackExchangeRedisCache` 或 `AddGovernedCaching`。启用时若已经注册 `IDistributedCache` 或 `IGovernedCache`，会拒绝重复注册。

| 配置 | 默认／要求 |
| --- | --- |
| `RedisCacheOptions.Configuration` | Redis 连接配置；也可使用带端点的 `ConfigurationOptions`，必须有有效端点 |
| `RedisCacheOptions.InstanceName` | 默认 `cache:`；非空且最多 128 个字符，作为 Redis 键前缀 |
| `RedisCacheOptions.ConnectionMultiplexerFactory` | 本适配不接受外部连接工厂，连接由官方 provider 独占管理 |
| `CachingOptions.Enabled` | 默认 true；false 时不执行 Redis 配置回调，不注册 Redis／HybridCache，读取直接调用源 |
| 两级 TTL、负缓存、回源预算、大小限制 | 沿用 [Caching 的配置与约束](../MS.Microservice.Caching/README.md#注册与配置) |

连接字符串采用 StackExchange.Redis 语法，例如 `localhost:6379,ssl=false`。它不接受部分其他客户端的 `poolsize`、`prefix` 参数：键前缀放到 `InstanceName`，并发命令通过长连接复用处理，无需设置连接池大小。[连接参数说明](https://stackexchange.github.io/StackExchange.Redis/Configuration.html)

Redis Cluster 可以从主节点或副本 seed 开始发现拓扑；客户端还需访问集群公布的其他节点地址。仅 seed 可达不代表能够路由所有键，应确保宿主能连接到各分片公布的端点。Cluster 使用数据库 0，不能按逻辑数据库编号隔离业务。

注册只检查配置，不证明 Redis 已连接。官方 provider 在实际缓存操作时建立并复用长连接；宿主释放容器时释放它。本适配不会注册可供其他组件借用的 `IConnectionMultiplexer`。连接所有权及健康检查的组合见[设计文档](docs/design.md#连接所有权)。

## 读写与失效

注入 `IGovernedCache`，与本地模式使用同一套键和 API：

~~~csharp
var key = cache.CreateKey<ProductView>("products.v1", trustedTenant, "p-1");
var product = await cache.GetOrCreateAsync(key,
    token => ValueTask.FromResult(new ProductView("p-1", "示例商品")),
    cancellationToken);

// 业务事务提交成功之后，使用相同的键失效。
await cache.RemoveAsync(key, cancellationToken);

// 已有完整 DTO，明确需要主动填充时：
await cache.SetAsync(key, product, cancellationToken);

public sealed record ProductView(string Id, string Name);
~~~

加载器使用传入的取消令牌，并自行管理数据库等依赖的生命周期。同实例同键可能合并请求，不应捕获随首个请求释放的 DbContext。缓存独立 DTO，避免共享跟踪实体；完整使用案例见 [Caching README](../MS.Microservice.Caching/README.md#常见使用案例)。

多个实例要共享值，应使用相同的 Redis 部署／数据库、`InstanceName`、应用名、region、类型和可信 Scope，且能兼容同一种序列化格式。region 中保留数据格式版本；修改 DTO 契约后更换版本，避免新旧服务误读。

### 源生成 JSON

需要显式 JSON 元数据时，直接使用第三个参数：

~~~csharp
using System.Text.Json.Serialization;

builder.Services.AddGovernedRedisCaching(
    redis => redis.Configuration = builder.Configuration.GetConnectionString("CacheRedis"),
    cache => cache.ApplicationName = "Orders",
    serialization: serializers =>
        serializers.AddGovernedJson(ProductJsonContext.Default.ProductView));

[JsonSerializable(typeof(ProductView))]
internal partial class ProductJsonContext : JsonSerializerContext;
~~~

为需要显式序列化的类型分别注册元数据。这提供确定的序列化入口，不代表整个宿主已完成 Native AOT／裁剪发布验证。Redis 中的 payload 由 HybridCache 和 provider 管理，不应从业务代码直接用 Redis 字符串命令读写这些键。

## TTL、一致性与故障

默认本地 TTL 为 30 秒，共享 TTL 为 300 秒，要求 `0 < 本地 TTL <= 共享 TTL`。本地过期后先查 Redis；Redis 也未命中时才执行加载器。读取共享值不会把其整体有效期无限延长。

`RemoveAsync` 清除当前实例 L1 和 Redis L2，**不清除其他实例已经持有的 L1**。其他实例可能持续返回旧副本直至本地过期，已经开始的旧加载也可能回填。本包没有失效广播、跨实例加载锁或数据库与缓存的共同事务；实时授权等严格新鲜的查询应绕过缓存。

Redis 不可用时，已有 L1 命中仍可使用。当前 HybridCache 对普通 L2 读错误记录日志后按未命中回源，对回填 L2 的错误记录日志并保留可用的本地结果。取消和业务加载异常仍需由调用方处理；读取成功不等于 Redis 回填成功。

`MaximumConcurrentLoads` 仅限制单实例正在执行的源工厂，耗尽抛出 `CacheLoadLimitException`。它不限制等待 Redis 的请求数，不缩短连接／命令超时，也不提供跨实例限流或熔断。宿主应按自身服务目标配置 Redis 超时、过载响应和故障恢复，失效失败也需要可靠通知或后续核对。

## 监测

继续使用治理层的 `MS.Microservice.Caching` Meter；本包不再创建一套逐键状态或重复指标。`cache.requests`、`cache.source.loads`、`cache.source.rejected` 和 `cache.source.duration` 的口径及 `dotnet-counters`／OpenTelemetry 接入见 [Caching 监测说明](../MS.Microservice.Caching/README.md#监测如何读取怎样解释)。

已有 Observability 注册会订阅此 Meter。它反映治理请求与回源行为，不能提供精确的 L1／L2 命中率、Redis 命令耗时、连接状态或服务端容量；这些信息应结合官方缓存日志和 Redis 侧监测。

## 在 Lab 体验

Lab 默认 `Caching:Backend=Memory`。改用 Redis 时，在环境变量提供 `ConnectionStrings__CacheRedis`，并设置：

~~~json
{
  "Caching": {
    "Enabled": true,
    "Backend": "Redis",
    "ApplicationName": "MS.Microservice.Lab",
    "ExpirationSeconds": 300,
    "LocalExpirationSeconds": 30,
    "Redis": { "InstanceName": "lab:cache:" }
  }
}
~~~

`Backend` 和 `Redis:InstanceName` 是 Lab 的组合配置。独立宿主使用前面的注册入口；配置更改后重启应用。

登录后连续调用 `GET /api/lab/tools/cache`，再通过 `PUT` 更新和 `DELETE` 失效，观察回源次数与指标。Lab 示例源保存在进程内，重启会重置；它用于体验 API，不作为多实例共享业务源或 Redis 容量验收。更多步骤见 [Lab 缓存治理示例](../../samples/Lab/MS.Microservice.Lab/README.md#缓存治理示例)。

已有[缓存微基准](../../docs/components/cache-sensitive-performance.md)衡量治理层的本地开销，不能用于推断 Redis 吞吐、网络延迟或故障恢复能力。真实部署需分别测量 L1 命中、L2 读取和回源；实现责任与性能边界见[设计文档](docs/design.md)。
