# 文档处理

`MS.Microservice.Documents` 提供 PDF 页面处理、文本提取、PNG 渲染，以及 DOCX 文本提取和内容控件模板填充。引用项目或包即可使用；[源码与包接入方式](../../docs/components/consumption.md)。

HTML 转 PDF 位于可选的 [Documents.Rendering](../MS.Microservice.Documents.Rendering/README.md) 组件，目前支持 Windows；普通 PDF／DOCX 使用者无需引用它。

| 功能 | 方法 | 结果 |
| --- | --- | --- |
| 元数据 | `InspectAsync` | 标题、作者、加密状态、页尺寸与旋转 |
| 选页／重排 | `SelectPagesAsync` | 按指定顺序输出 PDF，允许重复页 |
| 合并 | `MergeAsync` | 按输入顺序合并页面 |
| 拆分 | `SplitAsync` | 逐页返回独立 PDF 字节 |
| 文字提取 | `ExtractTextAsync` | 逐页文字；扫描图像页面返回空文本 |
| PNG 渲染 | `RenderPageAsync` / `RenderPagesAsync` | 单页或逐页图片 |
| 图片水印 | `AddOverlayAsync` | 将透明 PNG 铺到每页可见区域 |

## 选择页面

```csharp
using MS.Microservice.Documents.Pdf;

var processor = new PdfProcessor();
await using var input = File.OpenRead("input.pdf");
await using var output = new FileStream("selected.pdf", FileMode.CreateNew);
await processor.SelectPagesAsync(input, output, [1], cancellationToken: cancellationToken);
```

页码从 **1** 开始。所有流从当前位置读写，组件保持调用方流打开，输入与输出必须不同。文件覆盖和原子发布由宿主负责，示例用 `CreateNew` 保护既有文件。

## 参数与限制

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `dpi` | `150` | PNG 渲染分辨率，范围 1–1200 |
| `PdfOptions.Password` | 无 | 输入密码；同一次合并共用此密码 |
| `OutputUserPassword` / `OutputOwnerPassword` | 无 | 成对提供时输出 AES-256 加密 PDF |
| `AllowUnencryptedOutput` | `false` | 显式允许将加密源改写为明文 PDF |
| `MaximumInputBytes` | `64 MiB` | 每份输入的编码大小上限 |
| `MaximumMergedInputBytes` | `256 MiB` | 一次合并的累计输入上限 |
| `MaximumOutputBytes` | `128 MiB` | 每个输出结果的编码大小上限 |
| `MaximumPages` | `1000` | 源文档与选页／合并结果的页数上限 |
| `MaximumRasterDimension` / `MaximumRasterPixels` | `16384` / `40M` | 渲染及叠加图片的栅格上限 |

加密源的合并、选择、拆分和叠加默认要求输出密码对，避免无意生成明文；也可明确选择 `AllowUnencryptedOutput = true`。输出密码不会从输入密码推断，须为非空有效 Unicode，原值与 NFKC 规范化后均不超过 127 个 UTF-8 字节。部分密码的跨后端规范化不兼容时会明确拒绝输出。

- PDF 会完整载入内存，应限制文件大小和并发；取消无法强制中断正在执行的原生解析或渲染。
- 文字提取不自动执行 OCR；复杂版面不保证自然阅读顺序。
- 改写结果不保留数字签名有效性，也不保证全部目录、表单关系和文档级结构。
- PNG 渲染和文字提取输出明文。渲染需可用的 PDFium／Skia 原生运行时，支持 Windows、Linux、macOS 桌面部署，目标系统需验证对应依赖。
- 透明 PNG 会拉伸到整页可见区域。文字／Logo 可先用 [Media 水印接口](../../MS.Microservice.Media/README.md)生成透明叠加图，再传给 `AddOverlayAsync`。

实现、资源生命周期和密码验证规则见 [PDF 设计](docs/pdf-design.md)。

## 提取 Word 文本

```csharp
using MS.Microservice.Documents.Word;

await using var input = File.OpenRead("report.docx");
var text = await new WordTextExtractor().ExtractAsync(input,
    new WordTextExtractionOptions { IncludeHeadersAndFooters = true }, cancellationToken);
```

支持 Transitional／Strict DOCX，按正文（含表格）、页眉、页脚的顺序返回纯文本；每个引用部件只提取一次，页眉／页脚分别按部件路径排序。保留段落换行、单元格及文字内制表符，适合建立全文索引。设 `IncludeHeadersAndFooters = false` 可仅提取正文。

| `WordTextExtractionOptions` 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `MaximumInputBytes` | `64 MiB` | 从当前位置读取的 ZIP 编码大小 |
| `MaximumEntries` | `1024` | ZIP 条目数量 |
| `MaximumExpandedXmlBytes` | `128 MiB` | 本次读取的元数据、正文、页眉和页脚 XML 累计展开字节 |
| `MaximumXmlCharactersPerPart` | `16M` | 单个读取 XML 的字符数 |
| `MaximumTextCharacters` | `16M` | 返回文本及生成分隔符的 UTF-16 字符数 |

组件保持输入流打开，不修改包，不启动 Office，不读取外部链接。签名或编辑保护不阻止只读提取；密码加密文件、旧版 `.doc`、宏文档和模板不支持。图片不执行 OCR，脚注、尾注、批注及嵌入文档不提取；域只读取已保存的文字结果，删除修订不纳入文本。结果不含页码，不保证分栏、文本框等复杂版面的视觉阅读顺序。

读取策略与预算见 [Word 文本提取设计](docs/word-template-design.md#只读文本提取)。

## 填充 Word 模板

在 Word 的「开发工具」中插入纯文本或富文本内容控件，并将控件属性的 **标记（Tag）** 设为字段名。正文、表格单元格、页眉和页脚中的控件都可以填充；块级控件只支持单个段落。

```csharp
using MS.Microservice.Documents.Word;

var templates = new WordTemplateProcessor();
await using var template = File.OpenRead("contract-template.docx");
await using var filled = new FileStream("contract.docx", FileMode.CreateNew);
var result = await templates.FillAsync(template, filled,
    new Dictionary<string, string?> { ["CustomerName"] = "张三", ["Address"] = "上海市" },
    cancellationToken: cancellationToken);
```

Tag 区分大小写，同名控件全部填充。`null` 替换为空文本，无标记控件保持原样，多余字典键忽略。新文本沿用控件首个 run 的格式；控件外的文字、样式与非目标内容保持原样。换行和制表符会写成 Word 节点，单行纯文本控件拒绝换行。

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `WordTemplateOptions.MissingValue` | `Throw` | 缺少带 Tag 控件的值时失败；`KeepOriginal` 保留该控件 |
| `MaximumInputBytes` | `64 MiB` | 输入 DOCX 编码大小 |
| `MaximumOutputBytes` | `128 MiB` | 自有工作副本及输出编码大小，须能容纳输入副本 |
| `MaximumExpandedBytes` | `128 MiB` | ZIP 条目展开后的累计字节数，输入和输出均检查 |
| `MaximumXmlCharactersPerPart` | `16M` | 每个 XML 部件的字符上限 |
| `MaximumEntries` | `1024` | ZIP 条目数量上限 |
| `MaximumReplacementCharacters` | `16M` | 实际替换文本的 UTF-16 字符总数，重复控件分别计入 |

后六项位于 `WordTemplateOptions.Limits`。组件仅支持未签名、未启用文档保护的 Transitional DOCX。不能填充嵌套控件、数据绑定、图片、日期、选择框、内容锁定控件，或包含域、绘图等复杂内容的目标控件。脚注、尾注和文本框不作为模板填充入口。需要将填充后的 DOCX 排版为 PDF 时，可使用 [可选 Rendering 组件](../MS.Microservice.Documents.Rendering/README.md#docx-转-pdf)，该功能需要宿主提供 LibreOffice。

输入从当前位置读取，调用方流保持打开。先完成自有副本及验证，再从目标当前位置写入；复制期间 I/O 错误或取消仍可能留下部分输出，文件原子发布由宿主负责。输入输出不能共用同一流或底层文件。DOCX 会载入内存，应限制文件大小与并发；XML 校验不保证 Word 的分页或视觉布局。

控件替换规则、包验证和资源边界见 [Word 模板设计](docs/word-template-design.md)。
