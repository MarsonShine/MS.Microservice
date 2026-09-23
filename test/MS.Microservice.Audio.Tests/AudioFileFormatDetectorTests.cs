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

    [Theory]
    [InlineData(12, 10, AudioFormat.Mp3)]
    [InlineData(1024, 1022, AudioFormat.Mp3)]
    [InlineData(1025, 1023, AudioFormat.Auto)]
    public void DetectFormatFromStream_ScansOnlyTheFirst1024BytesForMp3Frame(int length, int frameOffset, AudioFormat expected)
    {
        var bytes = new byte[length];
        bytes[frameOffset] = 0xff;
        bytes[frameOffset + 1] = 0xe0;
        using var stream = new MemoryStream(bytes);
        stream.Position = 7;

        AudioFileFormatDetector.DetectFormatFromStream(stream).Should().Be(expected);
        stream.Position.Should().Be(7);
    }

    [Theory]
    [InlineData(0x66, 0x4c, 0x61, 0x43)]
    [InlineData(0x4f, 0x67, 0x67, 0x53)]
    public void DetectFormatFromStream_LeavesOtherFormatsAsAuto(byte first, byte second, byte third, byte fourth)
    {
        using var stream = new MemoryStream([first, second, third, fourth, 0, 0, 0, 0, 0, 0, 0, 0]);

        AudioFileFormatDetector.DetectFormatFromStream(stream).Should().Be(AudioFormat.Auto);
    }

    [Fact]
    public void DetectFormatFromStream_BoundsScanBeforeConvertingLargeLengthToInt()
    {
        using var stream = new LargeLengthStream(new byte[1024]);
        stream.Position = 3;

        AudioFileFormatDetector.DetectFormatFromStream(stream).Should().Be(AudioFormat.Auto);
        stream.Position.Should().Be(3);
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

    private sealed class LargeLengthStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override long Length => (long)int.MaxValue + 1;
    }
}
