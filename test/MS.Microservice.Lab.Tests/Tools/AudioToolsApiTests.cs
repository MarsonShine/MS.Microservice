using System.ComponentModel.DataAnnotations;
using MS.Microservice.Lab.Controller;
using NAudio.Wave;
using Xunit;
using static MS.Microservice.Lab.Tests.Tools.ToolDemoHttpFiles;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class AudioToolsApiTests
{
    [Fact]
    public async Task Audio_EditDownloadsActualTrimmedAndScaledWav()
    {
        using var host = new ToolDemoTestHost();
        var bytes = await PostFileAsync(host.Client, "/api/lab/tools/audio/edit", Wav(), "source.wav", "audio/wav",
            new() { ["StartSeconds"] = "0.025", ["DurationSeconds"] = "0.05", ["Gain"] = "0.5", ["Format"] = "Wav" });
        using var reader = new WaveFileReader(new MemoryStream(bytes));
        Assert.Equal(WaveFormatEncoding.IeeeFloat, reader.WaveFormat.Encoding);
        Assert.Equal(8000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(400, reader.Length / reader.WaveFormat.BlockAlign);
        var provider = reader.ToSampleProvider();
        var samples = new float[400];
        Assert.Equal(400, provider.Read(samples, 0, samples.Length));
        Assert.All(samples, sample => Assert.InRange(sample, 0.1249f, 0.1251f));
    }

    [Fact]
    public void SilenceRange_RejectsFractionalValuesBeyondDeclaredUpperBound()
    {
        var context = new ValidationContext(new AudioCombineForm()) { MemberName = nameof(AudioCombineForm.SilenceSeconds) };
        Assert.False(Validator.TryValidateProperty(60.4f, context, new List<ValidationResult>()));
        Assert.True(Validator.TryValidateProperty(60f, context, new List<ValidationResult>()));
    }

    private static byte[] Wav()
    {
        using var bytes = new MemoryStream();
        using (var writer = new WaveFileWriter(bytes, new WaveFormat(8000, 16, 1)))
            for (var index = 0; index < 1600; index++) writer.WriteSample(0.25f);
        return bytes.ToArray();
    }
}
