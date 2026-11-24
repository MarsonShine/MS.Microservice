# 只改一个字段，为什么要复制整份 Word 文档

想象一份业务回执：模板里有一个状态控件，还嵌着一张 3.67 MB 的照片。把状态换成“已完成”，照片一个字节都不用改。可在旧版 `FillAsync` 里，甚至还没找到状态控件，程序就已经把照片连同整个 DOCX 包搬了两遍。为什么改几个字会有这样的成本？先跟着这份模板走到打开 Word 文档之前。

## 填控件时为什么可以少留一份 DOCX

假设从调用方的 `template` 当前位置还能读出 4 MiB。沿着这 4 MiB 一直追到 SDK 打开工作副本，就能看到两种写法的区别。

| 同一起点、同一终点 | 旧版 | 新版 |
| --- | --- | --- |
| 读入模板，隔离调用方原件 | 循环读取 `template`，写入私有 `source` | `ReadFromAsync` 读入私有 `edited` |
| 检查输入包 | `CheckAsync(source, ...)` | `CheckAsync(edited, ...)` |
| 准备按输出预算编辑 | 创建 `edited`，把 `source` 的全部 4 MiB 复制进去 | 对同一份 `edited` 调用 `ChangeWriteLimit` |
| 交给 SDK | `edited.Position = 0` | `edited.Position = 0` |

容易漏算的是第一行。旧代码没有把“读入模板”写成一个命名方法，但那段操作一直都在。下面是旧版的同一段路径，省略了异步调用后的 `ConfigureAwait(false)`：

```csharp
using var source = new BoundedBuffer(limits.MaximumInputBytes, "DOCX input");
var buffer = new byte[(int)Math.Min(81920L, (long)limits.MaximumInputBytes + 1)];
int read;
while ((read = await template.ReadAsync(buffer.AsMemory(0,
    (int)Math.Min(buffer.Length, (long)limits.MaximumInputBytes - source.Length + 1)),
    cancellationToken)) != 0)
{
    source.Write(buffer, 0, read);
}

await WordPackage.CheckAsync(source, limits, cancellationToken);
using var edited = new BoundedBuffer(limits.MaximumOutputBytes, "DOCX output");
source.Position = 0;
await source.CopyToAsync(edited, cancellationToken);
edited.Position = 0;
```

读到 `CopyToAsync` 时，先问一句：**复制完成以后，还有谁需要访问编辑前的 `source`？** 调用方持有自己的原件，验包也已结束，SDK 随后只打开 `edited`。这说明“隔离调用方”和“提供可编辑缓冲”可以由同一份私有数据承担。不过还有一件事要接手：旧代码创建 `edited` 时，构造器顺便把写入上限从输入预算换成了输出预算。新版把这件事单独写出来：

```csharp
using var edited = new BoundedBuffer(limits.MaximumInputBytes, "DOCX input");
await edited.ReadFromAsync(template, cancellationToken);
await WordPackage.CheckAsync(edited, limits, cancellationToken);
cancellationToken.ThrowIfCancellationRequested();
edited.ChangeWriteLimit(limits.MaximumOutputBytes, "DOCX output");
edited.Position = 0;
```

新版的 `ReadFromAsync` 对应旧版开头的整段读取循环。真正消失的是验包后的 `source.CopyToAsync(edited, ...)`：它要把 4 MiB 全部从一个数组读出，再写入另一个数组。两边都有的 `Position = 0` 只移动游标，新增的取消检查也只查看令牌状态。

`ChangeWriteLimit` 先检查现有数据的 `Length` 能否放进输出上限，再检查数组的 `Capacity`。通常只需更新限额和错误提示；如果有效数据超限就报错，如果数据放得下但容量过大，才会收缩数组并复制有效内容。[FillAsync 的完整调用顺序](../Word/WordTemplateProcessor.cs#L29) · [ChangeWriteLimit 的两个分支](../Internal/BoundedBuffer.cs#L48)

## 顺着一次 XML 读取，同时完成解析和计数

模板进入 `WordPackage.CheckAsync` 后，程序需要知道一个 XML 部件**实际解压了多少字节**，还要检查 XML 是否有效。看普通的 `word/document.xml`，旧版分两步做这件事。下面是两条读取路径的核心语句，中间省略了预算计算和错误处理：

```csharp
// 第一遍：entry.Open() 得到解压流，读到末尾以累计 actual。
using (var stream = entry.Open()) { /* ReadAsync → actual += read */ }

// 第二遍：从同一条目开头重新解压，供 XML 解析器读取。
using var xmlStream = entry.Open();
using var xml = XmlReader.Create(xmlStream, XmlSettings(limits));
```

为什么一定要等计数结束，解析才能开始？每当 `XmlReader` 要下一批数据，解压流都已经知道这一批实际返回了多少字节。新版把计数放在这两个对象之间：

```csharp
using var stream = new ExpandedPartStream(entry.Open(), limits.MaximumExpandedBytes - actual, cancellationToken);
using var xml = XmlReader.Create(stream, XmlSettings(limits));
```

假设展开预算还剩 3 B，解析器请求 8192 B，`ExpandedPartStream` 最多向下层读 4 B：第 4 B 用来判断是否超限。边读边计数后，普通 XML 部件解压一遍就同时完成了限额检查和解析。[包校验循环](../Word/WordPackage.cs#L69) · [实际读取量如何受限](../Internal/ExpandedPartStream.cs#L18)

![旧版包校验把普通 XML 解压两遍，旧版文本提取先暂存全部展开字节；新版在解析时计数](assets/expanded-xml.svg)

图里的另一行是只提取文字时调用的 `WordTextExtractor`。它原来的额外开销有所不同：先把一个条目完整解压进 `BoundedBuffer`，再把这个暂存流交给 `XmlReader`。正文读完可以释放暂存流，但下一个页眉又得重新分配。对照它的消费端就能看清这份内存为什么可以省：

```diff
- using var buffer = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
- using var xml = CreateReader(buffer);
+ using var part = new ExpandedPartStream(entry.Open(), _remainingBytes, cancellationToken);
+ using var xml = CreateReader(part);
```

解析器顺序消费条目，省去了为每个展开部件分配暂存数组和向其中复制字节的步骤。每个部件成功读完后，按 `part.BytesRead` 扣除跨部件预算；正文在通过检查后才返回给调用方。[ReadXmlAsync 源码](../Word/WordTextExtractor.cs#L118)

### 最后的换行也可能多花一份全文的内存

提取器用 `StringBuilder` 收集文字，段落末尾会追加换行。假设最终内容是 `"甲\n乙\n\t"`，想返回的是 `"甲\n乙"`，中间的换行必须留下。旧写法 `text.ToString().TrimEnd('\n', '\t')` 先生成含尾巴的完整字符串，再为裁剪后的正文生成第二个字符串。正文越长，为了去掉两个字符而复制的内容就越多。

新版先检查 builder 中已有的块。以下为源码中的循环，`offset` 是当前块起点在全文中的位置，`length` 初值为 0：

```csharp
foreach (var chunk in text.GetChunks())
{
    int last = chunk.Span.LastIndexOfAnyExcept('\n', '\t');
    if (last >= 0)
    {
        length = offset + last + 1;
    }
    offset += chunk.Length;
}
return text.ToString(0, length);
```

如果最后一块全是分隔符，`last` 为 −1，上一块算出的终点仍然有效。这样既能保留中间的换行与正文末尾的普通空格，又能直接按有效长度生成一次字符串。[裁剪实现](../Word/WordTextExtractor.cs#L78)

## PDF 快照在哪里需要独占数组

再看“从 PDF 选一页”。输入端和输出端都有整份数组，但它们的用途不同。旧输入端先循环 `ReadAsync`，把读到的字节追加到可增长的 `data`，最后执行 `return data.ToArray()`。即使调用方传来的是已知长度的普通 `MemoryStream`，也会先写暂存数组，再分配精确长度的结果数组并整份复制。

新版在能确定剩余长度时直接填最终数组：

```csharp
if (RemainingMemoryLength(input, maximumBytes) is { } remaining)
{
    var snapshot = new byte[remaining];
    await input.ReadExactlyAsync(snapshot, cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    return snapshot;
}
```

例如流长 1000 B、当前位置 200，快照只需要 800 B。它与调用方的数组独立，后续解析可以安全持有。这里特意检查类型恰为 `MemoryStream` 且剩余量在预算内：子类可以改写读取行为，不能定位的流则根本没有可靠的 `Length`。这些情况继续按剩余预算逐块读取，直到 EOF 或超限。[输入分支](../Internal/BoundedBuffer.cs#L21)

输出端的判断反过来：`Save` 已经拥有刚刚生成的数组。对选页调用来说，下游只要把有效字节写进目标流。这里用一个视图就够了：

```diff
- var bytes = output.ToArray();
+ var bytes = output.GetBuffer().AsMemory(0, checked((int)output.Length));
```

`GetBuffer()` 取得已有数组，`AsMemory(0, Length)` 携带起点和有效长度；即使数组有备用容量，写入也只覆盖真正的 PDF 字节。加密结果仍须验证后才能写出。换成 `SplitAsync`，调用方拿到的却是可以长期持有的 `byte[]` 分片，所以它在返回分片时保留了 `ToArray()`。[Save](../Pdf/PdfProcessor.Buffers.cs#L103) · [SplitAsync](../Pdf/PdfProcessor.cs#L86)

[性能基准](../../../../benchmarks/MS.Microservice.Documents.Benchmarks/Program.cs) · [验收记录](../../../../docs/validation/DOCUMENTS-RENDERING-PERF/summary.md)
