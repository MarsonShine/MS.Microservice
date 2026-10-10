using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference.BackgroundRemoval;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.BackgroundRemoval;

public sealed class BiRefNetBackgroundRemoverTests
{
    [Fact]
    public async Task InvalidModelSizeAndPreCancellation_AreRejectedWithoutLoadingRuntime()
    {
        var path = Path.Combine(Path.GetTempPath(), "ms-biref-invalid-" + Guid.NewGuid().ToString("N") + ".onnx");
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => BiRefNetBackgroundRemover.CreateAsync(new() { ModelPath = path }));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BiRefNetBackgroundRemover.CreateAsync(new() { ModelPath = "not-opened" }, cancellation.Token));
        }
        finally { File.Delete(path); }
    }

    [BackgroundModelFact]
    public async Task RealModel_PortraitTransparentEdgesAndExifPreserveRgbAndSourceAlphaWithMatchingMatte()
    {
        var loading = Stopwatch.StartNew();
        await using var remover = await Load();
        var creationSeconds = loading.Elapsed.TotalSeconds;
        var portrait = File.ReadAllBytes(Fixture());
        using var portraitPixels = SKBitmap.Decode(portrait);
        var personEdges = Encoded(portraitPixels.Width, portraitPixels.Height, (x, y) =>
            portraitPixels.GetPixel(x, y).WithAlpha((byte)(x < portraitPixels.Width / 5 ? 0 : x < portraitPixels.Width * 2 / 5 ? 64 : x < portraitPixels.Width * 3 / 5 ? 128 : 255)));
        var partial = Encoded(63, 47, (x, _) => new(170, 60, 120, (byte)(x < 16 ? 0 : x < 32 ? 64 : x < 48 ? 128 : 255)));
        var transparent = Encoded(32, 16, (_, _) => new(200, 80, 160, 0));
        foreach (var (name, bytes, includeMask) in new[] {
            ("portrait", portrait, true), ("transparent", transparent, false), ("transparent-edges", partial, true), ("exif90", Exif(portrait, 6), true),
            ("person-transparent-edges", personEdges, true) })
        {
            using var baseline = await Baseline(bytes);
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            using var mask = new MemoryStream();
            var elapsed = Stopwatch.StartNew();
            var result = await remover.RemoveAsync(input, output, includeMask ? mask : null);
            using var actual = BackgroundPixelsTests.DecodeStraight(output.ToArray());
            using var actualMask = includeMask ? BackgroundPixelsTests.DecodeStraight(mask.ToArray()) : null;
            Assert.Equal(BiRefNetBackgroundRemover.ModelId, result.ModelId);
            Assert.True(result.ReviewRecommended);
            Assert.Equal(baseline.Width, result.Width);
            Assert.Equal(baseline.Height, result.Height);
            var visible = 0;
            var partialVisible = 0;
            long alphaSum = 0;
            for (var y = 0; y < actual.Height; y++)
            for (var x = 0; x < actual.Width; x++)
            {
                var pixel = actual.GetPixel(x, y);
                var original = baseline.GetPixel(x, y);
                Assert.True(pixel.Alpha <= original.Alpha);
                if (pixel.Alpha > 0)
                {
                    Assert.Equal(original.Red, pixel.Red);
                    Assert.Equal(original.Green, pixel.Green);
                    Assert.Equal(original.Blue, pixel.Blue);
                    visible++;
                    if (original.Alpha < 255) partialVisible++;
                }
                if (actualMask is not null) Assert.Equal(new SKColor(pixel.Alpha, pixel.Alpha, pixel.Alpha, 255), actualMask.GetPixel(x, y));
                alphaSum += pixel.Alpha;
            }
            Assert.Equal(visible, result.NonTransparentPixels);
            Assert.Equal((double)alphaSum / (255L * actual.Width * actual.Height), result.MeanAlpha, 8);
            if (name == "portrait") Assert.InRange(visible, 1, actual.Width * actual.Height - 1);
            if (name == "transparent") Assert.Equal(0, visible);
            if (name == "person-transparent-edges") Assert.True(partialVisible > 0);
            if (name == "exif90")
            {
                using var decoded = SKBitmap.Decode(portrait);
                Assert.Equal(decoded.Height, result.Width);
                Assert.Equal(decoded.Width, result.Height);
            }
            Assert.True(input.CanRead && output.CanWrite && mask.CanWrite);
            var artifact = Environment.GetEnvironmentVariable("MS_MEDIA_BIREF_PROOF_DIR");
            if (!string.IsNullOrWhiteSpace(artifact))
            {
                var directory = Path.Combine(artifact, name);
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, "original.bin"), bytes);
                await File.WriteAllBytesAsync(Path.Combine(directory, "result.png"), output.ToArray());
                if (includeMask) await File.WriteAllBytesAsync(Path.Combine(directory, "soft-mask.png"), mask.ToArray());
                using var current = Process.GetCurrentProcess();
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new {
                    Result = result, ModelCreationSeconds = creationSeconds, ProcessingSeconds = elapsed.Elapsed.TotalSeconds,
                    TestHostPeakWorkingSetBytes = current.PeakWorkingSet64, OutputBytes = output.Length, MaskBytes = mask.Length
                }));
            }
        }
    }

    [BackgroundModelFact]
    public async Task QueuedCancellationAndDisposal_DoNotReadQueuedInputsOrReleaseActiveSessionEarly()
    {
        await using var remover = await Load();
        using var cancellation = new CancellationTokenSource();
        using var first = new PausedStream(File.ReadAllBytes(Fixture()));
        using var output = new MemoryStream();
        using var queuedInput = new MemoryStream([1, 2, 3]);
        using var queuedOutput = new MemoryStream();
        var active = remover.RemoveAsync(first, output, cancellationToken: cancellation.Token);
        try
        {
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var queuedCancellation = new CancellationTokenSource();
            var queued = remover.RemoveAsync(queuedInput, queuedOutput, cancellationToken: queuedCancellation.Token);
            queuedCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(0, queuedInput.Position);
            var disposal = remover.DisposeAsync().AsTask();
            var afterDisposal = remover.RemoveAsync(queuedInput, queuedOutput);
            Assert.False(disposal.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
            await disposal;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => afterDisposal);
            await remover.DisposeAsync();
            Assert.True(first.CanRead && output.CanWrite && queuedInput.CanRead && queuedOutput.CanWrite);
        }
        finally { first.Resume.TrySetResult(); }
    }

    [BackgroundModelFact]
    public async Task EncodedAndRasterLimits_RejectBeforeInferenceAndLeaveStreamsOpen()
    {
        foreach (var limits in new[] { new ImageLimits { MaximumEncodedBytes = 1 }, new() { MaximumDimension = 16, MaximumPixels = 256 } })
        {
            await using var remover = await Load(limits);
            using var input = new MemoryStream(File.ReadAllBytes(Fixture()));
            using var output = new MemoryStream();
            using var mask = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(() => remover.RemoveAsync(input, output, mask));
            Assert.True(input.CanRead && output.CanWrite && mask.CanWrite);
            Assert.Equal(0, output.Length);
            Assert.Equal(0, mask.Length);
        }
    }

    private static Task<BiRefNetBackgroundRemover> Load(ImageLimits? limits = null) => BiRefNetBackgroundRemover.CreateAsync(new()
    {
        ModelPath = Environment.GetEnvironmentVariable(BackgroundModelFactAttribute.Variable)!, CpuThreads = 2,
        Limits = limits ?? new BiRefNetBackgroundRemovalOptions { ModelPath = "unused" }.Limits
    });
    private static string Fixture([CallerFilePath] string sourceFile = "") => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures", "grace-hopper.jpg");
    private static async Task<SKBitmap> Baseline(byte[] original)
    {
        using var input = new MemoryStream(original);
        using var output = new MemoryStream();
        await new ImageProcessor().ProcessAsync(input, output);
        return BackgroundPixelsTests.DecodeStraight(output.ToArray());
    }
    private static byte[] Encoded(int width, int height, Func<int, int, SKColor> pixel)
    {
        using var image = BackgroundPixels.CreateStraightBitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) image.SetPixel(x, y, pixel(x, y));
        using var encoded = SKImage.FromBitmap(image);
        using var data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    private static byte[] Exif(byte[] jpeg, int origin)
    {
        byte[] exif = [.. "Exif"u8, 0, 0, (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)origin, 0, 0, 0, 0, 0, 0, 0];
        return [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(exif.Length + 2), .. exif, .. jpeg.AsSpan(2)];
    }
    private sealed class PausedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

public sealed class BackgroundModelFactAttribute : FactAttribute
{
    internal const string Variable = "MS_MEDIA_BIREFNET_MODEL";
    public BackgroundModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to the fixed local BiRefNet-lite ONNX for actual CPU inference.";
    }
}
