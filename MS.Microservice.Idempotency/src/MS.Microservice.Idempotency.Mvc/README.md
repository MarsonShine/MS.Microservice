# MVC Action 如何接入 HTTP 幂等

MVC 的 `[RequireHttpIdempotency<ProfileCreateMvcIdempotencyFilter>]` 标记只决定哪个 Action 使用哪个 DI Filter。`MS.Microservice.Idempotency.Mvc` 不读取请求体、不管理事务，也不保存响应。单靠标记无法让一个写入接口幂等：幂等记录必须和业务数据、Outbox、可重放响应在同一个数据库事务内提交。

## 当前示例在哪里

Reference.Web 的正式宿主只映射 Minimal API。MVC 创建档案 Action 位于 `test/MS.Microservice.Reference.Web.Tests/ProfileMvcTestController.cs`，只在该测试的 TestServer 中注册和映射；Lab 的现有 Controller 没有标记。测试宿主使用以下方式接入：

```csharp
builder.Services.AddControllers().AddApplicationPart(typeof(ProfileMvcTestController).Assembly);
builder.Services.AddScoped<ProfileCreateMvcIdempotencyFilter>();
// 构建宿主并映射 Reference 的 Minimal API 后：
app.MapControllers();
```

Action 只需声明它选用的 Filter：

```csharp
[HttpPost]
[RequireHttpIdempotency<ProfileCreateMvcIdempotencyFilter>]
public Task<IActionResult> Create([FromBody] CreateProfile request) => CreateCoreAsync(request);
```

Attribute 实现 `IFilterFactory`，从当前请求的 DI 作用域取得 `ProfileCreateMvcIdempotencyFilter`。它只装在这个 Action 上；同一测试 Controller 中未标记的 `Plain` Action 不调用该 Filter。使用方须注册所选 Filter，且该 Filter 必须实现 `IAsyncActionFilter`。全局 `Http:Idempotency:Enabled` 仍由宿主控制幂等存储和清理任务的注册，Attribute 不会替宿主注册这些服务。

## 请求实际怎样执行

`ProfileCreateMvcIdempotencyFilter` 先检查全局开关和 `Idempotency-Key`。全局关闭或没有请求头时，它调用 `next()`，MVC Action 按原流程执行。全局开启且带键时，Filter 从已绑定的 `CreateProfile request` 参数取值，调用 Reference 原有的 `ProfileIdempotencyHandler.CreateAsync`，并将其 `IResult` 交给 MVC 写出；**这条带键路径不会执行 Action 方法体**。

该处理器先用已验证的用户身份、操作名、请求键和源码生成的 JSON 请求表示查记录。未命中时，它通过消息 `IUnitOfWork` 开启外层事务，在事务内占用键、调用 `ProfileService.CreateAsync`、保存响应快照，并把档案和 Outbox 一起提交。服务内部的工作单元加入外层事务。首次响应和后续重放使用同一份快照，因此断线发生在提交后、响应送达前时，重试仍能得到原来的 `201`、`Location` 和正文。相同键对应不同请求会返回 `409`；业务拒绝、异常或取消不会留下占位记录。

测试 Action 在无键时也用 `ApplicationErrorResults` 映射业务错误，并通过 `HttpResultActionResult` 执行生成的 `IResult`；带键路径直接执行同一个错误映射。这样同一个业务冲突不会只因是否有键而变成两种公开响应。测试对比了两条路径的状态、内容类型、`code` 和 `title`。

这里没有在 Action 结束后读取 MVC 的 `IActionResult` 再写幂等表。那时业务服务可能已经提交，随后保存响应失败就会留下“业务成功、却没有可重放响应”的窗口。Filter 直接复用现有处理器，是为了让首次带键请求沿用已经建立的事务边界。它也带来一个维护约束：如果以后修改 MVC Action 的业务规则或返回格式，须同时核对带键路径的 `ProfileIdempotencyHandler`，避免有键与无键请求出现不同业务结果。

## 范围和验证

这个 Filter 只接受名为 `request` 的 `CreateProfile` 参数，并只用于创建档案。给另一个 Action 加同一个 Attribute 类型参数，不会自动获得正确的操作名、请求指纹或响应规则；新操作需要自己的专用 Filter 和同事务处理路径。Lab 的现有 Action 使用不同的持久化与事务方式，不能直接套用这个 Filter。事务外的邮件、网络调用或其他独立提交也不在保证范围内。

`ReferenceHostTests` 中的 MVC TestServer 用例检查了全局关闭、未标记 Action、无键请求、无效键、首次响应与重放字节一致、业务拒绝后复用键，以及服务器异常时档案、Outbox 和幂等记录一起回滚。它们使用 SQLite；真实 PostgreSQL 并发、Wolverine 原生 Outbox 和进程崩溃恢复仍需相应环境验证。当前没有 MVC 接入前后的 Benchmark 数据，不能据此声称延迟或分配降低。

泛型 Attribute 本身没有引入业务代码的运行时反射；它通过 DI 按类型取出已注册的 Filter。但 **使用 Attribute 不等于 MVC 获得 Native AOT 支持**。[ASP.NET Core 的 .NET 10 兼容性表](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0)仍将 MVC 列为不支持。Reference 正式宿主没有启用 MVC，测试示例运行在非 AOT 的 TestServer。需要 Native AOT 的 HTTP 宿主应继续使用 Minimal API 接入，并分别验证其依赖与发布链路。
