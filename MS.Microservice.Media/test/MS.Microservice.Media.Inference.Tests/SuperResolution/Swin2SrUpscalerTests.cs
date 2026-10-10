using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference.SuperResolution;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.SuperResolution;

public sealed class Swin2SrUpscalerTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(7, 1, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 3, 0)]
    [InlineData(5, 3, 1)]
    [InlineData(15, 8, 1)]
    public void PaddingReflectsWithoutDuplicatingTheEdge(int index, int length, int expected)
        => Assert.Equal(expected, Swin2SrUpscaler.Reflect(index, length));

    [Fact]
    public void AlphaUsesBilinearPixelCentersAndClampsOuterEdges()
    {
        using var source = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        source.SetPixel(0, 0, SKColors.Transparent);
        source.SetPixel(1, 0, SKColors.White);
        Assert.Equal(new byte[] { 0, 64, 191, 255 }, Enumerable.Range(0, 4).Select(x => Swin2SrUpscaler.ScaleAlpha(source, x, 0)));
        Assert.Equal(new byte[] { 0, 64, 191, 255 }, Enumerable.Range(0, 4).Select(x => Swin2SrUpscaler.ScaleAlpha(source, x, 1)));
    }

    [Theory]
    [InlineData(0, 128, 16)]
    [InlineData(65, 128, 16)]
    [InlineData(1, 31, 0)]
    [InlineData(1, 257, 0)]
    [InlineData(1, 127, 16)]
    [InlineData(1, 32, 24)]
    [InlineData(1, 128, 17)]
    [InlineData(1, 128, -8)]
    public async Task InvalidConfigurationFailsBeforeOpeningAModel(int threads, int tile, int context)
        => await Assert.ThrowsAsync<ArgumentException>(() => Swin2SrUpscaler.CreateAsync(new()
        { ModelPath = "does-not-exist.onnx", CpuThreads = threads, TileSize = tile, ContextPixels = context }));

    [Fact]
    public async Task VerifiedModelRejectsLengthAndHashAndKeepsItsVerifiedBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), "swin-model-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 }));
            await Assert.ThrowsAsync<InvalidDataException>(() => VerifiedOnnxModel.ReadAsync(path, 2, sha, default));
            await Assert.ThrowsAsync<InvalidDataException>(() => VerifiedOnnxModel.ReadAsync(path, 3, new string('0', 64), default));
            var snapshot = await VerifiedOnnxModel.ReadAsync(path, 3, sha, default);
            await File.WriteAllBytesAsync(path, [9, 9, 9]);
            Assert.Equal(new byte[] { 1, 2, 3 }, snapshot);
        }
        finally { File.Delete(path); }
    }

    [SuperResolutionTheory]
    [InlineData(1, 1, 255)]
    [InlineData(17, 25, 255)]
    [InlineData(65, 37, 255)]
    [InlineData(5, 7, 0)]
    [InlineData(9, 3, 128)]
    public async Task RealModelDoublesOddAndTiledImagesAndRetainsAlpha(int width, int height, byte alpha)
    {
        await using var upscaler = await CreateAsync(tile: 32, context: 8);
        using var input = Image(width, height, new(128, 128, 128, alpha));
        using var output = new MemoryStream();
        var result = await upscaler.UpscaleAsync(input, output);
        Assert.Equal((width * 2, height * 2), (result.Width, result.Height));
        Assert.Equal(((width + 31) / 32) * ((height + 31) / 32), result.Tiles);
        Assert.True(result.ReviewRecommended);
        Assert.True(input.CanRead && output.CanWrite);
        using var png = SKBitmap.Decode(output.ToArray());
        Assert.Equal((result.Width, result.Height), (png.Width, png.Height));
        foreach (var pixel in png.Pixels)
        {
            Assert.Equal(alpha, pixel.Alpha);
            if (alpha == 255)
            {
                Assert.InRange(pixel.Red, (byte)120, (byte)136);
                Assert.InRange(pixel.Green, (byte)120, (byte)136);
                Assert.InRange(pixel.Blue, (byte)120, (byte)136);
            }
        }
    }

    [SuperResolutionFact]
    public async Task RealLearnedOutputIsNotAnOrdinaryPixelReplication()
    {
        await using var upscaler = await CreateAsync();
        using var source = new SKBitmap(19, 13);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                source.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.Black : SKColors.White);
        using var input = Encode(source);
        using var output = new MemoryStream();
        await upscaler.UpscaleAsync(input, output);
        using var result = SKBitmap.Decode(output.ToArray());
        Assert.Contains(result.Pixels, pixel => pixel.Red is > 10 and < 245);
    }

    [SuperResolutionFact]
    public async Task BudgetsAreCheckedBeforeAnyDestinationBytesAreWritten()
    {
        await using var upscaler = await Swin2SrUpscaler.CreateAsync(new()
        { ModelPath = Model(), MaximumOutputPixels = 35 });
        using var input = Image(3, 3, SKColors.Gray);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => upscaler.UpscaleAsync(input, output));
        Assert.Equal(0, output.Length);
    }

    [SuperResolutionFact]
    public async Task KeepsCurrentPositionsAndSupportsNonSeekableInput()
    {
        await using var upscaler = await CreateAsync();
        using var png = Image(7, 5, SKColors.Gray);
        using var positioned = new MemoryStream([1, 2, 3, .. png.ToArray()]);
        positioned.Position = 3;
        using var output = new MemoryStream();
        output.Write([9, 8]);
        await upscaler.UpscaleAsync(positioned, output);
        Assert.Equal(new byte[] { 9, 8 }, output.ToArray()[..2]);
        using var nonSeekable = new NonSeekable(png.ToArray());
        using var other = new MemoryStream();
        await upscaler.UpscaleAsync(nonSeekable, other);
        Assert.True(nonSeekable.CanRead);
    }

    [SuperResolutionFact]
    public async Task CancellationAndDisposalDoNotPublishSuccessOrBreakALaterCall()
    {
        var upscaler = await CreateAsync();
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            using var input = Image(256, 256, SKColors.Gray);
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upscaler.UpscaleAsync(input, output, cancellation.Token));
            Assert.Equal(0, output.Length);
            using var normal = Image(3, 3, SKColors.Gray);
            await upscaler.UpscaleAsync(normal, output);
            Assert.NotEqual(0, output.Length);
            await upscaler.DisposeAsync();
            normal.Position = 0;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => upscaler.UpscaleAsync(normal, output));
        }
        finally { await upscaler.DisposeAsync(); }
    }

    [SuperResolutionFact]
    public async Task RejectsAliasedOrDamagedInputsAndEnforcesInputBytes()
    {
        await using var upscaler = await Swin2SrUpscaler.CreateAsync(new()
        { ModelPath = Model(), Limits = new ImageLimits { MaximumEncodedBytes = 8 } });
        using var stream = Image(4, 3, SKColors.Gray);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => upscaler.UpscaleAsync(stream, stream));
        await Assert.ThrowsAsync<InvalidDataException>(() => upscaler.UpscaleAsync(stream, output));
        using var damaged = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<InvalidDataException>(() => upscaler.UpscaleAsync(damaged, output));
        Assert.Equal(0, output.Length);
    }

    private static string Model() => Environment.GetEnvironmentVariable(SuperResolutionFactAttribute.Variable)!;
    private static Task<Swin2SrUpscaler> CreateAsync(int tile = 128, int context = 16)
        => Swin2SrUpscaler.CreateAsync(new() { ModelPath = Model(), TileSize = tile, ContextPixels = context });
    private static MemoryStream Image(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return Encode(bitmap);
    }
    private static MemoryStream Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(encoded.ToArray());
    }
    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}

internal sealed class SuperResolutionFactAttribute : FactAttribute
{
    internal const string Variable = "MS_MEDIA_SWIN2SR_MODEL";
    public SuperResolutionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))) Skip = $"Set {Variable} for real local twofold CPU inference.";
    }
}

internal sealed class SuperResolutionTheoryAttribute : TheoryAttribute
{
    public SuperResolutionTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SuperResolutionFactAttribute.Variable)))
            Skip = $"Set {SuperResolutionFactAttribute.Variable} for real local twofold CPU inference.";
    }
}
