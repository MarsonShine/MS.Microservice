using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;
using Xunit;

namespace MS.Microservice.Audio.Tests;

public sealed class AudioResourceOwnershipTests : IDisposable
{
    private static readonly WaveFormat Format = new(44100, 16, 2);
    private static readonly string Parent = Path.Combine(Path.GetTempPath(), "MS.Microservice.AudioResourceTests");
    private readonly string _directory = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
    private readonly AudioProcessor _processor = new();

    public AudioResourceOwnershipTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task SameFormatConversionReleasesInputAfterSuccess()
    {
        var input = CreateWav("input.wav");
        var output = Path.Combine(_directory, "output.wav");

        await _processor.ConvertAudioFormatAsync(input, output, new() { TargetFormat = Format });

        AssertExclusiveAccess(input);
        using var result = new WaveFileReader(output);
        Assert.Equal(Format, result.WaveFormat);
        Assert.Equal(16, result.Length);
    }

    [Fact]
    public async Task ConversionReleasesInputWhenOutputCannotBeOpened()
    {
        var input = CreateWav("input.wav");
        var output = Path.Combine(_directory, "output.wav");
        using var lockedOutput = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            _processor.ConvertAudioFormatAsync(input, output, new() { TargetFormat = Format }).AsTask());

        AssertExclusiveAccess(input);
    }

    [Fact]
    public async Task MixingReleasesEarlierReadersWhenALaterInputCannotBeOpened()
    {
        var first = CreateWav("first.wav");
        var second = CreateWav("second.wav");
        using var lockedInput = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAnyAsync<IOException>(() => _processor.MixAudioFilesAsync(
            [first, second], Path.Combine(_directory, "mixed.wav"), new() { TargetFormat = Format }).AsTask());

        AssertExclusiveAccess(first);
    }

    [Fact]
    public async Task FileCombinationReleasesEarlierStreamsWhenALaterInputIsMissing()
    {
        var first = CreateWav("first.wav");
        var missing = Path.Combine(_directory, "missing.wav");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _processor.CombineFilesToStreamAsync([first, missing], AudioFormat.Wav).AsTask());

        AssertExclusiveAccess(first);
    }

    private string CreateWav(string name)
    {
        var path = Path.Combine(_directory, name);
        using var writer = new WaveFileWriter(path, Format);
        writer.Write(new byte[16], 0, 16);
        return path;
    }

    private static void AssertExclusiveAccess(string path)
    {
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(_directory);
        if (!directory.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test directory must be within its temporary parent.");
        Directory.Delete(directory, recursive: true);
    }
}
