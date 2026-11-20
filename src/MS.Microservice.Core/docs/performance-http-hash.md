# 对照源码看 HTTP JSON 和 SHA-256 的中间分配

## 创建两个 HTTP 客户端，要创建几套转义规则？

Lab 用 `AddHttpClient<LogHttpClient>()` 注册客户端。旧版每次构造 `LogHttpClient`，都会执行下面字段初始化器中的 `JavaScriptEncoder.Create`。客户端可以有许多实例，传给编码器的 `UnicodeRanges.All` 始终相同。[注册入口](../../../samples/Lab/MS.Microservice.Lab/Infrastructure/Extensions/IServiceCollectionExtensions.cs#L71)

```diff
+private static readonly JavaScriptEncoder DefaultEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);
 public JsonSerializerOptions JsonSerializerOptions = new()
 {
     PropertyNameCaseInsensitive = true,
-    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
+    Encoder = DefaultEncoder
 };
```

假设先创建客户端 A，再创建客户端 B。现在转义规则只构造一次，A 和 B 的 `Encoder` 引用指向同一个不可变对象。注意 `JsonSerializerOptions = new()` 仍是实例字段初始化器，因此仍会创建两份选项。调用者可以在首次序列化之前，只给 A 设置驼峰命名。此时 A 把 `UserName` 写成 `userName`，B 仍使用 `UserName`。如果把整份配置一起共享，这两个客户端就会互相影响。[客户端的编码器与选项](../Net/Http/LogHttpClient.cs#L14) · [选项隔离测试](../../../test/MS.Microservice.Core.Tests/Net/Http/LogHttpClientTests.cs#L40)

`DefaultSerializeSetting.ChineseEncoder` 也有固定规则，允许的范围是 Basic Latin 和指定的中文字符区。之前每次取这个属性都会构造编码器。现在 `Lazy<JavaScriptEncoder>` 在第一次读取时构造，以后直接返回已有实例。两种编码器各自保留原有字符范围。这里节省的是重复构造的耗时和分配，进程仍要保留已经用到的编码器。[中文转义规则](../Serialization/DefaultSerializeSetting.cs#L19)

## POST 发出 81,934 字节，途中为什么还会有一个长字符串？

`PostAsync` 把正文和当前客户端的选项交给 `SendAsync`。旧版的 `Serialize` 先返回完整的 .NET 字符串，`StringContent` 随即按 UTF-8 把它编码成 HTTP 正文字节。调用方发出请求后就用不到这个中间字符串。新版直接取得序列化后的字节数组，交给 `ByteArrayContent`。[请求构造](../Net/Http/LogHttpClient.cs#L49)

```diff
-request.Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions), Encoding.UTF8, "application/json");
+request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions));
+request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
```

用基准里的 `new { Message = "A中&" }` 走一遍。默认转义策略保留“中”，把 `&` 写成六个 ASCII 字符 `\u0026`。正文是 `{"Message":"A中\u0026"}`。`{"Message":""}` 的固定部分占 14 个 ASCII 字符。引号内这一段占 8 个 UTF-16 码元；编码成 UTF-8 时，“中”占 3 字节，因此这一段占 10 字节。[基准输入](../../../benchmarks/MS.Microservice.Core.Benchmarks/InfrastructureCases.cs#L24)

| 引号内的输入 | 旧版中间字符串长度（UTF-16 码元） | 两版发出的正文长度（字节） |
| --- | ---: | ---: |
| `A中&` 一次 | 14 + 8 = 22 | 14 + 10 = 24 |
| `A中&` 重复 8192 次 | 14 + 8 × 8192 = 65,550 | 14 + 10 × 8192 = 81,934 |

对于大输入，单是 65,550 个码元就占 131,100 字节，还要为字符串对象和最终输出数组留出空间。新版仍然需要 81,934 字节的发送缓冲，但省去了完整的 UTF-16 中间结果和再编码步骤。正文仍在交给 HTTP handler 之前完成序列化，每个请求仍有自己的内容头，媒体类型及字符集保持 `application/json; charset=utf-8`，`Content-Length` 按实际字节数报告。新测试用另一组中文和 `&` 逐字节核对了这一契约。[正文和请求头回归](../../../test/MS.Microservice.Core.Tests/Net/Http/LogHttpClientTests.cs#L40)

## 日志关闭时，哪些计算可以省下来？

`SendAsync` 捕获原始异常后调用 `logger.HttpRequestFailed(id, method, exception, started)`，再用 `throw;` 向上传播。扩展方法先检查 Information 级别，启用时才读取异常类型、计算耗时并填充日志模板。如果在调用处先传入 `Stopwatch.GetElapsedTime(started).TotalMilliseconds`，即使日志已关闭，这个参数仍会先求值。开始和成功事件也在各自记录时检查级别，三个事件沿用同一请求 ID。失败事件只记录异常类型，避免输出可能包含凭据的异常消息。[事件实现](../Net/Http/HttpClientLogExtensions.cs) · [启用与禁用的回归测试](../../../test/MS.Microservice.Core.Tests/Net/Http/HttpClientLogExtensionsTests.cs)

## 摘要固定为 32 字节，输出为什么曾经要拼接 32 次？

`GitCommitIdGenerator` 把文件内容、提交消息、作者和时间拼好后，会调用 `CryptologyHelper.SHA256`。旧循环中 `b.ToString("x2")` 每次把一个字节格式化成两位小写十六进制，再追加到 `StringBuilder`，最后还要调用一次 `ToString()` 取出结果。无论输入多长，SHA-256 的输出都是 32 字节，因此每次都会执行 32 次单字节格式化。[摘要调用方](../Common/GitCommitIdGenerator.cs#L19)

```diff
-foreach (byte b in hashBytes)
-{
-    sb.Append(b.ToString("x2"));
-}
-return sb.ToString();
+Span<byte> digest = stackalloc byte[System.Security.Cryptography.SHA256.HashSizeInBytes];
+System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value), digest);
+return Convert.ToHexStringLower(digest);
```

以 `订单🔐` 为例，“订”和“单”的 UTF-8 编码各占 3 字节，锁形字符占 4 字节。两版都对同样的 10 字节求摘要。现在摘要写到栈上的 32 字节空间里，`Convert.ToHexStringLower` 一次生成完整的 64 字符字符串。摘要开头三个字节是 `23 86 2F`，结果开头仍是 `23862f`。输入的 UTF-8 字节数组和返回的字符串仍是这条路径的分配，测试分别固定了空输入与上述 Unicode 输入的完整摘要。[哈希实现](../Security/Cryptology/CryptologyHelper.cs#L41) · [已知摘要测试](../../../test/MS.Microservice.Core.Tests/Cryptology/HmacSha256Tests.cs#L12)

各场景的同条件 Release 耗时、托管分配及复测方式见[性能验收](../../../docs/validation/CORE-PERFORMANCE/summary.md)。
