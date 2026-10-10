using MS.Microservice.Media.Inference.BackgroundRemoval;
using MS.Microservice.Media.Images;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.BackgroundRemoval;

public sealed class BackgroundPixelsTests
{
    [Fact]
    public void ConfigurationAndStreams_ValidateBeforeModelOrImageIo()
    {
        var options = new BiRefNetBackgroundRemovalOptions { ModelPath = "not-read" };
        options.Validate();
        Assert.Equal(Math.Min(2, Environment.ProcessorCount), options.CpuThreads);
        Assert.Throws<ArgumentException>(() => (options with { CpuThreads = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { CpuThreads = 65 }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Limits = new() { MaximumEncodedBytes = 0 } }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Limits = new() { MaximumPixels = 0 } }).Validate());
        using var input = new MemoryStream([1]);
        using var output = new MemoryStream();
        using var mask = new MemoryStream();
        BiRefNetBackgroundRemover.ValidateStreams(input, output, null);
        BiRefNetBackgroundRemover.ValidateStreams(input, output, mask);
        Assert.Throws<ArgumentException>(() => BiRefNetBackgroundRemover.ValidateStreams(input, input, mask));
        Assert.Throws<ArgumentException>(() => BiRefNetBackgroundRemover.ValidateStreams(input, output, input));
        Assert.Throws<ArgumentException>(() => BiRefNetBackgroundRemover.ValidateStreams(input, output, output));
        using var readOnly = new MemoryStream([1], writable: false);
        Assert.Throws<ArgumentException>(() => BiRefNetBackgroundRemover.ValidateStreams(input, readOnly, null));
        input.Dispose();
        Assert.Throws<ArgumentException>(() => BiRefNetBackgroundRemover.ValidateStreams(input, output, mask));
    }

    [Fact]
    public void Tensor_CompositesWhiteBeforeBilinearInterpolationThenUsesImageNetNchw()
    {
        using var source = BackgroundPixels.CreateStraightBitmap(2, 1);
        source.SetPixel(0, 0, new(0, 0, 255, 0));
        source.SetPixel(1, 0, SKColors.Red);
        var tensor = BackgroundPixels.CreateTensor(source, 3, default);
        Assert.Equal(27, tensor.Length);
        Assert.Equal((1 - .485f) / .229f, tensor[0], 5);
        Assert.Equal((1 - .456f) / .224f, tensor[9], 5);
        Assert.Equal((1 - .406f) / .225f, tensor[18], 5);
        Assert.Equal((1 - .485f) / .229f, tensor[1], 5);
        Assert.Equal((.5f - .456f) / .224f, tensor[10], 5);
        Assert.Equal((.5f - .406f) / .225f, tensor[19], 5);
        Assert.Equal((0 - .456f) / .224f, tensor[11], 5);
        Assert.Equal((0 - .406f) / .225f, tensor[20], 5);
    }

    [Fact]
    public void StableSigmoid_HandlesLargeFiniteValuesAndRejectsWrongShape()
    {
        var values = BackgroundPixels.Probabilities([-1000, 0, 1000, MathF.Log(3)], 2, default);
        Assert.Equal(0, values[0]);
        Assert.Equal(.5f, values[1]);
        Assert.Equal(1, values[2]);
        Assert.Equal(.75f, values[3], 6);
        Assert.Throws<InvalidDataException>(() => BackgroundPixels.Probabilities([1, 2, 3], 2, default));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonFiniteLogits_AreRejectedBeforeOutput(float value) =>
        Assert.Throws<InvalidDataException>(() => BackgroundPixels.Probabilities([0, 0, value, 0], 2, default));

    [Fact]
    public void HalfPixelMaskResize_IncludesBordersAndUsesOneFinalAlphaForBothOutputs()
    {
        using var source = BackgroundPixels.CreateStraightBitmap(3, 3);
        source.Erase(new SKColor(17, 89, 151));
        using var output = BackgroundPixels.CreateStraightBitmap(3, 3);
        using var mask = new SKBitmap(3, 3);
        var statistics = BackgroundPixels.Compose(source, output, mask, [0, 1, 1, 0], 2, default);
        byte[] expected = [0, 128, 255, 128, 128, 128, 255, 128, 0];
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var alpha = expected[y * 3 + x];
            Assert.Equal(alpha, output.GetPixel(x, y).Alpha);
            Assert.Equal(new SKColor(alpha, alpha, alpha, 255), mask.GetPixel(x, y));
            if (alpha > 0) Assert.Equal(new SKColor(17, 89, 151, alpha), output.GetPixel(x, y));
        }
        Assert.Equal(7, statistics.NonTransparent);
        Assert.Equal((255 * 2 + 128 * 5) / (255d * 9), statistics.MeanAlpha, 8);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(64, 32)]
    [InlineData(128, 64)]
    [InlineData(255, 128)]
    public void EffectiveMatte_MultipliesSourceAlphaAndCannotRestoreTransparentPixels(byte original, byte expected)
    {
        using var source = BackgroundPixels.CreateStraightBitmap(1, 1);
        source.SetPixel(0, 0, new(80, 120, 160, original));
        using var output = BackgroundPixels.CreateStraightBitmap(1, 1);
        using var mask = new SKBitmap(1, 1);
        BackgroundPixels.Compose(source, output, mask, [.5f], 1, default);
        Assert.Equal(expected, output.GetPixel(0, 0).Alpha);
        Assert.Equal(expected, mask.GetPixel(0, 0).Red);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void NarrowImages_ClampCoordinatesAndSupportAnOmittedMask(int width, int height)
    {
        using var source = BackgroundPixels.CreateStraightBitmap(width, height);
        source.Erase(SKColors.CornflowerBlue);
        using var output = BackgroundPixels.CreateStraightBitmap(width, height);
        BackgroundPixels.Compose(source, output, null, [.5f], 1, default);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) Assert.Equal(128, output.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void PngEncoding_KeepStraightNormalizedRgbAndAnExactlyMatchingGrayscaleMatte()
    {
        using var source = BackgroundPixels.CreateStraightBitmap(2, 1);
        source.SetPixel(0, 0, new(17, 89, 151, 128));
        source.SetPixel(1, 0, new(255, 64, 32, 255));
        using var output = BackgroundPixels.CreateStraightBitmap(2, 1);
        using var mask = new SKBitmap(2, 1);
        BackgroundPixels.Compose(source, output, mask, [.5f], 1, default);
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var decoded = DecodeStraight(encoded.ToArray());
        Assert.Equal(new SKColor(17, 89, 151, 64), decoded.GetPixel(0, 0));
        Assert.Equal(new SKColor(255, 64, 32, 128), decoded.GetPixel(1, 0));
        using var maskImage = SKImage.FromBitmap(mask);
        using var maskEncoded = maskImage.Encode(SKEncodedImageFormat.Png, 100);
        using var maskDecoded = DecodeStraight(maskEncoded.ToArray());
        Assert.Equal(decoded.GetPixel(0, 0).Alpha, maskDecoded.GetPixel(0, 0).Red);
        Assert.Equal(decoded.GetPixel(1, 0).Alpha, maskDecoded.GetPixel(1, 0).Red);
    }

    [Fact]
    public void Cancellation_IsCheckedBeforeEachPixelStage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var source = BackgroundPixels.CreateStraightBitmap(1, 1);
        using var output = BackgroundPixels.CreateStraightBitmap(1, 1);
        Assert.ThrowsAny<OperationCanceledException>(() => BackgroundPixels.CreateTensor(source, 1, cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => BackgroundPixels.Probabilities([0], 1, cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => BackgroundPixels.Compose(source, output, null, [.5f], 1, cancellation.Token));
    }

    internal static SKBitmap DecodeStraight(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data)!;
        var bitmap = BackgroundPixels.CreateStraightBitmap(codec.Info.Width, codec.Info.Height);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels()));
        return bitmap;
    }
}
