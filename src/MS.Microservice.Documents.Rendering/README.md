# HTML 转 PDF（Windows，可选）

`MS.Microservice.Documents.Rendering` 用已安装的 Edge／Chrome 将静态 HTML 和 CSS 排版为 PDF，支持中文、图片、字体和 CSS 分页。它是独立的可选组件，普通 [Documents](../MS.Microservice.Documents/README.md) 使用者无需携带浏览器驱动；[源码与包接入方式](../../docs/components/consumption.md)。

当前只支持 Windows，需要可用的 `dotnet` 主机和已安装的 Chromium 浏览器。组件不会下载或安装浏览器，不使用用户登录配置，浏览器沙箱必须可用。Playwright 依赖包约 203.6 MiB，本机 Windows 驱动复制约 102.2 MiB；发布时须保留驱动目录及 worker 文件。

## 使用

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

## 必要限制

- 禁用页面 JavaScript、Service Worker、下载和网络访问；只加载精确匹配 `Assets` 的额外资源。远程 URL、`file:` 和 HTML 内的 `data:` 资源不会加载，系统已安装字体仍可使用。
- 内嵌框架（iframe）内容会清空，iframe 及被阻止图片的布局盒仍可能占空间或增加空页。缺失资源、字体回退和浏览器版本差异可能改变排版，应检查输出；中文需要宿主提供相应字体。
- 成功前会完成 PDF 验证和自有资源清理。验证失败时不写目标；最终复制失败或取消仍可能留下部分 PDF。清理失败会保留主异常及残留目录位置。
- 编码、像素与时间预算不是浏览器进程的内存硬配额，应由宿主限制并发及系统资源。
- 不支持 Linux／macOS、单文件、裁剪或 Native AOT 部署，也不提供 Word 转 PDF。正常项目引用、NuGet 和目录发布会复制 worker 元数据及 `.playwright`；不要只分发一个 DLL。

流程、进程所有权、打包规则和验证证据见 [渲染设计](docs/html-rendering-design.md)。
