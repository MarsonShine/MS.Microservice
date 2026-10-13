using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.Core.Serialization;
using MS.Microservice.Core.Text;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;

namespace MS.Microservice.Lab.Controller;

public sealed class TextComparisonRequest
{
    /// <summary>原文本，保留空白和换行；最多 256,000 个 UTF-16 单元。</summary>
    [Required(AllowEmptyStrings = true), StringLength(256_000), DefaultValue("第一行\n原内容\n")]
    public string? Original { get; set; }
    /// <summary>新文本，按完整行精确比较；最多 256,000 个 UTF-16 单元。</summary>
    [Required(AllowEmptyStrings = true), StringLength(256_000), DefaultValue("第一行\n新内容\n")]
    public string? Updated { get; set; }
}

public sealed class JsonFormatRequest
{
    /// <summary>一个完整 JSON 值的文本；最多 256,000 个 UTF-16 单元。</summary>
    [Required, StringLength(256_000), DefaultValue("{\"name\":\"示例\",\"enabled\":true}")]
    public string? Json { get; set; }
    /// <summary>true 输出缩进，false 压缩空白；默认 true。</summary>
    public bool Indented { get; set; } = true;
}

public enum DemoTextEncoding { Utf8, Utf16LittleEndian, Utf16BigEndian, Latin1 }

public sealed class TextEncodingForm
{
    /// <summary>需要转换的文本文件，最多 20 MiB。</summary>
    [Required]
    public IFormFile? File { get; set; }
    /// <summary>源编码：0=UTF-8、1=UTF-16LE、2=UTF-16BE、3=Latin1；默认 0，不猜测编码。</summary>
    public DemoTextEncoding SourceEncoding { get; set; } = DemoTextEncoding.Utf8;
    /// <summary>目标编码：0=UTF-8、1=UTF-16LE、2=UTF-16BE、3=Latin1；默认 1。</summary>
    public DemoTextEncoding TargetEncoding { get; set; } = DemoTextEncoding.Utf16LittleEndian;
    /// <summary>源编码是否声明 BOM，默认 false；仅消费与该编码匹配的 BOM。</summary>
    public bool SourceBom { get; set; }
    /// <summary>目标是否写入 BOM，默认 true；Latin1 不支持 BOM。</summary>
    public bool TargetBom { get; set; } = true;
}

[LabOnly]
[Route("api/lab/tools/text")]
[Tags("工具示例：文本与 JSON")]
public sealed class TextToolsController(ToolDemoFiles files) : ToolDemoControllerBase(files)
{
    /// <summary>比较两份文本，返回逐行的保留、删除和新增内容。</summary>
    /// <remarks>提交 original 和 updated，例如两份配置文本。返回 kind：0=保留、1=新增、2=删除；替换表示删除和新增。比较保留原 CR/LF/CRLF、空白及大小写。工作预算固定为 4,000,000，超额返回参数错误；不提供模糊匹配或输入长度之外的质量判断。</remarks>
    [HttpPost("compare"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType<IReadOnlyList<TextDiffLine>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public ActionResult<IReadOnlyList<TextDiffLine>> Compare(TextComparisonRequest request)
    {
        _ = Owner;
        return Ok(TextDiff.Compare(request.Original!, request.Updated!));
    }

    /// <summary>用指定编码转换文本文件，下载转换后的字节。</summary>
    /// <remarks>multipart 上传 File 并选择 SourceEncoding、TargetEncoding 和 BOM 选项。非法源字节或目标编码无法表示的字符会失败，不替换为问号；Latin1 无 BOM。接口只演示编码转换，不修改上传原件，不自动识别编码。</remarks>
    [HttpPost("encoding"), Consumes("multipart/form-data"), Produces("application/octet-stream")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<IActionResult> ConvertEncoding([FromForm] TextEncodingForm form)
    {
        _ = Owner;
        var sourceEncoding = EncodingFor(form.SourceEncoding, form.SourceBom);
        var targetEncoding = EncodingFor(form.TargetEncoding, form.TargetBom);
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token), writable: false);
        using var output = new MemoryStream();
        await TextEncodingConverter.ConvertAsync(input, output, sourceEncoding, targetEncoding, Token);
        if (output.Length > ToolDemoFiles.MaximumOutputBytes) throw new InvalidDataException("转换结果超过下载大小限制。");
        return Download(output.ToArray(), "application/octet-stream", "converted.txt");
    }

    /// <summary>格式化或压缩一个 JSON 值，返回实际 JSON 内容。</summary>
    /// <remarks>在 json 字段中提交待处理文本，indented 默认 true。保持属性顺序、重复属性和数字 token；字符串转义可能改变。只接受一个合法 JSON 值，不接受注释、尾逗号或多个值，也不应用业务对象命名策略。</remarks>
    [HttpPost("json"), Consumes("application/json"), Produces("application/json")]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public IActionResult FormatJson(JsonFormatRequest request)
    {
        _ = Owner;
        return Content(JsonText.Format(request.Json!, request.Indented), "application/json", Encoding.UTF8);
    }

    private static Encoding EncodingFor(DemoTextEncoding encoding, bool bom) => encoding switch
    {
        DemoTextEncoding.Utf8 => new UTF8Encoding(bom, true),
        DemoTextEncoding.Utf16LittleEndian => new UnicodeEncoding(false, bom, true),
        DemoTextEncoding.Utf16BigEndian => new UnicodeEncoding(true, bom, true),
        DemoTextEncoding.Latin1 when !bom => Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new ArgumentException("请选择受支持的编码；Latin1 不支持 BOM。")
    };
}
