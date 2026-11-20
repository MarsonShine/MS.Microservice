using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;
using Xunit;

namespace MS.Microservice.Audio.Tests;

[CollectionDefinition("Audio demo working directory", DisableParallelization = true)]
public sealed class AudioDemoDirectoryCollection;

[Collection("Audio demo working directory")]
public sealed class AudioDemoTests
{
    [Fact]
    public async Task DemoStreamAndDataProcessingWritesAllSixExamples()
    {
        var previousDirectory = Environment.CurrentDirectory;
        var directory = Directory.CreateTempSubdirectory("audio-demo-");
        try
        {
            Environment.CurrentDirectory = directory.FullName;
            var format = new WaveFormat(44100, 16, 2);
            byte[] first = [1, 2, 3, 4];
            byte[] second = [5, 6, 7, 8];
            using (var writer = new WaveFileWriter("audio1.wav", format))
                writer.Write(first, 0, first.Length);
            using (var writer = new WaveFileWriter("audio2.wav", format))
                writer.Write(second, 0, second.Length);

            await new AudioProcessor().DemoStreamAndDataProcessingAsync();

            var silence = new byte[format.AverageBytesPerSecond];
            byte[] single = [.. first, .. silence];
            byte[] combined = [.. single, .. second, .. silence];
            foreach (var name in new[] { "combined_from_data.wav", "combined_data_output.wav", "combined_from_streams.wav", "combined_stream_output.wav" })
                AssertPcm(name, format, combined);
            AssertPcm("single_from_data.wav", format, single);
            AssertPcm("single_from_stream.wav", format, single);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            directory.Delete(recursive: true);
        }
    }

    private static void AssertPcm(string path, WaveFormat format, byte[] expected)
    {
        using var reader = new WaveFileReader(path);
        Assert.Equal(format, reader.WaveFormat);
        Assert.Equal(expected.LongLength, reader.Length);
        var actual = new byte[expected.Length];
        reader.ReadExactly(actual);
        Assert.Equal(expected, actual);
    }
}
