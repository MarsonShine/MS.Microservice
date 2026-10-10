using MS.Microservice.Media.Images;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Tests.Images;

public sealed class ImageBackgroundTests
{
    [Theory]
    [InlineData(ImageOutputFormat.Png, 0)]
    [InlineData(ImageOutputFormat.Png, 128)]
    [InlineData(ImageOutputFormat.Png, 255)]
    [InlineData(ImageOutputFormat.Jpeg, 0)]
    [InlineData(ImageOutputFormat.Jpeg, 128)]
    [InlineData(ImageOutputFormat.Jpeg, 255)]
    [InlineData(ImageOutputFormat.Webp, 0)]
    [InlineData(ImageOutputFormat.Webp, 128)]
    [InlineData(ImageOutputFormat.Webp, 255)]
    public async Task OpaqueBackgroundCompositesTransparentPartialAndOpaqueInput(ImageOutputFormat format, byte alpha)
    {
        using var input = Source(alpha);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output, new()
        { Format = format, Quality = 100, BackgroundColor = new(0, 0, 255) });
        Assert.True(input.CanRead && output.CanWrite);
        using var result = SKBitmap.Decode(output.ToArray());
        Assert.All(result.Pixels, pixel =>
        {
            Assert.Equal(255, pixel.Alpha);
            Assert.InRange((int)pixel.Red, Math.Max(0, alpha - 4), Math.Min(255, alpha + 4));
            Assert.InRange((int)pixel.Green, 0, 4);
            Assert.InRange((int)pixel.Blue, Math.Max(0, 255 - alpha - 4), Math.Min(255, 255 - alpha + 4));
        });
    }

    [Theory]
    [InlineData(ImageOutputFormat.Png)]
    [InlineData(ImageOutputFormat.Webp)]
    public async Task UnspecifiedBackgroundStillRetainsAlpha(ImageOutputFormat format)
    {
        using var input = Source(128);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output, new() { Format = format, Quality = 100 });
        using var result = SKBitmap.Decode(output.ToArray());
        Assert.All(result.Pixels, pixel => Assert.Equal(128, pixel.Alpha));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public async Task NonOpaqueBackgroundIsRejectedBeforeReadingInput(byte alpha)
    {
        using var input = new MemoryStream(new byte[] { 1, 2, 3 });
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => new ImageProcessor().ProcessAsync(input, output,
            new() { BackgroundColor = new(0, 0, 255, alpha) }));
        Assert.Equal(0, input.Position);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task AllFormatBackgroundOverridesTheLegacyJpegFallback()
    {
        using var input = Source(0);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output, new()
        { Format = ImageOutputFormat.Jpeg, Quality = 100, JpegBackground = new(0, 255, 0), BackgroundColor = new(0, 0, 255) });
        using var result = SKBitmap.Decode(output.ToArray());
        Assert.All(result.Pixels, pixel => { Assert.InRange((int)pixel.Blue, 251, 255); Assert.InRange((int)pixel.Green, 0, 4); });
    }

    [Fact]
    public async Task BackgroundUsesTheCanvasAfterRotationAndResize()
    {
        using var input = Source(128, 2, 4);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output, new()
        { RotationDegrees = 90, Resize = new(8, 8), BackgroundColor = new(0, 0, 255) });
        using var result = SKBitmap.Decode(output.ToArray());
        Assert.Equal((8, 4), (result.Width, result.Height));
        Assert.All(result.Pixels, pixel => Assert.Equal(new SKColor(128, 0, 127, 255), pixel));
    }

    private static MemoryStream Source(byte alpha, int width = 16, int height = 16)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new(255, 0, 0, alpha));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(png.ToArray());
    }
}
