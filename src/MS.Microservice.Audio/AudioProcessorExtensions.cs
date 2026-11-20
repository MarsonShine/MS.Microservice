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
            public Task BatchConvertAudioFormatAsync(
                IEnumerable<string> inputFiles, string outputDirectory, AudioFormat outputFormat,
                AudioConvertOptions? options = null)
                => processor.BatchConvertAsync(inputFiles, outputDirectory, outputFormat, options);

            /// <summary>批量转换；旧入口默认使用处理器数作为并发上限，等待所有项后传播失败。</summary>
            public Task BatchConvertAsync(IEnumerable<string> inputFiles, string outputDirectory,
                AudioFormat outputFormat, AudioConvertOptions? options = null)
                => processor.BatchConvertAsync(inputFiles, outputDirectory, outputFormat, options, Environment.ProcessorCount, CancellationToken.None);

            /// <summary>受限并发转换；取消停止后续派发，已开始的旧音频转换仍需等待完成。</summary>
            public async Task BatchConvertAsync(IEnumerable<string> inputFiles, string outputDirectory,
                AudioFormat outputFormat, AudioConvertOptions? options, int maxConcurrency, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(inputFiles);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
                cancellationToken.ThrowIfCancellationRequested();
                options ??= new AudioConvertOptions();
                var files = inputFiles.ToArray();
                Directory.CreateDirectory(outputDirectory);
                var results = await MS.Microservice.Threading.BatchProcessing.CollectAsync<string, bool>(files, async (inputFile, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var extension = outputFormat == AudioFormat.Mp3 ? ".mp3" : ".wav";
                    var outputFile = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputFile) + extension);
                    await processor.ConvertAudioFormatAsync(inputFile, outputFile, options);
                    return true;
                }, maxConcurrency, cancellationToken);
                // Task.WhenAll reports faults ahead of canceled items; retain that legacy contract.
                var failure = results.FirstOrDefault(result => result.Error is not null and not OperationCanceledException)
                    ?? results.FirstOrDefault(result => !result.IsSuccess);
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.Error!).Throw();
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
                ArgumentNullException.ThrowIfNull(inputFiles);
                var streams = new List<FileStream>();
                try
                {
                    foreach (var file in inputFiles)
                    {
                        streams.Add(new FileStream(file, FileMode.Open, FileAccess.Read));
                    }

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

                // 每次合并都接管输入流。复用音频数据，为每次调用创建独立的流。
                MemoryStream[] OpenInputStreams() => [new(audioData1, writable: false), new(audioData2, writable: false)];
                await processor.CombineAudioStreamsAsync(OpenInputStreams(), "combined_from_streams.wav");

                using var resultStream = await processor.CombineAudioStreamsAsync(OpenInputStreams(), AudioFormat.Wav);
                using var outputFile = new FileStream("combined_stream_output.wav", FileMode.Create, FileAccess.Write);
                await resultStream.CopyToAsync(outputFile);

                await processor.CreateAudioFileFromDataAsync(audioData1, "single_from_data.wav");

                using var singleInput = new MemoryStream(audioData1, writable: false);
                await processor.CreateAudioFileFromStreamAsync(singleInput, "single_from_stream.wav");
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

                    var fileInfo = GetExistingAudioFileInfo(filePath);

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

            return GetExistingAudioFileInfo(filePath);
        }

        private static AudioFileInfo GetExistingAudioFileInfo(string filePath)
        {
            var format = AudioFileFormatDetector.GetFormatFromExtension(filePath);
            if (format is not (AudioFormat.Wav or AudioFormat.Mp3))
                return GetGenericAudioFileInfo(filePath);

            using var stream = File.OpenRead(filePath);
            return GetWavOrMp3Info(stream, format, filePath);
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

                return format is AudioFormat.Wav or AudioFormat.Mp3
                    ? GetWavOrMp3Info(audioStream, format, "<Stream>")
                    : GetGenericStreamInfo(audioStream);
            }
            finally
            {
                audioStream.Position = originalPosition;
            }
        }

        private static AudioFileInfo GetWavOrMp3Info(Stream stream, AudioFormat format, string filePath)
        {
            using WaveStream reader = format == AudioFormat.Wav ? new WaveFileReader(stream) : new Mp3FileReader(stream);
            return new AudioFileInfo
            {
                FilePath = filePath,
                Duration = reader.TotalTime,
                SampleRate = reader.WaveFormat.SampleRate,
                Channels = reader.WaveFormat.Channels,
                BitsPerSample = reader.WaveFormat.BitsPerSample,
                FileSize = stream.Length
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
