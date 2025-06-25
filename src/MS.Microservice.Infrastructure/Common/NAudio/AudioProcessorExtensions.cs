using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Common.NAudio
{
    /// <summary>
    /// 音频处理器扩展方法
    /// </summary>
    public static class AudioProcessorExtensions
    {
        /// <summary>
        /// 批量处理目录中的音频文件
        /// </summary>
        /// <param name="processor">音频处理器</param>
        /// <param name="inputDirectory">输入目录</param>
        /// <param name="outputFile">输出文件</param>
        /// <param name="searchPattern">搜索模式</param>
        /// <param name="options">合并选项</param>
        public static async Task CombineDirectoryAudioFilesAsync(this IAudioProcessor processor,
            string inputDirectory, string outputFile, string searchPattern = "*.mp3",
            AudioCombineOptions? options = null)
        {
            if (!Directory.Exists(inputDirectory))
                throw new DirectoryNotFoundException($"输入目录不存在: {inputDirectory}");

            var files = Directory.GetFiles(inputDirectory, searchPattern);
            if (files.Length == 0)
                throw new InvalidOperationException($"在目录 {inputDirectory} 中未找到匹配的音频文件");

            await processor.CombineAudioFilesAsync(files, outputFile, options);
        }

        /// <summary>
        /// 批量转换音频格式
        /// </summary>
        /// <param name="processor">音频处理器</param>
        /// <param name="inputFiles">输入文件集合</param>
        /// <param name="outputDirectory">输出目录</param>
        /// <param name="outputFormat">输出格式</param>
        /// <param name="options">转换选项</param>
        public static async Task BatchConvertAudioFormatAsync(this IAudioProcessor processor,
            IEnumerable<string> inputFiles, string outputDirectory, AudioFormat outputFormat,
            AudioConvertOptions? options = null)
        {
            options ??= new AudioConvertOptions();

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var tasks = inputFiles.Select(async inputFile =>
            {
                var fileName = Path.GetFileNameWithoutExtension(inputFile);
                var extension = outputFormat == AudioFormat.Mp3 ? ".mp3" : ".wav";
                var outputFile = Path.Combine(outputDirectory, fileName + extension);

                await processor.ConvertAudioFormatAsync(inputFile, outputFile, options);
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 获取音频文件信息
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>音频信息</returns>
        public static AudioFileInfo GetAudioFileInfo(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            // 根据文件扩展名选择合适的读取器来获取原始格式信息
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".wav" => GetWavFileInfo(filePath),
                ".mp3" => GetMp3FileInfo(filePath),
                _ => GetGenericAudioFileInfo(filePath)
            };
        }

        private static AudioFileInfo GetWavFileInfo(string filePath)
        {
            // 使用 WaveFileReader 读取原始WAV格式信息
            using var reader = new WaveFileReader(filePath);
            return new AudioFileInfo
            {
                FilePath = filePath,
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitsPerSample = reader.WaveFormat.BitsPerSample, // 保持原始位深
                FileSize = new FileInfo(filePath).Length
            };
        }

        private static AudioFileInfo GetMp3FileInfo(string filePath)
        {
            // 使用 Mp3FileReader 读取MP3格式信息
            using var reader = new Mp3FileReader(filePath);
            return new AudioFileInfo
            {
                FilePath = filePath,
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitsPerSample = reader.WaveFormat.BitsPerSample,
                FileSize = new FileInfo(filePath).Length
            };
        }

        private static AudioFileInfo GetGenericAudioFileInfo(string filePath)
        {
            // 对于其他格式，使用 AudioFileReader（会转换为32位浮点）
            using var reader = new AudioFileReader(filePath);
            return new AudioFileInfo
            {
                FilePath = filePath,
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitsPerSample = reader.WaveFormat.BitsPerSample,
                FileSize = new FileInfo(filePath).Length
            };
        }
    }

    /// <summary>
    /// 音频文件信息
    /// </summary>
    public class AudioFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public long FileSize { get; set; }

        public override string ToString()
        {
            return $"文件: {Path.GetFileName(FilePath)}, " +
                   $"时长: {Duration:mm\\:ss}, " +
                   $"采样率: {SampleRate}Hz, " +
                   $"声道: {Channels}, " +
                   $"位深: {BitsPerSample}bit, " +
                   $"大小: {FileSize / 1024.0 / 1024.0:F2}MB";
        }
    }
}