using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Tests.Common.NAudio
{
    // 集成测试需要实际的音频文件和NAudio操作
    // 这些测试默认会被跳过，因为它们需要实际的文件系统和音频文件
    // 在运行测试前，请准备好测试音频文件
    [Trait("Category", "Integration")]
    public class AudioProcessorIntegrationTests : IDisposable
    {
        private readonly IAudioProcessor _audioProcessor;
        private readonly string _testDirectory;
        private readonly List<string> _createdFiles;
        
        public AudioProcessorIntegrationTests()
        {
            _audioProcessor = new AudioProcessor();
            _testDirectory = Path.Combine(Path.GetTempPath(), $"AudioTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            _createdFiles = new List<string>();
            
            // 注意: 运行此测试前需要手动准备测试音频文件
            // 这里不会创建真实的音频文件，因为需要真实的音频数据
        }
        
        public void Dispose()
        {
            // 清理测试创建的文件
            foreach (var file in _createdFiles)
            {
                if (File.Exists(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 忽略清理错误
                    }
                }
            }
            
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch
                {
                    // 忽略清理错误
                }
            }
        }
        
        [Fact(Skip = "需要实际音频文件")]
        public async Task CombineAudioFilesAsync_WithRealFiles_ShouldCreateCombinedOutput()
        {
            // Arrange - 这里需要指定实际存在的测试音频文件
            var inputFiles = new[]
            {
                @"TestData\input1.wav",
                @"TestData\input2.wav"
            };
            
            var outputFile = Path.Combine(_testDirectory, "combined.wav");
            _createdFiles.Add(outputFile);
            
            var options = new AudioCombineOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                SilenceDuration = 0.5f
            };
            
            // Act
            await _audioProcessor.CombineAudioFilesAsync(inputFiles, outputFile, options);
            
            // Assert
            Assert.True(File.Exists(outputFile));
            Assert.True(new FileInfo(outputFile).Length > 0);
        }
        
        [Fact(Skip = "需要实际音频文件")]
        public async Task MixAudioFilesAsync_WithRealFiles_ShouldCreateMixedOutput()
        {
            // Arrange - 这里需要指定实际存在的测试音频文件
            var inputFiles = new[]
            {
                @"TestData\input1.wav",
                @"TestData\input2.wav"
            };
            
            var outputFile = Path.Combine(_testDirectory, "mixed.wav");
            _createdFiles.Add(outputFile);
            
            var options = new AudioMixOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2)
            };
            
            // Act
            await _audioProcessor.MixAudioFilesAsync(inputFiles, outputFile, options);
            
            // Assert
            Assert.True(File.Exists(outputFile));
            Assert.True(new FileInfo(outputFile).Length > 0);
        }
        
        [Fact(Skip = "需要实际音频文件")]
        public async Task ConvertAudioFormatAsync_WithRealFile_ShouldConvertFormat()
        {
            // Arrange - 这里需要指定实际存在的测试音频文件
            var inputFile = @"TestData\input1.wav";
            var outputFile = Path.Combine(_testDirectory, "converted.mp3");
            _createdFiles.Add(outputFile);
            
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2)
            };
            
            // Act
            await _audioProcessor.ConvertAudioFormatAsync(inputFile, outputFile, options);
            
            // Assert
            Assert.True(File.Exists(outputFile));
            Assert.True(new FileInfo(outputFile).Length > 0);
        }
        
        [Fact(Skip = "需要实际音频文件")]
        public async Task ConvertMonoToStereoAsync_WithRealMonoFile_ShouldConvertToStereo()
        {
            // Arrange - 这里需要指定实际存在的单声道测试音频文件
            var inputFile = @"TestData\mono.wav";
            var outputFile = Path.Combine(_testDirectory, "stereo.wav");
            _createdFiles.Add(outputFile);
            
            // Act
            await _audioProcessor.ConvertMonoToStereoAsync(inputFile, outputFile);
            
            // Assert
            Assert.True(File.Exists(outputFile));
            
            // 验证输出是立体声
            using (var reader = new AudioFileReader(outputFile))
            {
                Assert.Equal(2, reader.WaveFormat.Channels);
            }
        }
    }
}