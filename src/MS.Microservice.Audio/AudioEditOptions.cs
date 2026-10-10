using NAudio.Lame;

namespace MS.Microservice.Infrastructure.Common.NAudio;

public sealed record AudioEditOptions
{
    /// <summary>
    /// 裁剪起始时间
    /// </summary>
    public TimeSpan Start { get; init; }
    /// <summary>
    /// 裁剪时长
    /// </summary>
    public TimeSpan? Duration { get; init; }
    /// <summary>
    /// 音量倍数
    /// </summary>
    public float Gain { get; init; } = 1;
    /// <summary>
    /// 淡入时长，默认不启用
    /// </summary>
    public TimeSpan FadeIn { get; init; }
    /// <summary>
    /// 淡出时长，默认不启用
    /// </summary>
    public TimeSpan FadeOut { get; init; }
    /// <summary>
    /// MP3 编码预设
    /// </summary>
    public LAMEPreset Mp3Preset { get; init; } = LAMEPreset.STANDARD;
}
