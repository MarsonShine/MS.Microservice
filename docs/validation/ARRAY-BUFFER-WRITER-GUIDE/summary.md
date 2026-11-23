# ArrayBufferWriter 指南的示例验证

2026 年 11 月 23 日，Windows x64，.NET SDK 11.0.100，从仓库根目录执行 `dotnet run --file samples/Guides/ArrayBufferWriterDemo.cs -c Release`，退出码为 0。[指南](../../basic-concept/array-buffer-writer.md)中的五组输出共 14 行，与[程序](../../../samples/Guides/ArrayBufferWriterDemo.cs)的标准输出逐行一致。

核对项包括提交前后的有效长度、复用后的旧视图和独立副本、UTF-8 短读、两种 JSON 写法的逐字节比较，以及 1024 轮同步消费视图和复制数组的分配差异。分配计数只覆盖预热后的当前线程。[测量代码](../../../samples/Guides/ArrayBufferWriterDemo.cs#L78)

Edge 实际渲染检查覆盖缓冲区示意图、API 签名表、选型表和示例输出；960 像素视口下图表均在页面范围内，源码链接与行号有效。
