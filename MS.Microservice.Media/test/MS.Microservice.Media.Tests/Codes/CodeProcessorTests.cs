using MS.Microservice.Media.Codes;
using MS.Microservice.Media.Images;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Tests.Codes;

public sealed class CodeProcessorTests
{
    private readonly CodeProcessor _processor = new();

    [Theory]
    [InlineData(CodeFormat.Qr, "你好，微服务🙂")]
    [InlineData(CodeFormat.Code128, "FRAMEWORK-123")]
    [InlineData(CodeFormat.Ean13, "5901234123457")]
    [InlineData(CodeFormat.Ean8, "96385074")]
    [InlineData(CodeFormat.UpcA, "012345678905")]
    [InlineData(CodeFormat.Code39, "CODE39-123")]
    [InlineData(CodeFormat.DataMatrix, "DATA123")]
    [InlineData(CodeFormat.Pdf417, "PDF417-123")]
    public async Task GeneratesAndRecognizesRealCodes(CodeFormat format, string text)
    {
        using var image = await Generate(text, new() { Format = format, Width = 512, Height = 256 });
        var decoded = Assert.Single(await _processor.DecodeAsync(image));
        if (format == CodeFormat.UpcA)
        {
            Assert.Contains(decoded.Format, new[] { CodeFormat.UpcA, CodeFormat.Ean13 });
            Assert.Equal(decoded.Format == CodeFormat.Ean13 ? "0" + text : text, decoded.Text);
        }
        else
        {
            Assert.Equal(format, decoded.Format);
            Assert.Equal(text, decoded.Text);
        }
        Assert.NotEmpty(decoded.Points);
        Assert.True(image.CanRead);
    }

    [Theory]
    [InlineData(QrErrorCorrection.Low)]
    [InlineData(QrErrorCorrection.Medium)]
    [InlineData(QrErrorCorrection.Quartile)]
    [InlineData(QrErrorCorrection.High)]
    public async Task QrErrorCorrectionLevelsProduceReadableCrispPng(QrErrorCorrection correction)
    {
        using var image = await Generate("QR-123", new() { Width = 256, Height = 256, QrErrorCorrection = correction, Margin = 4 });
        var bytes = image.ToArray();
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            Assert.Contains(bitmap.GetPixel(x, y), new[] { SKColors.Black, SKColors.White });
        Assert.Equal("QR-123", Assert.Single(await _processor.DecodeAsync(image)).Text);
    }

    [Fact]
    public async Task EanWriterCanCalculateMissingCheckDigitAndRejectWrongCheckDigit()
    {
        using var image = await Generate("590123412345", new() { Format = CodeFormat.Ean13 });
        Assert.Equal("5901234123457", Assert.Single(await _processor.DecodeAsync(image)).Text);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _processor.GenerateAsync("5901234123458", output,
            new() { Format = CodeFormat.Ean13 }));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(CodeFormat.Ean13, "not-numeric")]
    [InlineData(CodeFormat.Ean8, "123")]
    [InlineData(CodeFormat.UpcA, "123")]
    [InlineData(CodeFormat.Code128, "中文")]
    public async Task InvalidPayloadDoesNotWriteOutput(CodeFormat format, string text)
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _processor.GenerateAsync(text, output, new() { Format = format }));
        Assert.Empty(output.ToArray());
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(CodeFormat.Qr)]
    [InlineData(CodeFormat.Code128)]
    [InlineData(CodeFormat.Ean13)]
    [InlineData(CodeFormat.Ean8)]
    [InlineData(CodeFormat.UpcA)]
    [InlineData(CodeFormat.Code39)]
    [InlineData(CodeFormat.DataMatrix)]
    [InlineData(CodeFormat.Pdf417)]
    public async Task MalformedUtf16IsRejectedAtTheSharedBoundaryWithoutReplacingOrWriting(CodeFormat format)
    {
        foreach (var text in new[] { "\ud800", "a\udc00b", "a\ud800", "\udc00\ud800", "\ud800\ud800" })
        {
            using var output = new MemoryStream();
            output.WriteByte(99);
            var error = await Assert.ThrowsAsync<ArgumentException>(() => _processor.GenerateAsync(text, output,
                new() { Format = format }));
            Assert.Equal("text", error.ParamName);
            Assert.Equal(new byte[] { 99 }, output.ToArray());
            Assert.Equal(1, output.Position);
            Assert.True(output.CanWrite);
        }
    }

    [Theory]
    [InlineData("\ud800\udc00")]
    [InlineData("\udbff\udfff")]
    [InlineData("a🙂b")]
    [InlineData("\ufffd")]
    public async Task ValidUnicodePairsAndLiteralReplacementCharacterRetainExactPayload(string text)
    {
        using var generated = await Generate(text);
        Assert.Equal(text, Assert.Single(await _processor.DecodeAsync(generated)).Text);
    }

    [Fact]
    public async Task UnicodeLengthLimitCountsUtf16UnitsAndAcceptsAnExactSurrogatePairBoundary()
    {
        using var generated = await Generate("🙂", new() { MaximumTextCharacters = 2 });
        Assert.Equal("🙂", Assert.Single(await _processor.DecodeAsync(generated)).Text);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => _processor.GenerateAsync("🙂", output,
            new() { MaximumTextCharacters = 1 }));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(CodeFormat.Qr)]
    [InlineData(CodeFormat.Code128)]
    [InlineData(CodeFormat.DataMatrix)]
    [InlineData(CodeFormat.Pdf417)]
    public async Task TooSmallCanvasRejectsEnlargedWriterMatrixInsteadOfClipping(CodeFormat format)
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _processor.GenerateAsync("CODE-123", output,
            new() { Format = format, Width = 1, Height = 1 }));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task InvalidOptionsAreRejectedBeforeWriting()
    {
        CodeGenerationOptions[] invalid =
        [
            new() { Format = (CodeFormat)999 }, new() { QrErrorCorrection = (QrErrorCorrection)999 },
            new() { Margin = -1 }, new() { Margin = 128 }, new() { MaximumTextCharacters = 2 },
            new() { Foreground = new(255, 255, 255), Background = new(0, 0, 0) },
            new() { Foreground = new(0, 0, 0, 128) }, new() { Background = new(255, 255, 255, 0) }
        ];
        foreach (var options in invalid)
        {
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<ArgumentException>(() => _processor.GenerateAsync("CODE", output, options));
            Assert.Empty(output.ToArray());
        }
    }

    [Fact]
    public async Task PlainImagesReturnEmptyResultsAndFormatsCanBeRestricted()
    {
        using var white = Image(64, 64, SKColors.White);
        Assert.Empty(await _processor.DecodeAsync(white));
        using var qr = await Generate("QR-only");
        Assert.Empty(await _processor.DecodeAsync(qr, new() { Formats = [CodeFormat.Ean13] }));
        qr.Position = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => _processor.DecodeAsync(qr, new() { Formats = [] }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _processor.DecodeAsync(qr,
            new() { Formats = [(CodeFormat)999] }));
    }

    [Fact]
    public async Task RecognizesMultipleQrCodesInOneImage()
    {
        using var first = await Generate("first");
        using var second = await Generate("second");
        using var firstBitmap = SKBitmap.Decode(first);
        using var secondBitmap = SKBitmap.Decode(second);
        using var firstImage = SKImage.FromBitmap(firstBitmap);
        using var secondImage = SKImage.FromBitmap(secondBitmap);
        using var combined = new SKBitmap(600, 300, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(combined))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(firstImage, 10, 10, new SKSamplingOptions(SKFilterMode.Nearest));
            canvas.DrawImage(secondImage, 320, 10, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        using var input = Encode(combined);
        var results = await _processor.DecodeAsync(input, new() { Formats = [CodeFormat.Qr] });
        Assert.Equal(new[] { "first", "second" }, results.Select(result => result.Text).Order());
    }

    [Theory]
    [InlineData(CodeFormat.Code128, 90)]
    [InlineData(CodeFormat.Code128, 180)]
    [InlineData(CodeFormat.Code128, 270)]
    [InlineData(CodeFormat.Code39, 90)]
    [InlineData(CodeFormat.Code39, 180)]
    [InlineData(CodeFormat.Code39, 270)]
    [InlineData(CodeFormat.Qr, 90)]
    [InlineData(CodeFormat.Qr, 180)]
    [InlineData(CodeFormat.Qr, 270)]
    [InlineData(CodeFormat.DataMatrix, 90)]
    [InlineData(CodeFormat.DataMatrix, 180)]
    [InlineData(CodeFormat.DataMatrix, 270)]
    [InlineData(CodeFormat.Pdf417, 90)]
    [InlineData(CodeFormat.Pdf417, 180)]
    [InlineData(CodeFormat.Pdf417, 270)]
    public async Task RecognizesRotatedInputAndReturnsPointsInsideTheDisplayedImage(CodeFormat format, int rotation)
    {
        using var generated = await Generate("ROTATED-123", new() { Format = format, Width = 512, Height = 200 });
        using var original = SKBitmap.Decode(generated.ToArray());
        var bounds = ContentBounds(original);
        (int Left, int Top, int Right, int Bottom) expected = rotation switch
        {
            90 => (original.Height - 1 - bounds.Bottom, bounds.Left,
                original.Height - 1 - bounds.Top, bounds.Right),
            180 => (original.Width - 1 - bounds.Right, original.Height - 1 - bounds.Bottom,
                original.Width - 1 - bounds.Left, original.Height - 1 - bounds.Top),
            _ => (bounds.Top, original.Width - 1 - bounds.Right, bounds.Bottom, original.Width - 1 - bounds.Left)
        };
        using var rotated = new MemoryStream();
        var result = await new ImageProcessor().ProcessAsync(generated, rotated, new() { RotationDegrees = rotation });
        rotated.Position = 0;
        var decoded = Assert.Single(await _processor.DecodeAsync(rotated, new() { Formats = [format] }));
        Assert.Equal("ROTATED-123", decoded.Text);
        Assert.All(decoded.Points, point =>
        {
            Assert.InRange(point.X, 0, result.Width);
            Assert.InRange(point.Y, 0, result.Height);
            Assert.InRange(point.X, expected.Left - 2, expected.Right + 2);
            Assert.InRange(point.Y, expected.Top - 2, expected.Bottom + 2);
            if (format is CodeFormat.Code128 or CodeFormat.Code39)
                Assert.InRange(rotation is 90 or 270 ? point.X : point.Y,
                    (rotation is 90 or 270 ? result.Width : result.Height) / 2f - 1,
                    (rotation is 90 or 270 ? result.Width : result.Height) / 2f + 1);
        });
    }

    [Theory]
    [InlineData("DATA123", 512, 200)]
    [InlineData("ROTATED-123", 512, 200)]
    [InlineData("123456789012345678901", 320, 180)]
    public async Task DefaultDataMatrixQuietZoneWorksAcrossPayloadsSizesAndQuarterTurns(string text, int width, int height)
    {
        using var generated = await Generate(text, new() { Format = CodeFormat.DataMatrix, Width = width, Height = height });
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            generated.Position = 0;
            using var rotated = new MemoryStream();
            var size = await new ImageProcessor().ProcessAsync(generated, rotated, new() { RotationDegrees = rotation });
            rotated.Position = 0;
            var code = Assert.Single(await _processor.DecodeAsync(rotated, new() { Formats = [CodeFormat.DataMatrix] }));
            Assert.Equal(text, code.Text);
            Assert.All(code.Points, point =>
            {
                Assert.InRange(point.X, 0, size.Width);
                Assert.InRange(point.Y, 0, size.Height);
            });
        }
    }

    [Fact]
    public async Task ExplicitZeroDataMatrixMarginRetainsCallerChoiceEvenWhenItImpairsDetection()
    {
        using var generated = await Generate("ROTATED-123",
            new() { Format = CodeFormat.DataMatrix, Width = 512, Height = 200, Margin = 0 });
        Assert.Empty(await _processor.DecodeAsync(generated, new() { Formats = [CodeFormat.DataMatrix] }));
    }

    [Fact]
    public async Task DisablingAutoRotationDoesNotAllowAnImplicitOneDReaderRotation()
    {
        using var generated = await Generate("NO-ROTATION", new() { Format = CodeFormat.Code128, Width = 512, Height = 200 });
        using var rotated = new MemoryStream();
        await new ImageProcessor().ProcessAsync(generated, rotated, new() { RotationDegrees = 90 });
        rotated.Position = 0;
        Assert.Empty(await _processor.DecodeAsync(rotated,
            new() { Formats = [CodeFormat.Code128], AutoRotate = false, TryHarder = true }));
    }

    [Fact]
    public async Task ExifRotatedBarcodePointsUseTheDisplayedNonSquareFrame()
    {
        using var generated = await Generate("EXIF-CODE128", new() { Format = CodeFormat.Code128, Width = 512, Height = 200 });
        using var bitmap = SKBitmap.Decode(generated);
        using var jpeg = Encode(bitmap, SKEncodedImageFormat.Jpeg);
        using var input = new MemoryStream(WithExif(jpeg.ToArray(), 6));
        var decoded = Assert.Single(await _processor.DecodeAsync(input, new() { Formats = [CodeFormat.Code128] }));
        Assert.Equal("EXIF-CODE128", decoded.Text);
        Assert.All(decoded.Points, point =>
        {
            Assert.InRange(point.X, 99, 101);
            Assert.InRange(point.Y, 0, 511);
        });
    }

    [Fact]
    public async Task ExifOrientationAndTransparentBackgroundUseSharedImageDecoder()
    {
        using var generated = await Generate("EXIF-123", new() { Width = 256, Height = 256 });
        using var bitmap = SKBitmap.Decode(generated);
        using var jpeg = Encode(bitmap, SKEncodedImageFormat.Jpeg);
        using var oriented = new MemoryStream(WithExif(jpeg.ToArray(), 6));
        Assert.Equal("EXIF-123", Assert.Single(await _processor.DecodeAsync(oriented)).Text);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y) == SKColors.White) bitmap.SetPixel(x, y, SKColors.Transparent);
        using var transparent = Encode(bitmap);
        Assert.Equal("EXIF-123", Assert.Single(await _processor.DecodeAsync(transparent)).Text);
    }

    [Fact]
    public async Task DefaultReaderRecognizesAnInvertedQrImage()
    {
        using var generated = await Generate("inverted");
        using var bitmap = SKBitmap.Decode(generated);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, bitmap.GetPixel(x, y) == SKColors.White ? SKColors.Black : SKColors.White);
        using var inverted = Encode(bitmap);
        Assert.Equal("inverted", Assert.Single(await _processor.DecodeAsync(inverted)).Text);
    }

    [Fact]
    public async Task ImageAndOutputDimensionLimitsAreEnforced()
    {
        using var generated = await Generate("limits");
        var bytes = generated.ToArray();
        var limits = new ImageLimits { MaximumDimension = 256, MaximumPixels = 256 * 256, MaximumEncodedBytes = bytes.Length };
        Assert.Single(await _processor.DecodeAsync(generated, new() { Limits = limits }));
        using var tooManyPixels = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.DecodeAsync(tooManyPixels,
            new() { Limits = limits with { MaximumPixels = 256 * 256 - 1 } }));
        using var tooManyBytes = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.DecodeAsync(tooManyBytes,
            new() { Limits = limits with { MaximumEncodedBytes = bytes.Length - 1 } }));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.GenerateAsync("limits", output,
            new() { Width = 257, Limits = limits }));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task InvalidImagesAndOutputFailuresPropagateWithoutClosingCallerStreams()
    {
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.DecodeAsync(input));
        Assert.True(input.CanRead);
        using var output = new FailingStream();
        await Assert.ThrowsAsync<IOException>(() => _processor.GenerateAsync("failure", output));
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task SupportsCurrentStreamPositionsAndLeavesCallerStreamsOpen()
    {
        using var output = new MemoryStream();
        output.WriteByte(99);
        var result = await _processor.GenerateAsync("position", output);
        Assert.Equal(new ImageProcessingResult(256, 256, ImageOutputFormat.Png), result);
        Assert.Equal(99, output.ToArray()[0]);
        output.Position = 1;
        Assert.Equal("position", Assert.Single(await _processor.DecodeAsync(output)).Text);
        Assert.True(output.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task CancellationBeforeGenerationAndDuringDecodePreservesOwnership()
    {
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.GenerateAsync("cancel", output,
            cancellationToken: new CancellationToken(true)));
        Assert.Empty(output.ToArray());
        using var valid = await Generate("cancel");
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingStream(valid.ToArray(), cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.DecodeAsync(input,
            cancellationToken: cancellation.Token));
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    private async Task<MemoryStream> Generate(string text, CodeGenerationOptions? options = null)
    {
        var output = new MemoryStream();
        try
        {
            await _processor.GenerateAsync(text, output, options);
            Assert.True(output.CanWrite);
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private static MemoryStream Image(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        return Encode(bitmap);
    }

    private static (int Left, int Top, int Right, int Bottom) ContentBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y) == SKColors.Black)
            {
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        return (left, top, right, bottom);
    }

    private static MemoryStream Encode(SKBitmap bitmap, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return new(data.ToArray());
    }

    private static byte[] WithExif(byte[] jpeg, int origin)
    {
        byte[] exif = [.. "Exif"u8, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)origin, 0, 0, 0, 0, 0, 0, 0];
        return [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(exif.Length + 2), .. exif, .. jpeg.AsSpan(2)];
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("write failed"));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException(new IOException("write failed"));
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
