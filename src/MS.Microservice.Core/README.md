# Core：通用代码辅助

Core 提供跨业务可复用的函数式结果、规格、集合、序列化、HTTP 等辅助能力。
它依赖 Domain.Primitives 的小契约，不依赖 Reference、Lab、具体数据库或业务 User/Order 模型。

它不是所有基础设施的总入口。消息、AI、日志和 ORM 有各自模块；
只有确实需要这些通用类型的项目才引用 Core，不能为了目录“统一”制造额外依赖。

## 阅读思路

- Functional 中的 Option/Either/Result 表达缺失值和业务失败，避免把所有分支都变成异常控制流。
- Specification 表达可复用查询意图；数据库 Provider 的翻译与执行属于持久化模块。
- Net/Http 负责请求编码、生命周期和异常语义，不能替调用方决定业务重试或授权。
- Domain.Primitives 中的实体/审计契约可单独使用；具体聚合根与规则属于应用 Domain。

公共契约的注释说明用法与限制，非直观分支附近说明原因。
更高层取舍见[总体架构](../../docs/Architecture-Overview.md)；
复制或包引用需要包含声明的依赖，见[组件消费](../../docs/components/consumption.md)。

对称加密只接受调用方提供的 32 字节密钥，并使用带版本号的 AES-256-GCM 密文；旧 AES-ECB 和 3DES 格式已停用。原因、格式和密钥使用方式见[对称加密格式](Security/Cryptology/authenticated-encryption.md)。

`HmacSha256` 要求调用方明确传入密钥。旧密码哈希的固定密钥只保留在 Lab 的[旧密码验证说明](../../samples/Lab/MS.Microservice.Lab/Application/Identity/legacy-password-verification.md)所述登录升级路径中。

RSA 新密文使用 OAEP-SHA256 和版本前缀；无前缀的 PKCS#1 v1.5 密文仍可读取。密文长度限制、兼容边界和旧分支退出条件见[RSA 密文格式](Security/Cryptology/rsa-oaep.md)。

### HTTP 请求辅助

LogHttpClient 的 GET 参数可为公开可读属性对象或 IDictionary；null 值省略，空字符串保留，
集合展开为同名参数，数值使用 invariant culture，日期使用往返格式。键和值分别 URL 编码，
追加参数时保留已有查询串和片段。POST 使用 UTF-8 application/json。
传入的请求头仅属于本次请求，不修改 HttpClient.DefaultRequestHeaders。

取消、HTTP 状态失败和 JSON 解析失败分别保留 OperationCanceledException、
HttpRequestException、JsonException；调用方应更新旧的“统一解析异常”捕获逻辑。
默认日志不记录 URL、参数、正文及异常消息。

`LoggingHttpClientHandler` 是另一种可选的 `DelegatingHandler`。Information 日志关闭时，它直接转发原始 `HttpContent`；开启时，默认异步读取并记录完整 URI、请求和响应正文。正文缓存在原 `HttpContent` 内，Handler 不再替换内容对象。宿主需要只记录元数据时，可显式开启 `EnableRedaction`：

```csharp
services.Configure<LoggingHttpClientHandlerOptions>(options => options.EnableRedaction = true);
services.AddTransient<LoggingHttpClientHandler>();
services.AddHttpClient("remote-api").AddHttpMessageHandler<LoggingHttpClientHandler>();
```

开启后，该 Handler 只记录方法、不含查询字符串的路径、响应状态或失败类型、耗时。它不读取或记录请求与响应正文；HTTP 内容和异常仍原样交给调用方。路径本身可能包含业务标识，调用方仍应避免把凭据放进路径。没有开启该设置的客户端维持完整正文日志行为。两种模式的读取、缓冲与资源所有权见[正文日志实现说明](Net/Http/body-logging.md)。

缓存与 HTTP JSON 辅助方法复用固定的序列化配置；原因、格式差异与分配数据见[JSON 配置复用说明](Serialization/json-configuration-reuse.md)。
