using System;
using System.IO;

namespace MS.Microservice.Infrastructure.Common.NAudio
{
    /// <summary>
    /// 音频文件格式检测器
    /// </summary>
    public static class AudioFileFormatDetector
    {
        /// <summary>
        /// 检测音频文件的真实格式
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>真实的音频格式</returns>
        public static AudioFormat DetectActualFormat(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                return DetectFormatFromStream(fileStream);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法检测文件格式: {filePath}, 错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从流中检测音频格式
        /// </summary>
        /// <param name="stream">音频流</param>
        /// <returns>检测到的格式</returns>
        public static AudioFormat DetectFormatFromStream(Stream stream)
        {
            if (stream.Length < 12)
                throw new InvalidOperationException("文件太小，无法确定格式");

            var originalPosition = stream.Position;
            stream.Position = 0;

            try
            {
                var header = new byte[12];
                stream.ReadExactly(header, 0, 12);

                // 检测 WAV 格式 (RIFF....WAVE)
                if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // "RIFF"
                    header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45)   // "WAVE"
                {
                    return AudioFormat.Wav;
                }

                // 检测 MP3 格式
                stream.Position = 0;
                if (IsMp3Format(stream))
                {
                    return AudioFormat.Mp3;
                }

                // 检测其他可能的格式
                stream.Position = 0;
                var firstBytes = new byte[4];
                stream.ReadExactly(firstBytes, 0, 4);

                // 检测 FLAC 格式
                if (firstBytes[0] == 0x66 && firstBytes[1] == 0x4C && firstBytes[2] == 0x61 && firstBytes[3] == 0x43) // "fLaC"
                {
                    return AudioFormat.Auto; // 暂时归类为Auto，后续可以扩展
                }

                // 检测 OGG 格式
                if (firstBytes[0] == 0x4F && firstBytes[1] == 0x67 && firstBytes[2] == 0x67 && firstBytes[3] == 0x53) // "OggS"
                {
                    return AudioFormat.Auto;
                }

                return AudioFormat.Auto; // 未知格式
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        /// <summary>
        /// 检测是否为MP3格式
        /// </summary>
        private static bool IsMp3Format(Stream stream)
        {
            var buffer = new byte[10];
            stream.ReadExactly(buffer, 0, 10);

            // 检测 ID3v2 标签
            if (buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33) // "ID3"
            {
                return true;
            }

            // 检测 MP3 帧头
            stream.Position = 0;
            var searchBuffer = new byte[Math.Min(1024, (int)stream.Length)];
            stream.ReadExactly(searchBuffer);

            for (int i = 0; i < searchBuffer.Length - 1; i++)
            {
                // MP3帧同步字：11111111 111xxxxx (0xFF 0xFx or 0xFF 0xEx)
                if (searchBuffer[i] == 0xFF &&
                    (searchBuffer[i + 1] & 0xE0) == 0xE0)
                {
                    return true;
                }
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
        private static AudioFormat GetFormatFromExtension(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".mp3" => AudioFormat.Mp3,
                ".wav" => AudioFormat.Wav,
                _ => AudioFormat.Auto
            };
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