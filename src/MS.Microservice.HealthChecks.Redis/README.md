# Redis 就绪探针

Lab 曾有一个名为 `RedisHealthCheck` 的类，但它只读取配置，任何时候都返回 `Healthy`，也没有注册到就绪检查。这样的结果不能证明 Redis 可用。当前 Reference 没有 Redis 依赖，Lab 使用的是内存缓存，因此两者都不会默认注册 Redis 探针。

需要 Redis 的宿主可单独引用本项目，并把业务使用的 `IConnectionMultiplexer` 注册到依赖注入后，显式调用 `AddRedisHealthCheck()`：

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddPlatformHealthChecks()
    .AddRedisHealthCheck();
```

这段接入代码只适用于业务也使用这个共享连接的宿主。检查不会自己创建第二条连接。若应用只注册了 `IDistributedCache`，还需要确认它使用的 Redis 客户端怎样与探针共享连接；不要因为配置里有 Redis 地址就假设探针已经接入。

注册后，探针带 `ready` 标签，默认名称为 `redis`，五秒内用现有连接执行一次 `PING`。成功返回 `Healthy`；缺少客户端、连接构造失败或 `PING` 失败返回 `Unhealthy`。它不参与 `/health/live`。结果描述固定为 `Redis PING failed.`，不会把异常中的连接串写进健康结果。只有实际使用 Redis，且故障会使实例无法服务时，才把它注册为就绪依赖。

`StackExchange.Redis` 的 `PingAsync` 没有取消参数。探针用请求取消令牌停止等待，并把取消向上传递；已发出的 PING 不会因此被撤回。五秒检查期限限制健康请求等待的时间，Redis 客户端自身的命令超时仍应按部署环境配置。

此适配通过显式 DI 解析和 `IConnectionMultiplexer` 接口调用，不扫描类型或动态生成代码。测试覆盖正常 PING、失败 PING、缺少注册、客户端构造失败和调用取消。它们使用模拟连接；真实 Redis、TLS、认证以及故障切换仍需在接入它的应用中验证。这是功能修正，没有性能基准数据。
