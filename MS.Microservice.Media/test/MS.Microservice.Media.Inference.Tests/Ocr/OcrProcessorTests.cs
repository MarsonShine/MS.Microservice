using System.Text.Json;
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Inference.Ocr;
using MS.Microservice.Media.Video;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.Ocr;

public sealed class OcrProcessorTests
{
    [Theory]
    [InlineData("threads", 0)]
    [InlineData("threads", 65)]
    [InlineData("detector", 31)]
    [InlineData("detector", 33)]
    [InlineData("detector", 2080)]
    [InlineData("recognizer", 319)]
    [InlineData("recognizer", 4097)]
    [InlineData("regions", 0)]
    [InlineData("regions", 257)]
    [InlineData("characters", 0)]
    [InlineData("characters", 32769)]
    public void Options_RejectUnboundedInference(string field, int value)
    {
        using var root = new TestRoot();
        var options = LocalOptions(root);
        options = field switch
        {
            "threads" => options with { CpuThreads = value },
            "detector" => options with { DetectionMaximumSide = value },
            "recognizer" => options with { MaximumRecognitionWidth = value },
            "regions" => options with { MaximumRegions = value },
            _ => options with { MaximumTextCharacters = value }
        };
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options));
    }

    [Theory]
    [InlineData(-.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Options_RejectInvalidScores(double value)
    {
        using var root = new TestRoot();
        var options = LocalOptions(root);
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { DetectionThreshold = value }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { BoxThreshold = value }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { MinimumConfidence = value }));
    }

    [Fact]
    public void Options_RequireLocalArtifactsTrustedExecutableAndFiniteTimeout()
    {
        using var root = new TestRoot();
        var options = LocalOptions(root);
        _ = new OcrProcessor(options);
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { PythonExecutablePath = "python" }));
        Assert.Throws<FileNotFoundException>(() => new OcrProcessor(options with { PythonExecutablePath = Path.Combine(root.Path, "missing.exe") }));
        Assert.Throws<FileNotFoundException>(() => new OcrProcessor(options with { ModelDirectory = Path.Combine(root.Path, "missing-models") }));
        Assert.Throws<DirectoryNotFoundException>(() => new OcrProcessor(options with { TemporaryDirectory = Path.Combine(root.Path, "missing") }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { Timeout = Timeout.InfiniteTimeSpan }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { DetectionThreshold = 0 }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { DetectionThreshold = 1 }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { Limits = new() { MaximumPixels = 0 } }));
        Assert.Throws<ArgumentException>(() => new OcrProcessor(options with { MaximumIntermediateBytes = 0 }));
    }

    [Fact]
    public void PackagedWorkerManifestAndPinnedDependencies_AreCopiedToConsumerOutput()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "media-inference");
        Assert.True(File.Exists(Path.Combine(directory, "ocr_worker.py")));
        Assert.True(File.Exists(Path.Combine(directory, "ocr_pipeline.py")));
        Assert.True(File.Exists(Path.Combine(directory, "ocr-requirements.txt")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ocr-modelmanifest.json")));
        Assert.Equal(OcrProcessor.ModelId, manifest.RootElement.GetProperty("modelId").GetString());
        Assert.Equal("Apache-2.0", manifest.RootElement.GetProperty("license").GetString());
        Assert.Equal(3, manifest.RootElement.GetProperty("artifacts").GetArrayLength());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ResultValidation_RejectsUnboundedCoordinates(double x)
    {
        var result = new OcrResult(100, 60, [new("text", .9, [new(x, 1), new(10, 1), new(10, 10), new(1, 10)])]);
        Assert.Throws<InvalidDataException>(() => OcrProcessor.ValidateResult(result, 100, 60, new() { PythonExecutablePath = "unused", ModelDirectory = "unused" }));
    }

    [Fact]
    public void ResultValidation_EnforcesIdentityCountsConfidenceAndUtf16CharacterBudget()
    {
        var options = new OcrOptions { PythonExecutablePath = "unused", ModelDirectory = "unused", MaximumRegions = 1, MaximumTextCharacters = 2 };
        OcrRegion region = new("中文", .9, [new(1, 1), new(10, 1), new(10, 10), new(1, 10)]);
        OcrProcessor.ValidateResult(new(100, 60, [region]), 100, 60, options);
        OcrProcessor.ValidateResult(new(100, 60, [region with { Text = "😀" }]), 100, 60, options);
        foreach (var result in new OcrResult[]
        {
            new(99, 60, [region]), new(100, 60, [region, region]), new(100, 60, [region with { Text = "123" }]),
            new(100, 60, [region with { Confidence = .49 }]), new(100, 60, [region with { Confidence = double.NaN }]),
            new(100, 60, [region with { Text = "" }]), new(100, 60, [region with { Corners = [new(1, 1)] }])
        }) Assert.Throws<InvalidDataException>(() => OcrProcessor.ValidateResult(result, 100, 60, options));
    }

    [Fact]
    public async Task InvalidImageAndPreCancellation_DoNotStartWorkerOrCloseCaller()
    {
        using var root = new TestRoot();
        var processor = new OcrProcessor(LocalOptions(root));
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.RecognizeAsync(input));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        input.Position = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.RecognizeAsync(input, cancel.Token));
        Assert.Equal(0, input.Position);
        Assert.True(input.CanRead);
        Assert.Equal(3, Directory.GetFiles(root.Path).Length);
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [OcrFact]
    public async Task PrintedBilingualText_UsesActualModelsAndReturnsLocatedImmutableRegions()
    {
        using var root = new TestRoot();
        var processor = Create(root.Path);
        using var input = new MemoryStream([0, 0, .. Fixture("bilingual.png")]);
        input.Position = 2;
        var result = await processor.RecognizeAsync(input);
        AssertBilingual(result);
        Assert.Equal(1000, result.Width);
        Assert.Equal(260, result.Height);
        Assert.InRange(result.Regions[0].Corners.Average(p => p.X), 170, 240);
        Assert.InRange(result.Regions[0].Corners.Average(p => p.Y), 40, 90);
        Assert.InRange(result.Regions[1].Corners.Average(p => p.Y), 135, 190);
        Assert.True(input.CanRead);
        Assert.Throws<NotSupportedException>(() => ((IList<OcrRegion>)result.Regions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<OcrPoint>)result.Regions[0].Corners).Clear());
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData("tilted.png")]
    [InlineData("low-contrast.png")]
    [InlineData("exif-3.jpg")]
    [InlineData("exif-6.jpg")]
    [InlineData("transparent-text.png")]
    public async Task TiltContrastAndExif_AreActualRecognizedNormalizedPixelCoordinates(string name)
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture(name));
        var result = await Create(root.Path).RecognizeAsync(input);
        AssertBilingual(result);
        if (name.StartsWith("exif", StringComparison.Ordinal))
        {
            Assert.Equal(1000, result.Width);
            Assert.Equal(260, result.Height);
            Assert.InRange(result.Regions[0].Corners.Average(p => p.Y), 40, 90);
            Assert.InRange(result.Regions[1].Corners.Average(p => p.Y), 135, 190);
        }
        foreach (var region in result.Regions)
        {
            Assert.Equal(4, region.Corners.Count);
            Assert.InRange(region.Confidence, .5, 1);
            Assert.All(region.Corners, p => { Assert.InRange(p.X, 0, result.Width - 1); Assert.InRange(p.Y, 0, result.Height - 1); });
        }
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task BlankAndTransparentImages_ReturnNoInventedText()
    {
        using var root = new TestRoot();
        foreach (var color in new[] { SKColors.White, SKColors.Transparent })
        {
            using var input = Image(640, 480, color);
            var result = await Create(root.Path).RecognizeAsync(input);
            Assert.Empty(result.Regions);
            Assert.True(input.CanRead);
        }
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task LongText_IsRecognizedWithoutSilentWidthClipping()
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture("long-line.png"));
        var result = await Create(root.Path).RecognizeAsync(input);
        Assert.Equal("Long text recognition 0123456789 Long text recognition 0123456789", Assert.Single(result.Regions).Text);
        input.Position = 0;
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => Create(root.Path, o => o with { MaximumRecognitionWidth = 320 }).RecognizeAsync(input));
        Assert.Contains("recognition width budget", error.StandardError, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData("regions")]
    [InlineData("characters")]
    public async Task OutputBudgets_FailRatherThanReturnTruncatedSuccess(string budget)
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture("bilingual.png"));
        var processor = Create(root.Path, o => budget == "regions" ? o with { MaximumRegions = 1 } : o with { MaximumTextCharacters = 1 });
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => processor.RecognizeAsync(input));
        Assert.Contains("budget", error.StandardError, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData("encoded")]
    [InlineData("pixels")]
    [InlineData("dimension")]
    public async Task ImageBudgets_AreEnforcedBeforeWorker(string budget)
    {
        using var root = new TestRoot();
        var bytes = Fixture("bilingual.png");
        using var input = new MemoryStream(bytes);
        var limits = budget switch
        {
            "encoded" => new ImageLimits { MaximumEncodedBytes = bytes.Length - 1 },
            "pixels" => new ImageLimits { MaximumPixels = 259999 },
            _ => new ImageLimits { MaximumDimension = 999 }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(root.Path, o => o with { Limits = limits }).RecognizeAsync(input));
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task NonSeekableAndExactEncodedBudget_PreserveCallerOwnership()
    {
        using var root = new TestRoot();
        var bytes = Fixture("bilingual.png");
        using var input = new NonSeekableStream(bytes);
        var result = await Create(root.Path, o => o with { Limits = new() { MaximumEncodedBytes = bytes.Length } }).RecognizeAsync(input);
        AssertBilingual(result);
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task CompressedSource_UsesIndependentIntermediateBudget()
    {
        using var root = new TestRoot();
        var bytes = Fixture("compressed.png");
        using var input = new MemoryStream(bytes);
        var processor = Create(root.Path, o => o with { Limits = new() { MaximumEncodedBytes = bytes.Length } });
        Assert.Empty((await processor.RecognizeAsync(input)).Regions);
        input.Position = 0;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Create(root.Path, o => o with
        {
            Limits = new() { MaximumEncodedBytes = bytes.Length }, MaximumIntermediateBytes = 100
        }).RecognizeAsync(input));
        Assert.Contains("intermediate byte budget", error.Message, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutAndCancellation_TerminateRealWorkerAndRemoveOnlyOwnedArtifacts(bool cancel)
    {
        using var root = new TestRoot();
        var keep = Path.Combine(root.Path, "keep.txt");
        await File.WriteAllTextAsync(keep, "keep");
        using var input = new MemoryStream(Fixture("bilingual.png"));
        using var token = new CancellationTokenSource();
        if (cancel) token.CancelAfter(TimeSpan.FromMilliseconds(100));
        var task = Create(root.Path, o => cancel ? o : o with { Timeout = TimeSpan.FromMilliseconds(10) }).RecognizeAsync(input, token.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else await Assert.ThrowsAsync<TimeoutException>(() => task);
        Assert.True(input.CanRead);
        Assert.Equal("keep", await File.ReadAllTextAsync(keep));
        Assert.Single(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task CorruptModel_IsRejectedBeforeInferenceAndDoesNotUseAFallback()
    {
        using var root = new TestRoot();
        var modelRoot = Path.Combine(root.Path, "models");
        Directory.CreateDirectory(modelRoot);
        foreach (var name in new[] { OcrProcessor.DetectionFile, OcrProcessor.RecognitionFile, OcrProcessor.DictionaryFile })
            File.Copy(Path.Combine(Environment.GetEnvironmentVariable(OcrFactAttribute.Models)!, name), Path.Combine(modelRoot, name));
        await using (var file = new FileStream(Path.Combine(modelRoot, OcrProcessor.DetectionFile), FileMode.Open, FileAccess.Write))
            await file.WriteAsync(new byte[] { 0 });
        using var input = new MemoryStream(Fixture("bilingual.png"));
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => Create(root.Path, o => o with { ModelDirectory = modelRoot }).RecognizeAsync(input));
        Assert.Contains("SHA-256 mismatch", error.StandardError, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.Equal([modelRoot], Directory.GetDirectories(root.Path));
    }

    private static void AssertBilingual(OcrResult result) => Assert.Equal(["Hello OCR 2026", "中文识别测试"], result.Regions.Select(r => r.Text));
    private static OcrProcessor Create(string temporary, Func<OcrOptions, OcrOptions>? transform = null)
    {
        var options = new OcrOptions { PythonExecutablePath = Environment.GetEnvironmentVariable(OcrFactAttribute.Python)!,
            ModelDirectory = Environment.GetEnvironmentVariable(OcrFactAttribute.Models)!, TemporaryDirectory = temporary, CpuThreads = 2 };
        return new(transform?.Invoke(options) ?? options);
    }

    private static OcrOptions LocalOptions(TestRoot root)
    {
        foreach (var name in new[] { OcrProcessor.DetectionFile, OcrProcessor.RecognitionFile, OcrProcessor.DictionaryFile })
            File.WriteAllBytes(Path.Combine(root.Path, name), [1]);
        return new() { PythonExecutablePath = Environment.ProcessPath!, ModelDirectory = root.Path, TemporaryDirectory = root.Path };
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureRoot(), "Fixtures", name));
    private static string FixtureRoot([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    private static MemoryStream Image(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(png.ToArray());
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }

    private sealed class TestRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-ocr-tests-" + Guid.NewGuid().ToString("N"));
        internal TestRoot() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

internal sealed class OcrFactAttribute : FactAttribute
{
    internal const string Python = "MS_MEDIA_OCR_PYTHON";
    internal const string Models = "MS_MEDIA_OCR_MODELS";
    public OcrFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Python)) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Models)))
            Skip = $"Set {Python} and {Models} for real local PP-OCRv5 CPU inference.";
    }
}

internal sealed class OcrTheoryAttribute : TheoryAttribute
{
    public OcrTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OcrFactAttribute.Python)) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OcrFactAttribute.Models)))
            Skip = $"Set {OcrFactAttribute.Python} and {OcrFactAttribute.Models} for real local PP-OCRv5 CPU inference.";
    }
}
