# Reference 为什么调用 `AddValidation()`

访问 `GET /api/v1/profiles?take=201` 时，`take` 能解析成整数，但超过了列表接口允许的 200 条上限。Reference 以前在端点方法中手写范围判断，超出范围时同样返回 `400`。后来把约束移到端点参数上的 `[Range(1, 200)]`。**仅写属性并不会让 Minimal API 自动执行它**；宿主还要调用 [`builder.Services.AddValidation()`](../../../samples/Reference/MS.Microservice.Reference.Web/ReferenceHost.cs)，才能在端点方法运行前拒绝越界值。

这一修改没有新增分页规则。它把原来分散在档案列表、审计列表和失败消息列表中的判断放到各自的参数声明处，同时保留省略参数时的默认值。实际定义见 [`ProfileEndpoints.Map`](../../../samples/Reference/MS.Microservice.Reference.Web/ProfileEndpoints.cs)：`skip` 为 0 到 `int.MaxValue`，两个列表的 `take` 为 1 到 200，失败消息的 `limit` 为 1 到 1000。

档案列表的关键签名是：

```csharp
profiles.MapGet("", async (IProfileRepository repository, CancellationToken token,
    [Range(0, int.MaxValue)] int skip = 0, [Range(1, 200)] int take = 50) =>
    Results.Ok((await repository.ListAsync(skip, take, token)).Select(ProfileView.From)));
```

## 四种输入为什么得到不同结果

以下请求假设调用方已经通过相应端点的授权。表中的结果由 [`ReferenceHostTests`](../../../test/MS.Microservice.Reference.Web.Tests/ReferenceHostTests.cs) 的 TestServer 测试覆盖；表只列出测试已断言的状态和关键调用，不假定所有 `400` 具有相同的响应体。

| 输入 | 结果 | 拒绝或处理的位置 |
| --- | --- | --- |
| `GET /api/v1/profiles` | `200`；仓储收到 `skip=0, take=50` | 查询参数缺省，C# 参数默认值生效；`0` 和 `50` 都在范围内。 |
| `GET /api/v1/profiles?take=201` | `400`；仓储不被调用 | `201` 已绑定为 `int`，随后 `[Range(1, 200)]` 校验失败。 |
| `GET /api/v1/profiles?take=invalid` | `400`；仓储不被调用 | `invalid` 无法绑定为 `int`，请求在执行范围校验前就失败。这个 `400` 不依赖 `AddValidation()`。 |
| `POST /api/v1/profiles`，JSON 中的 `issuer` 为 `""` | `400`，响应的公开错误码为 `validation` | 请求体可以绑定为 `CreateProfile`，但 `ProfileService` 调用领域模型时发现身份不合法，再由 `ApplicationErrorResults` 转成 HTTP 响应。 |

最后一行使用的请求体与测试一致：

```json
{"issuer":"","subject":"subject","displayName":"name","roles":[]}
```

因此，看到 `400` 时不能只看状态码就断定是 `[Range]` 起了作用。`take=invalid` 是**绑定失败**，`take=201` 是**绑定后的属性校验失败**，空 `issuer` 是**端点执行后的业务校验失败**。`AddValidation()` 解决的是中间那一步。

## 从请求到仓储会经过什么

Minimal API 先按端点签名绑定参数。对 `int take = 50`，没有提供 `take` 时使用默认值；提供了无法解析的文字时，绑定直接失败。绑定成功后，ASP.NET Core 的校验端点过滤器检查参数上的 DataAnnotations 属性。`take=201` 不符合 `[Range(1, 200)]`，过滤器返回 `400`，端点委托以及仓储都不会执行。校验通过，委托才会调用 `IProfileRepository.ListAsync(skip, take, token)`。

`AddValidation()` 在依赖注入中注册这套校验服务。构建项目时，.NET 10 的验证源码生成器发现端点签名中需要校验的参数和类型，并生成校验所需的元数据；**生成发生在编译期，输入检查仍发生在每次请求时**。端点过滤器使用这些元数据检查已绑定的值。官方文档说明了[注册、生成范围和执行顺序](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0)。

生成器按程序集发现类型。Reference 的端点和 `AddValidation()` 调用都在 `MS.Microservice.Reference.Web` 中，所以这里能发现 `[Range]`。如果以后把端点移到另一个程序集，只在启动程序集调用 `AddValidation()`，可能出现服务已经注册、校验却没有执行的情况；需要在定义端点的程序集提供调用 `AddValidation()` 的注册方法，再由宿主调用它。当前 Web 项目使用 `Microsoft.NET.Sdk.Web`，验证程序集由 Web 框架引用提供，不需要额外添加同版本包引用。

源码生成的校验元数据符合项目优先考虑 AOT 和 Trimming 的方向，避免为发现这些规则自行扫描程序集。这里没有做性能对比，也不能据此断定整个 Reference 宿主已经通过 Native AOT 发布验证。

## 哪些规则还留在业务层

[`CreateProfile` 和 `ChangeProfile`](../../../samples/Reference/MS.Microservice.Reference.Application/Contracts.cs) 没有声明 DataAnnotations。`AddValidation()` 不会凭 DTO 的属性名推断身份、名称或角色是否合法。创建和修改档案时，[`UserProfile`](../../../samples/Reference/MS.Microservice.Reference.Domain/UserProfile.cs) 检查外部身份、名称长度和角色目录；[`ProfileService`](../../../samples/Reference/MS.Microservice.Reference.Application/ProfileService.cs) 还检查修改请求的预期版本。这些规则在非 HTTP 调用路径也必须成立，所以不能只放在端点属性上。

业务层返回的 `Error.Validation` 经 [`ApplicationErrorResults`](../ApplicationErrorResults.cs) 映射成带 `code=validation` 的 ProblemDetails。自动参数校验由 ASP.NET Core 产生自己的验证错误响应；目前测试只断言它返回 `400` 且不调用仓储，并未固定完整 JSON 格式。调用方若需要统一两类错误的字段，应先明确响应契约，再添加相应的集成测试。

新增查询参数时，先写清缺省值、可解析类型和合法范围，再分别测试省略、合法值、上下界、越界值与无法解析的值。只给参数加属性而漏掉同程序集的验证注册，会让可解析但越界的值进入端点；只测 `take=invalid` 无法发现这个遗漏。
