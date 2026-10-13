using System.ComponentModel.DataAnnotations;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Lab.Application.Tools;

public sealed class ImageTransformForm
{
    /// <summary>待处理图片，支持PNG、JPEG、WebP，最多20MiB。</summary>
    [Required] public IFormFile? File { get; set; }
    /// <summary>输出格式：0=Png、1=Jpeg、2=Webp。</summary>
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;
    /// <summary>JPEG/WebP编码质量，0到100；PNG不使用有损质量。</summary>
    [Range(0, 100)] public int Quality { get; set; } = 90;
    /// <summary>目标宽度；与Height同时填写。输出最多4096边长、400万像素。</summary>
    [Range(1, 4096)] public int? Width { get; set; }
    /// <summary>目标高度；与Width同时填写。</summary>
    [Range(1, 4096)] public int? Height { get; set; }
    /// <summary>缩放模式：0=Contain等比包含（实际尺寸可能较小）、1=Cover等比裁满、2=Stretch拉伸。</summary>
    public ImageResizeMode ResizeMode { get; set; } = ImageResizeMode.Contain;
    /// <summary>裁剪左上角X，位于EXIF方向修正后的原图；四个Crop参数需全部填写。</summary>
    [Range(0, 4096)] public int? CropX { get; set; }
    /// <summary>裁剪左上角Y。</summary>
    [Range(0, 4096)] public int? CropY { get; set; }
    /// <summary>裁剪宽度。</summary>
    [Range(1, 4096)] public int? CropWidth { get; set; }
    /// <summary>裁剪高度。</summary>
    [Range(1, 4096)] public int? CropHeight { get; set; }
    /// <summary>顺时针旋转：0、90、180或270度。</summary>
    public int RotationDegrees { get; set; }
    /// <summary>是否水平翻转。</summary>
    public bool FlipHorizontal { get; set; }
    /// <summary>是否垂直翻转。</summary>
    public bool FlipVertical { get; set; }
    /// <summary>可选纯色背景RRGGBB或#RRGGBB；不填时PNG/WebP保留透明，JPEG使用白底。</summary>
    public string? Background { get; set; }

    internal ImageProcessingOptions Options() => new()
    {
        Format = Format, Quality = Quality, Resize = MediaToolParameters.Size(Width, Height),
        ResizeMode = ResizeMode, Crop = MediaToolParameters.Crop(CropX, CropY, CropWidth, CropHeight),
        RotationDegrees = RotationDegrees, FlipHorizontal = FlipHorizontal, FlipVertical = FlipVertical,
        BackgroundColor = Background is null ? null : MediaToolParameters.Color(Background), Limits = MediaToolParameters.Limits
    };
}

public sealed class ImageTextWatermarkForm : TextWatermarkDemoForm
{
    /// <summary>原图，支持PNG、JPEG、WebP，最多20MiB、400万像素。</summary>
    [Required] public IFormFile? File { get; set; }
    /// <summary>输出格式：0=Png、1=Jpeg、2=Webp。</summary>
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;
    /// <summary>JPEG/WebP编码质量，0到100。</summary>
    [Range(0, 100)] public int Quality { get; set; } = 90;
}

public sealed class ImageLogoWatermarkForm : LogoWatermarkDemoForm
{
    /// <summary>原图，支持PNG、JPEG、WebP，最多20MiB、400万像素。</summary>
    [Required] public IFormFile? File { get; set; }
    /// <summary>输出格式：0=Png、1=Jpeg、2=Webp。</summary>
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;
    /// <summary>JPEG/WebP编码质量，0到100。</summary>
    [Range(0, 100)] public int Quality { get; set; } = 90;
}
