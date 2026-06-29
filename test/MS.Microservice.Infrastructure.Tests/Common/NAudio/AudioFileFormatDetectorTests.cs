using System;
using System.IO;
using FluentAssertions;
using MS.Microservice.Infrastructure.Common.NAudio;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Common.NAudio;

public sealed class AudioFileFormatDetectorTests
{
    [Fact]
    public void DetectFormatFromStream_ShouldDetectWav_AndRestorePosition()
    {
        using var stream = new MemoryStream([
            0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45
        ]);
        stream.Position = 5;

        var format = AudioFileFormatDetector.DetectFormatFromStream(stream);

        format.Should().Be(AudioFormat.Wav);
        stream.Position.Should().Be(5);
    }

    [Fact]
    public void DetectFormatFromStream_ShouldDetectMp3FromId3Header()
    {
        using var stream = new MemoryStream([
            0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ]);

        var format = AudioFileFormatDetector.DetectFormatFromStream(stream);

        format.Should().Be(AudioFormat.Mp3);
    }

    [Fact]
    public void DetectFormatFromStream_WhenStreamTooSmall_ShouldThrow()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        Action action = () => AudioFileFormatDetector.DetectFormatFromStream(stream);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("文件太小，无法确定格式");
    }

    [Fact]
    public void DetectActualFormat_And_GetFormatInfo_ShouldReportMismatch()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, [
            0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45
        ]);

        try
        {
            var info = AudioFileFormatDetector.GetFormatInfo(path);

            info.DetectedFormat.Should().Be(AudioFormat.Wav);
            info.ExtensionFormat.Should().Be(AudioFormat.Mp3);
            info.IsFormatMismatch.Should().BeTrue();
            info.RecommendedExtension.Should().Be(".wav");
            info.ToString().Should().Contain("[格式不匹配!]");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DetectActualFormat_WhenFileDoesNotExist_ShouldThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");

        Action action = () => AudioFileFormatDetector.DetectActualFormat(path);

        action.Should().Throw<FileNotFoundException>();
    }
}
