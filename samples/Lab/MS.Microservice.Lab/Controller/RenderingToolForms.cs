using System.ComponentModel.DataAnnotations;
using MS.Microservice.Documents.Rendering;

namespace MS.Microservice.Lab.Controller;

/// <summary>静态 HTML 排版为 PDF 的参数。</summary>
public sealed class HtmlPdfDemoRequest
{
    /// <summary>静态 HTML，最多 200000 字符；默认包含两页中文报告。</summary>
    [Required, StringLength(200000)] public string Html { get; set; } = "<meta charset='utf-8'><h1>中文报告</h1><p style='break-after:page'>第一页</p><p>第二页</p>";
    /// <summary>可选内嵌资源，以单层文件名为键；值包含 ContentType 和 Base64 编码的 Content，HTML 使用相对文件名引用。</summary>
    public Dictionary<string, HtmlPdfAsset> Assets { get; set; } = [];
    /// <summary>纸张宽度，单位毫米，默认 210。</summary>
    [Range(10d, 1000d)] public double WidthMillimeters { get; set; } = 210;
    /// <summary>纸张高度，单位毫米，默认 297。</summary>
    [Range(10d, 1000d)] public double HeightMillimeters { get; set; } = 297;
    /// <summary>四边页边距，单位毫米，默认 10。</summary>
    [Range(0d, 499d)] public double MarginMillimeters { get; set; } = 10;
    /// <summary>是否优先采用 CSS @page 尺寸，默认 false。</summary>
    public bool PreferCssPageSize { get; set; }
    /// <summary>是否打印背景，默认 true。</summary>
    public bool PrintBackground { get; set; } = true;
    /// <summary>最大 PDF 页数，默认 20；超过时失败，不截断。</summary>
    [Range(1, 100)] public int MaximumPages { get; set; } = 20;
    /// <summary>worker 处理期限（秒），默认 30；回收进程另有最多 10 秒预算。</summary>
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>用 LibreOffice 将可信普通 DOCX 排版为 PDF。</summary>
public sealed class WordPdfDemoForm
{
    /// <summary>可信、未签名且未保护的普通 DOCX，最多 20 MiB；可上传 documents/word/fill 的结果。</summary>
    [Required] public IFormFile File { get; set; } = null!;
    /// <summary>最大输出页数，默认 20；超过时失败，不截断。</summary>
    [Range(1, 100)] public int MaximumPages { get; set; } = 20;
    /// <summary>worker 处理期限（秒），默认 60；回收进程另有最多 10 秒预算。</summary>
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 60;
}
