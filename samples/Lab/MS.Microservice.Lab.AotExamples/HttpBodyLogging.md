# HTTP 正文日志：为什么能移除自定义包装器

记录正文时，同一份内容有两个使用者：日志，以及实际发送请求或消费响应的代码。如果直接读完原始流，后一个使用者可能拿不到数据。旧实现用缓存来支持重复读取，这个需求本身是合理的。

`LoggableHttpContent` 是正文包装器，请求和响应都用了它。它保存字节、转发内容、复制头部并代理释放资源；`LazyContentLogger` 则把读取延迟到日志格式化时。这解释了旧代码为什么复杂。

## 先确认标准库已经承担了哪些职责

关键在于读取入口。`HttpContent.ReadAsStringAsync` 会通过 `LoadIntoBufferAsync` 在内容对象内部缓冲正文；同一个内容对象随后调用 `CopyToAsync` 时可以直接复制缓冲，不再调用原始序列化方法。[读取文档](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcontent.readasstringasync?view=net-10.0)、[复制文档](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcontent.copytoasync?view=net-10.0)都明确说明了这个行为。

因此，日志通过原 `HttpContent` 读取之后，发送端或调用方仍可通过同一个对象取得完整正文。这里有依据复用标准库提供的能力。

| 旧实现承担的职责 | 新实现为什么不再需要它 |
|---|---|
| `_cachedBytes` 保存完整正文 | 原 `HttpContent` 已经缓冲了字节，无需再维护一份缓存。 |
| `SemaphoreSlim` 保护缓存初始化 | 处理器先 await 读取，再发送或交付；日志参数也不再触发读取。本路径没有多个读取者同时初始化缓存。 |
| 复制 headers、重写 `SerializeToStreamAsync` 和 `TryComputeLength` | 内容对象没有被替换，原对象继续负责头部、内容长度和发送。 |
| 包装器代理 `Dispose` | 没有新增内容包装层，请求和响应仍拥有原内容；未交付的响应由处理器在异常时释放。 |
| `LazyContentLogger.ToString()` 获取正文 | 日志接收已经读取完成的字符串，格式化过程只处理文本。 |

这里复用的是字节缓冲，不应理解为每次调用 `ReadAsStringAsync` 都复用同一个字符串。新实现每个日志正文只读取一次，再把该字符串交给日志提供器。

## 真正改变的是读取顺序

旧流程在日志提供器调用 `ToString()` 时才读取正文，然后用 `GetAwaiter().GetResult()` 等待读取结束。正文尚未到齐时，格式化日志的线程会被占住；前面的 `ConfigureAwait(false)` 不能消除这个同步等待。

新流程把 IO 放回异步发送方法。请求方向的关键顺序如下，省略了日志模板等细节：

```csharp
var payload = await ReadContentForLogAsync(request.Content, cancellationToken);
logger.LogInformation("HTTP payload: {Payload}", payload);
var response = await base.SendAsync(request, cancellationToken);
```

读取需要等待时，调用方先得到未完成的 Task；读完后记录字符串，再发送已经缓冲的正文。响应方向同样先异步读取和记录，然后交付响应。多个日志接收端格式化同一个参数时，也不会重新读取或解码正文。

另一个独立问题是日志关闭后的行为：旧代码仍然安装包装器，而包装器在转发前必须读完整段正文。新代码在请求开始时检查日志级别，关闭时直接调用下一层处理器，不替换内容、不额外缓冲。这个收益即使完全不输出日志也成立。

## 适用前提与行为边界

- 后续发送和读取使用同一个 `HttpContent`；处理器按 await 顺序执行。这不是对同一个内容对象并发读写安全的承诺。
- 开启完整正文日志仍要缓冲整个正文，不能同时承诺首块立即交付。关闭日志只消除本处理器添加的缓冲，其他调用层仍可能选择缓冲。
- 普通正文读取失败仍记录错误标记；调用方取消会继续传播。响应尚未交付便发生取消或日志提供器异常时，处理器必须释放响应，不能指望调用方处理未拿到的对象。
- 本次简化不意味着内容包装器一概无用。例如要在流式转发过程中记录有界摘要，就需要另外设计读取方式，不能直接套用完整正文读取。

## 用行为验证简化没有丢掉职责

[回归测试](../../../test/MS.Microservice.Core.Tests/Net/Http/HttpLoggingRegressionTests.cs)用受控正文只释放首块：日志关闭时目标流应立即收到首块；开启时 `SendAsync` 应先返回未完成的 Task。超时只用于防止缺陷挂住测试，不用于比较性能快慢。

[内容与资源测试](../../../test/MS.Microservice.Core.Tests/Net/Http/LoggingHttpClientHandlerTests.cs)检查日志读取后仍能取得完整正文、头部保持、源内容只序列化一次，以及取消和异常下的释放责任。[新旧示例对照](../../../test/MS.Microservice.Lab.AotExamples.Tests/HttpLoggingExampleTests.cs)还保留了旧包装器的行为证据。

这些证据支持移除重复的包装机制和同步等待；代码行数减少只是结果。本主题属于运行时性能修复，不构成 NativeAOT 发布验证。
