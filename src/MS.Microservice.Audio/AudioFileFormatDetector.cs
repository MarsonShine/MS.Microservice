using System;
using System.IO;

namespace MS.Microservice.Infrastructure.Common.NAudio
{
    /// <summary>
    /// 音频文件格式检测器
    /// </summary>
    public static class AudioFileFormatDetector
    {
        private const int MaxProbeBytes = 1024;

        /// <summary>
        /// 检测音频文件的真实格式
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>真实的音频格式</returns>
        public static AudioFormat DetectActualFormat(string filePath)
        {
            try
            {
                using var fileStream = OpenProbeFile(filePath);
                if (fileStream is not null)
                    return DetectFormatFromStream(fileStream);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法检测文件格式: {filePath}, 错误: {ex.Message}", ex);
            }
            throw new FileNotFoundException($"文件不存在: {filePath}");
        }

        private static FileStream? OpenProbeFile(string filePath)
        {
            try
            {
                return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, MaxProbeBytes);
            }
            catch (Exception) when (!File.Exists(filePath))
            {
                // 只在打开失败时区分路径不存在与其他 I/O 错误。
                return null;
            }
        }

        /// <summary>
        /// 从流中检测音频格式
        /// </summary>
        /// <param name="stream">音频流</param>
        /// <returns>检测到的格式</returns>
        public static AudioFormat DetectFormatFromStream(Stream stream)
        {
            var length = stream.Length;
            if (length < 12)
                throw new InvalidOperationException("文件太小，无法确定格式");

            var originalPosition = stream.Position;
            stream.Position = 0;

            try
            {
                Span<byte> header = stackalloc byte[12];
                stream.ReadExactly(header);
                // 检测 WAV 格式 (RIFF....WAVE)
                if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                    && header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E')
                    return AudioFormat.Wav;
                if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                    return AudioFormat.Mp3;

                return HasMp3Frame(stream, header, (int)Math.Min(length, MaxProbeBytes))
                    ? AudioFormat.Mp3
                    : AudioFormat.Auto;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static bool HasMp3Frame(Stream stream, ReadOnlySpan<byte> header, int probeLength)
        {
            Span<byte> probe = stackalloc byte[probeLength];
            header.CopyTo(probe);
            stream.ReadExactly(probe[header.Length..]);

            // 先跳过首个候选之前的字节，再顺序检查相邻字节（含跨越文件头边界的字节对）。
            int first = probe[..^1].IndexOf((byte)0xff);
            if (first < 0)
                return false;
            for (int i = first; i < probe.Length - 1; i++)
            {
                if (probe[i] == 0xff && (probe[i + 1] & 0xe0) == 0xe0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 获取文件格式的描述信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>格式信息</returns>
        public static AudioFormatInfo GetFormatInfo(string filePath)
        {
            var detectedFormat = DetectActualFormat(filePath);
            var extensionFormat = GetFormatFromExtension(filePath);

            return new AudioFormatInfo
            {
                FilePath = filePath,
                DetectedFormat = detectedFormat,
                ExtensionFormat = extensionFormat,
                IsFormatMismatch = detectedFormat != extensionFormat,
                RecommendedExtension = GetRecommendedExtension(detectedFormat)
            };
        }

        /// <summary>
        /// 根据扩展名推断格式
        /// </summary>
        internal static AudioFormat GetFormatFromExtension(string filePath)
        {
            var extension = Path.GetExtension(filePath.AsSpan());
            if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                return AudioFormat.Mp3;
            if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                return AudioFormat.Wav;
            return AudioFormat.Auto;
        }

        /// <summary>
        /// 获取推荐的文件扩展名
        /// </summary>
        private static string GetRecommendedExtension(AudioFormat format)
        {
            return format switch
            {
                AudioFormat.Mp3 => ".mp3",
                AudioFormat.Wav => ".wav",
                _ => ".audio"
            };
        }
    }

    /// <summary>
    /// 音频格式信息
    /// </summary>
    public class AudioFormatInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public AudioFormat DetectedFormat { get; set; }
        public AudioFormat ExtensionFormat { get; set; }
        public bool IsFormatMismatch { get; set; }
        public string RecommendedExtension { get; set; } = string.Empty;

        public override string ToString()
        {
            var mismatchInfo = IsFormatMismatch ? " [格式不匹配!]" : "";
            return $"文件: {Path.GetFileName(FilePath)}, " +
                   $"检测格式: {DetectedFormat}, " +
                   $"扩展名格式: {ExtensionFormat}" +
                   mismatchInfo;
        }
    }
}
