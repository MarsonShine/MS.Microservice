using NAudio.Lame;
using NAudio.Wave;

namespace MS.Microservice.Infrastructure.Common.NAudio
{
    /// <summary>
    /// 音频合并选项
    /// </summary>
    public class AudioCombineOptions
    {
        /// <summary>
        /// 目标音频格式
        /// </summary>
        public WaveFormat? TargetFormat { get; set; }

        /// <summary>
        /// 文件间的静音间隔（秒）
        /// </summary>
        public float SilenceDuration { get; set; } = 1.0f;

        /// <summary>
        /// 是否按文件名排序
        /// </summary>
        public bool SortByFileName { get; set; } = true;

        /// <summary>
        /// 重采样质量 (1-60)
        /// </summary>
        public int ResamplerQuality { get; set; } = 60;
    }

    /// <summary>
    /// 音频混音选项
    /// </summary>
    public class AudioMixOptions
    {
        /// <summary>
        /// 目标音频格式
        /// </summary>
        public WaveFormat? TargetFormat { get; set; }

        /// <summary>
        /// 是否自动转换为立体声
        /// </summary>
        public bool AutoConvertToStereo { get; set; } = true;

        /// <summary>
        /// 混音器缓冲区大小
        /// </summary>
        public int BufferSize { get; set; } = 4096;
    }

    /// <summary>
    /// 音频转换选项
    /// </summary>
    public class AudioConvertOptions
    {
        /// <summary>
        /// 目标音频格式
        /// </summary>
        public WaveFormat TargetFormat { get; set; } = new WaveFormat(44100, 16, 2);

        /// <summary>
        /// MP3编码预设（仅MP3格式）
        /// </summary>
        public LAMEPreset Mp3Preset { get; set; } = LAMEPreset.STANDARD;

        /// <summary>
        /// 重采样质量 (1-60)
        /// </summary>
        public int ResamplerQuality { get; set; } = 60;
    }

    /// <summary>
    /// 支持的音频格式
    /// </summary>
    public enum AudioFormat
    {
        Mp3,
        Wav,
        Auto // 根据文件扩展名自动判断
    }
}