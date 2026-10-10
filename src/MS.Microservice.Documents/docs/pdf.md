# PDF 页面、文本、渲染与叠加

`PdfProcessor` 支持检查元数据、合并、页面选择、逐页拆分、文本提取、PDFium PNG 渲染，以及全页 PNG 叠加。公开页码统一从 1 开始。

```csharp
var processor = new PdfProcessor();
await processor.SelectPagesAsync(input, output, new[] { 3, 1, 3 }, cancellationToken: token);
await foreach (var part in processor.SplitAsync(input, cancellationToken: token))
{
    // part.PageNumber 为原文档页码；part.Content 是调用方拥有的独立单页 PDF 字节。
}
```

页面选择保留请求顺序，允许重复，重复页计入输出页数预算；空选择与空合并均拒绝。合并按输入流顺序拼接页面。新文档复制原文档或首个合并文档的标题、作者、主题和关键词，保留页面内容、尺寸与旋转；不承诺恢复目录、表单关系、签名或全部文档级结构。

`InspectAsync` 返回标题、作者、加密状态及每页媒体框尺寸和显示旋转。`ExtractTextAsync` 使用 PdfPig 的内容顺序提取器返回逐页文字；PDF 本身没有可靠的纯文本阅读顺序保证，复杂布局应结合后续版面分析。只有扫描图像的页面返回空字符串，不自动调用 OCR。

`RenderPageAsync` 写出一页 PNG，`RenderPagesAsync` 顺序返回 PNG 字节与实际栅格尺寸，可显式选择顺序和重复页。渲染入口支持本组件打包的 Windows、Linux、macOS 桌面运行时。DPI 默认 150，范围 1–1200；尺寸遵循 PDFium/PDFtoImage 的可见页面、旋转与像素舍入行为。渲染包含页面批注，默认白色背景。

`AddOverlayAsync(pdf, pngOverlay, output)` 将静态 PNG 拉伸到每页的可见区域，按 CropBox 偏移和页面显示旋转映射坐标，透明区域保留底层内容。可先由 Media 的文字或 Logo 叠加接口生成目标尺寸 PNG，应用负责组合；Documents 不依赖 Media、不自行创建字体，也不修改进程全局字体解析器。

`PdfOptions.Password` 用于打开输入，同次合并的所有输入使用该密码。源加密状态由 PdfPig 读取，不能依赖解密后已清空保护设置的 PDFsharp DOM。任何 PDF 改写路径（叠加、合并、选择、拆分）遇到加密源，默认要求成对的 `OutputUserPassword`、`OutputOwnerPassword`，以 AES-256 显式加密结果；未指定密码对时拒绝输出，除非明确设置 `AllowUnencryptedOutput = true`。合并中任一源加密就适用此规则。未加密源默认生成普通 PDF，也可提供密码对加密。

输出密码须为有效 Unicode、非空，且原值及 NFKC 规范化后都不超过 127 个 UTF-8 字节，防止后端截断。PDF 标准中的密码规范化仍由后端处理。加密结果在写给调用方之前会再次解析：无密码必须被明确拒绝，配置的 user/owner 密码均须能打开且页数一致；规范化导致任一密码成为可无密码访问的空值时拒绝发布，而不维护特殊字符黑名单。加密输出因此有额外解析开销。

读取和输出校验共享密码候选规则：先尝试原字符串，只有明确密码拒绝且 NFKC 候选不同才尝试规范候选；旧加密密码的原值优先，不无条件改写。解析、检查、提取与渲染使用同一解析出的候选。后端完整 SASLprep 行为存在差异，若指定密码及 NFKC 候选无法被读取后端确认，输出会在发布前拒绝，不保证任意 Unicode 密码跨后端通用。

输出密码不从输入密码自动推断；若需要继续使用原有 user/owner 密码，应显式提供相同的一对。PNG 渲染和文本提取是明确的明文提取操作，不受 PDF 改写的输出密码策略限制。文档重写不保留原数字签名有效性。密码配置为普通类，不包含会自动打印密码的 record 字符串。

默认预算为每个输入 64 MiB、一次合并的累计编码输入 256 MiB、每个输出结果 128 MiB、最多 1000 页、栅格单边 16,384、总计 40M 像素，并限制单个 RGBA 栅格小于 2 GiB。合并按 `MaximumMergedInputBytes` 剩余额度与单文件上限中较小值读取下一个源，超限最多额外探测一个字节，不先读取完整源再检查累计大小。页数约束同时应用于源文档及选择／合并结果。编码输入有界读取，PDF 输出写入有界自有缓冲区，实际写入超过上限会在扩容前失败；栅格目标在渲染前检查，PNG 编码大小在复制到托管结果前检查。

PDF 解析使用完整 DOM，输入缓冲、解析对象、输出缓冲和编码栅格会同时占用内存；输入字节限额不是解压对象或整个进程的峰值内存保证。宿主仍需限制并发。PDFtoImage 的 PDFium 调用由其内部锁串行化，单进程不提供原生 PDF 渲染并行。

全部输入输出从当前位置读写并保持打开；支持不可定位的分段输入。同一流实例不能兼作输入与输出。校验和序列化在写入调用方输出之前完成，I/O 错误或取消仍可能留下部分输出，应由宿主通过临时文件发布。逐页枚举一次只创建当前结果，调用方应及时消费或释放枚举器；中途取消会释放自有解析资源。原生解析、保存和单次渲染的中断能力受后端限制，取消检查在阶段及页面之间执行。

依赖为 PDFsharp 6.2.4（MIT）、PdfPig 0.1.16（Apache-2.0）、PDFtoImage 5.4.0（MIT）、匹配的 SkiaSharp 与 PDFium 原生资产。原生 PDFium 及其第三方组件须保留相应许可证和 notices；bblanchon NuGet 分发的许可证声明与上游原生源码许可证分别记录。部署不调用 Codex 自带路径、浏览器或办公软件。
