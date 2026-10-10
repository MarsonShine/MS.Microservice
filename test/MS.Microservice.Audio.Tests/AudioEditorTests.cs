using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;
using Xunit;

namespace MS.Microservice.Audio.Tests;

public sealed class AudioEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MS.Microservice.AudioEditorTests", Guid.NewGuid().ToString("N"));
    private readonly AudioEditor _editor = new();

    public AudioEditorTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Edit_TrimFloorsTimesToFramesAndPreservesChannels(int channels)
    {
        var samples = Enumerable.Range(0, 10).SelectMany(frame => Enumerable.Range(0, channels)
            .Select(channel => (frame * channels + channel) / 100f)).ToArray();
        var input = CreateWav(samples, channels);
        var output = Output("trim.wav");

        await _editor.EditAsync(input, output, new AudioEditOptions
        {
            Start = TimeSpan.FromTicks(1875),
            Duration = TimeSpan.FromTicks(4375),
            Gain = 2
        });

        var actual = ReadSamples(output, out var format);
        Assert.Equal(8000, format.SampleRate);
        Assert.Equal(channels, format.Channels);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, format.Encoding);
        Assert.Equal(samples.Skip(channels).Take(3 * channels).Select(value => value * 2), actual);
        AssertExclusiveInputAccess(input);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Edit_DefaultSettingsAndDurationBeyondEndPreserveRemainingAudio()
    {
        float[] samples = [0.1f, 0.2f, -0.3f, 0.4f];
        var input = CreateWav(samples);
        var complete = Output("complete.wav");
        var remainder = Output("remainder.wav");

        await _editor.EditAsync(input, complete);
        await _editor.EditAsync(input, remainder, new AudioEditOptions
        {
            Start = Frames(2), Duration = TimeSpan.FromHours(1)
        });

        Assert.Equal(samples, ReadSamples(complete, out _));
        Assert.Equal(samples[2..], ReadSamples(remainder, out _));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(float.MaxValue)]
    public async Task Edit_GainUsesOneSaturationRuleIncludingMuteAndExtremeValues(float gain)
    {
        float[] samples = [-0.75f, 0, 0.75f];
        var input = CreateWav(samples);
        var output = Output("gain.wav");

        await _editor.EditAsync(input, output, new AudioEditOptions { Gain = gain });

        Assert.Equal(samples.Select(value => (float)Math.Clamp((double)value * gain, -1, 1)), ReadSamples(output, out _));
    }

    [Fact]
    public async Task Edit_FadesUseSharedFrameGainAndExactEndpointsForStereo()
    {
        var input = CreateWav(Enumerable.Range(0, 8).SelectMany(_ => new[] { 0.6f, -0.6f }).ToArray(), channels: 2);
        var output = Output("fades.wav");

        await _editor.EditAsync(input, output, new AudioEditOptions { FadeIn = Frames(4), FadeOut = Frames(4) });

        var samples = ReadSamples(output, out _);
        double[] factors = [0, 1d / 3, 2d / 3, 1, 1, 2d / 3, 1d / 3, 0];
        for (var frame = 0; frame < factors.Length; frame++)
        {
            Assert.Equal((float)(0.6f * factors[frame]), samples[frame * 2]);
            Assert.Equal(-samples[frame * 2], samples[frame * 2 + 1]);
        }
    }

    [Fact]
    public async Task Edit_OverlappingFadesMultiplyAndSingleFrameFadesAreSilent()
    {
        var input = CreateWav([1, 1, 1, 1]);
        var overlap = Output("overlap.wav");
        var single = Output("single.wav");

        await _editor.EditAsync(input, overlap, new AudioEditOptions { FadeIn = Frames(3), FadeOut = Frames(3) });
        await _editor.EditAsync(input, single, new AudioEditOptions
        {
            Duration = Frames(1), FadeIn = Frames(1), FadeOut = Frames(1)
        });

        Assert.Equal(new float[] { 0, 0.5f, 0.5f, 0 }, ReadSamples(overlap, out _));
        Assert.Equal(new float[] { 0 }, ReadSamples(single, out _));
    }

    [Fact]
    public async Task Edit_Pcm16InputUsesDecodedSamplesWithoutResampling()
    {
        var input = Output("pcm16.wav");
        using (var writer = new WaveFileWriter(input, new WaveFormat(16000, 16, 2)))
            writer.WriteSamples([0.25f, -0.25f, 0.5f, -0.5f], 0, 4);
        var output = Output("pcm-edited.wav");
        var original = ReadSamples(input, out _);

        await _editor.EditAsync(input, output, new AudioEditOptions { Gain = 0.5f });

        Assert.Equal(original.Select(value => value * 0.5f), ReadSamples(output, out var format));
        Assert.Equal(16000, format.SampleRate);
        Assert.Equal(2, format.Channels);
    }

    private static AudioEditOptions[] InvalidOptions =>
    [
        new() { Start = TimeSpan.FromTicks(-1) },
        new() { Duration = TimeSpan.Zero },
        new() { Duration = TimeSpan.FromTicks(-1) },
        new() { Duration = TimeSpan.FromTicks(1) },
        new() { Start = Frames(4) },
        new() { Start = TimeSpan.MaxValue },
        new() { FadeIn = TimeSpan.FromTicks(-1) },
        new() { FadeOut = TimeSpan.FromTicks(-1) },
        new() { FadeIn = Frames(5) },
        new() { FadeOut = Frames(5) },
        new() { Gain = -1 },
        new() { Gain = float.NaN },
        new() { Gain = float.PositiveInfinity }
    ];

    [Fact]
    public async Task Edit_InvalidTimesGainOrEmptyRangeNeverPublish()
    {
        var input = CreateWav([0.1f, 0.2f, 0.3f, 0.4f]);
        var output = Output("invalid.wav");

        foreach (var options in InvalidOptions)
            await Assert.ThrowsAnyAsync<ArgumentException>(() => _editor.EditAsync(input, output, options));

        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
        AssertExclusiveInputAccess(input);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public async Task Edit_NonfiniteInputFailsWithoutPublishing(float value)
    {
        var input = CreateWav([0.1f, value]);
        var output = Output("nonfinite.wav");

        await Assert.ThrowsAsync<InvalidDataException>(() => _editor.EditAsync(input, output));

        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
        AssertExclusiveInputAccess(input);
    }

    [Fact]
    public async Task Edit_InputAndOutputMustBeDifferentNormalizedPaths()
    {
        var input = CreateWav([0.1f]);
        var original = await File.ReadAllBytesAsync(input);

        await Assert.ThrowsAsync<ArgumentException>(() => _editor.EditAsync(input, Path.Combine(_root, ".", Path.GetFileName(input))));

        Assert.Equal(original, await File.ReadAllBytesAsync(input));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Edit_ExistingOutputIsPreserved()
    {
        var input = CreateWav([0.1f]);
        var output = Output("exists.wav");
        await File.WriteAllBytesAsync(output, [10, 20, 30]);

        await Assert.ThrowsAsync<IOException>(() => _editor.EditAsync(input, output));

        Assert.Equal(new byte[] { 10, 20, 30 }, await File.ReadAllBytesAsync(output));
        AssertNoTemporaryFiles();
        AssertExclusiveInputAccess(input);
    }

    [Fact]
    public async Task Edit_OutputCreatedDuringProcessingIsNeverOverwritten()
    {
        var input = CreateWav(Enumerable.Repeat(0.25f, 10_000).ToArray());
        var output = Output("racing.wav");
        var progress = new InlineProgress(value =>
        {
            if (value == 0) File.WriteAllBytes(output, [10, 20]);
        });

        await Assert.ThrowsAsync<IOException>(() => _editor.EditAsync(input, output, progress: progress));

        Assert.Equal(new byte[] { 10, 20 }, await File.ReadAllBytesAsync(output));
        AssertNoTemporaryFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edit_CancellationBeforeOrDuringProcessingRemovesOnlyOwnedTemporaryFile(bool duringProcessing)
    {
        var input = CreateWav(Enumerable.Repeat(0.25f, 20_000).ToArray());
        var output = Output("cancelled.wav");
        var unrelated = Output("unrelated.tmp");
        await File.WriteAllBytesAsync(unrelated, [99]);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value =>
        {
            if (value <= 0) return;
            Assert.False(File.Exists(output));
            Assert.NotEmpty(Directory.GetFiles(_root, ".audio-*.tmp"));
            cancellation.Cancel();
        });
        if (!duringProcessing) cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _editor.EditAsync(
            input, output, progress: progress, cancellationToken: cancellation.Token));

        Assert.False(File.Exists(output));
        Assert.Equal(new byte[] { 99 }, await File.ReadAllBytesAsync(unrelated));
        AssertNoTemporaryFiles();
        AssertExclusiveInputAccess(input);
    }

    [Fact]
    public async Task Edit_ProgressCallbackFailureAbortsAndCleansUp()
    {
        var input = CreateWav([0.1f, 0.2f]);
        var output = Output("callback.wav");
        var progress = new InlineProgress(_ => throw new InvalidOperationException("callback failed"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _editor.EditAsync(input, output, progress: progress));

        Assert.Equal("callback failed", error.Message);
        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
        AssertExclusiveInputAccess(input);
    }

    [Fact]
    public async Task Edit_UnsupportedOutputAndCorruptInputDoNotPublish()
    {
        var input = CreateWav([0.1f]);
        await Assert.ThrowsAsync<NotSupportedException>(() => _editor.EditAsync(input, Output("unsupported.ogg")));
        var corrupt = Output("corrupt.wav");
        await File.WriteAllBytesAsync(corrupt, [1, 2, 3, 4]);

        await Assert.ThrowsAnyAsync<Exception>(() => _editor.EditAsync(corrupt, Output("corrupt-output.wav")));

        Assert.False(File.Exists(Output("corrupt-output.wav")));
        AssertNoTemporaryFiles();
        AssertExclusiveInputAccess(corrupt);
    }

    [Fact]
    public async Task Edit_Mp3UsesInstalledEncoderAndCanBeReadBackOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var samples = Enumerable.Range(0, 44100).Select(frame => (float)(0.25 * Math.Sin(frame * 2 * Math.PI * 440 / 44100))).ToArray();
        var input = CreateWav(samples, sampleRate: 44100);
        var output = Output("encoded.mp3");

        await _editor.EditAsync(input, output, new AudioEditOptions { Start = TimeSpan.FromSeconds(0.2), Duration = TimeSpan.FromSeconds(0.5), Gain = 0.5f });

        var decoded = ReadSamples(output, out var format);
        Assert.Equal(44100, format.SampleRate);
        Assert.Equal(1, format.Channels);
        Assert.InRange((double)decoded.Length / format.SampleRate, 0.49, 0.65);
        Assert.InRange(decoded.Max(value => Math.Abs(value)), 0.05f, 0.2f);
        AssertExclusiveInputAccess(input);
        AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Edit_MultichannelMp3IsRejectedBeforeOutputCreation()
    {
        var input = CreateWav([0.1f, 0.2f, 0.3f], channels: 3);
        var output = Output("multichannel.mp3");

        await Assert.ThrowsAsync<NotSupportedException>(() => _editor.EditAsync(input, output));

        Assert.False(File.Exists(output));
        AssertNoTemporaryFiles();
    }

    private static TimeSpan Frames(int count) => TimeSpan.FromTicks(count * 1250L);
    private string Output(string name) => Path.Combine(_root, name);

    private string CreateWav(float[] samples, int channels = 1, int sampleRate = 8000)
    {
        var path = Output(Guid.NewGuid().ToString("N") + ".wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    private static float[] ReadSamples(string path, out WaveFormat format)
    {
        using var reader = new AudioFileReader(path);
        format = reader.WaveFormat;
        var samples = new List<float>();
        var buffer = new float[1024 * format.Channels];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.Take(read));
        return samples.ToArray();
    }

    private static void AssertExclusiveInputAccess(string path)
    {
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private void AssertNoTemporaryFiles() => Assert.Empty(Directory.GetFiles(_root, ".audio-*.tmp"));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.AudioEditorTests"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(_root);
        if (root.StartsWith(parent, StringComparison.Ordinal) && Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
