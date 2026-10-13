using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MS.Microservice.AspNetCore.Uploads;
using MS.Microservice.Lab.Application.Tools;
using MS.Microservice.Lab.Infrastructure.Labs;
using MS.Microservice.Media.Images;

namespace MS.Microservice.Lab.Controller;

[LabOnly, ApiController]
[Route("api/lab/tools/images")]
[RequestSizeLimit(ToolDemoFiles.MaximumRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ToolDemoFiles.MaximumRequestBytes)]
public sealed class ImageToolsController(ToolDemoFiles files, IConfiguration configuration) : ToolDemoControllerBase(files)
{
    /// <summary>裁剪、旋转、缩放图片并转换格式。</summary>
    /// <remarks>上传PNG/JPEG/WebP；先修正EXIF方向，再裁剪、旋转/翻转、缩放。输入和输出均限4096边长及400万像素。Background可把透明区域合成纯色。</remarks>
    [HttpPost("process"), Consumes("multipart/form-data")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Process([FromForm] ImageTransformForm form)
    {
        var options = form.Options();
        var outputType = MediaToolParameters.ImageOutput(form.Format);
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token, UploadFileKind.Image));
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output, options, Token);
        return Download(output.ToArray(), outputType.Mime, "image" + outputType.Extension);
    }

    /// <summary>为图片添加文字水印。</summary>
    /// <remarks>支持九宫格锚点、透明度、旋转与平铺。默认使用系统字体，Windows优先微软雅黑；可用ToolDemos:FontFilePath覆盖，字体须包含所用文字。请求无需提供字体路径。</remarks>
    [HttpPost("watermarks/text"), Consumes("multipart/form-data")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TextWatermark([FromForm] ImageTextWatermarkForm form)
    {
        var text = form.Watermark(configuration);
        var outputType = MediaToolParameters.ImageOutput(form.Format);
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token, UploadFileKind.Image));
        using var output = new MemoryStream();
        await new ImageWatermarker().AddTextAsync(input, output, text, Options(form, form.Format, form.Quality), Token);
        return Download(output.ToArray(), outputType.Mime, "watermarked" + outputType.Extension);
    }

    /// <summary>为图片添加Logo水印。</summary>
    /// <remarks>分别上传原图与Logo。LogoWidth/LogoHeight成对填写；不填则使用Logo原始尺寸。PNG Logo的透明度与Opacity共同生效。</remarks>
    [HttpPost("watermarks/logo"), Consumes("multipart/form-data")]
    [Produces("image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoWatermark([FromForm] ImageLogoWatermarkForm form)
    {
        var size = form.LogoSize();
        var outputType = MediaToolParameters.ImageOutput(form.Format);
        using var input = new MemoryStream(await Files.ReadAsync(form.File, Token, UploadFileKind.Image));
        using var logo = new MemoryStream(await Files.ReadAsync(form.Logo, Token, UploadFileKind.Image));
        using var output = new MemoryStream();
        await new ImageWatermarker().AddLogoAsync(input, logo, output, size, Options(form, form.Format, form.Quality), Token);
        return Download(output.ToArray(), outputType.Mime, "watermarked" + outputType.Extension);
    }

    private static WatermarkOptions Options(WatermarkDemoForm form, ImageOutputFormat format, int quality) => new()
    {
        Format = format, Quality = quality, Anchor = form.Anchor, MarginX = form.MarginX, MarginY = form.MarginY,
        Opacity = form.Opacity, RotationDegrees = form.WatermarkAngle, Tile = form.Tile, TileGap = form.TileGap,
        MaximumTiles = 1000, Limits = MediaToolParameters.Limits
    };
}
