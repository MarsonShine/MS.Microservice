namespace MS.Microservice.Media.Video;

public sealed record AudioExtractionOptions
{
    /// <summary>Zero-based index among audio tracks.</summary>
    public int AudioTrackIndex { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan? Duration { get; init; }
}

public sealed record AudioExtractionResult(string? Codec, int SampleRate, int Channels, TimeSpan? Duration);
