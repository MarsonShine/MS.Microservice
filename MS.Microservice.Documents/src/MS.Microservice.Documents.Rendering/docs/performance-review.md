# 一页 PDF 回到调用方之前要复制几次

浏览器完成排版后，PDF 还要经过 worker 的标准输出才能交到调用方手中。旧版在父进程里已经收齐 PDF，准备写入目标流时却又复制了两份几乎一样的字节。要找出这两次复制，先看 worker 发回的数据长什么样。

## 从带页数头的帧中取出 PDF

worker 往标准输出先写一个四字节的小端整数，表示页数，再写 PDF 正文。父进程必须把二者分开。例如，两页 PDF 的帧以 `02 00 00 00 25 50 44 46 …` 开头。前四个字节表示 2；目标流的第一个字节应该是 `25`，即 `%PDF` 的百分号。

两版都先收齐这一帧，再把页数与正文交给调用方。旧版的额外工作发生在收齐以后：

```csharp
// ReadOutputAsync：output 是已经收完整帧的 MemoryStream
return output.ToArray();
```

```csharp
// RunCoreAsync：上一步给了它一个 byte[] wire
var pages = BinaryPrimitives.ReadInt32LittleEndian(wire);
// 页数检查通过
return (wire[4..], pages);
```

第一句 `ToArray()` 会分配新数组，把 `output` 中有效的字节全部复制进去，连四字节页数头也一起复制。第二句看起来只是“从下标 4 开始”，但 **C# 对数组做范围切片会创建新数组**。于是刚复制过的 PDF，又从 `wire` 被复制到只含正文的返回值。假设正文长 `N`，两步一共要新建容量为 `N + 4` 和 `N` 的两个数组，CPU 也要读写对应数量的字节。

新版保留相同的协议和下标，只改了交给下一步的数据表示：

```diff
  // ReadOutputAsync 的返回类型：byte[] → ReadOnlyMemory<byte>
- return output.ToArray();
+ return output.GetBuffer().AsMemory(0, (int)output.Length);
```

```csharp
// RunCoreAsync：现在 wire 是 ReadOnlyMemory<byte>
var pages = BinaryPrimitives.ReadInt32LittleEndian(wire.Span);
// 页数检查通过
return (wire[4..], pages);
```

`GetBuffer()` 取的是 `MemoryStream` 已经写过的底层数组。有一个细节不能省：流扩容后，数组的 `Capacity` 可能大于实际的 `Length`。`AsMemory(0, Length)` 先把有效帧限定到真正收到的末尾；对这种视图执行 `[4..]`，只是把起点往后移四格、长度减四。两段视图都指向原数组，既不会把页数头发出去，也不会把数组尾部的备用空间写进 PDF。[接收缓冲](../RenderWorkerSupervisor.cs#L147) · [拆帧并返回正文](../RenderWorkerSupervisor.cs#L103)

![从同一接收数组取两个视图：wire 含四字节头和正文，wire[4..] 只含正文，备用容量不参与写入](assets/pdf-frame.svg)

这段视图可以交给异步写入，因为每次渲染都有自己的暂存流，直到 `await destination.WriteAsync(...)` 完成，底层数组都由这次调用持有，也没有其他写入者会覆盖它。[父进程的最终写入](../HtmlPdfRenderer.cs#L31)

暂存流仍要保留到 worker 成功退出、资源清理完成且帧校验通过。这样即使 worker 在输出一半时退出，也不会把半份 PDF 交给调用方。[Word 和 HTML 共用的监督逻辑](../RenderWorkerSupervisor.cs) · [短读和帧边界测试](../../../test/MS.Microservice.Documents.Rendering.Tests/RenderWorkerMetadataTests.cs)

## 图片的第一次复制为什么要留着

一张图片在 `HtmlPdfAsset.Content` 中是一个可变的 `byte[]`。旧版的父进程和 worker 都调用 `Snapshot`，两次都对每份资源执行同一行：

```csharp
Content = asset.Content.ToArray()
```

放在父进程，这句有明确作用。设调用方把一张图片交给 `RenderAsync`，异步传输期间又复用了原数组去装下一张图。如果父进程只是把字典复制一份，两个字典里的 `Content` 仍指向同一数组，worker 实际收到的内容就可能改变。父进程需要在传输前固定字节，即使用的是只读字典也一样。

但是跟过进程边界再看一次：worker 从标准输入反序列化 JSON，`raw.Options.Assets` 里的字节已经位于 **worker 自己的堆**。调用方改写父进程里的图片，影响不到这份数据。此时再执行 `ToArray()`，只会让 worker 多分配一份同样长的数组。新版在两个调用点明确表达这个区别：

```csharp
// HtmlPdfRenderer.RenderAsync：父进程在传输前固定调用方提供的内容
var request = PrepareRequest(html, _browser, options ?? new(), copyAssetContents: true);
```

```csharp
// HtmlRenderWorker.RunAsync：raw 来自本进程刚完成的 JSON 反序列化
var request = HtmlPdfRenderer.PrepareRequest(raw.Html, raw.Browser, raw.Options, copyAssetContents: false);
```

```diff
  // PrepareRequest 创建新字典、检查名称和预算、归一化 MIME 时：
- Content = asset.Content.ToArray()
+ Content = copyAssetContents ? asset.Content.ToArray() : asset.Content
```

`false` 只控制最后的字节复制。路径、MIME、数量、字节数仍在两端校验，图片在 worker 内还要检查格式和像素数。另一个小改动也在这段循环里：原来对 HTML 和每个资源名调用 `new UTF8Encoding(false, true).GetByteCount(...)`，现在用共享的 `StrictUtf8.GetByteCount(...)`。严格 UTF-8 的判定相同，资源数增加时少创建一串相同配置的编码对象。[父进程与资源循环](../HtmlPdfRenderer.cs#L31) · [worker 入口](../HtmlRenderWorker.cs#L17)

Word 转 PDF 也走同一条输出协议。它在输入端复用了 Documents 的有界快照逻辑：预算内的普通 `MemoryStream` 直接填充精确长度数组，其他流仍按实际读取量计费。[Word 读取入口](../WordPdfConverter.cs#L48) · [快照的适用条件](../../MS.Microservice.Documents/docs/performance-review.md#pdf-快照在哪里需要独占数组)

[性能基准](../../../../benchmarks/MS.Microservice.Documents.Benchmarks/Program.cs) · [验收记录](../../../../docs/validation/DOCUMENTS-RENDERING-PERF/summary.md)
