using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Documents.Rendering;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;

namespace MS.Microservice.Lab.Controller;

/// <summary>使用宿主本地浏览器或 LibreOffice 排版为 PDF。</summary>
[LabOnly]
[Route("api/lab/tools/rendering")]
public sealed class RenderingToolsController(ToolDemoFiles files, IConfiguration configuration) : ToolDemoControllerBase(files)
{
    /// <summary>将静态 HTML、CSS 和内嵌资源排版为 PDF。</summary>
    /// <remarks>提交 HTML 后下载 PDF。仅 Windows：宿主配置 ToolDemos:HtmlExecutablePath，或 HtmlBrowserChannel（默认 MicrosoftEdge），并部署 Rendering worker 与 .playwright；浏览器必须已安装，中文需宿主字体。禁用页面脚本与外部资源，只加载 Assets；不自动安装浏览器。</remarks>
    [HttpPost("html-pdf"), Consumes("application/json"), Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> HtmlToPdf([FromBody] HtmlPdfDemoRequest request)
    {
        const string key = "ToolDemos:HtmlExecutablePath / ToolDemos:HtmlBrowserChannel";
        if (!OperatingSystem.IsWindows()) throw new ToolDependencyException(key);
        var executable = ToolDemoDefaults.Value(configuration, "HtmlExecutablePath");
        HtmlBrowserChannel? channel = null;
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = null;
            if (!Enum.TryParse<HtmlBrowserChannel>(ToolDemoDefaults.Value(configuration, "HtmlBrowserChannel") ?? "MicrosoftEdge", ignoreCase: true, out var selected)
                || !Enum.IsDefined(selected)) throw new ToolDependencyException("ToolDemos:HtmlBrowserChannel");
            channel = selected;
        }
        else if (!Path.IsPathFullyQualified(executable) || !System.IO.File.Exists(executable)) throw new ToolDependencyException("ToolDemos:HtmlExecutablePath");
        var renderer = new HtmlPdfRenderer(new() { ExecutablePath = executable, Channel = channel });
        using var output = new MemoryStream();
        await renderer.RenderAsync(request.Html, output, new()
        {
            Assets = request.Assets, WidthMillimeters = request.WidthMillimeters, HeightMillimeters = request.HeightMillimeters,
            MarginMillimeters = request.MarginMillimeters, PreferCssPageSize = request.PreferCssPageSize, PrintBackground = request.PrintBackground,
            Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds),
            Limits = new() { MaximumPdfBytes = ToolDemoFiles.MaximumOutputBytes, MaximumPages = request.MaximumPages,
                MaximumAssetBytes = ToolDemoFiles.MaximumFileBytes, MaximumTotalAssetBytes = ToolDemoFiles.MaximumRequestBytes, MaximumImagePixels = 4_000_000 }
        }, Token);
        return Download(output.ToArray(), "application/pdf", "html-report.pdf");
    }

    /// <summary>将可信普通 DOCX 转换为可下载 PDF。</summary>
    /// <remarks>可先通过 documents/word/template-sample 和 word/fill 准备 DOCX。仅 Windows：自动从PATH及Program Files查找已安装的LibreOffice；非标准位置可配置ToolDemos:LibreOfficeExecutablePath为绝对program/soffice.com路径，仍需部署Rendering worker。仅接受可信文档，拒绝宏、外链、VML／矢量图片、嵌入对象及未知动态字段；这不是不可信Office文件的沙箱。</remarks>
    [HttpPost("docx-pdf"), Consumes("multipart/form-data"), Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> WordToPdf([FromForm] WordPdfDemoForm form)
    {
        const string key = "ToolDemos:LibreOfficeExecutablePath";
        if (!OperatingSystem.IsWindows()) throw new ToolDependencyException(key);
        var executable = ToolDemoDefaults.LibreOfficeExecutable(configuration);
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var output = new MemoryStream();
        var converter = new WordPdfConverter(new() { LibreOfficeExecutablePath = executable });
        await converter.ConvertAsync(input, output, new()
        {
            Timeout = TimeSpan.FromSeconds(form.TimeoutSeconds),
            Limits = new() { MaximumInputBytes = ToolDemoFiles.MaximumFileBytes, MaximumExpandedBytes = ToolDemoFiles.MaximumOutputBytes,
                MaximumXmlCharactersPerPart = 4_000_000, MaximumImagePixels = 4_000_000, MaximumPdfBytes = ToolDemoFiles.MaximumOutputBytes, MaximumPages = form.MaximumPages }
        }, Token);
        return Download(output.ToArray(), "application/pdf", "document.pdf");
    }
}
