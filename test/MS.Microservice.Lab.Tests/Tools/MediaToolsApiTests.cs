using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MS.Microservice.Media.Codes;
using SkiaSharp;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class MediaToolsApiTests
{
    [Fact]
    public async Task ImageProcessCropsAndResizesUploadedPicture()
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        form.Add(Png(8, 4, canvas =>
        {
            canvas.Clear(SKColors.Red);
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(4, 0, 4, 4, paint);
        }), "File", "source.png");
        Add(form, "CropX", "4", "CropY", "0", "CropWidth", "4", "CropHeight", "4", "Width", "2", "Height", "2");

        using var response = await host.Client.PostAsync("/api/lab/tools/images/process", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        using var result = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.NotNull(result);
        Assert.Equal(2, result.Width);
        Assert.Equal(2, result.Height);
        Assert.Equal(SKColors.Blue, result.GetPixel(1, 1));
    }

    [Fact]
    public async Task ImageProcessCompositesTransparencyOntoSelectedBackground()
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        form.Add(Png(4, 4, canvas => canvas.Clear(SKColors.Transparent)), "File", "transparent.png");
        Add(form, "Background", "#12AB34");

        using var response = await host.Client.PostAsync("/api/lab/tools/images/process", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.NotNull(result);
        Assert.Equal(new SKColor(0x12, 0xAB, 0x34), result.GetPixel(2, 2));
    }

    [Fact]
    public async Task GeneratedUnicodeQrCanBeUploadedForRecognition()
    {
        using var host = new ToolDemoTestHost();
        const string text = "工具 Demo ✓ 2026";
        using var generated = await host.Client.PostAsJsonAsync("/api/lab/tools/codes/generate", new { text, format = 0 });
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        Assert.Equal("image/png", generated.Content.Headers.ContentType?.MediaType);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(await generated.Content.ReadAsByteArrayAsync());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "File", "code.png");

        using var decoded = await host.Client.PostAsync("/api/lab/tools/codes/decode", form);

        Assert.Equal(HttpStatusCode.OK, decoded.StatusCode);
        var codes = await decoded.Content.ReadFromJsonAsync<DecodedCode[]>();
        var code = Assert.Single(codes!);
        Assert.Equal(text, code.Text);
        Assert.Equal(CodeFormat.Qr, code.Format);
        Assert.NotEmpty(code.Points);
        Assert.All(code.Points, point =>
        {
            Assert.InRange(point.X, 0, 255);
            Assert.InRange(point.Y, 0, 255);
        });
    }

    [Fact]
    public async Task LogoWatermarkUsesBothUploadedImages()
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        form.Add(Png(8, 8, canvas => canvas.Clear(SKColors.Red)), "File", "source.png");
        form.Add(Png(2, 2, canvas => canvas.Clear(SKColors.Blue)), "Logo", "logo.png");
        Add(form, "Anchor", "0", "MarginX", "0", "MarginY", "0", "Opacity", "1");

        using var response = await host.Client.PostAsync("/api/lab/tools/images/watermarks/logo", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.NotNull(result);
        Assert.Equal(SKColors.Blue, result.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, result.GetPixel(7, 7));
    }

    [MediaDemoFfmpegFact]
    public async Task CorruptVideoReturnsLocatedClientErrorInsteadOfInternalServerError()
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent("not a media container"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(content, "File", "broken.mp4");

        using var response = await host.Client.PostAsync("/api/lab/tools/videos/probe", form);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("422", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("0.6")]
    [InlineData("512.4")]
    [InlineData("NaN")]
    public async Task FractionalFontBoundsAreValidatedBeforeLoadingHostFont(string size)
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        form.Add(Png(8, 8, canvas => canvas.Clear(SKColors.Red)), "File", "source.png");
        Add(form, "FontSize", size, "Text", "A");

        using var response = await host.Client.PostAsync("/api/lab/tools/images/watermarks/text", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("512")]
    public async Task ValidFontBoundsProceedToExplicitHostDependencyCheck(string size)
    {
        using var host = new ToolDemoTestHost(new Dictionary<string, string?> { ["ToolDemos:FontFilePath"] = "" });
        using var form = new MultipartFormDataContent();
        form.Add(Png(8, 8, canvas => canvas.Clear(SKColors.Red)), "File", "source.png");
        Add(form, "FontSize", size, "Text", "A");

        using var response = await host.Client.PostAsync("/api/lab/tools/images/watermarks/text", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ColorAcceptsOnlyOneOptionalHexPrefix()
    {
        using var host = new ToolDemoTestHost();
        using var form = new MultipartFormDataContent();
        form.Add(Png(4, 4, canvas => canvas.Clear(SKColors.Transparent)), "File", "source.png");
        Add(form, "Background", "##12AB34");

        using var response = await host.Client.PostAsync("/api/lab/tools/images/process", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static ByteArrayContent Png(int width, int height, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) draw(canvas);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var content = new ByteArrayContent(encoded.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    private static void Add(MultipartFormDataContent form, params string[] values)
    {
        for (var index = 0; index < values.Length; index += 2)
            form.Add(new StringContent(values[index + 1]), values[index]);
    }
}

internal sealed class MediaDemoFfmpegFactAttribute : FactAttribute
{
    public MediaDemoFfmpegFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_MEDIA_FFMPEG_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            Skip = "Set RUN_MEDIA_FFMPEG_TESTS=true to run the installed ffprobe HTTP integration.";
    }
}
