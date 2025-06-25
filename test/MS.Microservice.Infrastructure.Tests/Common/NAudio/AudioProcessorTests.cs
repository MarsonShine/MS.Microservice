using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MS.Microservice.Infrastructure.Tests.Common.NAudio
{
    public class AudioProcessorTests : IDisposable
    {
        private readonly string _testDataDirectory;
        private readonly string _outputDirectory;
        private readonly IAudioProcessor _audioProcessor;
        private readonly List<string> _createdFiles;

        public AudioProcessorTests()
        {
            _testDataDirectory = Path.Combine(Path.GetTempPath(), "AudioProcessorTests", Guid.NewGuid().ToString());
            _outputDirectory = Path.Combine(_testDataDirectory, "Output");
            _audioProcessor = new AudioProcessor();
            _createdFiles = new List<string>();

            // 创建测试目录
            Directory.CreateDirectory(_testDataDirectory);
            Directory.CreateDirectory(_outputDirectory);

            // 创建测试音频文件
            CreateTestAudioFiles();
        }

        public void Dispose()
        {
            // 清理测试文件和目录
            try
            {
                if (Directory.Exists(_testDataDirectory))
                {
                    Directory.Delete(_testDataDirectory, true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }

        #region 测试文件创建

        private void CreateTestAudioFiles()
        {
            // 创建测试用的WAV文件
            CreateTestWavFile("test1.wav", 2.0, 44100, 16, 1); // 2秒，单声道
            CreateTestWavFile("test2.wav", 1.5, 44100, 16, 2); // 1.5秒，立体声
            CreateTestWavFile("test3.wav", 3.0, 22050, 16, 1); // 3秒，单声道，低采样率
            CreateTestWavFile("mono.wav", 1.0, 44100, 16, 1);  // 单声道测试文件
            CreateTestWavFile("stereo.wav", 1.0, 44100, 16, 2); // 立体声测试文件
        }

        private void CreateTestWavFile(string filename, double durationSeconds, int sampleRate, int bitsPerSample, int channels)
        {
            var filePath = Path.Combine(_testDataDirectory, filename);
            var waveFormat = new WaveFormat(sampleRate, bitsPerSample, channels);

            using var writer = new WaveFileWriter(filePath, waveFormat);

            // 生成简单的正弦波音频数据
            var samplesPerSecond = sampleRate * channels;
            var totalSamples = (int)(samplesPerSecond * durationSeconds);
            var amplitude = 0.25; // 降低音量避免过载
            var frequency = 440.0; // A4音符

            var buffer = new byte[totalSamples * (bitsPerSample / 8)];

            for (int i = 0; i < totalSamples; i++)
            {
                var sampleIndex = i / channels;
                var time = (double)sampleIndex / sampleRate;
                var sampleValue = (short)(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * frequency * time));

                var bytes = BitConverter.GetBytes(sampleValue);
                var byteIndex = i * (bitsPerSample / 8);

                if (byteIndex + 1 < buffer.Length)
                {
                    buffer[byteIndex] = bytes[0];
                    buffer[byteIndex + 1] = bytes[1];
                }
            }

            writer.Write(buffer, 0, buffer.Length);
            _createdFiles.Add(filePath);
        }

        #endregion

        #region CombineAudioFilesAsync 测试

        [Fact]
        public async Task CombineAudioFilesAsync_WithValidWavFiles_ShouldCreateCombinedFile()
        {
            // Arrange
            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "test1.wav"),
                Path.Combine(_testDataDirectory, "test2.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "combined.wav");
            var options = new AudioCombineOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                SilenceDuration = 0.5f,
                SortByFileName = true,
                ResamplerQuality = 60
            };

            // Act
            await _audioProcessor.CombineAudioFilesAsync(inputFiles, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));

            // 验证输出文件的属性
            using var reader = new WaveFileReader(outputFile);
            Assert.Equal(44100, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.True(reader.TotalTime.TotalSeconds > 3.0); // 应该大于原始文件时长之和
        }

        [Fact]
        public async Task CombineAudioFilesAsync_WithEmptyInputFiles_ShouldThrowArgumentException()
        {
            // Arrange
            var emptyInputFiles = Array.Empty<string>();
            var outputFile = Path.Combine(_outputDirectory, "empty.wav");

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await _audioProcessor.CombineAudioFilesAsync(emptyInputFiles, outputFile));

            Assert.Equal("inputFiles", exception.ParamName);
        }

        [Fact]
        public async Task CombineAudioFilesAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "test1.wav"),
                Path.Combine(_testDataDirectory, "nonexistent.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "combined.wav");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _audioProcessor.CombineAudioFilesAsync(inputFiles, outputFile));
        }

        [Fact]
        public async Task CombineAudioFilesAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // Arrange
            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "test1.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "combined_default.wav");

            // Act
            await _audioProcessor.CombineAudioFilesAsync(inputFiles, outputFile, null);

            // Assert
            Assert.True(File.Exists(outputFile));
        }

        #endregion

        #region MixAudioFilesAsync 测试

        [Fact]
        public async Task MixAudioFilesAsync_WithValidWavFiles_ShouldCreateMixedFile()
        {
            // Arrange
            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "test1.wav"),
                Path.Combine(_testDataDirectory, "test2.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "mixed.wav");
            var options = new AudioMixOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                AutoConvertToStereo = true,
                BufferSize = 4096
            };

            // Act
            await _audioProcessor.MixAudioFilesAsync(inputFiles, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));

            // 验证输出文件的属性
            using var reader = new WaveFileReader(outputFile);
            Assert.Equal(44100, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
        }

        [Fact]
        public async Task MixAudioFilesAsync_WithEmptyInputFiles_ShouldThrowArgumentException()
        {
            // Arrange
            var emptyInputFiles = Array.Empty<string>();
            var outputFile = Path.Combine(_outputDirectory, "mixed_empty.wav");

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await _audioProcessor.MixAudioFilesAsync(emptyInputFiles, outputFile));

            Assert.Equal("inputFiles", exception.ParamName);
        }

        [Fact]
        public async Task MixAudioFilesAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // Arrange
            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "test1.wav"),
                Path.Combine(_testDataDirectory, "test2.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "mixed_default.wav");

            // Act
            await _audioProcessor.MixAudioFilesAsync(inputFiles, outputFile, null);

            // Assert
            Assert.True(File.Exists(outputFile));
        }

        #endregion

        #region ConvertAudioFormatAsync 测试

        [Fact]
        public async Task ConvertAudioFormatAsync_WavToWav_WithDifferentFormat_ShouldConvert()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "test3.wav"); // 22050Hz
            var outputFile = Path.Combine(_outputDirectory, "converted.wav");
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2), // 转换为44100Hz立体声
                ResamplerQuality = 60
            };

            // Act
            await _audioProcessor.ConvertAudioFormatAsync(inputFile, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));

            using var reader = new WaveFileReader(outputFile);
            Assert.Equal(44100, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
        }

        [Fact]
        public async Task ConvertAudioFormatAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "nonexistent.wav");
            var outputFile = Path.Combine(_outputDirectory, "converted.wav");
            var options = new AudioConvertOptions();

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _audioProcessor.ConvertAudioFormatAsync(inputFile, outputFile, options));
        }

        [Fact]
        public async Task ConvertAudioFormatAsync_SameFormat_ShouldStillProcess()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "test2.wav"); // 44100Hz立体声
            var outputFile = Path.Combine(_outputDirectory, "converted_same.wav");
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2) // 相同格式
            };

            // Act
            await _audioProcessor.ConvertAudioFormatAsync(inputFile, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));
        }

        #endregion

        #region ConvertMonoToStereoAsync 测试

        [Fact]
        public async Task ConvertMonoToStereoAsync_WithMonoFile_ShouldCreateStereoFile()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "mono.wav");
            var outputFile = Path.Combine(_outputDirectory, "mono_to_stereo.wav");
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2)
            };

            // Act
            await _audioProcessor.ConvertMonoToStereoAsync(inputFile, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));

            using var reader = new WaveFileReader(outputFile);
            Assert.Equal(2, reader.WaveFormat.Channels); // 应该转换为立体声
        }

        [Fact]
        public async Task ConvertMonoToStereoAsync_WithStereoFile_ShouldHandleCorrectly()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "stereo.wav");
            var outputFile = Path.Combine(_outputDirectory, "stereo_to_stereo.wav");
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2)
            };

            // Act
            await _audioProcessor.ConvertMonoToStereoAsync(inputFile, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));

            using var reader = new WaveFileReader(outputFile);
            Assert.Equal(2, reader.WaveFormat.Channels);
        }

        [Fact]
        public async Task ConvertMonoToStereoAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "mono.wav");
            var outputFile = Path.Combine(_outputDirectory, "mono_to_stereo_default.wav");

            // Act
            await _audioProcessor.ConvertMonoToStereoAsync(inputFile, outputFile, null);

            // Assert
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task ConvertMonoToStereoAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "nonexistent.wav");
            var outputFile = Path.Combine(_outputDirectory, "output.wav");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await _audioProcessor.ConvertMonoToStereoAsync(inputFile, outputFile));
        }

        #endregion

        #region 扩展方法测试

        [Fact]
        public async Task CombineDirectoryAudioFilesAsync_WithValidDirectory_ShouldCombineAllFiles()
        {
            // Arrange
            var outputFile = Path.Combine(_outputDirectory, "directory_combined.wav");
            var options = new AudioCombineOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                SilenceDuration = 0.2f
            };

            // Act
            await _audioProcessor.CombineDirectoryAudioFilesAsync(
                _testDataDirectory, outputFile, "*.wav", options);

            // Assert
            Assert.True(File.Exists(outputFile));

            using var reader = new WaveFileReader(outputFile);
            Assert.True(reader.TotalTime.TotalSeconds > 5.0); // 应该包含多个文件的总时长
        }

        [Fact]
        public async Task CombineDirectoryAudioFilesAsync_WithNonExistentDirectory_ShouldThrowDirectoryNotFoundException()
        {
            // Arrange
            var nonExistentDirectory = Path.Combine(_testDataDirectory, "nonexistent");
            var outputFile = Path.Combine(_outputDirectory, "output.wav");

            // Act & Assert
            await Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await _audioProcessor.CombineDirectoryAudioFilesAsync(nonExistentDirectory, outputFile));
        }

        [Fact]
        public void GetAudioFileInfo_WithValidFile_ShouldReturnCorrectInfo()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "test1.wav");

            // Act
            var audioInfo = AudioProcessorExtensions.GetAudioFileInfo(inputFile);

            // Assert
            Assert.NotNull(audioInfo);
            Assert.Equal(inputFile, audioInfo.FilePath);
            Assert.Equal(44100, audioInfo.SampleRate);
            Assert.Equal(1, audioInfo.Channels);
            Assert.Equal(16, audioInfo.BitsPerSample);
            Assert.True(audioInfo.Duration.TotalSeconds > 1.8); // 约2秒
            Assert.True(audioInfo.FileSize > 0);
        }

        [Fact]
        public void GetAudioFileInfo_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDataDirectory, "nonexistent.wav");

            // Act & Assert
            Assert.Throws<FileNotFoundException>(
                () => AudioProcessorExtensions.GetAudioFileInfo(nonExistentFile));
        }

        #endregion

        #region 性能和边界测试

        [Fact]
        public async Task CombineAudioFilesAsync_WithManyFiles_ShouldHandleCorrectly()
        {
            // Arrange - 创建多个小文件
            var inputFiles = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var filename = $"small_test_{i}.wav";
                CreateTestWavFile(filename, 0.5, 44100, 16, 1); // 0.5秒小文件
                inputFiles.Add(Path.Combine(_testDataDirectory, filename));
            }

            var outputFile = Path.Combine(_outputDirectory, "many_files_combined.wav");
            var options = new AudioCombineOptions
            {
                SilenceDuration = 0.1f
            };

            // Act
            await _audioProcessor.CombineAudioFilesAsync(inputFiles, outputFile, options);

            // Assert
            Assert.True(File.Exists(outputFile));

            using var reader = new WaveFileReader(outputFile);
            Assert.True(reader.TotalTime.TotalSeconds > 5.0); // 10个0.5秒文件 + 间隔
        }

        [Fact]
        public async Task MixAudioFilesAsync_WithDifferentLengthFiles_ShouldHandleCorrectly()
        {
            // Arrange
            CreateTestWavFile("short.wav", 1.0, 44100, 16, 2);
            CreateTestWavFile("long.wav", 3.0, 44100, 16, 2);

            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "short.wav"),
                Path.Combine(_testDataDirectory, "long.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "mixed_different_lengths.wav");

            // Act
            await _audioProcessor.MixAudioFilesAsync(inputFiles, outputFile);

            // Assert
            Assert.True(File.Exists(outputFile));

            using var reader = new WaveFileReader(outputFile);
            // 混音结果的长度应该等于最长文件的长度
            Assert.True(reader.TotalTime.TotalSeconds >= 2.8);
        }

        #endregion

        #region 错误处理测试

        [Fact]
        public async Task CombineAudioFilesAsync_WithUnsupportedOutputFormat_ShouldThrowNotSupportedException()
        {
            // Arrange
            var inputFiles = new[]
            {
                Path.Combine(_testDataDirectory, "test1.wav")
            };
            var outputFile = Path.Combine(_outputDirectory, "output.unsupported");

            // Act & Assert
            await Assert.ThrowsAsync<NotSupportedException>(
                async () => await _audioProcessor.CombineAudioFilesAsync(inputFiles, outputFile));
        }

        [Fact]
        public async Task ConvertAudioFormatAsync_WithInvalidOptions_ShouldHandleGracefully()
        {
            // Arrange
            var inputFile = Path.Combine(_testDataDirectory, "test1.wav");
            var outputFile = Path.Combine(_outputDirectory, "invalid_convert.wav");
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(1000, 8, 1), // 极低采样率
                ResamplerQuality = 1 // 最低质量
            };

            // Act & Assert
            // 应该能处理，但可能音质很差
            await _audioProcessor.ConvertAudioFormatAsync(inputFile, outputFile, options);
            Assert.True(File.Exists(outputFile));
        }

        #endregion
    }
}