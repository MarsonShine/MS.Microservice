using MS.Microservice.Media.Images;
using SkiaSharp;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MS.Microservice.Media.Inference.Tests;

public sealed class MiganInpainterTests
{
    [Theory]
    [InlineData(1, 1, 0, 0)]
    [InlineData(9, 7, 0, 0)]
    [InlineData(9, 7, 8, 6)]
    [InlineData(11, 13, 5, 6)]
    public void InferenceMask_PadsThreePixelsClippedToImageAndInvertsKnownConvention(int width, int height, int pointX, int pointY)
    {
        var edits = new byte[width * height];
        edits[pointY * width + pointX] = 1;

        var known = InpaintingPixels.CreateKnownMask(edits, width, height, default);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            Assert.Equal(Math.Abs(x - pointX) <= 3 && Math.Abs(y - pointY) <= 3 ? 0 : 255, known[y * width + x]);
        Assert.Equal(1, edits.Count(value => value == 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InferenceMask_EmptyOrFull_HasUniformKnownValues(byte selected)
    {
        var edits = Enumerable.Repeat(selected, 35).ToArray();

        Assert.All(InpaintingPixels.CreateKnownMask(edits, 7, 5, default), value => Assert.Equal(selected == 0 ? 255 : 0, value));
    }

    [Fact]
    public void InferenceMask_SeededVariedMasksMatchIndependentDistanceDefinition()
    {
        var random = new Random(2389);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var width = random.Next(1, 18);
            var height = random.Next(1, 18);
            var edits = Enumerable.Range(0, width * height).Select(_ => (byte)(random.Next(6) == 0 ? 1 : 0)).ToArray();
            var known = InpaintingPixels.CreateKnownMask(edits, width, height, default);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var selectedInRange = false;
                for (var yy = Math.Max(0, y - 3); yy <= Math.Min(height - 1, y + 3); yy++)
                for (var xx = Math.Max(0, x - 3); xx <= Math.Min(width - 1, x + 3); xx++)
                    selectedInRange |= edits[yy * width + xx] != 0;
                Assert.Equal(selectedInRange ? 0 : 255, known[y * width + x]);
            }
        }
    }

    [Fact]
    public void ReadMask_WhiteSelectsAndBlackKeeps()
    {
        using var mask = Bitmap(4, 3, (x, y) => x == y ? SKColors.White : SKColors.Black);

        var (edits, count) = InpaintingPixels.ReadMask(mask, 4, 3, default);

        Assert.Equal(3, count);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 }, edits);
    }

    [Theory]
    [InlineData(127, 127, 127, 255)]
    [InlineData(255, 0, 0, 255)]
    [InlineData(255, 255, 255, 128)]
    [InlineData(0, 0, 0, 0)]
    public void ReadMask_NonBinaryColoredOrTransparent_IsRejected(byte red, byte green, byte blue, byte alpha)
    {
        using var mask = Bitmap(1, 1, (_, _) => new SKColor(red, green, blue, alpha));

        Assert.Throws<ArgumentException>(() => InpaintingPixels.ReadMask(mask, 1, 1, default));
    }

    [Fact]
    public void ReadMask_DimensionMismatch_IsRejected()
    {
        using var mask = Bitmap(2, 3, (_, _) => SKColors.Black);

        Assert.Throws<ArgumentException>(() => InpaintingPixels.ReadMask(mask, 3, 2, default));
    }

    [Fact]
    public void RgbPlanesAndComposition_PreserveOutsidePixelsAndAllAlpha()
    {
        using var image = Bitmap(3, 2, (x, y) => new SKColor((byte)(30 + x * 50), (byte)(40 + y * 70), 80, (byte)(x == 0 ? 0 : x == 1 ? 128 : 255)));
        var before = Pixels(image);
        var actualPlanes = InpaintingPixels.CreateRgb(image, default);
        Assert.Equal(before.Select(pixel => pixel.Red).Concat(before.Select(pixel => pixel.Green)).Concat(before.Select(pixel => pixel.Blue)), actualPlanes);
        var model = Enumerable.Repeat((byte)200, 18).ToArray();
        byte[] edits = [0, 1, 0, 0, 0, 1];

        var changed = InpaintingPixels.Compose(image, edits, model, default);
        var after = Pixels(image);

        Assert.Equal(2, changed);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Equal(before[i].Alpha, after[i].Alpha);
            if (edits[i] == 0) Assert.Equal(before[i], after[i]);
        }
    }

    [Fact]
    public void PixelPreparation_PreCanceled_DoesNotMutateInput()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var image = Bitmap(3, 3, (_, _) => SKColors.Red);
        Assert.ThrowsAny<OperationCanceledException>(() => InpaintingPixels.CreateRgb(image, source.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => InpaintingPixels.CreateKnownMask(new byte[9], 3, 3, source.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => InpaintingPixels.ReadMask(image, 3, 3, source.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => InpaintingPixels.Compose(image, new byte[9], new byte[27], source.Token));
        Assert.All(Pixels(image), pixel => Assert.Equal(SKColors.Red, pixel));
    }

    [Fact]
    public async Task Factory_RejectsInvalidOptionsBeforeOpeningModel()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => MiganInpainter.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => MiganInpainter.CreateAsync(new() { ModelPath = " " }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => MiganInpainter.CreateAsync(new() { ModelPath = "missing", CpuThreads = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => MiganInpainter.CreateAsync(new() { ModelPath = "missing", Limits = new() { MaximumPixels = 0 } }));
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MiganInpainter.CreateAsync(new() { ModelPath = "missing" }, source.Token));
    }

    [Fact]
    public async Task Factory_RejectsMissingWrongLengthAndWrongDigestModelsWithoutNativeLoading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"migan-untrusted-{Guid.NewGuid():N}.onnx");
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => MiganInpainter.CreateAsync(new() { ModelPath = path }));
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            await Assert.ThrowsAsync<InvalidDataException>(() => MiganInpainter.CreateAsync(new() { ModelPath = path }));
            using (var file = File.OpenWrite(path)) file.SetLength(MiganInpainter.ModelBytes);
            await Assert.ThrowsAsync<InvalidDataException>(() => MiganInpainter.CreateAsync(new() { ModelPath = path }));
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(MiganInpainter.ModelBytes, exclusive.Length);
        }
        finally { File.Delete(path); }
    }

    [ModelTheory]
    [InlineData(3, 3)]
    [InlineData(64, 48)]
    [InlineData(257, 193)]
    public async Task RealModel_RepairsSelectedPixelsAndPreservesOutside(int width, int height)
    {
        await using var inpainter = await LoadModel();
        bool Select(int x, int y) => x >= width / 3 && x < Math.Max(width / 3 + 1, width * 2 / 3)
            && y >= height / 3 && y < Math.Max(height / 3 + 1, height * 2 / 3);
        using var input = Encoded(width, height, (x, y) => Select(x, y) ? SKColors.White : new SKColor((byte)(40 + x % 100), (byte)(60 + y % 100), 90));
        using var mask = Encoded(width, height, (x, y) => Select(x, y) ? SKColors.White : SKColors.Black);
        using var baseline = await Baseline(input.ToArray());
        using var output = new MemoryStream();

        var result = await inpainter.InpaintAsync(input, mask, output);
        output.Position = 0;
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(MiganInpainter.ModelId, result.ModelId);
        Assert.InRange(result.EditedPixelCount, 1, result.SelectedPixelCount);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            if (!Select(x, y)) Assert.Equal(baseline.GetPixel(x, y), actual.GetPixel(x, y));
        Assert.True(input.CanRead && mask.CanRead && output.CanWrite);
    }

    [ModelFact]
    public async Task RealModel_PngOutsidePixelsMatchNormalizedBaselineIncludingSemitransparency()
    {
        await using var inpainter = await LoadModel();
        byte[] alphas = [0, 1, 64, 128, 200, 255];
        using var input = Encoded(64, 48, (x, y) => new SKColor((byte)(50 + x), (byte)(70 + y), 120, alphas[x % alphas.Length]));
        using var mask = Encoded(64, 48, (x, y) => x is >= 24 and <= 32 && y is >= 18 and <= 27 ? SKColors.White : SKColors.Black);
        using var baseline = await Baseline(input.ToArray());
        using var output = new MemoryStream();

        await inpainter.InpaintAsync(input, mask, output);
        output.Position = 0;
        using var actual = SKBitmap.Decode(output.ToArray());
        for (var y = 0; y < 48; y++)
        for (var x = 0; x < 64; x++)
        {
            Assert.Equal(baseline.GetPixel(x, y).Alpha, actual.GetPixel(x, y).Alpha);
            if (!(x is >= 24 and <= 32 && y is >= 18 and <= 27))
                Assert.Equal(baseline.GetPixel(x, y), actual.GetPixel(x, y));
        }
    }

    [ModelTheory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task RealModel_MaskCoordinatesFollowExifNormalizedImage(int origin)
    {
        await using var inpainter = await LoadModel();
        using var jpeg = Encoded(8, 6, (x, y) => new SKColor((byte)(30 + x * 20), (byte)(40 + y * 20), 90), SKEncodedImageFormat.Jpeg);
        byte[] exif = [.. "Exif"u8, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)origin, 0, 0, 0, 0, 0, 0, 0];
        byte[] orientedInput = [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(exif.Length + 2), .. exif, .. jpeg.ToArray().AsSpan(2)];
        using var input = new MemoryStream(orientedInput);
        using var baseline = await Baseline(orientedInput);
        using var mask = Encoded(baseline.Width, baseline.Height, (x, y) => x == 1 && y == 1 ? SKColors.White : SKColors.Black);
        using var output = new MemoryStream();

        var result = await inpainter.InpaintAsync(input, mask, output);
        output.Position = 0;
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.Equal(baseline.Width, result.Width);
        Assert.Equal(baseline.Height, result.Height);
        Assert.Equal(1, result.SelectedPixelCount);
        for (var y = 0; y < baseline.Height; y++)
        for (var x = 0; x < baseline.Width; x++)
            if (x != 1 || y != 1) Assert.Equal(baseline.GetPixel(x, y), actual.GetPixel(x, y));
    }

    [ModelFact]
    public async Task EmptyMask_IsIdentityEvenBelowModelMinimumDimensions()
    {
        await using var inpainter = await LoadModel();
        using var input = Encoded(2, 1, (x, _) => x == 0 ? SKColors.Transparent : new SKColor(50, 100, 150, 128));
        using var mask = Encoded(2, 1, (_, _) => SKColors.Black);
        using var baseline = await Baseline(input.ToArray());
        using var output = new MemoryStream();

        var result = await inpainter.InpaintAsync(input, mask, output);
        output.Position = 0;
        using var actual = SKBitmap.Decode(output.ToArray());

        Assert.Equal(0, result.SelectedPixelCount);
        Assert.Equal(0, result.EditedPixelCount);
        Assert.Equal(Pixels(baseline), Pixels(actual));
    }

    [ModelTheory]
    [InlineData(1, 3)]
    [InlineData(3, 2)]
    public async Task NonemptyMask_BelowReflectionMinimumIsRejectedBeforeOutput(int width, int height)
    {
        await using var inpainter = await LoadModel();
        using var input = Encoded(width, height, (_, _) => SKColors.Red);
        using var mask = Encoded(width, height, (_, _) => SKColors.White);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => inpainter.InpaintAsync(input, mask, output));

        Assert.Equal(0, output.Length);
        Assert.True(input.CanRead && mask.CanRead && output.CanWrite);
    }

    [ModelFact]
    public async Task StreamAndMaskValidation_DoesNotCloseCallerStreams()
    {
        await using var inpainter = await LoadModel();
        using var input = Encoded(8, 6, (_, _) => SKColors.Red);
        using var wrongMask = Encoded(6, 8, (_, _) => SKColors.White);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => inpainter.InpaintAsync(input, wrongMask, output));
        await Assert.ThrowsAsync<ArgumentException>(() => inpainter.InpaintAsync(input, input, output));
        Assert.Equal(0, output.Length);
        Assert.True(input.CanRead && wrongMask.CanRead && output.CanWrite);
    }

    [ModelFact]
    public async Task PreCancellationAndInputLimits_DoNotPublishOutput()
    {
        await using var inpainter = await LoadModel(new ImageLimits { MaximumPixels = 20 });
        using var input = Encoded(8, 6, (_, _) => SKColors.Red);
        using var mask = Encoded(8, 6, (_, _) => SKColors.White);
        using var output = new MemoryStream();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inpainter.InpaintAsync(input, mask, output, source.Token));
        Assert.Equal(0, input.Position);
        await Assert.ThrowsAsync<InvalidDataException>(() => inpainter.InpaintAsync(input, mask, output));
        Assert.Equal(0, output.Length);
        Assert.True(input.CanRead && mask.CanRead && output.CanWrite);
    }

    [ModelFact]
    public async Task CancellationDuringInputRead_ReleasesGateAndLeavesCallerStreamsOpen()
    {
        await using var inpainter = await LoadModel();
        using var encoded = Encoded(4, 4, (_, _) => SKColors.Red);
        using var input = new PausedReadStream(encoded.ToArray());
        using var mask = Encoded(4, 4, (_, _) => SKColors.Black);
        using var output = new MemoryStream();
        using var source = new CancellationTokenSource();
        var operation = inpainter.InpaintAsync(input, mask, output, source.Token);
        try
        {
            await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        finally
        {
            source.Cancel();
            input.Resume.TrySetResult();
        }

        Assert.True(input.CanRead && mask.CanRead && output.CanWrite);
        Assert.Equal(0, output.Length);
        using var retryInput = Encoded(4, 4, (_, _) => SKColors.Blue);
        using var retryMask = Encoded(4, 4, (_, _) => SKColors.Black);
        Assert.Equal(0, (await inpainter.InpaintAsync(retryInput, retryMask, output)).SelectedPixelCount);
    }

    [ModelFact]
    public async Task CancellationWhileQueued_DoesNotReadInputsOrCancelActiveRequest()
    {
        await using var inpainter = await LoadModel();
        using var encoded = Encoded(4, 4, (_, _) => SKColors.Red);
        using var firstInput = new PausedReadStream(encoded.ToArray());
        using var firstMask = Encoded(4, 4, (_, _) => SKColors.Black);
        using var firstOutput = new MemoryStream();
        var active = inpainter.InpaintAsync(firstInput, firstMask, firstOutput);
        using var nextInput = Encoded(4, 4, (_, _) => SKColors.Blue);
        using var nextMask = Encoded(4, 4, (_, _) => SKColors.Black);
        using var nextOutput = new MemoryStream();
        using var source = new CancellationTokenSource();
        try
        {
            await firstInput.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var queued = inpainter.InpaintAsync(nextInput, nextMask, nextOutput, source.Token);
            source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(0, nextInput.Position);
            Assert.Equal(0, nextMask.Position);
            Assert.Equal(0, nextOutput.Length);
        }
        finally { firstInput.Resume.TrySetResult(); }
        Assert.Equal(0, (await active).SelectedPixelCount);
    }

    [ModelFact]
    public async Task Dispose_WaitsForActiveOperationQueuedCallsObserveDisposedAndRepeatedDisposalIsSafe()
    {
        var inpainter = await LoadModel();
        using var firstEncoded = Encoded(4, 4, (_, _) => SKColors.Red);
        using var firstInput = new PausedReadStream(firstEncoded.ToArray());
        using var firstMask = Encoded(4, 4, (_, _) => SKColors.Black);
        using var firstOutput = new MemoryStream();
        var active = inpainter.InpaintAsync(firstInput, firstMask, firstOutput);
        using var nextInput = Encoded(4, 4, (_, _) => SKColors.Blue);
        using var nextMask = Encoded(4, 4, (_, _) => SKColors.Black);
        using var nextOutput = new MemoryStream();
        try
        {
            await firstInput.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var disposal = inpainter.DisposeAsync().AsTask();
            var queued = inpainter.InpaintAsync(nextInput, nextMask, nextOutput);
            Assert.False(disposal.IsCompleted);
            Assert.False(queued.IsCompleted);

            firstInput.Resume.TrySetResult();
            await active;
            await disposal;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            await inpainter.DisposeAsync();
        }
        finally
        {
            firstInput.Resume.TrySetResult();
            await inpainter.DisposeAsync();
        }

        Assert.True(firstInput.CanRead && firstMask.CanRead && firstOutput.CanWrite);
        Assert.True(nextInput.CanRead && nextMask.CanRead && nextOutput.CanWrite);
        Assert.Equal(0, nextInput.Position);
        Assert.Equal(0, nextOutput.Length);
    }

    private static Task<MiganInpainter> LoadModel(ImageLimits? limits = null) => MiganInpainter.CreateAsync(new()
    {
        ModelPath = Environment.GetEnvironmentVariable(ModelFactAttribute.Variable)!,
        CpuThreads = 2, Limits = limits ?? new ImageLimits { MaximumPixels = 1_000_000 }
    });

    private static SKBitmap Bitmap(int width, int height, Func<int, int, SKColor> color)
    {
        var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) image.SetPixel(x, y, color(x, y));
        return image;
    }

    private static MemoryStream Encoded(int width, int height, Func<int, int, SKColor> color, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = Bitmap(width, height, color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 100);
        return new MemoryStream(encoded.ToArray());
    }

    private static async Task<SKBitmap> Baseline(byte[] input)
    {
        using var source = new MemoryStream(input);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(source, output);
        output.Position = 0;
        return SKBitmap.Decode(output.ToArray());
    }

    private static SKColor[] Pixels(SKBitmap image) => Enumerable.Range(0, image.Height)
        .SelectMany(y => Enumerable.Range(0, image.Width).Select(x => image.GetPixel(x, y))).ToArray();

    private sealed class PausedReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _paused;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_paused)
            {
                _paused = true;
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

public sealed class ModelFactAttribute : FactAttribute
{
    internal const string Variable = "MS_MEDIA_MIGAN_MODEL";
    public ModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))) Skip = $"Set {Variable} to a verified local MI-GAN pipeline to run actual model integration.";
    }
}

public sealed class ModelTheoryAttribute : TheoryAttribute
{
    public ModelTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ModelFactAttribute.Variable))) Skip = $"Set {ModelFactAttribute.Variable} to run actual model integration.";
    }
}
