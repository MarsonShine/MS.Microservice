using System.ComponentModel.DataAnnotations;

namespace MS.Microservice.Lab.Controller;

/// <summary>音频下载格式。</summary>
public enum AudioToolFormat { Wav, Mp3 }

/// <summary>上传待处理的 WAV 或 MP3。</summary>
public class AudioInputForm
{
    /// <summary>必填音频文件，支持 .wav、.mp3，单文件最多 20 MiB。</summary>
    [Required] public IFormFile File { get; set; } = null!;
}

/// <summary>音频编辑与下载参数。</summary>
public sealed class AudioEditForm : AudioInputForm
{
    /// <summary>输出格式，默认 Wav；WAV 编辑结果为 32 位浮点采样。</summary>
    [EnumDataType(typeof(AudioToolFormat))] public AudioToolFormat Format { get; set; } = AudioToolFormat.Wav;
    /// <summary>从第几秒开始，默认 0。</summary>
    [Range(0d, double.MaxValue)] public double StartSeconds { get; set; }
    /// <summary>保留时长（秒）；省略时保留到音频末尾。</summary>
    [Range(double.Epsilon, double.MaxValue)] public double? DurationSeconds { get; set; }
    /// <summary>音量倍数，默认 1；0 表示静音。</summary>
    [Range(0d, float.MaxValue)] public float Gain { get; set; } = 1;
    /// <summary>淡入时长（秒），默认 0；不得超过保留片段。</summary>
    [Range(0d, double.MaxValue)] public double FadeInSeconds { get; set; }
    /// <summary>淡出时长（秒），默认 0；不得超过保留片段。</summary>
    [Range(0d, double.MaxValue)] public double FadeOutSeconds { get; set; }
}

/// <summary>多文件音频处理参数。</summary>
public class AudioFilesForm
{
    /// <summary>按提交顺序处理 2–8 个 WAV／MP3，整个请求最多 25 MiB。</summary>
    [MinLength(2), MaxLength(8)] public List<IFormFile> Files { get; set; } = [];
    /// <summary>输出格式，默认 Wav。</summary>
    [EnumDataType(typeof(AudioToolFormat))] public AudioToolFormat Format { get; set; } = AudioToolFormat.Wav;
}

/// <summary>顺序合并音频的参数。</summary>
public sealed class AudioCombineForm : AudioFilesForm
{
    /// <summary>每段后附加的静音（秒），包含最后一段，默认 0.5。</summary>
    [Range(0d, 60d)] public float SilenceSeconds { get; set; } = 0.5f;
}

/// <summary>音频转码的格式与采样率参数。</summary>
public class AudioTranscodeForm : AudioInputForm
{
    /// <summary>输出格式，默认 Wav；MP3 编码需要 LAME 原生库。</summary>
    [EnumDataType(typeof(AudioToolFormat))] public AudioToolFormat Format { get; set; } = AudioToolFormat.Wav;
    /// <summary>目标采样率（Hz），默认 44100。</summary>
    [Range(8000, 192000)] public int SampleRate { get; set; } = 44100;
}

/// <summary>音频转码与目标声道参数。</summary>
public sealed class AudioConvertForm : AudioTranscodeForm
{
    /// <summary>目标声道数，默认 2；支持 1 或 2。</summary>
    [Range(1, 2)] public int Channels { get; set; } = 2;
}

/// <summary>音频信息，不包含服务端临时文件路径。</summary>
public sealed class AudioToolInfo
{
    /// <summary>探测到的音频格式。</summary>
    public string Format { get; init; } = "";
    /// <summary>音频时长，单位秒。</summary>
    public double DurationSeconds { get; init; }
    /// <summary>采样率，单位 Hz。</summary>
    public int SampleRate { get; init; }
    /// <summary>声道数。</summary>
    public int Channels { get; init; }
    /// <summary>解码采样位数。</summary>
    public int BitsPerSample { get; init; }
    /// <summary>源文件编码字节数。</summary>
    public long FileBytes { get; init; }
}
