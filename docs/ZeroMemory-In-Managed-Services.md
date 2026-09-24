# 托管缓冲区清零：`CryptographicOperations.ZeroMemory` 的收益与边界

带 `Idempotency-Key` 的请求进入 Reference 后，[HTTP 幂等执行器](../samples/Reference/MS.Microservice.Reference.Web/HttpIdempotency/ReferenceHttpIdempotencyExecutor.cs)会把 Body 读入 `MemoryStream`：先计算请求指纹，再把同一份正文交给端点。请求结束时释放流，并不等于擦除它的字节数组。.NET 10 的 `MemoryStream.Dispose` 会关闭流，但仍保留底层缓冲区，允许调用者继续用 `GetBuffer` 取得它。[.NET 10 源码](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/MemoryStream.cs)

## 它清除的是哪块内存

`CryptographicOperations.ZeroMemory(Span<byte>)` 把传入的字节范围写成零。`Span` 是现有内存的视图；把数组的一段作为 `Span` 传入，不会因此复制数组。例如：

```csharp
byte[] bytes = [1, 2, 3];
CryptographicOperations.ZeroMemory(bytes.AsSpan(0, 2));
// bytes 现在是 [0, 0, 3]
```

它比普通清零调用多了一项明确的运行时保证：清零写入不会因后续没有读取而被优化掉。.NET 10 的实现为此禁止该方法被内联和优化，然后调用 `buffer.Clear()`。这不表示当前 .NET 会跳过普通的 `Array.Clear`；官方文档也说目前没有计划引入那种优化。[API 文档](https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory)、[.NET 10 源码](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/CryptographicOperations.cs)

在执行器里，`bodyBytes.Span` 和 `body.GetBuffer().AsSpan(0, (int)body.Length)` 指向同一个 `MemoryStream` 底层数组的已使用部分。代码多次出现清零调用，是因为退出位置不同：

| 请求走到哪里 | 清零位置 | 不清零时的功能结果 |
| --- | --- | --- |
| 读取时超过 1 MiB，提前返回 `413` | 返回前清理已读入的部分 | 仍返回 `413` |
| JSON 或幂等键无效，提前返回 `400` | 对应异常分支清理正文副本 | 仍返回 `400` |
| 查到记录并重放，或运行端点后结束 | `finally` 恢复原请求流，再清理正文副本 | 重放和业务结果不变 |

例如，请求发送 `{"sku":"SKU-42","quantity":2}`。清零后，执行器当前持有的正文数组中这段内容变成零；若不清零，已经持有该数组引用的代码在流释放后仍可通过 `GetBuffer()` 看到正文。两种情况下客户端得到的 `201` 或重放响应相同。这里的 `Idempotency-Key` 是 HTTP 请求头中的字符串，并不会被这些正文数组清零调用擦除。

[身份作用域代码](../samples/Reference/MS.Microservice.Reference.Web/HttpIdempotency/ReferenceIdempotencyActorScope.cs)清理的是另一块数组：把 issuer 和 subject 序列化后用于哈希的临时字节。它不清理认证中间件持有的 claims。[解密模型绑定器](../src/MS.Microservice.AspNetCore.Encryption/ApiDecryptModelBinder.cs)中的用法更直接：RSA 解出的 AES 密钥转成 `byte[]`，用完后在 `finally` 中清除该密钥数组。

## 不写会有什么影响

对当前 HTTP 幂等执行器，删掉这些正文清零调用不会改变请求指纹、事务、响应、重放或 AOT 行为。区别在于：这份正文副本在流释放后仍可能留在托管堆中，直到相关内存被回收或覆盖，因此也可能出现在进程内存转储中。清零缩短的是**当前数组中这份正文**的留存时间。

对 AES 密钥这样的密钥材料，清理更有明确价值：不清理时，密钥在该数组中的留存时间也交给内存回收与覆盖过程决定。清零至少能在使用结束时处理**当前持有的这一份数组**。

这项操作不能证明敏感数据已从进程内存消失。HTTP 服务器可能持有原始 Body；JSON 解析、编码转换或 `MemoryStream` 扩容可能产生其他数组。扩容后 `GetBuffer()` 指向新数组，清理当前数组不会回头清理旧数组。[`GetBuffer` 文档](https://learn.microsoft.com/dotnet/api/system.io.memorystream.getbuffer)也说明扩容可能更换底层数组。claims 和其他 `string` 不受这次字节清零影响；日志、数据库和已经发送的响应更不会被它改变。

还有一个当前代码没有覆盖的退出路径：若读取 Body 的 `ReadBodyAsync` 自身抛出异常，执行器尚未进入后面的 `try/finally`，不会运行该处的正文清零。`ArrayPool<byte>.Return(buffer, clearArray: true)` 处理的是另一个租用缓冲区；[官方文档](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.return)说明，清理行为取决于池是否保留该数组供后续复用。因此，不能把当前实现描述成“请求退出时所有正文副本都会清零”。

## 本项目如何取舍

密钥、解密后的明文等明确的敏感临时字节适合在最后一次使用后清理，并用 `finally` 覆盖异常路径。对普通 HTTP JSON 正文，清零是一项局部的防御措施，不能代替减少副本、限制日志内容和管理内存转储访问。是否保留，应依据这些正文的敏感程度及进程内存转储的威胁模型判断。

清零需要再写一遍目标缓冲区；`Span` 本身不产生这份数据的副本，也没有引入反射。当前项目没有针对这些调用的耗时、分配或 GC 前后数据，因此本文不声称清零的成本可忽略，也不以性能为由建议删除。若以后要调整请求正文的清理策略，先测量带键请求的成本，再核对实际还能存在哪些正文副本。
