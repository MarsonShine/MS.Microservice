# 渲染进程的资源快照与 PDF 回传代码评审

一次 HTML 打印涉及调用方、父进程、.NET worker 和浏览器。对带 3.67 MB 照片的一页文档，父进程每次调用的托管分配减少约 7.36 MB。要找到这笔开销，从 `RenderAsync` 往下跟两处边界：谁会改动输入图片，谁会继续持有输出 PDF。[完整进程与分段基准](../../../../docs/validation/DOCUMENTS-RENDERING-PERF/summary.md)

## 同一份资源为什么只在父进程复制正文

> 评审位置：`HtmlPdfRenderer` 与 `HtmlRenderWorker.RunAsync` 都调用资源准备逻辑。两次调用时，`asset.Content` 指向的是同一个数组吗？

旧版统一调用 `Snapshot(...)`，每个资产都执行 `Content = asset.Content.ToArray()`。父进程收到的是调用方可以改写的 `byte[]`，渲染开始后仍要异步传输，因而必须先固定内容。worker 则从标准输入反序列化出自己的数组。再复制一份，正好把每张图片、每份 CSS 的字节重搬一遍。

```csharp
// RenderAsync：调用方拥有原数组
PrepareRequest(html, _browser, options ?? new(), copyAssetContents: true);
// worker：raw 已由 JSON 反序列化到本进程
PrepareRequest(raw.Html, raw.Browser, raw.Options, copyAssetContents: false);
// PrepareRequest 在写入新资源字典时决定是否复制正文
Content = copyAssetContents ? asset.Content.ToArray() : asset.Content
```

两个入口都校验资源路径、种类、数量和字节预算，都产生按序数比较的新字典并归一化 MIME。每个名称和 HTML 本身仍作严格 UTF-8 计数，计数时复用一个只读使用的编码实例。worker 的图片格式和像素校验仍在启动打印之前。这里的判断依据是数组的所有权；即使调用方提供的字典只读，它的值里仍可能有可变数组。[父进程调用与资源准备](../HtmlPdfRenderer.cs#L31) · [worker 调用](../HtmlRenderWorker.cs#L17)

用四份各 256 KiB 的 CSS 调用 worker 的准备方法，单次分配从约 **1050318 B 降至 920 B**，耗时从 **169.044 降至 0.709 µs**。只测这一段时，省掉的就是约 1 MiB 内容复制及编码实例分配。若走父进程入口，仍要为四份正文付出约 1 MiB 的快照成本。这个实验不包含 JSON 传输、浏览器启动和排版。

## 一样的 wire[4..] 为什么不再复制 PDF

> 评审位置：`RenderWorkerSupervisor.ReadOutputAsync`。worker 的 stdout 以四个小端字节报告页数，后面紧接 PDF；最终调用方只需要 PDF。切片到底复制多少，取决于 `wire` 的类型。

```csharp
// 旧：ReadOutputAsync 返回 byte[]
return output.ToArray();              // 复制页数头和全部 PDF
// RunCoreAsync 收到 byte[] wire，再次复制剩下的 PDF：
return (wire[4..], pages);
```

```csharp
// 新：ReadOutputAsync 返回 ReadOnlyMemory<byte>
return output.GetBuffer().AsMemory(0, (int)output.Length);
// RunCoreAsync 收到有效的内存区间
var pages = BinaryPrimitives.ReadInt32LittleEndian(wire.Span);
// 校验页数后
return (wire[4..], pages);
```

![页数头、PDF 和备用容量在同一数组中的位置，以及最终写给调用方的有效区间](assets/pdf-frame.svg)

第一次界定长度排除数组的备用容量，第二次切片跳过协议头。对 8 MiB PDF，旧版两个结果数组的数据区合计 `(4 + N) + N` 字节；新版的两个内存视图持有同一份私有数组，释放暂存流不会回收或覆盖这块数组。测接收和切片一段，分配从 **37749300 B 降至 20972024 B**。剩余开销包含管道读缓冲和暂存流扩容。备用容量会随视图活到目标流写完，所以这个分配差不能直接推算常驻内存或峰值。[收集有效前缀](../RenderWorkerSupervisor.cs#L147) · [检查页数并切片](../RenderWorkerSupervisor.cs#L103)

为什么仍然先接收完整结果？worker 可能在产生部分 stdout 后失败，父进程还需要检查退出码、清理进程树与私有目录，最后校验帧和页数。边收边直接写入调用方的流，会让失败的渲染提前留下结果。HTML 和 Word 两个入口都在监督层成功返回后才发布；目标流本身在最终写入时失败，仍可能留下部分 PDF。[Word 转换入口](../WordPdfConverter.cs) · [监督层回归](../../../test/MS.Microservice.Documents.Rendering.Tests/RenderWorkerMetadataTests.cs)

## 哪些成本仍由排版引擎承担

Word 输入和输出文件读取现在共用 Documents 的有界快照逻辑。普通 `MemoryStream` 在预算内可以按剩余长度一次读取独立数组，文件、ZIP 部件流与其他不可寻址输入继续按实际读取量检查上限。[Word 的读取入口](../WordPdfConverter.cs#L48) · [快照的适用条件](../../MS.Microservice.Documents/docs/performance-review.md#pdf-快照在哪里需要独占数组)

真实 Edge 端到端测试中，每次冷启动 worker 与浏览器，输出 1 页、3679597 B 的 PDF。耗时从 **3.72 变为 3.65 秒**，远小于只测数组复制时的相对差别。独立采样中，新版两次进程树峰值私有提交量反而较高。这里可以确认父进程少分配了内存，整棵进程树的峰值和外部引擎速度需在真实语料及并发条件下另行测量。[端到端读数与峰值采样口径](../../../../docs/validation/DOCUMENTS-RENDERING-PERF/summary.md)
