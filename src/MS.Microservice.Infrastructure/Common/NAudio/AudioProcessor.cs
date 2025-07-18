using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Common.NAudio
{
    /// <summary>
    /// 音频处理器实现
    /// </summary>
    public class AudioProcessor : IAudioProcessor
    {
        /// <summary>
        /// 合并多个音频文件为一个文件
        /// </summary>
        public async ValueTask CombineAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioCombineOptions? options = null)
        {
            options ??= new AudioCombineOptions();
            var files = inputFiles.ToArray();

            if (files.Length == 0)
                throw new ArgumentException("输入文件列表不能为空", nameof(inputFiles));

            ValidateInputFiles(files);

            // 将文件路径转换为流源
            var streamSources = files.Select(file => new FileStreamSource(file)).ToArray();

            try
            {
                if (options.SortByFileName)
                {
                    streamSources = streamSources.OrderBy(s => new FileInfo(s.SourcePath).Name).ToArray();
                }

                await CombineAudioSourcesAsync(streamSources, outputFile, options);
            }
            finally
            {
                foreach (var source in streamSources)
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>
        /// 合并多个音频流为一个文件
        /// </summary>
        public async ValueTask CombineAudioStreamsAsync(IEnumerable<Stream> inputStreams, string outputFile, AudioCombineOptions? options = null)
        {
            options ??= new AudioCombineOptions();
            var streams = inputStreams.ToArray();

            if (streams.Length == 0)
                throw new ArgumentException("输入流列表不能为空", nameof(inputStreams));

            // 将流转换为流源
            var streamSources = streams.Select(stream => new StreamSource(stream)).ToArray();

            try
            {
                await CombineAudioSourcesAsync(streamSources, outputFile, options);
            }
            finally
            {
                foreach (var source in streamSources)
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>
        /// 合并多个音频流并返回结果流
        /// </summary>
        public async ValueTask<Stream> CombineAudioStreamsAsync(IEnumerable<Stream> inputStreams, AudioFormat outputFormat, AudioCombineOptions? options = null)
        {
            options ??= new AudioCombineOptions();
            var streams = inputStreams.ToArray();

            if (streams.Length == 0)
                throw new ArgumentException("输入流列表不能为空", nameof(inputStreams));

            var outputStream = new MemoryStream();

            // 将流转换为流源
            var streamSources = streams.Select(stream => new StreamSource(stream)).ToArray();

            try
            {
                await CombineAudioSourcesAsync(streamSources, outputStream, outputFormat, options);
                outputStream.Position = 0;
                return outputStream;
            }
            catch
            {
                outputStream.Dispose();
                throw;
            }
            finally
            {
                foreach (var source in streamSources)
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>
        /// 合并多个音频字节数组为一个文件
        /// </summary>
        public async ValueTask CombineAudioDataAsync(IEnumerable<byte[]> inputAudioData, string outputFile, AudioCombineOptions? options = null)
        {
            options ??= new AudioCombineOptions();
            var audioDataArray = inputAudioData.ToArray();

            if (audioDataArray.Length == 0)
                throw new ArgumentException("输入音频数据列表不能为空", nameof(inputAudioData));

            // 将字节数组转换为流源
            var streamSources = audioDataArray.Select(data => new StreamSource(new MemoryStream(data))).ToArray();

            try
            {
                await CombineAudioSourcesAsync(streamSources, outputFile, options);
            }
            finally
            {
                foreach (var source in streamSources)
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>
        /// 合并多个音频字节数组并返回结果字节数组
        /// </summary>
        public async ValueTask<byte[]> CombineAudioDataAsync(IEnumerable<byte[]> inputAudioData, AudioFormat outputFormat, AudioCombineOptions? options = null)
        {
            options ??= new AudioCombineOptions();
            var audioDataArray = inputAudioData.ToArray();

            if (audioDataArray.Length == 0)
                throw new ArgumentException("输入音频数据列表不能为空", nameof(inputAudioData));

            var outputStream = new MemoryStream();

            // 将字节数组转换为流源
            var streamSources = audioDataArray.Select(data => new StreamSource(new MemoryStream(data))).ToArray();

            try
            {
                await CombineAudioSourcesAsync(streamSources, outputStream, outputFormat, options);
                return outputStream.ToArray();
            }
            finally
            {
                outputStream.Dispose();
                foreach (var source in streamSources)
                {
                    source.Dispose();
                }
            }
        }

        /// <summary>
        /// 混音多个音频文件
        /// </summary>
        public async ValueTask MixAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioMixOptions? options = null)
        {
            options ??= new AudioMixOptions();
            var files = inputFiles.ToArray();

            if (files.Length == 0)
                throw new ArgumentException("输入文件列表不能为空", nameof(inputFiles));

            ValidateInputFiles(files);
            EnsureOutputDirectory(outputFile);

            await Task.Run(() =>
            {
                var targetFormat = options.TargetFormat ?? new WaveFormat(44100, 16, 2);
                var outputFormat = GetOutputFormat(outputFile);

                var audioFileReaders = files.Select(file => new AudioFileReader(file)).ToList();
                try
                {
                    var sampleProviders = audioFileReaders
                        .Select(reader => options.AutoConvertToStereo ? ConvertToStereo(reader) : reader.ToSampleProvider())
                        .ToList();

                    var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(targetFormat.SampleRate, targetFormat.Channels));

                    foreach (var sampleProvider in sampleProviders)
                    {
                        mixer.AddMixerInput(sampleProvider);
                    }

                    WriteAudioToFile(mixer, outputFile, outputFormat, targetFormat, options);
                }
                finally
                {
                    audioFileReaders.ForEach(reader => reader.Dispose());
                }
            });
        }

        /// <summary>
        /// 转换音频格式
        /// </summary>
        public async ValueTask ConvertAudioFormatAsync(string inputFile, string outputFile, AudioConvertOptions options)
        {
            if (!File.Exists(inputFile))
                throw new FileNotFoundException($"输入文件不存在: {inputFile}");

            EnsureOutputDirectory(outputFile);

            await Task.Run(() =>
            {
                var inputFormat = GetInputFormat(inputFile);
                var outputFormat = GetOutputFormat(outputFile);

                var reader = CreateAudioReader(inputFile, inputFormat);

                if (!reader.WaveFormat.Equals(options.TargetFormat))
                {
                    using var resampler = new MediaFoundationResampler(reader, options.TargetFormat)
                    {
                        ResamplerQuality = options.ResamplerQuality
                    };

                    WriteConvertedAudio(resampler, outputFile, outputFormat, options);
                }
                else
                {
                    WriteConvertedAudio(reader, outputFile, outputFormat, options);
                }
            });
        }

        /// <summary>
        /// 将单声道转换为立体声
        /// </summary>
        public async ValueTask ConvertMonoToStereoAsync(string inputFile, string outputFile, AudioConvertOptions? options = null)
        {
            options ??= new AudioConvertOptions();

            if (!File.Exists(inputFile))
                throw new FileNotFoundException($"输入文件不存在: {inputFile}");

            EnsureOutputDirectory(outputFile);

            await Task.Run(() =>
            {
                using var reader = new AudioFileReader(inputFile);

                if (reader.WaveFormat.Channels == 2)
                {
                    // 已经是立体声，直接复制或转换格式
                    if (reader.WaveFormat.Equals(options.TargetFormat))
                    {
                        File.Copy(inputFile, outputFile, true);
                        return;
                    }
                }

                var stereoProvider = ConvertToStereo(reader);
                var outputFormat = GetOutputFormat(outputFile);

                switch (outputFormat)
                {
                    case AudioFormat.Mp3:
                        WriteSampleProviderToMp3(stereoProvider, outputFile, options.TargetFormat, options.Mp3Preset);
                        break;
                    case AudioFormat.Wav:
                        WriteSampleProviderToWav(stereoProvider, outputFile);
                        break;
                    default:
                        throw new NotSupportedException($"不支持的输出格式: {outputFormat}");
                }
            });
        }

        #region Core Combine Methods (DRY Implementation)

        /// <summary>
        /// 核心合并方法 - 合并音频源到文件
        /// </summary>
        private async ValueTask CombineAudioSourcesAsync(IAudioSource[] sources, string outputFile, AudioCombineOptions options)
        {
            EnsureOutputDirectory(outputFile);

            await Task.Run(() =>
            {
                var outputFormat = GetOutputFormat(outputFile);
                var targetFormat = options.TargetFormat ?? new WaveFormat(44100, 16, 2);

                CombineAudioSources(sources, outputFile, outputFormat, targetFormat, options);
            });
        }

        /// <summary>
        /// 核心合并方法 - 合并音频源到流
        /// </summary>
        private async ValueTask CombineAudioSourcesAsync(IAudioSource[] sources, Stream outputStream, AudioFormat outputFormat, AudioCombineOptions options)
        {
            await Task.Run(() =>
            {
                var targetFormat = options.TargetFormat ?? new WaveFormat(44100, 16, 2);
                CombineAudioSources(sources, outputStream, outputFormat, targetFormat, options);
            });
        }

        /// <summary>
        /// 核心合并逻辑 - 所有合并方法的基础实现
        /// </summary>
        private static void CombineAudioSources(IAudioSource[] sources, object output, AudioFormat outputFormat, WaveFormat targetFormat, AudioCombineOptions options)
        {
            switch (outputFormat)
            {
                case AudioFormat.Mp3:
                    CombineAudioSourcesAsMp3(sources, output, targetFormat, options);
                    break;
                case AudioFormat.Wav:
                    CombineAudioSourcesAsWav(sources, output, targetFormat, options);
                    break;
                default:
                    throw new NotSupportedException($"不支持的输出格式: {outputFormat}");
            }
        }

        /// <summary>
        /// 将音频源合并为MP3格式
        /// </summary>
        private static void CombineAudioSourcesAsMp3(IAudioSource[] sources, object output, WaveFormat targetFormat, AudioCombineOptions options)
        {
            using var mp3Writer = CreateMp3Writer(output, targetFormat);

            foreach (var source in sources)
            {
                var reader = source.CreateReader();
                try
                {
                    using var resampler = new MediaFoundationResampler(reader, targetFormat)
                    {
                        ResamplerQuality = options.ResamplerQuality
                    };

                    CopyAudioDataToMp3(resampler, mp3Writer);

                    if (options.SilenceDuration > 0)
                    {
                        WriteSilenceToMp3(mp3Writer, targetFormat, options.SilenceDuration);
                    }
                }
                finally
                {
                    (reader as IDisposable)?.Dispose();
                }
            }
        }

        /// <summary>
        /// 将音频源合并为WAV格式
        /// </summary>
        private static void CombineAudioSourcesAsWav(IAudioSource[] sources, object output, WaveFormat targetFormat, AudioCombineOptions options)
        {
            using var waveWriter = CreateWaveWriter(output, targetFormat);

            foreach (var source in sources)
            {
                var reader = source.CreateReader();
                try
                {
                    if (!reader.WaveFormat.Equals(targetFormat))
                    {
                        using var resampler = new MediaFoundationResampler(reader, targetFormat)
                        {
                            ResamplerQuality = options.ResamplerQuality
                        };
                        CopyAudioDataToWav(resampler, waveWriter);
                    }
                    else
                    {
                        CopyAudioDataToWav(reader, waveWriter);
                    }

                    if (options.SilenceDuration > 0)
                    {
                        WriteSilenceToWav(waveWriter, targetFormat, options.SilenceDuration);
                    }
                }
                finally
                {
                    (reader as IDisposable)?.Dispose();
                }
            }
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// 创建MP3写入器
        /// </summary>
        private static LameMP3FileWriter CreateMp3Writer(object output, WaveFormat targetFormat)
        {
            return output switch
            {
                string filePath => new LameMP3FileWriter(filePath, targetFormat, LAMEPreset.STANDARD),
                Stream stream => new LameMP3FileWriter(stream, targetFormat, LAMEPreset.STANDARD),
                _ => throw new ArgumentException("不支持的输出类型", nameof(output))
            };
        }

        /// <summary>
        /// 创建WAV写入器
        /// </summary>
        private static WaveFileWriter CreateWaveWriter(object output, WaveFormat targetFormat)
        {
            return output switch
            {
                string filePath => new WaveFileWriter(filePath, targetFormat),
                Stream stream => new WaveFileWriter(stream, targetFormat),
                _ => throw new ArgumentException("不支持的输出类型", nameof(output))
            };
        }

        #endregion

        #region Audio Source Abstractions

        /// <summary>
        /// 音频源接口
        /// </summary>
        private interface IAudioSource : IDisposable
        {
            string SourcePath { get; }
            IWaveProvider CreateReader();
        }

        /// <summary>
        /// 文件流音频源
        /// </summary>
        private class FileStreamSource : IAudioSource
        {
            public string SourcePath { get; }

            public FileStreamSource(string filePath)
            {
                SourcePath = filePath;
            }

            public IWaveProvider CreateReader()
            {
                return CreateAudioReader(SourcePath);
            }

            public void Dispose()
            {
                // 文件路径不需要显式释放
            }
        }

        /// <summary>
        /// 内存流音频源
        /// </summary>
        private class StreamSource : IAudioSource
        {
            private readonly Stream _stream;

            public string SourcePath => "<Stream>";

            public StreamSource(Stream stream)
            {
                _stream = stream;
            }

            public IWaveProvider CreateReader()
            {
                _stream.Position = 0;
                return CreateAudioReaderFromStream(_stream);
            }

            public void Dispose()
            {
                _stream?.Dispose();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 写入音频到文件 - 统一混音输出方法
        /// </summary>
        private static void WriteAudioToFile(ISampleProvider mixer, string outputFile, AudioFormat outputFormat, WaveFormat targetFormat, AudioMixOptions options)
        {
            switch (outputFormat)
            {
                case AudioFormat.Mp3:
                    WriteMixedAudioToMp3(mixer, outputFile, targetFormat, options);
                    break;
                case AudioFormat.Wav:
                    WriteMixedAudioToWav(mixer, outputFile, options);
                    break;
                default:
                    throw new NotSupportedException($"不支持的输出格式: {outputFormat}");
            }
        }

        #endregion

        #region Private Methods

        private static void ValidateInputFiles(string[] files)
        {
            foreach (var file in files)
            {
                if (!File.Exists(file))
                    throw new FileNotFoundException($"输入文件不存在: {file}");
            }
        }

        private static void EnsureOutputDirectory(string outputFile)
        {
            var directory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static AudioFormat GetOutputFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".mp3" => AudioFormat.Mp3,
                ".wav" => AudioFormat.Wav,
                _ => throw new NotSupportedException($"不支持的文件格式: {extension}")
            };
        }

        private static AudioFormat GetInputFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".mp3" => AudioFormat.Mp3,
                ".wav" => AudioFormat.Wav,
                _ => AudioFormat.Auto
            };
        }

        private static IWaveProvider CreateAudioReader(string filePath, AudioFormat format)
        {
            return format switch
            {
                AudioFormat.Mp3 => new Mp3FileReader(filePath),
                AudioFormat.Wav => new WaveFileReader(filePath),
                AudioFormat.Auto => new AudioFileReader(filePath),
                _ => new AudioFileReader(filePath)
            };
        }

        private static IWaveProvider CreateAudioReader(string filePath)
        {
            try
            {
                var actualFormat = AudioFileFormatDetector.DetectActualFormat(filePath);
                var extensionFormat = GetInputFormat(filePath);
                // 如果格式不匹配,记录警告并使用实际格式
                if (actualFormat != extensionFormat && extensionFormat != AudioFormat.Auto)
                {
                    Console.WriteLine($"警告: 文件 {filePath} 的扩展名为 {Path.GetExtension(filePath)}, " +
                                     $"但实际格式为 {actualFormat}");
                }
                return CreateAudioReader(filePath, actualFormat);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("sample rate changes"))
            {
                // 尝试使用MediaFoundationReader作为备用
                try
                {
                    return new MediaFoundationReader(filePath);
                }
                catch (Exception mediaEx)
                {
                    throw new InvalidOperationException(
                        $"无法读取MP3文件 {filePath}。该文件可能包含不一致的采样率或已损坏。" +
                        $"原始错误: {ex.Message}. MediaFoundation备用方案也失败: {mediaEx.Message}", ex);
                }
            }
        }

        private static ISampleProvider ConvertToStereo(ISampleProvider input)
        {
            return input.WaveFormat.Channels == 2 ? input : new MonoToStereoSampleProvider(input);
        }

        private static void WriteMixedAudioToMp3(ISampleProvider mixer, string outputFile, WaveFormat targetFormat, AudioMixOptions options)
        {
            using var mp3Writer = new LameMP3FileWriter(outputFile, targetFormat, LAMEPreset.STANDARD);

            // 需要将ISampleProvider转换为字节流
            var waveProvider = mixer.ToWaveProvider();
            CopyAudioDataToMp3(waveProvider, mp3Writer);
        }

        private static void WriteMixedAudioToWav(ISampleProvider mixer, string outputFile, AudioMixOptions options)
        {
            using var waveWriter = new WaveFileWriter(outputFile, mixer.WaveFormat);
            var buffer = new float[options.BufferSize];
            int samplesRead;

            while ((samplesRead = mixer.Read(buffer, 0, buffer.Length)) > 0)
            {
                waveWriter.WriteSamples(buffer, 0, samplesRead);
            }
        }

        private static void WriteConvertedAudio(IWaveProvider source, string outputFile, AudioFormat outputFormat, AudioConvertOptions options)
        {
            switch (outputFormat)
            {
                case AudioFormat.Mp3:
                    using (var mp3Writer = new LameMP3FileWriter(outputFile, options.TargetFormat, options.Mp3Preset))
                    {
                        CopyAudioDataToMp3(source, mp3Writer);
                    }
                    break;
                case AudioFormat.Wav:
                    using (var waveWriter = new WaveFileWriter(outputFile, options.TargetFormat))
                    {
                        CopyAudioDataToWav(source, waveWriter);
                    }
                    break;
            }
        }

        private static void WriteSampleProviderToMp3(ISampleProvider sampleProvider, string outputFile, WaveFormat targetFormat, LAMEPreset preset)
        {
            using var mp3Writer = new LameMP3FileWriter(outputFile, targetFormat, preset);

            // 将ISampleProvider转换为IWaveProvider
            var waveProvider = sampleProvider.ToWaveProvider();
            CopyAudioDataToMp3(waveProvider, mp3Writer);
        }

        private static void WriteSampleProviderToWav(ISampleProvider sampleProvider, string outputFile)
        {
            using var waveWriter = new WaveFileWriter(outputFile, sampleProvider.WaveFormat);
            var buffer = new float[4096];
            int samplesRead;

            while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                waveWriter.WriteSamples(buffer, 0, samplesRead);
            }
        }

        // 修正后的数据复制方法
        private static void CopyAudioDataToWav(IWaveProvider source, WaveFileWriter destination)
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, bytesRead);
            }
        }

        private static void CopyAudioDataToMp3(IWaveProvider source, LameMP3FileWriter destination)
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, bytesRead);
            }
        }

        // 修正后的静音写入方法
        private static void WriteSilenceToWav(WaveFileWriter output, WaveFormat format, float durationSeconds)
        {
            int bytesPerSample = format.BitsPerSample / 8;
            int samplesPerSecond = format.SampleRate * format.Channels;
            int totalSamples = (int)(samplesPerSecond * durationSeconds);
            var silenceBuffer = new byte[totalSamples * bytesPerSample];

            output.Write(silenceBuffer, 0, silenceBuffer.Length);
        }

        private static void WriteSilenceToMp3(LameMP3FileWriter output, WaveFormat format, float durationSeconds)
        {
            int bytesPerSample = format.BitsPerSample / 8;
            int samplesPerSecond = format.SampleRate * format.Channels;
            int totalSamples = (int)(samplesPerSecond * durationSeconds);
            var silenceBuffer = new byte[totalSamples * bytesPerSample];

            output.Write(silenceBuffer, 0, silenceBuffer.Length);
        }

        private static IWaveProvider CreateAudioReaderFromStream(Stream stream)
        {
            try
            {
                // 检测流的格式
                var format = AudioFileFormatDetector.DetectFormatFromStream(stream);
                stream.Position = 0;

                return format switch
                {
                    AudioFormat.Mp3 => new Mp3FileReader(stream),
                    AudioFormat.Wav => new WaveFileReader(stream),
                    _ => new StreamAudioFileReader(stream)
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("sample rate changes"))
            {
                // 对于有问题的MP3文件，尝试使用临时文件方案
                try
                {
                    return new StreamAudioFileReader(stream);
                }
                catch (Exception mediaEx)
                {
                    throw new InvalidOperationException(
                        $"无法读取音频流。该流可能包含不一致的采样率或已损坏。" +
                        $"原始错误: {ex.Message}. 备用方案也失败: {mediaEx.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 支持从流读取的音频文件读取器包装器
        /// </summary>
        private class StreamAudioFileReader : IWaveProvider, IDisposable
        {
            private readonly string _tempFile;
            private readonly AudioFileReader _innerReader;
            private bool _disposed = false;

            public StreamAudioFileReader(Stream stream)
            {
                _tempFile = Path.GetTempFileName();
                try
                {
                    stream.Position = 0;
                    using (var fileStream = new FileStream(_tempFile, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }

                    _innerReader = new AudioFileReader(_tempFile);
                }
                catch
                {
                    CleanupTempFile();
                    throw;
                }
            }

            public WaveFormat WaveFormat => _innerReader.WaveFormat;

            public int Read(byte[] buffer, int offset, int count)
            {
                return _innerReader.Read(buffer, offset, count);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _innerReader?.Dispose();
                    CleanupTempFile();
                    _disposed = true;
                }
            }

            private void CleanupTempFile()
            {
                if (File.Exists(_tempFile))
                {
                    try { File.Delete(_tempFile); } catch { }
                }
            }
        }

        #endregion
    }
}