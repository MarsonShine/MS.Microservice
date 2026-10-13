using System.ComponentModel.DataAnnotations;
using MS.Microservice.Documents.Pdf;
using MS.Microservice.Documents.Word;
using MS.Microservice.Lab.Application.Tools;

namespace MS.Microservice.Lab.Controller;

/// <summary>PDF 读取参数。</summary>
public abstract class PdfReadSettingsForm
{
    /// <summary>加密源 PDF 的打开密码；未加密文件省略。</summary>
    [DataType(DataType.Password)] public string? Password { get; set; }

    internal PdfOptions ToOptions()
    {
        var rewrite = this as PdfWriteSettingsForm;
        return new()
        {
            Password = Password, OutputUserPassword = rewrite?.OutputUserPassword, OutputOwnerPassword = rewrite?.OutputOwnerPassword,
            AllowUnencryptedOutput = rewrite?.AllowUnencryptedOutput ?? false,
            Limits = new()
            {
                MaximumInputBytes = ToolDemoFiles.MaximumFileBytes, MaximumMergedInputBytes = ToolDemoFiles.MaximumRequestBytes,
                MaximumOutputBytes = ToolDemoFiles.MaximumOutputBytes, MaximumPages = 100, MaximumRasterDimension = 4096, MaximumRasterPixels = 4_000_000
            }
        };
    }
}

/// <summary>PDF 改写时的输出加密选择。</summary>
public abstract class PdfWriteSettingsForm : PdfReadSettingsForm
{
    /// <summary>输出阅读密码；与 OutputOwnerPassword 成对填写。加密源默认须设置新密码或显式允许明文输出。</summary>
    [DataType(DataType.Password)] public string? OutputUserPassword { get; set; }
    /// <summary>输出所有者密码；与 OutputUserPassword 成对填写。</summary>
    [DataType(DataType.Password)] public string? OutputOwnerPassword { get; set; }
    /// <summary>是否明确允许将加密输入改写为未加密 PDF，默认 false。</summary>
    public bool AllowUnencryptedOutput { get; set; }
}

/// <summary>单个 PDF 读取请求。</summary>
public class PdfFileForm : PdfReadSettingsForm
{
    /// <summary>必填 PDF，最多 20 MiB、100 页。</summary>
    [Required] public IFormFile File { get; set; } = null!;
}

/// <summary>单个 PDF 改写请求。</summary>
public class PdfRewriteForm : PdfWriteSettingsForm
{
    /// <summary>必填 PDF，最多 20 MiB、100 页。</summary>
    [Required] public IFormFile File { get; set; } = null!;
}

/// <summary>按指定顺序选取 PDF 页面。</summary>
public sealed class PdfSelectForm : PdfRewriteForm
{
    /// <summary>从 1 开始的页码列表，默认 [1]；保留顺序，可重复，最多 100 项。</summary>
    [Required, MinLength(1), MaxLength(100)] public List<int> Pages { get; set; } = [1];
}

/// <summary>合并多个 PDF。</summary>
public sealed class PdfMergeForm : PdfWriteSettingsForm
{
    /// <summary>按提交顺序合并 2–8 份 PDF，累计受 25 MiB 请求限制；加密输入须共用 Password。</summary>
    [Required, MinLength(2), MaxLength(8)] public List<IFormFile> Files { get; set; } = [];
}

/// <summary>将 PDF 单页渲染为 PNG。</summary>
public sealed class PdfRasterForm : PdfFileForm
{
    /// <summary>从 1 开始的页码，默认 1。</summary>
    [Range(1, 100)] public int Page { get; set; } = 1;
    /// <summary>分辨率，默认 150 DPI；结果仍须不超过 4096 边长和 4M 像素。</summary>
    [Range(1, 1200)] public int Dpi { get; set; } = 150;
}

/// <summary>向 PDF 每页叠加一张 PNG。</summary>
public sealed class PdfOverlayForm : PdfRewriteForm
{
    /// <summary>必填静态 PNG，按每页显示区域铺满；建议使用带透明背景的完整页面水印。</summary>
    [Required] public IFormFile Overlay { get; set; } = null!;
}

/// <summary>填充带 Tag 内容控件的 Word 模板。</summary>
public sealed class WordTemplateFillForm
{
    /// <summary>必填 DOCX，可先下载 word/template-sample。</summary>
    [Required] public IFormFile File { get; set; } = null!;
    /// <summary>Tag 到文本的 JSON 字典，区分大小写；默认对应示例模板的两个字段。</summary>
    [Required, StringLength(1_000_000)] public string ValuesJson { get; set; } = "{\"CustomerName\":\"张三\",\"Address\":\"上海市\"}";
    /// <summary>缺少 Tag 值时的处理方式，默认 Throw；KeepOriginal 保留原内容。</summary>
    [EnumDataType(typeof(MissingWordTemplateValue))] public MissingWordTemplateValue MissingValue { get; set; } = MissingWordTemplateValue.Throw;
}
