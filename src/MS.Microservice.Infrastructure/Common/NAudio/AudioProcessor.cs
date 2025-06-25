using Microsoft.AspNetCore.Components.Forms;
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
            EnsureOutputDirectory(outputFile);

            await Task.Run(() =>
            {
                if (options.SortByFileName)
                {
                    files = files.OrderBy(f => new FileInfo(f).Name).ToArray();
                }

                var outputFormat = GetOutputFormat(outputFile);
                var targetFormat = options.TargetFormat ?? new WaveFormat(44100, 16, 2);

                switch (outputFormat)
                {
                    case AudioFormat.Mp3:
                        CombineMp3Files(files, outputFile, targetFormat, options);
                        break;
                    case AudioFormat.Wav:
                        CombineWavFiles(files, outputFile, targetFormat, options);
                        break;
                    default:
                        throw new NotSupportedException($"不支持的输出格式: {outputFormat}");
                }
            });
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

        private static ISampleProvider ConvertToStereo(ISampleProvider input)
        {
            return input.WaveFormat.Channels == 2 ? input : new MonoToStereoSampleProvider(input);
        }

        private static void CombineMp3Files(string[] inputFiles, string outputFile, WaveFormat targetFormat, AudioCombineOptions options)
        {
            using var mp3Writer = new LameMP3FileWriter(outputFile, targetFormat, LAMEPreset.STANDARD);

            foreach (var file in inputFiles)
            {
                var mp3Reader = CreateAudioReader(file);
                using var resampler = new MediaFoundationResampler(mp3Reader, targetFormat)
                {
                    ResamplerQuality = options.ResamplerQuality
                };

                CopyAudioDataToMp3(resampler, mp3Writer);

                if (options.SilenceDuration > 0)
                {
                    WriteSilenceToMp3(mp3Writer, targetFormat, options.SilenceDuration);
                }
            }
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

        private static void CombineWavFiles(string[] inputFiles, string outputFile, WaveFormat targetFormat, AudioCombineOptions options)
        {
            using var waveWriter = new WaveFileWriter(outputFile, targetFormat);

            foreach (var file in inputFiles)
            {
                var reader = CreateAudioReader(file);

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

        #endregion
    }
}