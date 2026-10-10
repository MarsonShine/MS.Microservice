using NAudio.Lame;

namespace MS.Microservice.Infrastructure.Common.NAudio;

public sealed record AudioEditOptions
{
    public TimeSpan Start { get; init; }
    public TimeSpan? Duration { get; init; }
    public float Gain { get; init; } = 1;
    public TimeSpan FadeIn { get; init; }
    public TimeSpan FadeOut { get; init; }
    public LAMEPreset Mp3Preset { get; init; } = LAMEPreset.STANDARD;
}
