# MVC Action 如何接入 HTTP 幂等

旧示例要求每种业务操作写一个 Action Filter。创建档案的 Filter 从已绑定的 `CreateProfile` 参数取值，转而调用 `ProfileIdempotencyHandler`，带键请求根本不执行 MVC Action。假如后来只修改了 Action 的业务判断或返回内容，带键请求仍会走旧处理器，两条路径便可能得到不同结果。逐个编写 Filter 是这套接入方式造成的，不是请求哈希或 MVC 的要求。

现在 Reference 使用同一个 `ReferenceHttpIdempotencyExecutor` 处理请求键、指纹、存储、事务和响应快照。Minimal API 和 MVC 各负责把所标记端点的执行过程交给它。`MS.Microservice.Idempotency.Mvc` 只提供选择 Filter 的 Attribute；共用 Filter 和执行器目前仍在 Reference.Web，并非引用 MVC 包后任意 Controller 就自动具备幂等能力。

## 标记 Action

Reference 的正式宿主只有 Minimal API。下面的 MVC Controller 位于 `test/MS.Microservice.Reference.Web.Tests/ProfileMvcTestController.cs`，只在非 AOT 的 TestServer 中映射：

```csharp
builder.Services.AddControllers().AddApplicationPart(typeof(ProfileMvcTestController).Assembly);
builder.Services.AddScoped<ReferenceHttpIdempotencyResourceFilter>();
var app = builder.Build();
ReferenceHost.MapApplication(app);
app.MapControllers();
```

```csharp
[HttpPost]
[RequireHttpIdempotency<ReferenceHttpIdempotencyResourceFilter>("profiles.create")]
public Task<IActionResult> Create([FromBody] CreateProfile request) => CreateCoreAsync(request);
```

`profiles.create` 是稳定的操作名。同一个 Filter 也用于测试 Controller 的 `Alternate` 和 `Echo` Action，它们各有自己的操作名；未标记的 `Plain` Action 不经过该 Filter。Attribute 通过 DI 取得指定 Filter，要求它实现 `IAsyncResourceFilter`。使用方仍须为自己的宿主注册持久化存储、工作单元和 Filter；全局 `Http:Idempotency:Enabled` 只控制基础设施是否启用。

## 一次带键请求怎样执行

全局开关关闭，或请求没有 `Idempotency-Key` 时，Filter 直接继续 MVC 流程。全局开启且 Action 已标记、请求也带键时，它在模型绑定之前调用共用执行器。执行器读取并回卷 Body，因此 MVC 仍能正常绑定 `[FromBody]` 参数。它把操作名与已认证身份、请求键用于定位记录；请求指纹包含 Method、Path、QueryString、完整的 Content-Type 和 Body 的表示。UTF-8 JSON 直接解析；标为 `charset=utf-16` 的 JSON 先按 UTF-16 解码。随后递归按字段名排序每层对象；字符串之外的 JSON 空白不参与比较，数组元素仍保持原顺序。非 JSON Body 按原始字节比较。各部分分别带长度写入哈希，避免字段拼接歧义。一个 JSON 对象中若字段名重复，**包括只差大小写**的 `displayName` 与 `DisplayName`，在模型绑定前返回 `400`，不占用键。

例如，第一次发送 `POST /test/mvc/profiles`、键 `K1` 和下面的正文，创建成功返回 `201`：

```json
{"issuer":"https://issuer.example","subject":"mvc-subject","displayName":"first","roles":["reader","editor"]}
```

同一身份以 `K1` 重试完全相同的请求，Filter 不再执行 Action，而是返回首次保存的状态码、`Location` 和正文字节。只把正文调换为 `{"roles":["reader","editor"],"displayName":"first","subject":"mvc-subject","issuer":"https://issuer.example"}`，或改变对象字段间的空白，仍视为同一请求，重放首次的 `201`。如果把 `roles` 数组调换为 `["editor","reader"]`，同一个键会返回 `409`。路径、查询字符串或完整的 `Content-Type` 字符串变化（包括 `charset` 参数）也会改变指纹；不同操作名可以各自使用 `K1`。无关的 `X-Request-Id` 请求头不参与比较。

首次请求查不到记录时，共用执行器在消息 `IUnitOfWork` 的外层事务内先占用键，再调用 MVC 的余下管线。`IAsyncResourceFilter` 包围模型绑定、Action、结果序列化和结果 Filter，所以它能在响应发送前取得实际状态码和字节。创建档案的服务在内层调用同一个工作单元，档案、Outbox 和成功响应快照一起提交。事务提交后，才把缓冲的响应写给客户端。相比旧 Filter，带键请求现在确实执行 Action；无键和带键路径不再分别维护业务调用。

业务校验或冲突返回 `4xx` 时，外层事务回滚占键，再把这次错误送给客户端；修正请求后可复用该键。异常、取消和 `5xx` 也不保存记录。**Reference 执行器只保存 `2xx` 响应**；`Content-Type` 可以为空。测试中的 `ReturnNoContent` Action 首次返回无正文、无 `Content-Type` 的 `204`，同键重试仍返回 `204`。底层 `IdempotencyResponse` 可表示 `200–499`，但本接入层没有启用错误响应的重放。

## 接入边界与验证

带键请求的 Body 最多 1 MiB；超过时返回 `413`，Action 不执行。响应暂存流也限制在 64 KiB：写出超过上限时立即失败，整个事务回滚，不会继续缓存更大的正文。快照只保存状态码、可为空的 `Content-Type`、`Location` 和正文；依赖其他响应头或流式输出的 Action 不应直接标记。业务写入和 Outbox 必须参加执行器开启的同一个数据库事务；事务外的邮件或网络调用不在此保证内。

旧 `profiles.create` 记录的指纹来自绑定后的 `CreateProfile` JSON，现有记录不会改写。新指纹查询遇到冲突时，Reference 会仅对这个操作名按旧规则绑定并序列化 `CreateProfile`，再查询一次：若旧指纹匹配，直接重放旧响应；若仍不匹配，返回 `409`。这是让旧记录在 24 小时保留期内继续处理重试的过渡逻辑，记录实际清理前仍可能重放。其他操作名不尝试旧格式匹配，新的接口不能把这段兼容逻辑当作长期 API 契约。

SQLite TestServer 测试覆盖多个 Action 共用 Filter、UTF-16 JSON 字段重排、`200` 和 `204` 重放、旧创建档案记录重放、不同操作名、查询变化、业务拒绝后复用键，以及异常和大小限制的回滚。这些测试不能证明其他字符集、真实 PostgreSQL 并发或 Wolverine 原生 Outbox 的行为；本次也没有延迟和分配的前后测量，不能宣称性能收益。MVC 示例未进行 Native AOT 发布验证。需要了解 Reference 的配置、Minimal API 接入及迁移行为，可读[创建档案时如何处理重复 HTTP 请求](../../../samples/Reference/MS.Microservice.Reference.Web/idempotent-profile-create.md)。
