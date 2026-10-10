# 文档处理

`MS.Microservice.Documents` 提供 PDF 页面处理、文本提取和 PNG 渲染。引用项目或包即可使用；[源码与包接入方式](../../docs/components/consumption.md)。

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
