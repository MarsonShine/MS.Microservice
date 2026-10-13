using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Lab.Application.Tools;

public class WatermarkDemoForm
{
    /// <summary>锚点：0左上、1上中、2右上、3左中、4正中、5右中、6左下、7下中、8右下。</summary>
    public WatermarkAnchor Anchor { get; set; } = WatermarkAnchor.BottomRight;
    /// <summary>水平边距，单位为像素。</summary>
    [Range(0, 4096)] public int MarginX { get; set; } = 16;
    /// <summary>垂直边距，单位为像素。</summary>
    [Range(0, 4096)] public int MarginY { get; set; } = 16;
    /// <summary>水印透明度，0完全透明，1完全不透明。</summary>
    [Range(0d, 1d)] public double Opacity { get; set; } = 0.35;
    /// <summary>水印顺时针旋转角度，可为负数。</summary>
    public float WatermarkAngle { get; set; }
    /// <summary>是否平铺水印。</summary>
    public bool Tile { get; set; }
    /// <summary>平铺水印之间的间距，单位为像素。</summary>
    [Range(0, 4096)] public int TileGap { get; set; } = 32;

    internal WatermarkPlacement Placement() => new()
    {
        Anchor = Anchor, MarginX = MarginX, MarginY = MarginY, Opacity = Opacity,
        RotationDegrees = WatermarkAngle, Tile = Tile, TileGap = TileGap,
        MaximumTiles = 1000, Limits = MediaToolParameters.Limits
    };
}

public class TextWatermarkDemoForm : WatermarkDemoForm
{
    /// <summary>单行水印文字，最多4096个UTF-16字符；宿主字体必须包含对应字形。</summary>
    [Required, StringLength(4096)] public string Text { get; set; } = "演示水印";
    /// <summary>字号，单位为像素。</summary>
    [Range(1d, 512d)] public float FontSize { get; set; } = 32;
    /// <summary>文字颜色，使用RRGGBB或#RRGGBB，例如FFFFFF。</summary>
    public string Color { get; set; } = "FFFFFF";

    internal TextWatermark Watermark(IConfiguration configuration)
    {
        var font = configuration["ToolDemos:FontFilePath"];
        if (string.IsNullOrWhiteSpace(font) || !System.IO.File.Exists(font))
            throw new ToolDependencyException("ToolDemos:FontFilePath");
        return new(Text) { FontFile = font, FontSize = FontSize, Color = MediaToolParameters.Color(Color) };
    }
}

public class LogoWatermarkDemoForm : WatermarkDemoForm
{
    /// <summary>Logo图片，支持PNG、JPEG、WebP；PNG透明背景可保留。</summary>
    [Required] public IFormFile? Logo { get; set; }
    /// <summary>Logo最大宽度；与LogoHeight同时填写，按比例缩放。不填则使用原始尺寸。</summary>
    [Range(1, 4096)] public int? LogoWidth { get; set; }
    /// <summary>Logo最大高度；与LogoWidth同时填写。</summary>
    [Range(1, 4096)] public int? LogoHeight { get; set; }

    internal ImageSize? LogoSize() => MediaToolParameters.Size(LogoWidth, LogoHeight);
}

internal static class MediaToolParameters
{
    internal static readonly ImageLimits Limits = new()
    {
        MaximumDimension = 4096, MaximumPixels = 4_000_000,
        MaximumEncodedBytes = ToolDemoFiles.MaximumFileBytes
    };

    internal static ImageSize? Size(int? width, int? height)
    {
        if (width.HasValue != height.HasValue)
            throw new ArgumentException("宽度和高度必须同时填写。");
        return width is { } w ? new(w, height!.Value) : null;
    }

    internal static ImageCrop? Crop(int? x, int? y, int? width, int? height)
    {
        if (x is null && y is null && width is null && height is null) return null;
        if (x is null || y is null || width is null || height is null)
            throw new ArgumentException("裁剪坐标和宽高必须全部填写。");
        return new(x.Value, y.Value, width.Value, height.Value);
    }

    internal static ImageColor Color(string? value)
    {
        var hex = value is { Length: 7 } && value[0] == '#' ? value[1..] : value;
        if (hex is null || hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("颜色必须为RRGGBB或#RRGGBB。");
        return new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    internal static (string Mime, string Extension) ImageOutput(ImageOutputFormat format) => format switch
    {
        ImageOutputFormat.Png => ("image/png", ".png"), ImageOutputFormat.Jpeg => ("image/jpeg", ".jpg"),
        ImageOutputFormat.Webp => ("image/webp", ".webp"), _ => throw new ArgumentException("不支持的图片输出格式。")
    };

    internal static TimeSpan Time(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentException("时间必须为非负有限秒数。");
        try { return TimeSpan.FromSeconds(seconds); }
        catch (OverflowException error) { throw new ArgumentException("时间超过可表示范围。", error); }
    }
}
