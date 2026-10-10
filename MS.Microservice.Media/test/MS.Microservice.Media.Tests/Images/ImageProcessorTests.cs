using MS.Microservice.Media.Images;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Tests.Images;

public sealed class ImageProcessorTests
{
    private readonly ImageProcessor _processor = new();
    private static readonly SKColor[] Colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.Cyan, SKColors.Magenta];

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task Process_QuarterTurnsPreserveExactPixels(int degrees)
    {
        var bytes = CreateImage(3, 2, (x, y) => Colors[y * 3 + x]);
        using var output = await Process(bytes, new ImageProcessingOptions { RotationDegrees = degrees });
        using var actual = SKBitmap.Decode(output);
        var expected = degrees switch
        {
            90 => new[] { 3, 0, 4, 1, 5, 2 },
            180 => [5, 4, 3, 2, 1, 0],
            270 => [2, 5, 1, 4, 0, 3],
            _ => [0, 1, 2, 3, 4, 5]
        };
        Assert.Equal(degrees is 90 or 270 ? 2 : 3, actual.Width);
        Assert.Equal(degrees is 90 or 270 ? 3 : 2, actual.Height);
        Assert.Equal(expected.Select(index => Colors[index]), Pixels(actual));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Process_FlipsUseDisplayedCoordinates(bool horizontal, bool vertical)
    {
        var bytes = CreateImage(3, 2, (x, y) => Colors[y * 3 + x]);
        using var output = await Process(bytes, new ImageProcessingOptions { FlipHorizontal = horizontal, FlipVertical = vertical });
        using var actual = SKBitmap.Decode(output);

        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 3; x++)
            Assert.Equal(Colors[(vertical ? 1 - y : y) * 3 + (horizontal ? 2 - x : x)], actual.GetPixel(x, y));
    }

    [Fact]
    public async Task Process_CropThenRotateThenFlipUsesSpecifiedOrder()
    {
        var bytes = CreateImage(3, 2, (x, y) => Colors[y * 3 + x]);
        using var output = await Process(bytes, new ImageProcessingOptions
        {
            Crop = new(1, 0, 2, 2), RotationDegrees = 90, FlipHorizontal = true,
            Resize = new(2, 2), ResizeMode = ImageResizeMode.Stretch
        });
        using var actual = SKBitmap.Decode(output);

        Assert.Equal(new[] { Colors[1], Colors[4], Colors[2], Colors[5] }, Pixels(actual));
    }

    [Theory]
    [InlineData(ImageResizeMode.Contain, 3, 2)]
    [InlineData(ImageResizeMode.Cover, 3, 3)]
    [InlineData(ImageResizeMode.Stretch, 3, 3)]
    public async Task Process_ResizeModesProduceTheirDocumentedDimensions(ImageResizeMode mode, int width, int height)
    {
        using var output = await Process(CreateImage(4, 2, (_, _) => SKColors.Red),
            new ImageProcessingOptions { Resize = new(3, 3), ResizeMode = mode });
        using var actual = SKBitmap.Decode(output);

        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
        Assert.All(Pixels(actual), color => Assert.Equal(SKColors.Red, color));
    }

    [Fact]
    public async Task Process_CoverCropsFromTheCenter()
    {
        var bytes = CreateImage(4, 2, (x, _) => x == 0 ? SKColors.Red : x == 3 ? SKColors.Blue : SKColors.Green);
        using var output = await Process(bytes, new ImageProcessingOptions { Resize = new(2, 2), ResizeMode = ImageResizeMode.Cover });
        using var actual = SKBitmap.Decode(output);

        Assert.All(Pixels(actual), color => Assert.Equal(SKColors.Green, color));
    }

    [Theory]
    [InlineData(ImageOutputFormat.Png)]
    [InlineData(ImageOutputFormat.Jpeg)]
    [InlineData(ImageOutputFormat.Webp)]
    public async Task Process_FormatsHaveRealHeadersAndDecodeCorrectly(ImageOutputFormat format)
    {
        using var output = await Process(CreateImage(16, 16, (_, _) => new SKColor(255, 0, 0, 128)),
            new ImageProcessingOptions { Format = format, Quality = 100, JpegBackground = new(0, 0, 255) });
        var bytes = output.ToArray();
        Assert.True(format switch
        {
            ImageOutputFormat.Png => bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ImageOutputFormat.Jpeg => bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }),
            _ => bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
        });
        using var actual = SKBitmap.Decode(output);
        var pixel = actual.GetPixel(8, 8);
        if (format == ImageOutputFormat.Jpeg)
        {
            Assert.Equal(255, pixel.Alpha);
            Assert.InRange(pixel.Red, (byte)124, (byte)132);
            Assert.InRange(pixel.Green, (byte)0, (byte)4);
            Assert.InRange(pixel.Blue, (byte)123, (byte)132);
        }
        else Assert.Equal(128, pixel.Alpha);
    }

    [Fact]
    public async Task Process_PngPreservesFullyTransparentPixelsAndJpegFlattensToWhiteByDefault()
    {
        var bytes = CreateImage(8, 8, (_, _) => SKColors.Transparent);
        using var png = await Process(bytes);
        using var jpeg = await Process(bytes, new ImageProcessingOptions { Format = ImageOutputFormat.Jpeg, Quality = 100 });
        using var pngBitmap = SKBitmap.Decode(png);
        using var jpegBitmap = SKBitmap.Decode(jpeg);

        Assert.Equal(0, pngBitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(SKColors.White, jpegBitmap.GetPixel(0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Process_NormalizesAllExifOriginsBeforeCropping(int origin)
    {
        var jpeg = CreateImage(3, 2, (x, y) => new SKColor((byte)(20 + (y * 3 + x) * 35), (byte)(20 + (y * 3 + x) * 35), (byte)(20 + (y * 3 + x) * 35)),
            SKEncodedImageFormat.Jpeg);
        using var baseline = SKBitmap.Decode(jpeg);
        var originalPixels = Pixels(baseline);
        using var output = await Process(WithExif(jpeg, origin));
        using var oriented = SKBitmap.Decode(output);
        int[] indices = origin switch
        {
            2 => [2, 1, 0, 5, 4, 3], 3 => [5, 4, 3, 2, 1, 0], 4 => [3, 4, 5, 0, 1, 2],
            5 => [0, 3, 1, 4, 2, 5], 6 => [3, 0, 4, 1, 5, 2], 7 => [5, 2, 4, 1, 3, 0],
            8 => [2, 5, 1, 4, 0, 3], _ => [0, 1, 2, 3, 4, 5]
        };
        Assert.Equal(origin >= 5 ? 2 : 3, oriented.Width);
        Assert.Equal(origin >= 5 ? 3 : 2, oriented.Height);
        Assert.Equal(indices.Select(index => originalPixels[index]), Pixels(oriented));

        using var croppedOutput = await Process(WithExif(jpeg, origin), new ImageProcessingOptions { Crop = new(0, 0, 1, 1) });
        using var cropped = SKBitmap.Decode(croppedOutput);
        Assert.Equal(originalPixels[indices[0]], cropped.GetPixel(0, 0));
    }

    [Fact]
    public async Task Process_RasterAndEncodedByteLimitsAllowExactBoundaryAndRejectAboveIt()
    {
        var bytes = CreateImage(3, 2, (_, _) => SKColors.Red);
        var limits = new ImageLimits { MaximumDimension = 3, MaximumPixels = 6, MaximumEncodedBytes = bytes.Length };
        using var valid = await Process(bytes, new ImageProcessingOptions { Limits = limits });

        await AssertRejected(bytes, new ImageProcessingOptions { Limits = limits with { MaximumPixels = 5 } });
        await AssertRejected(bytes, new ImageProcessingOptions { Limits = limits with { MaximumDimension = 2 } });
        await AssertRejected(bytes, new ImageProcessingOptions { Limits = limits with { MaximumEncodedBytes = bytes.Length - 1 } });
        await AssertRejected(bytes, new ImageProcessingOptions { Limits = limits, Resize = new(3, 3) });
    }

    [Fact]
    public async Task Process_InvalidCropRotationQualityOrDimensionsDoNotWriteOutput()
    {
        var bytes = CreateImage(3, 2, (_, _) => SKColors.Red);
        ImageProcessingOptions[] invalid =
        [
            new() { Crop = new(-1, 0, 1, 1) }, new() { Crop = new(2, 1, 2, 1) },
            new() { Crop = new(int.MaxValue, 0, int.MaxValue, 1) }, new() { Crop = new(0, 0, 0, 1) },
            new() { RotationDegrees = 45 }, new() { Quality = -1 }, new() { Quality = 101 },
            new() { Resize = new(0, 2) }, new() { Limits = new() { MaximumPixels = 0 } }
        ];
        foreach (var options in invalid)
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<Exception>(() => _processor.ProcessAsync(input, output, options));
            Assert.Empty(output.ToArray());
            Assert.True(input.CanRead);
            Assert.True(output.CanWrite);
        }
    }

    [Fact]
    public async Task Process_InvalidTruncatedAndAnimatedImagesDoNotProduceOutput()
    {
        var bytes = CreateImage(32, 32, (x, y) => new SKColor((byte)(x * 7), (byte)(y * 7), (byte)(x + y)));
        await AssertRejected([1, 2, 3, 4]);
        await AssertRejected(bytes[..(bytes.Length / 2)]);
        byte[] gif = [.. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 255, 255, 255,
            0x21, 0xF9, 4, 0, 1, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 1, 0,
            0x21, 0xF9, 4, 0, 1, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 1, 0, 0x3B];
        using var input = new MemoryStream(gif);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => _processor.ProcessAsync(input, output));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Process_PngWithoutIendRetainsFullyDecodedPixels()
    {
        var bytes = CreateImage(16, 16, (_, _) => SKColors.Red);
        using var output = await Process(bytes[..^12]);
        using var decoded = SKBitmap.Decode(output);

        Assert.Equal(16, decoded.Width);
        Assert.Equal(16, decoded.Height);
        Assert.All(Pixels(decoded), color => Assert.Equal(SKColors.Red, color));
    }

    [Fact]
    public async Task Process_JpegMissingEoiIsReportedIncompleteAndRejected()
    {
        var bytes = CreateImage(16, 16, (_, _) => SKColors.Red, SKEncodedImageFormat.Jpeg);

        await AssertRejected(bytes[..^2]);
    }

    [Fact]
    public async Task Process_OutputFailurePropagatesAndLeavesCallerStreamsOpen()
    {
        using var input = new MemoryStream(CreateImage(3, 2, (_, _) => SKColors.Blue));
        using var output = new FailingWriteStream();

        await Assert.ThrowsAsync<IOException>(() => _processor.ProcessAsync(input, output));

        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Process_CancellationBeforeOrDuringReadLeavesStreamsOpen(bool duringRead)
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new ChunkedReadStream(CreateImage(3, 2, (_, _) => SKColors.Red))
        {
            AfterRead = duringRead ? cancellation.Cancel : null
        };
        using var output = new MemoryStream();
        if (!duringRead) cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.ProcessAsync(
            input, output, cancellationToken: cancellation.Token));

        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Process_NonseekableShortReadsAndCurrentOutputPositionAreSupported()
    {
        using var input = new ChunkedReadStream(CreateImage(3, 2, (_, _) => SKColors.Red));
        using var output = new MemoryStream();
        output.WriteByte(99);

        var result = await _processor.ProcessAsync(input, output);

        Assert.Equal(new ImageProcessingResult(3, 2, ImageOutputFormat.Png), result);
        Assert.Equal(99, output.ToArray()[0]);
        using var decoded = SKBitmap.Decode(output.ToArray()[1..]);
        Assert.Equal(SKColors.Red, decoded.GetPixel(0, 0));
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Process_RejectsSameStreamWithoutMutatingIt()
    {
        var bytes = CreateImage(1, 1, (_, _) => SKColors.Blue);
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<ArgumentException>(() => _processor.ProcessAsync(stream, stream));

        Assert.Equal(bytes, stream.ToArray());
        Assert.Equal(0, stream.Position);
    }

    private async Task<MemoryStream> Process(byte[] bytes, ImageProcessingOptions? options = null)
    {
        using var input = new MemoryStream(bytes);
        var output = new MemoryStream();
        try
        {
            await _processor.ProcessAsync(input, output, options);
            Assert.True(input.CanRead);
            Assert.True(output.CanWrite);
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private async Task AssertRejected(byte[] bytes, ImageProcessingOptions? options = null)
    {
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.ProcessAsync(input, output, options));
        Assert.Empty(output.ToArray());
    }

    private static byte[] CreateImage(int width, int height, Func<int, int, SKColor> color, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, color(x, y));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 100);
        return encoded.ToArray();
    }

    private static SKColor[] Pixels(SKBitmap bitmap) => Enumerable.Range(0, bitmap.Height)
        .SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, y))).ToArray();

    private static byte[] WithExif(byte[] jpeg, int origin)
    {
        byte[] exif = [.. "Exif"u8, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)origin, 0, 0, 0, 0, 0, 0, 0];
        return [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(exif.Length + 2), .. exif, .. jpeg.AsSpan(2)];
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("output failed"));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException(new IOException("output failed"));
    }

    private sealed class ChunkedReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public Action? AfterRead { get; init; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 3));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
            AfterRead?.Invoke();
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
