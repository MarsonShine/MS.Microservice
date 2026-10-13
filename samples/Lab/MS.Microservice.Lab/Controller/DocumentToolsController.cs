using System.IO.Compression;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Documents.Pdf;
using MS.Microservice.Documents.Word;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;

namespace MS.Microservice.Lab.Controller;

/// <summary>PDF 处理与 Word 内容控件模板填充示例。</summary>
[LabOnly]
[Route("api/lab/tools/documents")]
public sealed class DocumentToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    private const string PdfMime = "application/pdf";
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>读取 PDF 元数据、页数和页面尺寸。</summary>
    /// <remarks>上传 PDF，返回标题、作者、加密状态与从 1 开始的页面列表。加密文件填写 Password，不需要安装 Office。</remarks>
    [HttpPost("pdf/info"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType(typeof(PdfDocumentInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> PdfInfo([FromForm] PdfFileForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        return Ok(await new PdfProcessor().InspectAsync(input, form.ToOptions(), Token));
    }

    /// <summary>按页提取 PDF 的文字层。</summary>
    /// <remarks>返回页码和文本 JSON；扫描件没有文字层时可能为空，此接口不执行 OCR。加密源填写 Password，提取结果为普通文本。</remarks>
    [HttpPost("pdf/text"), Consumes("multipart/form-data"), Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<PdfPageText>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PdfText([FromForm] PdfFileForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        return Ok(await new PdfProcessor().ExtractTextAsync(input, form.ToOptions(), Token));
    }

    /// <summary>按页码顺序选页并生成新 PDF。</summary>
    /// <remarks>Pages 从 1 开始，可重复和重排，例如 3、1、3。下载新 PDF；加密源须成对设置输出密码或明确允许清除加密。</remarks>
    [HttpPost("pdf/select"), Consumes("multipart/form-data"), Produces(PdfMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectPdf([FromForm] PdfSelectForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var output = new MemoryStream();
        await new PdfProcessor().SelectPagesAsync(input, output, form.Pages, form.ToOptions(), Token);
        return Download(output.ToArray(), PdfMime, "selected.pdf");
    }

    /// <summary>按上传顺序合并多份 PDF。</summary>
    /// <remarks>选择 2–8 份 PDF，下载 merged.pdf。加密输入共用 Password；任何源加密时须设置输出密码对或明确允许明文输出。</remarks>
    [HttpPost("pdf/merge"), Consumes("multipart/form-data"), Produces(PdfMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> MergePdf([FromForm] PdfMergeForm form)
    {
        var inputs = new List<MemoryStream>();
        try
        {
            foreach (var file in form.Files) inputs.Add(new MemoryStream(await Files.ReadAsync(file, Token), writable: false));
            using var output = new MemoryStream();
            await new PdfProcessor().MergeAsync(inputs, output, form.ToOptions(), Token);
            return Download(output.ToArray(), PdfMime, "merged.pdf");
        }
        finally { foreach (var input in inputs) input.Dispose(); }
    }

    /// <summary>将 PDF 拆为单页 PDF，并打包下载。</summary>
    /// <remarks>每页对应 ZIP 内的 page-0001.pdf 等文件。最多 100 页，拆分产物累计最多 64 MiB；加密源仍需明确输出加密选择。</remarks>
    [HttpPost("pdf/split"), Consumes("multipart/form-data"), Produces("application/zip")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SplitPdf([FromForm] PdfRewriteForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            long total = 0;
            await foreach (var part in new PdfProcessor().SplitAsync(input, form.ToOptions(), Token))
            {
                if (part.Content.Length > ToolDemoFiles.MaximumOutputBytes - total) throw new ToolUploadException(413, "单页 PDF 累计大小超过 64 MiB，请先选取较少页面。");
                total += part.Content.Length;
                await using var entry = archive.CreateEntry($"page-{part.PageNumber:D4}.pdf").Open();
                await entry.WriteAsync(part.Content, Token);
            }
        }
        return Download(output.ToArray(), "application/zip", "pages.zip");
    }

    /// <summary>将 PDF 指定页面渲染为 PNG。</summary>
    /// <remarks>上传 PDF 并填写一基页码和 DPI，下载页面图片。需要部署包中的 PDFium 原生运行库，无需 Office；PNG 为明文提取结果。</remarks>
    [HttpPost("pdf/render-page"), Consumes("multipart/form-data"), Produces("image/png")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RenderPdfPage([FromForm] PdfRasterForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var output = new MemoryStream();
        await new PdfProcessor().RenderPageAsync(input, form.Page, output, form.Dpi, form.ToOptions(), Token);
        return Download(output.ToArray(), "image/png", $"page-{form.Page}.png");
    }

    /// <summary>将透明 PNG 水印叠加到 PDF 每一页。</summary>
    /// <remarks>同时上传 PDF 和静态 PNG，PNG 铺满每页的显示区域，透明度由图片自身决定。下载新 PDF；加密源须明确输出加密选择。</remarks>
    [HttpPost("pdf/overlay"), Consumes("multipart/form-data"), Produces(PdfMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> OverlayPdf([FromForm] PdfOverlayForm form)
    {
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var overlay = new MemoryStream(await Files.ReadAsync(form.Overlay, Token, UploadFileKind.Image), writable: false);
        using var output = new MemoryStream();
        await new PdfProcessor().AddOverlayAsync(input, overlay, output, form.ToOptions(), Token);
        return Download(output.ToArray(), PdfMime, "watermarked.pdf");
    }

    /// <summary>下载带 CustomerName、Address 内容控件的 Word 模板。</summary>
    /// <remarks>下载 DOCX 后使用 word/fill，默认 ValuesJson 即可填充这两个 Tag。模板生成不需要安装 Word 或 LibreOffice。</remarks>
    [HttpGet("word/template-sample"), Produces(DocxMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public IActionResult WordTemplateSample()
    {
        using var output = new MemoryStream();
        using (var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("工具演示合同"))),
                new Paragraph(new Run(new Text("客户：")), Field("CustomerName", "请输入客户名称")),
                new Paragraph(new Run(new Text("地址：")), Field("Address", "请输入地址")),
                new SectionProperties(new PageSize { Width = 11906, Height = 16838 })));
        }
        return Download(output.ToArray(), DocxMime, "contract-template.docx");
    }

    /// <summary>按 Tag 字典填充 Word 内容控件模板。</summary>
    /// <remarks>上传 word/template-sample 或自己的普通未签名、未保护 DOCX，将 Tag 与文本 JSON 填入 ValuesJson，下载 filled.docx。无需 Office；需要 PDF 时再调用 rendering/docx-pdf。</remarks>
    [HttpPost("word/fill"), Consumes("multipart/form-data"), Produces(DocxMime)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> FillWord([FromForm] WordTemplateFillForm form)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(form.ValuesJson) ?? throw new ArgumentException("ValuesJson 必须为 Tag 到文本的 JSON 对象。");
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var output = new MemoryStream();
        await new WordTemplateProcessor().FillAsync(input, output, values, new()
        {
            MissingValue = form.MissingValue,
            Limits = new() { MaximumInputBytes = ToolDemoFiles.MaximumFileBytes, MaximumOutputBytes = ToolDemoFiles.MaximumOutputBytes,
                MaximumExpandedBytes = ToolDemoFiles.MaximumOutputBytes, MaximumXmlCharactersPerPart = 4_000_000, MaximumReplacementCharacters = 1_000_000 }
        }, Token);
        return Download(output.ToArray(), DocxMime, "filled.docx");
    }

    private static SdtRun Field(string tag, string placeholder) => new(new SdtProperties(new Tag { Val = tag }, new SdtContentText()),
        new SdtContentRun(new Run(new Text(placeholder))));
}
