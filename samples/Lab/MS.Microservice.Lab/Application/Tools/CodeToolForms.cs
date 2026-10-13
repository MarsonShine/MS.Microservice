using System.ComponentModel.DataAnnotations;
using MS.Microservice.Media.Codes;

namespace MS.Microservice.Lab.Application.Tools;

public sealed class CodeGenerateRequest
{
    /// <summary>码内容，最多4096个UTF-16字符。数字商品码需满足格式长度及校验位要求。</summary>
    [Required, StringLength(4096)] public string Text { get; set; } = "https://example.com";
    /// <summary>格式数值：0=Qr、1=Code128、2=Ean13、3=Ean8、4=UpcA、5=Code39、6=DataMatrix、7=Pdf417。</summary>
    public CodeFormat Format { get; set; } = CodeFormat.Qr;
    /// <summary>输出PNG宽度，像素；尺寸不足以容纳完整码时返回错误。</summary>
    [Range(1, 4096)] public int Width { get; set; } = 256;
    /// <summary>输出PNG高度，像素；宽高乘积不超过400万。</summary>
    [Range(1, 4096)] public int Height { get; set; } = 256;
    /// <summary>格式对应的空白边距；省略使用引擎默认值，DataMatrix默认1个模块。</summary>
    [Range(0, 2047)] public int? Margin { get; set; }
    /// <summary>QR纠错数值：0=Low、1=Medium、2=Quartile、3=High；仅QR使用。</summary>
    public QrErrorCorrection QrErrorCorrection { get; set; } = QrErrorCorrection.Medium;
    /// <summary>前景色RRGGBB或#RRGGBB，必须比背景色暗。</summary>
    public string Foreground { get; set; } = "000000";
    /// <summary>背景色RRGGBB或#RRGGBB，必须比前景色亮。</summary>
    public string Background { get; set; } = "FFFFFF";
}

public sealed class CodeDecodeForm
{
    /// <summary>包含二维码或条码的PNG、JPEG、WebP图片，最多20MiB、400万像素。</summary>
    [Required] public IFormFile? File { get; set; }
    /// <summary>可选格式列表，省略则全部尝试：0=Qr、1=Code128、2=Ean13、3=Ean8、4=UpcA、5=Code39、6=DataMatrix、7=Pdf417。</summary>
    public List<CodeFormat>? Formats { get; set; }
    /// <summary>是否尝试90度倍数旋转。</summary>
    public bool AutoRotate { get; set; } = true;
    /// <summary>是否进行更充分的识别尝试。</summary>
    public bool TryHarder { get; set; } = true;
    /// <summary>是否同时尝试反色识别。</summary>
    public bool TryInverted { get; set; } = true;
}
