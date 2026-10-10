namespace MS.Microservice.Media.Video;

/// <summary>Trusted host configuration for installed FFmpeg executables.</summary>
public sealed record FfmpegOptions
{
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string FfprobePath { get; init; } = "ffprobe";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>Metadata of the first video track which is not an attached cover image.</summary>
public sealed record VideoInfo(
    int Width, int Height, string? Codec, TimeSpan? Duration,
    double? FrameRate, bool HasAudio, int RotationDegrees);

public sealed class MediaProcessException(string executable, int exitCode, string standardError)
    : Exception($"{Path.GetFileName(executable)} exited with code {exitCode}: {standardError}")
{
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = standardError;
}
