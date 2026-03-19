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
    public static partial class AudioProcessorExtensions
    {
        extension(IAudioProcessor processor)
        {
            /// <summary>
            /// 批量处理目录中的音频文件
            /// </summary>
            public async Task CombineDirectoryAudioFilesAsync(
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
            public async Task BatchConvertAudioFormatAsync(
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
            /// 批量转换音频文件格式
            /// </summary>
            public async Task BatchConvertAsync(
                IEnumerable<string> inputFiles,
                string outputDirectory,
                AudioFormat outputFormat,
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
            /// 从内存中的音频数据创建音频文件
            /// </summary>
            public async ValueTask CreateAudioFileFromDataAsync(
                byte[] audioData,
                string outputFile,
                AudioCombineOptions? options = null)
            {
                await processor.CombineAudioDataAsync(new[] { audioData }, outputFile, options);
            }

            /// <summary>
            /// 从内存中的音频流创建音频文件
            /// </summary>
            public async ValueTask CreateAudioFileFromStreamAsync(
                Stream audioStream,
                string outputFile,
                AudioCombineOptions? options = null)
            {
                await processor.CombineAudioStreamsAsync(new[] { audioStream }, outputFile, options);
            }

            /// <summary>
            /// 将多个音频文件合并为单个内存流
            /// </summary>
            public async ValueTask<Stream> CombineFilesToStreamAsync(
                IEnumerable<string> inputFiles,
                AudioFormat outputFormat,
                AudioCombineOptions? options = null)
            {
                var streams = inputFiles.Select(file => new FileStream(file, FileMode.Open, FileAccess.Read)).ToList();
                try
                {
                    return await processor.CombineAudioStreamsAsync(streams, outputFormat, options);
                }
                finally
                {
                    foreach (var stream in streams)
                    {
                        stream.Dispose();
                    }
                }
            }

            /// <summary>
            /// 将多个音频文件合并为字节数组
            /// </summary>
            public async ValueTask<byte[]> CombineFilesToDataAsync(
                IEnumerable<string> inputFiles,
                AudioFormat outputFormat,
                AudioCombineOptions? options = null)
            {
                using var stream = await processor.CombineFilesToStreamAsync(inputFiles, outputFormat, options);
                return ((MemoryStream)stream).ToArray();
            }

            /// <summary>
            /// 演示如何使用新的音频处理功能
            /// </summary>
            public async Task DemoStreamAndDataProcessingAsync()
            {
                var audioData1 = await File.ReadAllBytesAsync("audio1.wav");
                var audioData2 = await File.ReadAllBytesAsync("audio2.wav");
                var audioDataList = new[] { audioData1, audioData2 };

                await processor.CombineAudioDataAsync(audioDataList, "combined_from_data.wav");

                var combinedData = await processor.CombineAudioDataAsync(audioDataList, AudioFormat.Wav);
                await File.WriteAllBytesAsync("combined_data_output.wav", combinedData);

                using var stream1 = new FileStream("audio1.wav", FileMode.Open, FileAccess.Read);
                using var stream2 = new FileStream("audio2.wav", FileMode.Open, FileAccess.Read);
                var streamList = new[] { stream1, stream2 };

                await processor.CombineAudioStreamsAsync(streamList, "combined_from_streams.wav");

                stream1.Position = 0;
                stream2.Position = 0;

                using var resultStream = await processor.CombineAudioStreamsAsync(streamList, AudioFormat.Wav);
                using var outputFile = new FileStream("combined_stream_output.wav", FileMode.Create, FileAccess.Write);
                await resultStream.CopyToAsync(outputFile);

                await processor.CreateAudioFileFromDataAsync(audioData1, "single_from_data.wav");

                stream1.Position = 0;
                await processor.CreateAudioFileFromStreamAsync(stream1, "single_from_stream.wav");
            }

            /// <summary>
            /// 验证音频处理结果
            /// </summary>
            public bool ValidateAudioFile(string filePath)
            {
                try
                {
                    if (!File.Exists(filePath))
                        return false;

                    var fileInfo = GetAudioFileInfo(filePath);

                    return fileInfo.FileSize > 0 &&
                           fileInfo.Duration > TimeSpan.Zero &&
                           fileInfo.SampleRate > 0 &&
                           fileInfo.Channels > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 获取音频文件信息
        /// </summary>
        public static AudioFileInfo GetAudioFileInfo(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在: {filePath}");

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".wav" => GetWavFileInfo(filePath),
                ".mp3" => GetMp3FileInfo(filePath),
                _ => GetGenericAudioFileInfo(filePath)
            };
        }

        /// <summary>
        /// 获取音频流信息
        /// </summary>
        public static AudioFileInfo GetAudioStreamInfo(Stream audioStream)
        {
            if (audioStream == null)
                throw new ArgumentNullException(nameof(audioStream));

            var originalPosition = audioStream.Position;
            try
            {
                audioStream.Position = 0;
                var format = AudioFileFormatDetector.DetectFormatFromStream(audioStream);
                audioStream.Position = 0;

                return format switch
                {
                    AudioFormat.Mp3 => GetMp3StreamInfo(audioStream),
                    AudioFormat.Wav => GetWavStreamInfo(audioStream),
                    _ => GetGenericStreamInfo(audioStream)
                };
            }
            finally
            {
                audioStream.Position = originalPosition;
            }
        }

        private static AudioFileInfo GetWavFileInfo(string filePath)
        {
            using var reader = new WaveFileReader(filePath);
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

        private static AudioFileInfo GetMp3FileInfo(string filePath)
        {
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

        private static AudioFileInfo GetWavStreamInfo(Stream stream)
        {
            using var reader = new WaveFileReader(stream);
            return new AudioFileInfo
            {
                FilePath = "<Stream>",
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitsPerSample = reader.WaveFormat.BitsPerSample,
                FileSize = stream.Length
            };
        }

        private static AudioFileInfo GetMp3StreamInfo(Stream stream)
        {
            using var reader = new Mp3FileReader(stream);
            return new AudioFileInfo
            {
                FilePath = "<Stream>",
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitsPerSample = reader.WaveFormat.BitsPerSample,
                FileSize = stream.Length
            };
        }

        private static AudioFileInfo GetGenericStreamInfo(Stream stream)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var originalPosition = stream.Position;
                stream.Position = 0;
                using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
                stream.Position = originalPosition;

                using var reader = new AudioFileReader(tempFile);
                return new AudioFileInfo
                {
                    FilePath = "<Stream>",
                    Duration = reader.TotalTime,
                    SampleRate = reader.WaveFormat.SampleRate,
                    Channels = reader.WaveFormat.Channels,
                    BitsPerSample = reader.WaveFormat.BitsPerSample,
                    FileSize = stream.Length
                };
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
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
                   $"文件大小: {FileSize / 1024.0:F2}KB";
        }
    }
}