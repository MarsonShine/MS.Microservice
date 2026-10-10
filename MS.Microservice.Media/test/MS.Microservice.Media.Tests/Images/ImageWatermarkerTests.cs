using MS.Microservice.Media.Images;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Xunit;

namespace MS.Microservice.Media.Tests.Images;

public sealed class ImageWatermarkerTests
{
    private readonly ImageWatermarker _watermarker = new();

    [Theory]
    [InlineData(WatermarkAnchor.TopLeft, 1, 1)]
    [InlineData(WatermarkAnchor.TopCenter, 5, 1)]
    [InlineData(WatermarkAnchor.TopRight, 9, 1)]
    [InlineData(WatermarkAnchor.CenterLeft, 1, 4)]
    [InlineData(WatermarkAnchor.Center, 5, 4)]
    [InlineData(WatermarkAnchor.CenterRight, 9, 4)]
    [InlineData(WatermarkAnchor.BottomLeft, 1, 7)]
    [InlineData(WatermarkAnchor.BottomCenter, 5, 7)]
    [InlineData(WatermarkAnchor.BottomRight, 9, 7)]
    public async Task Logo_AllAnchorsPlaceOnlyTheIntendedPixels(WatermarkAnchor anchor, int left, int top)
    {
        using var logo = new MemoryStream(Png(2, 2, (_, _) => SKColors.Red));
        using var output = new MemoryStream();

        await _watermarker.RenderLogoOverlayAsync(12, 10, logo, output, placement: new()
        {
            Anchor = anchor, MarginX = 1, MarginY = 1, Opacity = 1
        });

        using var actual = SKBitmap.Decode(output.ToArray());
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 12; x++)
            if (x >= left && x < left + 2 && y >= top && y < top + 2)
                Assert.Equal(SKColors.Red, actual.GetPixel(x, y));
            else Assert.Equal(0, actual.GetPixel(x, y).Alpha);
        Assert.True(logo.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task Logo_QuarterTurnAnchorsUseRotatedBounds(int rotation)
    {
        SKColor[] colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.Cyan, SKColors.Magenta];
        using var logo = new MemoryStream(Png(3, 2, (x, y) => colors[y * 3 + x]));
        using var output = new MemoryStream();
        await _watermarker.RenderLogoOverlayAsync(10, 10, logo, output, placement: new()
        {
            Anchor = WatermarkAnchor.TopLeft, MarginX = 1, MarginY = 1, Opacity = 1, RotationDegrees = rotation
        });

        using var actual = SKBitmap.Decode(output.ToArray());
        int[] indices = rotation switch
        {
            90 => [3, 0, 4, 1, 5, 2], 180 => [5, 4, 3, 2, 1, 0],
            270 => [2, 5, 1, 4, 0, 3], _ => [0, 1, 2, 3, 4, 5]
        };
        var width = rotation is 90 or 270 ? 2 : 3;
        for (var index = 0; index < 6; index++)
            Assert.Equal(colors[indices[index]], actual.GetPixel(1 + index % width, 1 + index / width));
        Assert.Equal(6, Pixels(actual).Count(color => color.Alpha != 0));
    }

    [Fact]
    public async Task Logo_ArbitraryRotationStaysInsideMargins()
    {
        using var logo = new MemoryStream(Png(4, 2, (_, _) => SKColors.Red));
        using var output = new MemoryStream();
        await _watermarker.RenderLogoOverlayAsync(20, 20, logo, output, placement: new()
        {
            Anchor = WatermarkAnchor.BottomRight, MarginX = 2, MarginY = 2, Opacity = 1, RotationDegrees = 45
        });
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.Contains(Pixels(actual), color => color.Alpha > 0);
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 20; x++)
            if (x < 2 || y < 2 || x >= 18 || y >= 18) Assert.Equal(0, actual.GetPixel(x, y).Alpha);
    }

    [Fact]
    public async Task Logo_SizePreservesAspectAndOpacityMultipliesItsOwnAlpha()
    {
        using var logo = new MemoryStream(Png(4, 2, (_, _) => new SKColor(255, 0, 0, 128)));
        using var output = new MemoryStream();
        await _watermarker.RenderLogoOverlayAsync(10, 10, logo, output, new(2, 2), new()
        {
            Anchor = WatermarkAnchor.TopLeft, MarginX = 0, MarginY = 0, Opacity = 0.5
        });
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.Equal(2, Pixels(actual).Count(color => color.Alpha != 0));
        Assert.InRange(actual.GetPixel(0, 0).Alpha, (byte)63, (byte)65);
        Assert.Equal(0, actual.GetPixel(0, 1).Alpha);
    }

    [Fact]
    public async Task Logo_TilingExactBudgetHasNoOffscreenPhantomRowOrColumn()
    {
        using var logo = new MemoryStream(Png(1, 1, (_, _) => SKColors.Red));
        using var output = new MemoryStream();
        var placement = new WatermarkPlacement
        {
            Anchor = WatermarkAnchor.TopLeft, MarginX = 0, MarginY = 0, Opacity = 1,
            Tile = true, TileGap = 0, MaximumTiles = 100
        };

        await _watermarker.RenderLogoOverlayAsync(10, 10, logo, output, placement: placement);
        using var actual = SKBitmap.Decode(output.ToArray());
        Assert.All(Pixels(actual), color => Assert.Equal(SKColors.Red, color));
        logo.Position = 0;
        using var rejected = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderLogoOverlayAsync(
            10, 10, logo, rejected, placement: placement with { MaximumTiles = 99 }));
        Assert.Empty(rejected.ToArray());
    }

    [Fact]
    public async Task Logo_TilingGapAndAnchorPhaseDoNotLeakIntoMargins()
    {
        using var logo = new MemoryStream(Png(2, 2, (_, _) => SKColors.Red));
        using var output = new MemoryStream();
        await _watermarker.RenderLogoOverlayAsync(12, 10, logo, output, placement: new()
        {
            Anchor = WatermarkAnchor.Center, MarginX = 1, MarginY = 1, Opacity = 1,
            Tile = true, TileGap = 2, MaximumTiles = 12, RotationDegrees = 90
        });
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.InRange(Pixels(actual).Count(color => color.Alpha != 0), 5, 80);
        for (var y = 0; y < 10; y++) Assert.Equal(0, actual.GetPixel(0, y).Alpha);
        for (var x = 0; x < 12; x++) Assert.Equal(0, actual.GetPixel(x, 0).Alpha);
    }

    [Fact]
    public async Task Logo_ZeroOpacityPreservesInputPixelsAndCallerAssets()
    {
        var original = Png(40, 40, (x, y) => new SKColor((byte)(x * 5), (byte)(y * 5), 50, 128));
        var logoBytes = Png(2, 2, (_, _) => SKColors.Red);
        using var input = new MemoryStream(original);
        using var logo = new MemoryStream(logoBytes);
        using var output = new MemoryStream();

        await _watermarker.AddLogoAsync(input, logo, output, options: new() { MarginX = 0, MarginY = 0, Opacity = 0 });
        using var expected = SKBitmap.Decode(original);
        using var actual = SKBitmap.Decode(output.ToArray());
        Assert.Equal(Pixels(expected), Pixels(actual));
        Assert.Equal(original, input.ToArray());
        Assert.Equal(logoBytes, logo.ToArray());
        Assert.True(input.CanRead);
        Assert.True(logo.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(ImageOutputFormat.Png)]
    [InlineData(ImageOutputFormat.Jpeg)]
    [InlineData(ImageOutputFormat.Webp)]
    public async Task Logo_FullImageOutputUsesSharedCodec(ImageOutputFormat format)
    {
        using var input = new MemoryStream(Png(24, 24, (_, _) => SKColors.Blue));
        using var logo = new MemoryStream(Png(8, 8, (_, _) => SKColors.Red));
        using var output = new MemoryStream();

        var result = await _watermarker.AddLogoAsync(input, logo, output, options: new()
        {
            Format = format, Quality = 100, Anchor = WatermarkAnchor.TopLeft, MarginX = 0, MarginY = 0, Opacity = 1
        });
        using var codec = SKCodec.Create(new MemoryStream(output.ToArray()));
        Assert.Equal(format switch
        {
            ImageOutputFormat.Png => SKEncodedImageFormat.Png,
            ImageOutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Webp
        }, codec.EncodedFormat);
        Assert.Equal(new ImageProcessingResult(24, 24, format), result);
        using var actual = SKBitmap.Decode(output.ToArray());
        Assert.InRange(actual.GetPixel(3, 3).Red, (byte)240, (byte)255);
        Assert.InRange(actual.GetPixel(20, 20).Blue, (byte)240, (byte)255);
    }

    [Fact]
    public async Task Text_DefaultFontRendersVisibleSingleLineOnTransparentOverlay()
    {
        using var output = new MemoryStream();
        await _watermarker.RenderTextOverlayAsync(200, 80, output, new("Watermark") { FontSize = 20, Color = new(255, 0, 0) },
            new() { Anchor = WatermarkAnchor.TopLeft, MarginX = 2, MarginY = 2, Opacity = 1 });
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.Contains(Pixels(actual), color => color.Red > 0 && color.Alpha > 0);
        Assert.Equal(0, actual.GetPixel(199, 79).Alpha);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Text_ExplicitChineseFontAndHarfBuzzArabicShapingWorkOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fontDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var chineseFont = Path.Combine(fontDirectory, "msyh.ttc");
        var arabicFont = Path.Combine(fontDirectory, "arial.ttf");
        Assert.True(File.Exists(chineseFont));
        Assert.True(File.Exists(arabicFont));
        foreach (var (text, path) in new[] { ("水印中文", chineseFont), ("لا", arabicFont) })
        {
            using var output = new MemoryStream();
            await _watermarker.RenderTextOverlayAsync(240, 80, output,
                new(text) { FontFile = path, FontSize = 24, Color = new(255, 0, 0) },
                new() { Anchor = WatermarkAnchor.TopLeft, MarginX = 0, MarginY = 0, Opacity = 1 });
            using var actual = SKBitmap.Decode(output.ToArray());
            Assert.Contains(Pixels(actual), color => color.Alpha != 0);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        using var typeface = SKTypeface.FromFile(arabicFont);
        using var font = new SKFont(typeface, 24);
        using var shaper = new SKShaper(typeface);
        Assert.True(shaper.Shape("لا", font).Codepoints.Length < 2);
    }

    [Fact]
    public async Task Text_ZeroOpacityPreservesSourcePixelsWithRotationAndTiling()
    {
        var original = Png(100, 100, (_, _) => SKColors.Blue);
        using var input = new MemoryStream(original);
        using var output = new MemoryStream();
        await _watermarker.AddTextAsync(input, output, new("Hi") { FontSize = 12 }, new()
        {
            MarginX = 0, MarginY = 0, Opacity = 0, RotationDegrees = 45, Tile = true, TileGap = 10
        });
        using var actual = SKBitmap.Decode(output.ToArray());
        Assert.All(Pixels(actual), color => Assert.Equal(SKColors.Blue, color));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Text_RotatedTiledOverlayMatchesFullImageComposition()
    {
        using var input = new MemoryStream(Png(200, 120, (_, _) => SKColors.Transparent));
        using var composed = new MemoryStream();
        using var overlay = new MemoryStream();
        var text = new TextWatermark("Demo") { FontSize = 16, Color = new(255, 0, 0, 180) };
        var options = new WatermarkOptions
        {
            Anchor = WatermarkAnchor.Center, MarginX = 3, MarginY = 3, Opacity = 0.5,
            RotationDegrees = 30, Tile = true, TileGap = 12
        };

        await _watermarker.AddTextAsync(input, composed, text, options);
        await _watermarker.RenderTextOverlayAsync(200, 120, overlay, text, options);
        using var composedBitmap = SKBitmap.Decode(composed.ToArray());
        using var overlayBitmap = SKBitmap.Decode(overlay.ToArray());

        Assert.Equal(Pixels(overlayBitmap), Pixels(composedBitmap));
        Assert.Contains(Pixels(overlayBitmap), color => color.Alpha > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("line\nbreak")]
    [InlineData("tab\t")]
    public async Task Text_InvalidUnicodeOrMultilineIsRejectedWithoutWriting(string text)
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderTextOverlayAsync(200, 80, output, new(text)));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Text_UnpairedSurrogateIsRejectedBeforeShaping()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderTextOverlayAsync(
            200, 80, output, new(new string('\uD800', 1))));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Text_MissingGlyphAndMissingFontFailExplicitly()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => _watermarker.RenderTextOverlayAsync(
            200, 80, output, new("\U0010FFFF")));
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderTextOverlayAsync(
            200, 80, output, new("text") { FontFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ttf") }));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Text_InvalidSizeAndTextResourceLimitAreRejected()
    {
        TextWatermark[] invalid =
        [
            new("text") { FontSize = 0 }, new("text") { FontSize = float.NaN },
            new("text") { FontSize = float.PositiveInfinity }, new(new string('a', 4097))
        ];
        foreach (var text in invalid)
        {
            using var output = new MemoryStream();
            await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderTextOverlayAsync(200, 80, output, text));
            Assert.Empty(output.ToArray());
        }
    }

    [Fact]
    public async Task Logo_InvalidPlacementRasterAndOversizeAreRejectedBeforeOutput()
    {
        WatermarkPlacement[] invalid =
        [
            new() { MarginX = -1 }, new() { MarginY = -1 }, new() { Opacity = double.NaN },
            new() { Opacity = -1 }, new() { Opacity = 2 }, new() { RotationDegrees = float.PositiveInfinity },
            new() { TileGap = -1 }, new() { MaximumTiles = 0 }, new() { MarginX = int.MaxValue },
            new() { MarginX = 0, MarginY = 0, Limits = new() { MaximumPixels = 50 } }
        ];
        foreach (var placement in invalid)
        {
            using var logo = new MemoryStream(Png(2, 2, (_, _) => SKColors.Red));
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<Exception>(() => _watermarker.RenderLogoOverlayAsync(10, 10, logo, output, placement: placement));
            Assert.Empty(output.ToArray());
            Assert.True(logo.CanRead);
        }

        using var oversized = new MemoryStream(Png(12, 1, (_, _) => SKColors.Red));
        using var rejected = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderLogoOverlayAsync(10, 10, oversized, rejected,
            placement: new() { MarginX = 0, MarginY = 0 }));
        Assert.Empty(rejected.ToArray());
    }

    [Fact]
    public async Task Overlay_PreCancellationLeavesCallerStreamsOpenAndEmpty()
    {
        using var logo = new MemoryStream(Png(2, 2, (_, _) => SKColors.Red));
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _watermarker.RenderLogoOverlayAsync(
            10, 10, logo, output, placement: new() { MarginX = 0, MarginY = 0 }, cancellationToken: new(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _watermarker.RenderTextOverlayAsync(
            200, 80, output, new("text"), cancellationToken: new(true)));
        Assert.Empty(output.ToArray());
        Assert.True(logo.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Logo_CancellationDuringAssetReadReleasesSourceAndPreservesCallerStreams()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new MemoryStream(Png(40, 40, (_, _) => SKColors.Blue));
        using var logo = new CancellingReadStream(Png(2, 2, (_, _) => SKColors.Red), cancellation);
        using var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _watermarker.AddLogoAsync(
            input, logo, output, cancellationToken: cancellation.Token));

        Assert.True(input.CanRead);
        Assert.True(logo.CanRead);
        Assert.True(output.CanWrite);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Logo_OutputFailurePropagatesAndKeepsAllCallerStreamsOpen()
    {
        using var input = new MemoryStream(Png(40, 40, (_, _) => SKColors.Blue));
        using var logo = new MemoryStream(Png(2, 2, (_, _) => SKColors.Red));
        using var output = new FailingWriteStream();

        await Assert.ThrowsAsync<IOException>(() => _watermarker.AddLogoAsync(input, logo, output));

        Assert.True(input.CanRead);
        Assert.True(logo.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Logo_SourceOutputAliasesAreRejectedWithoutMutation()
    {
        var bytes = Png(2, 2, (_, _) => SKColors.Red);
        using var image = new MemoryStream(bytes);
        using var logo = new MemoryStream(bytes);
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.AddLogoAsync(image, logo, image));
        await Assert.ThrowsAsync<ArgumentException>(() => _watermarker.RenderLogoOverlayAsync(20, 20, logo, logo));
        Assert.Equal(bytes, image.ToArray());
        Assert.Equal(bytes, logo.ToArray());
    }

    private static byte[] Png(int width, int height, Func<int, int, SKColor> color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, color(x, y));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static SKColor[] Pixels(SKBitmap bitmap) => Enumerable.Range(0, bitmap.Height)
        .SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, y))).ToArray();

    private sealed class CancellingReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
            cancellation.Cancel();
            return read;
        }
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("output failed"));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException(new IOException("output failed"));
    }
}
