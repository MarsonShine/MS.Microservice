# 缓存治理

`MS.Microservice.Caching` 在官方 HybridCache 上提供类型／Scope 隔离、过期策略、并发回源预算和聚合指标，适合缓存业务查询的 DTO。默认使用本地内存；宿主注册 `IDistributedCache` 后可接入共享后端。本包不依赖 Lab、EF Core、Redis 或 OpenTelemetry 导出器。

## 提供什么

| 能力 | 使用入口与行为 |
| --- | --- |
| 类型与用户／租户隔离 | `CreateKey<T>`，应用、region、类型、Scope 和资源共同决定键 |
| 缓存读取与并发合并 | `GetOrCreateAsync`，同实例同键复用 HybridCache 的加载合并 |
| 写入与失效 | `SetAsync`、`RemoveAsync`，读取与失效使用相同键定义 |
| 固定 TTL 与实时读取 | `CachePolicy`；`CachePolicy.Bypass` 直接访问源，不回填 |
| 空值与缺失结果 | 空字符串、空集合、0、false、null 都可缓存；需要短期负缓存时选短 TTL |
| 回源保护 | 不同未命中键共用源加载预算，耗尽快速抛 `CacheLoadLimitException` |
| 显式 JSON 元数据 | `AddGovernedJson` 接收 `JsonTypeInfo<T>`，可使用源生成上下文 |
| 监测 | `MS.Microservice.Caching` Meter 输出区域级请求、加载、拒绝与加载时长 |

监测提供聚合数值，**不提供逐键操作历史、驻留列表或精确的 L1／L2 命中率**。旧 CacheManager 的这些数据结构已移除，取舍见[设计说明](docs/design.md#监测为什么改用-meter)。

## 注册与配置

在宿主 DI 中注册一次：

~~~csharp
using MS.Microservice.Caching;

builder.Services.AddGovernedCaching(options =>
{
    options.ApplicationName = "Orders";
    options.ExpirationSeconds = 300;
    options.LocalExpirationSeconds = 30;
    options.MaximumConcurrentLoads = 32;
});
~~~

按配置文件注册时，使用：

~~~csharp
builder.Services.AddGovernedCaching(options =>
    builder.Configuration.GetSection(CachingOptions.SectionName).Bind(options));
~~~

~~~json
{
  "Caching": {
    "Enabled": true,
    "ApplicationName": "Orders",
    "ExpirationSeconds": 300,
    "LocalExpirationSeconds": 30,
    "MaximumConcurrentLoads": 32,
    "MaximumPayloadBytes": 1048576
  }
}
~~~

| 参数 | 默认 | 必要约束 |
| --- | --- | --- |
| `Enabled` | true | false 不注册本组件的 HybridCache，读取直接调用加载器 |
| `ApplicationName` / region | 应用默认 MS.Microservice | 1–64 个 ASCII 字母、数字、点、下划线或连字符 |
| Scope / resourceId | 必填 | 可信宿主提供，非空有效 Unicode，最多 256／512 字符；不要把请求参数直接作为授权 Scope |
| `ExpirationSeconds` / `LocalExpirationSeconds` | 300／30 | 均正数，本地 TTL 不超过共享 TTL，绝对过期 |
| `MaximumConcurrentLoads` | 32 | 1–10,000，按本组件实例限制正在执行的源加载 |
| `MaximumPayloadBytes` | 1 MiB | 正数，交给 HybridCache 约束序列化缓存项，不能代替请求限制或总内存容量管理 |

配置变更后重启。region 使用固定业务名称和数据格式版本，例如 `products.v1`，不要从用户输入生成。

需要 Redis 二级缓存时，引用可选的 [`MS.Microservice.Caching.Redis`](../MS.Microservice.Caching.Redis/README.md)，调用 `AddGovernedRedisCaching` 一次即可同时注册治理层和原生 Redis 后端。L1 是进程内存，L2 才是共享 Redis；仅使用分布式内存实现不等于跨进程共享缓存。

已有 `IDistributedCache` 的宿主继续调用 `AddGovernedCaching` 复用后端，不重复注册 Redis 适配器。无需额外启动框架缓存服务。

## 常见使用案例

### 查询一次，后续读取复用结果

注入 `IGovernedCache`。下面使用虚构数据源，实际应用可替换为查询服务：

~~~csharp
var key = cache.CreateKey<ProductView>("products.v1", trustedTenant, "p-1");
var product = await cache.GetOrCreateAsync(key, "p-1",
    static (id, token) => ValueTask.FromResult(new ProductView(id, "示例商品")),
    cancellationToken);

public sealed record ProductView(string Id, string Name);
~~~

类型参与键隔离；读取、写入和删除应共享同一键构建方法。键不会直接包含 Scope 或资源原文，但摘要不代替授权或加密。热路径可复用预建键，避免反复计算摘要。

加载器应使用传入的 token。同键加载可能跨请求合并，应自行管理资源生命周期，例如在工厂内创建和释放 DbContext，避免捕获随首个请求释放的上下文。优先缓存独立 DTO；共享可变对象或 EF 跟踪实体会引入额外生命周期风险。

### 成功提交后失效，或主动写入

~~~csharp
await repository.UpdateAndCommitAsync(product, cancellationToken);
await cache.RemoveAsync(key, cancellationToken);

// 已有完整 DTO，明确需要主动填充时：
await cache.SetAsync(key, product, cancellationToken);
~~~

失效与数据库提交没有共同事务。提交后失效失败需要业务核对或可靠通知；已有 Outbox 可用于保证通知最终产生。回滚时不应先清除缓存。

### 不存在的结果采用短 TTL

~~~csharp
var shortPolicy = new CachePolicy(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
var optionalKey = cache.CreateKey<ProductView?>("optional-products.v1", trustedTenant, productId, shortPolicy);
var optional = await cache.GetOrCreateAsync(optionalKey,
    async token => await FindProductAsync(productId, token), cancellationToken);
~~~

null 与空集合不会反复触发加载。本版一个策略的正常值与 null 共用 TTL，不按返回值动态调整；同一区域和身份应保持策略一致。源加载异常不缓存。

### 强一致查询绕过缓存

~~~csharp
var liveKey = cache.CreateKey<ProductView>("live-products.v1", trustedTenant, productId, CachePolicy.Bypass);
var current = await cache.GetOrCreateAsync(liveKey,
    async token => await ReadCurrentProductAsync(productId, token), cancellationToken);
~~~

绕过与全局关闭都直接调用源，不应用缓存回源预算；Set 不回填。Remove 仍可清除此前已缓存的内容。Lab 的 RBAC 直接查数据库，授权不依赖缓存。

### 使用源生成 JSON 元数据

~~~csharp
using System.Text.Json.Serialization;

builder.Services.AddGovernedCaching(
    options => options.ApplicationName = "Orders",
    serializers => serializers.AddGovernedJson(ProductJsonContext.Default.ProductView));

[JsonSerializable(typeof(ProductView))]
internal partial class ProductJsonContext : JsonSerializerContext;
~~~

为每种需要显式序列化的缓存类型注册元数据；这不代表整个宿主已经通过 Native AOT／裁剪发布验证。

## 监测：如何读取、怎样解释

`Meter` 是发布指标的 .NET API：`Counter` 表示事件累计，`Histogram` 记录时长分布。收集器负责聚合、导出与保留数据；本组件不会自己维护历史库或管理界面。

Meter 名称为 `GovernedCache.MeterName`，即 `MS.Microservice.Caching`：

| 指标 | 类型／单位 | 统计口径 |
| --- | --- | --- |
| `cache.requests` | Counter / 次 | 已启用且未绕过的 GetOrCreate 调用；验证失败或预先取消不计入，Set／Remove 不计入 |
| `cache.source.loads` | Counter / 次 | 取得回源预算后实际开始的工厂执行，包含后来失败或取消的执行 |
| `cache.source.rejected` | Counter / 次 | 因源加载预算耗尽被拒绝的工厂，不计入 loads |
| `cache.source.duration` | Histogram / ms | 已开始工厂的耗时，包含失败与取消，不包含缓存命中、预算拒绝或完整请求时间 |

所有指标仅使用固定 `region` 标签。`loads / requests` 表示回源比例；并发合并也会减少 loads，不能据此推导真实 L1／L2 命中率或错误率。关闭／绕过路径不产生这些指标。

本机临时观察可使用 [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)，把 12345 换成实际进程 ID：

~~~powershell
dotnet-counters ps
dotnet-counters monitor --process-id 12345 --counters MS.Microservice.Caching
~~~

使用仓库的 `AddMsOpenTelemetry(configuration)` 时已订阅缓存 Meter；设置 `OpenTelemetry:ConsoleExporterEnabled=true` 或 `OtlpExporterEnabled=true` 才会启用相应导出。导出配置见 [Observability](../MS.Microservice.Observability/README.md)。

独立宿主也可自行引用 OpenTelemetry.Extensions.Hosting 和所需导出器：

~~~csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
    metrics.AddMeter(GovernedCache.MeterName).AddOtlpExporter());
~~~

本组件没有监听器时不会保存可回放的历史指标。导出后端负责查询窗口、直方图桶与保留期，不能将监测后台视为缓存键管理器。

## 在 Lab 体验

登录后连续调用 `GET /api/lab/tools/cache`，观察源读取次数不再增加；`PUT` 提交 `{ "value": "已更新" }`，再 GET 观察失效与加载；`DELETE` 可主动清除。与此同时观察上述 Meter 的 requests、loads 和 duration。界面步骤见 [Lab](../../samples/Lab/MS.Microservice.Lab/README.md#缓存治理示例)。

Lab 默认没有共享后端，shared TTL 字段表示策略配置，不代表已经连接 Redis。示例源在内存中，重启重置；源读取计数是示例进程的全局计数。

## 使用边界与迁移

- 同实例同键合并回源，不保证跨实例只查询一次。
- 删除会影响当前实例与共享后端，其他实例的本地副本可持续至本地 TTL；旧的进行中加载也可能回填。强一致读取应绕过缓存。
- 回源预算耗尽后，宿主可映射为过载响应；不要无界重试。后端故障策略沿用 HybridCache，源预算约束并发回源。
- 缓存敏感原值需要独立的存储保护；输出脱敏标签不会自动加密缓存。

| 旧入口 | 迁移方式 |
| --- | --- |
| Core 的分布式缓存 JSON 扩展 | 改用 `IGovernedCache`；旧键和序列化字节不复用，没有旧 API 转发 |
| `CacheOptions` | 改为 `Caching` 配置；旧滑动扫描频率不再充当条目 TTL |
| Lab `CacheManager` / `ICacheStore` / `IKeyStore` | 已删除；缓存存储交给 HybridCache，监测交给 Meter／收集器 |
| `CacheMetadata` / `CacheOperation` / `OperationLogBuffer` | 不再提供逐键时间、计数、操作列表或缓冲定时器；见[设计取舍](docs/design.md#监测为什么改用-meter) |
| Core 弱引用容器 | 保留在 `Core.Memory`，作为内存生命周期工具，不承担业务缓存职责 |

依赖、回源与失效流程、监测替换的完整取舍见[设计文档](docs/design.md)。
本地热路径、指标监听和键构建的实测开销见[性能记录](../../docs/components/cache-sensitive-performance.md)，不等同于共享后端容量或完整接口延迟。
