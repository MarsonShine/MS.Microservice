# 文档排版与 PDF 转换（Windows，可选）

`MS.Microservice.Documents.Rendering` 用已安装的 Edge／Chrome 排版静态 HTML，用已安装的 LibreOffice 将 DOCX 转为 PDF。它是独立的可选组件，普通 [Documents](../MS.Microservice.Documents/README.md) 使用者无需携带浏览器驱动；[源码与包接入方式](../../docs/components/consumption.md)。

当前只支持 Windows，需要可用的 `dotnet` 主机，以及对应功能的排版引擎。组件不会下载或安装浏览器、LibreOffice，不使用用户登录或 Office 配置。HTML 转换要求浏览器沙箱可用。Playwright 依赖包约 203.6 MiB，本机 Windows 驱动复制约 102.2 MiB；发布时须保留驱动目录及 worker 文件。

## HTML 转 PDF

```csharp
using MS.Microservice.Documents.Rendering;

var renderer = new HtmlPdfRenderer(new()
{
    Channel = HtmlBrowserChannel.MicrosoftEdge,
    TemporaryDirectory = Path.Combine(Path.GetTempPath(), "my-app-rendering")
});
await using var output = new FileStream("report.pdf", FileMode.CreateNew);
var result = await renderer.RenderAsync(
    "<meta charset=utf-8><h1>中文报告</h1><p style='break-after:page'>第一页</p><p>第二页</p>",
    output, cancellationToken: cancellationToken);
```

`Channel` 也可选 `GoogleChrome`，或改为可信宿主配置的绝对 `ExecutablePath`；两者必须且只能提供一个。临时目录应由服务账户控制并允许创建、删除。输入是 HTML 字符串，目标从当前位置写入并保持打开；文件覆盖与原子发布由宿主负责。

额外资源通过 `HtmlPdfOptions.Assets` 显式传入，例如 `options.Assets["logo.png"]` 对应 HTML 的 `<img src="logo.png">`。值为 `HtmlPdfAsset(内容类型, 字节数组)`，支持 PNG、JPEG、静态 WebP、CSS、WOFF／WOFF2／TTF／OTF。名称区分大小写，须为单层文件名，不能是 `index.html`、`.`、`..` 或包含 URL／路径分隔符。MIME 类型大小写不敏感。

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `WidthMillimeters` / `HeightMillimeters` | `210` / `297` | 纸张宽高，范围 10–1000 mm |
| `MarginMillimeters` | `10` | 四边等宽，须为页面保留有效内容区域 |
| `PreferCssPageSize` | `false` | 允许 CSS `@page size` 优先；结果仍受尺寸上限约束 |
| `PrintBackground` | `true` | 打印背景色与背景图片 |
| `Timeout` | `30 s` | worker 处理期限，包含启动、排版、PDF 校验和 SDK 关闭；可配置至 10 分钟 |
| `MaximumHtmlBytes` | `4 MiB` | HTML 的 UTF-8 编码大小 |
| `MaximumAssets` / `MaximumAssetBytes` / `MaximumTotalAssetBytes` | `64` / `4 MiB` / `32 MiB` | 资源数量、单份与累计编码大小 |
| `MaximumImagePixels` | `20M` | 每份图片资源的像素上限 |
| `MaximumPdfBytes` / `MaximumPages` | `32 MiB` / `100` | PDF 编码大小和页数上限；页数可配置为 1–10000 |

`Maximum*` 参数位于 `HtmlPdfOptions.Limits`。超时或取消后，进程树退出和临时目录清理另有最多 **10 秒**预算；整个调用不承诺 30 秒硬上限。目标流复制遵从调用方取消令牌，其 I/O 时长由目标流决定。

### HTML 限制

- 禁用页面 JavaScript、Service Worker、下载和网络访问；只加载精确匹配 `Assets` 的额外资源。远程 URL、`file:` 和 HTML 内的 `data:` 资源不会加载，系统已安装字体仍可使用。
- 内嵌框架（iframe）内容会清空，iframe 及被阻止图片的布局盒仍可能占空间或增加空页。缺失资源、字体回退和浏览器版本差异可能改变排版，应检查输出；中文需要宿主提供相应字体。

HTML 流程、浏览器约束及验证见 [HTML 渲染设计](docs/html-rendering-design.md)。

## DOCX 转 PDF

`libreOfficeExecutablePath` 由可信宿主配置为已安装或完整便携 LibreOffice 的绝对 `program/soffice.com` 路径。仅使用 DOCX 转换时不需要安装浏览器；组件不会自动安装 LibreOffice 或修改系统注册表。

```csharp
using MS.Microservice.Documents.Rendering;

var converter = new WordPdfConverter(new()
{
    LibreOfficeExecutablePath = libreOfficeExecutablePath,
    TemporaryDirectory = Path.Combine(Path.GetTempPath(), "my-app-rendering")
});
await using var input = File.OpenRead("contract.docx");
await using var output = new FileStream("contract.pdf", FileMode.CreateNew);
var result = await converter.ConvertAsync(input, output, cancellationToken: cancellationToken);
```

支持普通、未签名、未启用保护的 Transitional DOCX，包含正文、表格、页眉、页脚、分页及现代 DrawingML 内嵌 PNG／JPEG。支持 `PAGE`、`NUMPAGES`、`SECTION`、`SECTIONPAGES` 页码类字段及常用通用格式开关；跨 run 的字段也可识别。

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `WordPdfOptions.Timeout` | `60 s` | worker 处理期限，包含 DOCX 检查、引擎启动、转换、PDF 校验和结果传输；最多 10 分钟 |
| `MaximumInputBytes` | `64 MiB` | 从输入当前位置读取的 DOCX 编码大小 |
| `MaximumEntries` | `1024` | ZIP 条目数量 |
| `MaximumExpandedBytes` | `128 MiB` | ZIP 实际展开的累计字节数 |
| `MaximumXmlCharactersPerPart` | `16M` | 每个 XML 部件的字符上限 |
| `MaximumImagePixels` | `20M` | 每幅内嵌 PNG／JPEG 的像素上限 |
| `MaximumPdfBytes` / `MaximumPages` | `32 MiB` / `100` | PDF 编码大小和页数；超限失败，不返回截断 PDF |

`Maximum*` 参数位于 `WordPdfOptions.Limits`。DOCX 检查和转换在独立进程内完成；输入源不修改，输入和目标流从当前位置使用并保持打开，不能共用同一流或底层文件。

### DOCX 限制

- 只处理可信文档。组件会拒绝宏、Strict DOCX、签名、保护、全部外部关系（包括普通超链接）、VML、矢量图片、OLE／嵌入包、altChunk 及非支持字段，但这些检查和独立进程不是文件或网络沙箱。
- 禁用宏和链接更新仍不能保证 LibreOffice 不读取宿主文件；例如本地图片外链会在该配置下被读取，因此入口会拒绝外部关系。不得把此组件当作任意不可信 Office 文件的隔离服务。
- 排版由 LibreOffice 完成，不保证与 Microsoft Word 逐像素一致。字体、引擎版本和文档特性会影响换行与分页，中文需要宿主字体，应检查输出。已用 LibreOffice 26.8.1.1 验证真实转换。
- 完整引擎目录和其运行依赖由宿主维护；不能只复制 `soffice.com`。便携分发须保留 `program`、`share`、`presets` 及许可文件，遵守 [LibreOffice 许可](https://www.libreoffice.org/licenses/)。

字段语法、包验证、进程生命周期及实际转换测试见 [DOCX 渲染设计](docs/word-rendering-design.md)。

## 部署与输出约束

- 成功前会完成 PDF 验证和自有资源清理。验证失败时不写目标；最终复制失败或取消仍可能留下部分 PDF。清理失败会保留主异常及残留目录位置。
- worker 期限之后，进程树退出和临时目录清理另有最多 **10 秒**预算；输入读取及最终目标复制遵从调用方取消令牌，其 I/O 时长由流决定。整个调用不承诺固定总时长。
- 编码、展开、像素和时间预算不是原生进程的内存或磁盘硬配额，应由宿主限制并发及系统资源。
- 不支持 Linux／macOS、单文件、裁剪或 Native AOT 部署。正常项目引用、NuGet 和目录发布会复制 worker 元数据及 `.playwright`；不要只分发一个 DLL。
