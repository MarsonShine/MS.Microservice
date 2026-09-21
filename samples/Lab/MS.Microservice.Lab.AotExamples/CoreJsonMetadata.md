# HTTP 和缓存：复用配置以后，为什么还要类型元数据？

旧实现把任意 `T` 或 `object` 交给 `JsonSerializer` 和 options。P04 已经复用了编码器和配置，减少每次调用的分配；但类型成员仍由默认解析器在运行时发现。第一次遇到 DTO 时需要发现属性并构造契约，trimming 也无法从这个调用确定必须保留哪些成员。这一步主要是 **AOT compatibility**，不要把它说成每次请求都重新反射全部属性。

新实现沿用框架的 Source Generator。生成器提供 `JsonTypeInfo<T>`，缓存和 HTTP 扩展直接使用它；不再自己持有一套可变默认 options。`LogHttpClient` 的请求体原来接收 object，因此使用 `JsonTypeRegistry` 登记有限的请求/响应契约，通过实际类型查表。`GetType()` 在这里仅是字典键，不发现属性、不构造泛型或编译代码。没有登记的根类型在发请求前失败，嵌套 object 的实际类型也必须由生成上下文支持。

```csharp
[JsonSerializable(typeof(OrderDto))]
internal partial class OrderJson : JsonSerializerContext;

await cache.SetAsync("order", order, OrderJson.Default.OrderDto, TimeSpan.FromMinutes(5));
var item = await cache.GetAsync("order", OrderJson.Default.OrderDto);
var contracts = new JsonTypeRegistry(OrderJson.Default.OrderDto);
var client = new LogHttpClient(logger, httpClient, contracts);
var response = await client.PostAsync<OrderDto>("/orders", order);
```

名字、大小写、枚举、null、编码等规则现在是调用方上下文的一部分。需要保持旧 HTTP 行为时，在创建上下文的 options 中设置 `PropertyNameCaseInsensitive = true`；缓存原规则为 false。两者原来都使用 `JavaScriptEncoder.Create(UnicodeRanges.All)`，调用方应只创建一次上下文和编码器。旧 `JsonSerializerOptions` 字段已经移除，不能再在运行中修改全局行为。不要给上下文组合 `DefaultJsonTypeInfoResolver`：那会重新引入隐式反射。

Lab 的 RBAC 缓存登记 `UserCacheItem` 及其生成的嵌套契约。Lab 注册了空的 HTTP 契约表，因为当前没有使用这个客户端的业务调用；新增调用时必须在组合入口明确登记其 DTO。匿名请求对象应改成命名 DTO。工厂缓存仍先读再按既有空值规则调用工厂，绝对/滑动过期、UTF-8 BOM 和取消语义保留。返回模型的 HTTP 扩展负责释放内部 response；返回原始 response 的重载仍由调用方释放。

`Legacy/CoreJson` 保存原入口全文和独立旧示例；`Static/CoreJson` 直接演示框架类型化重载。对照测试使用同一嵌套 DTO 验证结果；`MS.Microservice.Aot.Tests` 关闭默认反射，复用真实 HTTP/缓存回归测试，并验证未知根类型、未知嵌套类型、null 请求、中文转义和缓存工厂。Core 原套件仍启用反射，因为其他 JSON 转换器的历史测试需要它。这里没有执行 NativeAOT 发布，不能据此宣称整个应用兼容。
