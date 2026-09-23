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

测试通过真实 TestServer 管线检查四类公开错误、未知错误的信息隐藏、细节及 traceId。这里修复的是响应契约与信息暴露，没有性能优化数据。输入字段的结构校验应在宿主程序集注册 .NET 10 的源码生成验证；领域校验仍在应用服务内。这样框架不会靠反射发现业务模型，也不会替业务定义规则。
