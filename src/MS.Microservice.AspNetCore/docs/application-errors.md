# 应用错误映射到 HTTP

Reference 原先在端点中按 `Error.Code` 选择状态码，并把 `Error.Message` 直接放进 ProblemDetails。`Error.FromException` 会保存异常消息；当它落入未识别的错误码分支时，响应虽然是 500，却可能把数据库、连接或内部实现信息返回给客户端。多个端点各写一份映射还容易让同一错误得到不同状态码。

`ApplicationErrorResults.ToProblem` 在 ASP.NET Core 边界统一映射已知错误码。业务层仍负责产生错误和决定哪些文字可公开，HTTP 层只决定对外状态和响应字段：

| 错误码 | 状态 |
| --- | ---: |
| `validation` | 400 |
| `unauthorized` | 401 |
| `not_found` | 404 |
| `conflict` | 409 |
| 其他值，包括 `unexpected` | 500 |

已知 4xx 保留调用方给出的摘要与细节。未知错误一律返回固定标题和 `unexpected` 代码，不输出原消息或细节。结果通过框架的 `Results.Problem` 写出，继续使用宿主已注册的 ProblemDetails 设置，包括 traceId。此适配层只接收字符串和细节列表，不引用业务模型，也不扫描程序集或生成动态代码。

## Reference 的输入校验接入

Reference 原先在三个列表端点内分别检查 `skip`、`take` 和 `limit`。检查发生在处理方法内部；以后若增加同类端点，很容易漏掉一处，或者让缺省值与范围不一致。现在这些 HTTP 查询约束放在端点参数上：档案列表的 `skip` 为 0 到 `int.MaxValue`，`take` 为 1 到 200；审计列表的 `take` 为 1 到 200；失败消息列表的 `limit` 为 1 到 1000。省略时仍分别使用 `skip=0`、`take=50`、`limit=100`。无法解析的整数和超出范围的值在处理方法运行前返回 `400`，不会调用仓储列表方法。

端点参数保持非空 `int`，并通过 C# 默认参数值实现可选查询。Reference.Web 在定义端点的同一个程序集调用 `AddValidation()`，由 .NET 10 的验证源码生成器处理 `[Range]`。领域约束仍在 `ProfileService` 和领域模型中；例如档案身份、名称及角色的检查没有移到 HTTP 层。

这里没有使用 `[AsParameters]` 查询类。当前项目的 .NET 10.0.12 TestServer 验证表明：类属性的初始化默认值在该绑定方式下被当作必填参数，省略 `take` 返回 `400`；位置记录虽然能保留默认值，但 `[Range]` 对展开后的成员未生效，`skip=-1` 返回 `200`。直接可选参数同时保留了缺省语义和框架验证结果。`Microsoft.Extensions.Validation` 的程序集与生成器由当前 Web SDK 的 `Microsoft.AspNetCore.App` 提供；直接添加同版本 PackageReference 会触发 `NU1510`，所以没有保留冗余包引用。

Reference 的 `Respond` 调用同一映射，业务错误也遵守上表；共享适配层不引用业务模型，也不扫描程序集。

如果需要从实际请求理解 `AddValidation()`、参数绑定和业务校验分别处理什么，参见 [Reference 为什么调用 `AddValidation()`](minimal-api-validation.md)。

## 验证与范围

TestServer 验证了八种非法分页值在仓储调用前被拒绝，以及省略、正常值和上界值实际传入仓储。Reference 的领域验证和冲突响应仍带公开错误码；共享适配层的测试覆盖四类公开错误、未知错误的信息隐藏、细节及 traceId。这项修改修复响应契约和信息暴露，没有性能优化数据。后续新增查询字段时，应在 HTTP 边界写明解析、缺省和范围，同时保留业务层独有的规则。
