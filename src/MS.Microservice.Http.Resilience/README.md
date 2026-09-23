# 出站 HTTP 韧性

Core 的重试工具不负责 `IHttpClientFactory` 管线，AI 模块的超时、重试和熔断只适用于 AI 请求。普通服务调用另一个 HTTP 服务时，宿主需要为每个客户端重复决定超时、并发、重试和熔断规则。

此包只提供一个可选的 `IHttpClientBuilder` 扩展，使用 `Microsoft.Extensions.Http.Resilience` 的标准管线。它把总超时、单次尝试超时、并发限制、重试和熔断接在同一个命名或类型化客户端上。调用方可以配置标准选项；最后固定关闭 POST、PUT、PATCH、DELETE、CONNECT 等可能产生副作用的方法的自动重试。需要为某个写请求重试时，应先建立业务幂等契约，再由宿主直接配置专用客户端。此扩展不扫描程序集，不使用反射或动态代码生成。

```csharp
services.AddHttpClient("catalog")
    .AddMsHttpResilience(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    });
```

## 边界与验证

该注册只影响选定客户端，不改变全局 `HttpClient` 行为，也不替调用方判断一个业务操作是否允许重试。标准策略会重试部分瞬时 HTTP 错误；调用方仍须设置上游的容量和超时预算。重试会增加上游请求量，熔断也可能暂时拒绝新请求，应在目标依赖的故障场景中观察这些行为。

测试使用可控的 `HttpMessageHandler` 检查 GET 的瞬时失败重试、POST 不重试、调用方取消以及超时/熔断边界。这里是功能验证，没有测量真实网络的耗时或吞吐，也不据此声明性能收益。后续若出现服务发现或按目标服务区分的策略，可在宿主注册处组合 .NET 现有组件；当前不增加服务注册中心。
