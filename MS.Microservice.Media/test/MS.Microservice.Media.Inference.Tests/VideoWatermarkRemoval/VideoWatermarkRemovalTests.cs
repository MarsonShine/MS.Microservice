using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MS.Microservice.Media.Inference.VideoWatermarkRemoval;
using MS.Microservice.Media.Video;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.VideoWatermarkRemoval;

public sealed class VideoWatermarkRemovalTests
{
    [Fact]
    public void Options_RequireTrustedPythonExistingModelFiniteLimitsAndFlow()
    {
        using var root = new TestRoot();
        var options = FakeOptions(root.Path);
        options.Validate();
        Assert.Throws<ArgumentException>(() => (options with { PythonExecutablePath = "python" }).Validate());
        Assert.Throws<FileNotFoundException>(() => (options with { MiganModelPath = Path.Combine(root.Path, "missing") }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { CpuThreads = 65 }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { ProcessTimeout = Timeout.InfiniteTimeSpan }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Limits = new() { MaximumDurationSeconds = double.NaN } }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Limits = new() { MaximumInputBytes = 0 } }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Limits = new() { MaximumAudioTracks = -1 } }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Flow = new() { MaximumKnownRegionError = double.PositiveInfinity } }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Flow = new() { MinimumTexturedFraction = 1.01 } }).Validate());
        Assert.Throws<ArgumentException>(() => (options with { Flow = new() { GeneratedNeighborWeight = 0 } }).Validate());
    }

    [Fact]
    public void Request_RequiresOneMaskAndStableCorrespondingOrderedKeyframes()
    {
        using var root = new TestRoot();
        var mask = Path.Combine(root.Path, "mask");
        File.WriteAllBytes(mask, [0]);
        new VideoWatermarkRemovalRequest { StaticMaskPath = mask }.Snapshot();
        Assert.Throws<ArgumentException>(() => new VideoWatermarkRemovalRequest().Snapshot());
        Assert.Throws<ArgumentException>(() => new VideoWatermarkRemovalRequest { StaticMaskPath = mask, RectangleKeyframes = [] }.Snapshot());
        Assert.Throws<ArgumentException>(() => new VideoWatermarkRemovalRequest { RectangleKeyframes = [] }.Snapshot());
        Assert.Throws<ArgumentException>(() => Frames(new VideoMaskKeyframe(TimeSpan.FromSeconds(1), [new(0, 0, 1, 1)])).Snapshot());
        Assert.Throws<ArgumentException>(() => Frames(new VideoMaskKeyframe(TimeSpan.Zero, [])).Snapshot());
        Assert.Throws<ArgumentException>(() => Frames(new VideoMaskKeyframe(TimeSpan.Zero, [new(-1, 0, 1, 1)])).Snapshot());
        Assert.Throws<ArgumentException>(() => Frames(new VideoMaskKeyframe(TimeSpan.Zero, [new(0, 0, 0, 1)])).Snapshot());
        Assert.Throws<ArgumentException>(() => Frames(new(TimeSpan.Zero, [new(0, 0, 1, 1)]), new(TimeSpan.Zero, [new(1, 0, 1, 1)])).Snapshot());
        Assert.Throws<ArgumentException>(() => Frames(new(TimeSpan.Zero, [new(0, 0, 1, 1)]), new(TimeSpan.FromSeconds(1), [new(0, 0, 1, 1), new(1, 0, 1, 1)])).Snapshot());
        Frames(new(TimeSpan.Zero, [new(0, 0, 1, 1)]), new(TimeSpan.FromSeconds(1), [new(1, 0, 1, 1)])).Snapshot();
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, false)]
    [InlineData(0, 3, false)]
    [InlineData(2, long.MaxValue, true)]
    public async Task Snapshot_IsBoundedHashedLeavesCallerOpenAndReadsAtMostOneProbeByte(int length, long limit, bool succeeds)
    {
        using var root = new TestRoot();
        using var source = new MemoryStream(new byte[length]);
        var path = Path.Combine(root.Path, "snapshot");
        if (succeeds)
        {
            var hash = await VideoWatermarkRemover.SnapshotAsync(source, path, limit, default);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[length])), hash);
            Assert.Equal(length, new FileInfo(path).Length);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(() => VideoWatermarkRemover.SnapshotAsync(source, path, limit, default));
        Assert.True(source.CanRead);
        Assert.True(source.Position <= Math.Min(length, limit == long.MaxValue ? length : limit + 1));
    }

    [Fact]
    public async Task Snapshot_CancellationLeavesCallerStreamOpen()
    {
        using var root = new TestRoot();
        using var source = new MemoryStream([1, 2]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoWatermarkRemover.SnapshotAsync(source, Path.Combine(root.Path, "snapshot"), 10, cancellation.Token));
        Assert.Equal(0, source.Position);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task PreCancelledAndDisposedRequestsDoNotCreateOutputOrWorkspace()
    {
        using var root = new TestRoot();
        await using var remover = new VideoWatermarkRemover(FakeOptions(root.Path));
        var request = Frames(new VideoMaskKeyframe(TimeSpan.Zero, [new(0, 0, 1, 1)]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remover.RemoveAsync("missing-video", Path.Combine(root.Path, "result"), request, cancellation.Token));
        await remover.DisposeAsync();
        await remover.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => remover.RemoveAsync("missing-video", Path.Combine(root.Path, "result"), request));
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [Fact]
    public void PackagedVideoWorkerContainsPinnedActualModelContract()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "media-inference");
        Assert.True(File.Exists(Path.Combine(directory, "video_watermark_worker.py")));
        Assert.True(File.Exists(Path.Combine(directory, "video_flow.py")));
        Assert.True(File.Exists(Path.Combine(directory, "video-requirements.txt")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "video-modelmanifest.json")));
        Assert.Equal(MiganInpainter.ModelId, manifest.RootElement.GetProperty("modelId").GetString());
        Assert.Equal(MiganInpainter.ModelSha256.ToLowerInvariant(), manifest.RootElement.GetProperty("sha256").GetString());
    }

    [VideoModelFact]
    public async Task ManualRepair_UsesActualModelAndFlowPreservesVfrUnsignedAudioOffsetAndAtomicBundle()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        var source = Path.Combine(root.Path, "source.mkv");
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var output = Path.Combine(root.Path, "result");

        var result = await remover.RemoveAsync(source, output, new() { StaticMaskPath = Path.Combine(root.Path, "mask.png") });

        Assert.Equal(5, result.FrameCount);
        Assert.Equal(128, result.Width);
        Assert.Equal(96, result.Height);
        Assert.True(result.EditedPixelCount > 0);
        Assert.True(result.FlowContributedPixelCount > 0);
        Assert.True(result.ReviewRecommended);
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        var audio = Assert.Single(result.Audio);
        Assert.Equal(0, audio.StreamIndex);
        Assert.Equal(44100, audio.SampleRate);
        Assert.Equal(26019, audio.Samples);
        Assert.Equal(.05, audio.FirstSeconds, 6);
        Assert.Equal(1, audio.Channels);
        Assert.Equal(["NONE"], audio.ChannelRoles);
        Assert.Equal(VideoWatermarkRemover.BundleBytes(output), result.OutputBytes);
        Assert.All(new[] { "result.mkv", "mask-preview.mkv", "frame-metadata.json", "summary.json", "original-frame.png", "mask-frame.png", "result-frame.png" }, name => Assert.True(File.Exists(Path.Combine(output, name))));
        Assert.False(File.Exists(Path.Combine(output, "video-only.mkv")));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "frame-metadata.json")));
        Assert.Equal([0, 40, 140, 300, 410], metadata.RootElement.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("outputPtsMilliseconds").GetInt32()));
        Assert.Equal([40, 100, 160, 110, 80], metadata.RootElement.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("outputDurationMilliseconds").GetInt32()));
        Assert.Equal([output], Directory.GetDirectories(root.Path));
        await Assert.ThrowsAsync<IOException>(() => remover.RemoveAsync(source, output, new() { StaticMaskPath = Path.Combine(root.Path, "mask.png") }));
    }

    [VideoModelFact]
    public async Task RectangleKeyframes_InterpolateInActualPresentationTimeAndHoldFinalPosition()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var request = Frames(new(TimeSpan.Zero, [new(0, 0, 10, 10)]), new(TimeSpan.FromMilliseconds(100), [new(10, 0, 20, 10)]));
        var result = await remover.RemoveAsync(Path.Combine(root.Path, "source.mkv"), Path.Combine(root.Path, "result"), request);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "frame-metadata.json")));
        Assert.Equal([100, 140, 200, 200, 200], metadata.RootElement.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("selectedPixels").GetInt32()));
    }

    [VideoModelFact]
    public async Task EmptyTinyMask_IsLosslessIdentityAndDoesNotRunFlow()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var result = await remover.RemoveAsync(Path.Combine(root.Path, "tiny.mkv"), Path.Combine(root.Path, "result"), new() { StaticMaskPath = Path.Combine(root.Path, "tiny-mask.png") });
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(0, result.SelectedPixelCount);
        Assert.Equal(0, result.EditedPixelCount);
        Assert.Equal(0, result.FlowContributedPixelCount);
        Assert.Empty(result.Audio);
    }

    [VideoModelFact]
    public async Task FineVfr_IsQuantizedWithoutCfrAndCollisionIsRejectedWithoutPublication()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var request = new VideoWatermarkRemovalRequest { StaticMaskPath = Path.Combine(root.Path, "empty-mask.png") };
        var result = await remover.RemoveAsync(Path.Combine(root.Path, "fine.mov"), Path.Combine(root.Path, "fine"), request);
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "frame-metadata.json")));
        Assert.Equal([0, 33, 100, 200], metadata.RootElement.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("outputPtsMilliseconds").GetInt32()));
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(Path.Combine(root.Path, "collision.mov"), Path.Combine(root.Path, "collision"), request));
        Assert.Contains("colliding", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "collision")));
        Assert.DoesNotContain(Directory.GetDirectories(root.Path), path => Path.GetFileName(path).StartsWith("ms-watermark-", StringComparison.Ordinal));
    }

    [VideoModelFact]
    public async Task ModelMaskAndActualBudgetFailures_DoNotPublishPartialBundle()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        var source = Path.Combine(root.Path, "source.mkv");
        foreach (var name in new[] { "wrong-size.png", "nonbinary.png" })
        {
            await using var remover = new VideoWatermarkRemover(RealOptions());
            await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(source, Path.Combine(root.Path, name + "-result"), new() { StaticMaskPath = Path.Combine(root.Path, name) }));
        }
        foreach (var limits in new[] { new VideoRepairLimits { MaximumFrames = 2 }, new() { MaximumFramePixels = 100 }, new() { MaximumTotalPixels = 20000 }, new() { MaximumAudioSampleValues = 10 }, new() { MaximumOutputBytes = 100 }, new() { MaximumDurationSeconds = .1 } })
        {
            await using var remover = new VideoWatermarkRemover(RealOptions() with { Limits = limits });
            await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(source, Path.Combine(root.Path, "limited"), new() { StaticMaskPath = Path.Combine(root.Path, "mask.png") }));
        }
        var badModel = Path.Combine(root.Path, "bad.onnx");
        await File.WriteAllBytesAsync(badModel, [1, 2, 3]);
        await using (var remover = new VideoWatermarkRemover(RealOptions() with { MiganModelPath = badModel }))
            await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(source, Path.Combine(root.Path, "bad-model"), new() { StaticMaskPath = Path.Combine(root.Path, "mask.png") }));
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [VideoModelFact]
    public async Task InflightCancellationAndQueuedDispose_DrainAndRemoveOwnedArtifacts()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var request = new VideoWatermarkRemovalRequest { StaticMaskPath = Path.Combine(root.Path, "mask.png") };
        using var cancellation = new CancellationTokenSource();
        var active = remover.RemoveAsync(Path.Combine(root.Path, "source.mkv"), Path.Combine(root.Path, "first"), request, cancellation.Token);
        while (!Directory.EnumerateDirectories(root.Path).Any()) await Task.Delay(10);
        var queued = remover.RemoveAsync(Path.Combine(root.Path, "source.mkv"), Path.Combine(root.Path, "second"), request);
        var disposal = remover.DisposeAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await disposal;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        await remover.DisposeAsync();
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [VideoModelFact]
    public async Task AacNonzeroOriginAndLongerSurroundTracks_PreserveActualSamplesOffsetsAndDeclaredRoles()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path, extended: true);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var request = new VideoWatermarkRemovalRequest { StaticMaskPath = Path.Combine(root.Path, "empty-mask.png") };
        var origin = await remover.RemoveAsync(Path.Combine(root.Path, "aac-origin.mp4"), Path.Combine(root.Path, "origin-result"), request);
        Assert.Equal(.2, origin.FirstVideoSeconds, 6);
        Assert.True(Assert.Single(origin.Audio).FirstSeconds < origin.FirstVideoSeconds);
        var surround = await remover.RemoveAsync(Path.Combine(root.Path, "aac-surround.mp4"), Path.Combine(root.Path, "surround-result"), request);
        Assert.Equal(2, surround.Audio.Count);
        Assert.All(surround.Audio, track => Assert.Equal("aac", track.SourceCodec));
        var six = surround.Audio[1];
        Assert.Equal(6, six.Channels);
        Assert.Equal("5.1", six.Layout);
        Assert.Equal(["FL", "FR", "FC", "LFE", "BL", "BR"], six.ChannelRoles);
        Assert.True((double)six.Samples / six.SampleRate > surround.VideoDurationSeconds);
        Assert.Contains(surround.Warnings, warning => warning.Contains("speaker roles", StringComparison.Ordinal));
    }

    [VideoModelFact]
    public async Task QuarterTurn_IsNormalizedOnceAndUnsupportedGeometryOrAudioTimelinesAreRejected()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path, extended: true);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var turned = await remover.RemoveAsync(Path.Combine(root.Path, "rotation90.mp4"), Path.Combine(root.Path, "turned"),
            Frames(new VideoMaskKeyframe(TimeSpan.Zero, [new(0, 0, 10, 20)])));
        Assert.Equal(96, turned.Width);
        Assert.Equal(128, turned.Height);
        foreach (var name in new[] { "rotation37.mp4", "reflection.mp4", "nonsquare.mp4", "audio-gap.mkv", "unknown-surround.mkv", "negative-origin.mkv", "unknown-last-duration.mp4" })
        {
            var output = Path.Combine(root.Path, name + "-result");
            await Assert.ThrowsAsync<MediaProcessException>(() => remover.RemoveAsync(Path.Combine(root.Path, name), output,
                Frames(new VideoMaskKeyframe(TimeSpan.Zero, [new(0, 0, 10, 10)]))));
            Assert.False(Directory.Exists(output));
        }
        Assert.Equal([Path.Combine(root.Path, "turned")], Directory.GetDirectories(root.Path));
    }

    [VideoModelFact]
    public Task FlowAndRationalMasks_RejectCutsAndInsufficientSupportWithoutChangingOutside() =>
        TestPython(TestFile("flow_contracts.py"), Path.Combine(AppContext.BaseDirectory, "media-inference"));

    [VideoModelFact]
    public async Task QueuedRequest_UsesBoundedSnapshotEvenWhenCallerMutatesItsLists()
    {
        using var root = new TestRoot();
        await Fixtures(root.Path);
        await using var remover = new VideoWatermarkRemover(RealOptions());
        var source = Path.Combine(root.Path, "source.mkv");
        var active = remover.RemoveAsync(source, Path.Combine(root.Path, "first"), new() { StaticMaskPath = Path.Combine(root.Path, "empty-mask.png") });
        var rectangles = new List<VideoMaskRectangle> { new(0, 0, 10, 10) };
        var frames = new List<VideoMaskKeyframe> { new(TimeSpan.Zero, rectangles) };
        var request = new VideoWatermarkRemovalRequest { RectangleKeyframes = frames };
        var queued = remover.RemoveAsync(source, Path.Combine(root.Path, "second"), request);
        rectangles[0] = new(int.MaxValue, 0, int.MaxValue, 10);
        frames.Clear();
        await active;
        var result = await queued;
        Assert.Equal(500, result.SelectedPixelCount);
        await Assert.ThrowsAsync<ArgumentException>(() => remover.RemoveAsync(source, Path.Combine(root.Path, "third"), request));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "third")));
        Assert.DoesNotContain(Directory.GetDirectories(root.Path), path => Path.GetFileName(path).StartsWith("ms-watermark-", StringComparison.Ordinal));
    }

    private static VideoWatermarkRemovalRequest Frames(params VideoMaskKeyframe[] frames) => new() { RectangleKeyframes = frames };
    private static string TestFile(string name, [CallerFilePath] string sourceFile = "") => Path.Combine(Path.GetDirectoryName(sourceFile)!, name);
    private static VideoWatermarkRemovalOptions FakeOptions(string directory)
    {
        var model = Path.Combine(directory, "model");
        File.WriteAllBytes(model, [0]);
        return new() { PythonExecutablePath = Environment.ProcessPath!, MiganModelPath = model };
    }
    private static VideoWatermarkRemovalOptions RealOptions() => new()
    {
        PythonExecutablePath = Environment.GetEnvironmentVariable(VideoModelFactAttribute.PythonVariable)!,
        MiganModelPath = Environment.GetEnvironmentVariable(ModelFactAttribute.Variable)!, CpuThreads = 2
    };
    private static Task Fixtures(string directory, bool extended = false, [CallerFilePath] string sourceFile = "") =>
        TestPython(Path.Combine(Path.GetDirectoryName(sourceFile)!, "video_fixtures.py"), extended ? [directory, "--extended"] : [directory]);
    private static async Task TestPython(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable(VideoModelFactAttribute.PythonVariable)!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        await output;
        Assert.True(process.ExitCode == 0, await error);
    }
    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-video-repair-tests-" + Guid.NewGuid().ToString("N"));
        public TestRoot() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

public sealed class VideoModelFactAttribute : FactAttribute
{
    internal const string PythonVariable = "MS_MEDIA_WATERMARK_PYTHON";
    public VideoModelFactAttribute()
    {
        if (new[] { ModelFactAttribute.Variable, PythonVariable }.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = $"Set {ModelFactAttribute.Variable} and {PythonVariable} with the pinned video dependencies and FFmpeg installed.";
    }
}
