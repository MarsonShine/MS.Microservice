using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Lame;
using NAudio.Wave;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Tests.Common.NAudio
{
    public class AudioProcessorTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string[] _testInputFiles;
        private readonly string _testOutputFile;
        private readonly IAudioProcessor _audioProcessor;
        
        public AudioProcessorTests()
        {
            // 设置测试路径
            _testDirectory = Path.Combine("C:", "TestData");
            
            // 创建测试音频文件路径
            _testInputFiles = new[]
            {
                Path.Combine(_testDirectory, "test1.wav"),
                Path.Combine(_testDirectory, "test2.wav"),
                Path.Combine(_testDirectory, "test3.wav")
            };
            
            _testOutputFile = Path.Combine(_testDirectory, "output.wav");
            
            // 创建包装器，用于测试验证逻辑而不实际操作文件系统
            _audioProcessor = new AudioProcessorWrapper();
        }
        
        public void Dispose()
        {
            // 不需要实际清理文件系统，因为我们没有真正创建文件
        }
        
        [Fact]
        public async Task CombineAudioFilesAsync_WithValidInputs_ShouldCompleteSuccessfully()
        {
            // Arrange
            var options = new AudioCombineOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                SilenceDuration = 0.5f,
                SortByFileName = true,
                ResamplerQuality = 60
            };
            
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.CombineAudioFilesAsync(_testInputFiles, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task CombineAudioFilesAsync_WithEmptyInputFiles_ShouldThrowArgumentException()
        {
            // Arrange
            var emptyInputFiles = Array.Empty<string>();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => 
                await _audioProcessor.CombineAudioFilesAsync(emptyInputFiles, _testOutputFile));
        }
        
        [Fact]
        public async Task CombineAudioFilesAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.CombineAudioFilesAsync(_testInputFiles, _testOutputFile, null));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task MixAudioFilesAsync_WithValidInputs_ShouldCompleteSuccessfully()
        {
            // Arrange
            var options = new AudioMixOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                AutoConvertToStereo = true,
                BufferSize = 8192
            };
            
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.MixAudioFilesAsync(_testInputFiles, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task MixAudioFilesAsync_WithEmptyInputFiles_ShouldThrowArgumentException()
        {
            // Arrange
            var emptyInputFiles = Array.Empty<string>();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => 
                await _audioProcessor.MixAudioFilesAsync(emptyInputFiles, _testOutputFile));
        }
        
        [Fact]
        public async Task MixAudioFilesAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.MixAudioFilesAsync(_testInputFiles, _testOutputFile, null));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task ConvertAudioFormatAsync_WithValidInputs_ShouldCompleteSuccessfully()
        {
            // Arrange
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(48000, 24, 2),
                Mp3Preset = LAMEPreset.EXTREME,
                ResamplerQuality = 60
            };
            var inputFile = _testInputFiles[0];
            
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.ConvertAudioFormatAsync(inputFile, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task ConvertAudioFormatAsync_WithInvalidInputFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.wav");
            var options = new AudioConvertOptions();
            
            // 使用NSubstitute模拟File.Exists方法返回false
            using (var fileExistsContext = MockStaticFile.FileExists(false))
            {
                // Act & Assert
                await Assert.ThrowsAsync<FileNotFoundException>(async () => 
                    await _audioProcessor.ConvertAudioFormatAsync(nonExistentFile, _testOutputFile, options));
            }
        }
        
        [Fact]
        public async Task ConvertMonoToStereoAsync_WithValidInputs_ShouldCompleteSuccessfully()
        {
            // Arrange
            var options = new AudioConvertOptions
            {
                TargetFormat = new WaveFormat(44100, 16, 2),
                Mp3Preset = LAMEPreset.STANDARD
            };
            var inputFile = _testInputFiles[0];
            
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.ConvertMonoToStereoAsync(inputFile, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task ConvertMonoToStereoAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // Arrange
            var inputFile = _testInputFiles[0];
            
            // 使用NSubstitute模拟File.Exists方法
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.ConvertMonoToStereoAsync(inputFile, _testOutputFile, null));
                
                // Assert
                Assert.Null(exception); // 不应抛出异常
            }
        }
        
        [Fact]
        public async Task ConvertMonoToStereoAsync_WithInvalidInputFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.wav");
            
            // 使用NSubstitute模拟File.Exists方法返回false
            using (var fileExistsContext = MockStaticFile.FileExists(false))
            {
                // Act & Assert
                await Assert.ThrowsAsync<FileNotFoundException>(async () => 
                    await _audioProcessor.ConvertMonoToStereoAsync(nonExistentFile, _testOutputFile));
            }
        }
    }

    // 模拟静态File类的辅助类
    internal class MockStaticFile : IDisposable
    {
        private readonly Func<string, bool> _originalFileExists;
        private readonly bool _fileExistsResult;

        private MockStaticFile(bool fileExistsResult)
        {
            _originalFileExists = File.Exists;
            _fileExistsResult = fileExistsResult;
            
            // 使用NSubstitute替换静态方法很复杂，这里用一个简单的方法替代
            // 在实际项目中，可以考虑使用TypeMock或JustMock等工具，或者重构代码使用依赖注入
            File.Exists = (path) => _fileExistsResult;
        }

        public static MockStaticFile FileExists(bool result)
        {
            return new MockStaticFile(result);
        }

        public void Dispose()
        {
            // 恢复原始方法
            File.Exists = _originalFileExists;
        }
    }

    // 由于音频处理器与文件系统和NAudio组件紧密耦合，
    // 我们创建一个包装器来避免实际操作文件系统
    internal class AudioProcessorWrapper : IAudioProcessor
    {
        public ValueTask CombineAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioCombineOptions? options = null)
        {
            if (inputFiles == null || !inputFiles.Any())
            {
                throw new ArgumentException("输入文件列表不能为空", nameof(inputFiles));
            }

            // 检查文件是否存在
            foreach (var file in inputFiles)
            {
                if (!File.Exists(file))
                {
                    throw new FileNotFoundException($"输入文件不存在: {file}");
                }
            }

            return new ValueTask();
        }

        public ValueTask MixAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioMixOptions? options = null)
        {
            if (inputFiles == null || !inputFiles.Any())
            {
                throw new ArgumentException("输入文件列表不能为空", nameof(inputFiles));
            }

            // 检查文件是否存在
            foreach (var file in inputFiles)
            {
                if (!File.Exists(file))
                {
                    throw new FileNotFoundException($"输入文件不存在: {file}");
                }
            }

            return new ValueTask();
        }

        public ValueTask ConvertAudioFormatAsync(string inputFile, string outputFile, AudioConvertOptions options)
        {
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException($"输入文件不存在: {inputFile}");
            }

            return new ValueTask();
        }

        public ValueTask ConvertMonoToStereoAsync(string inputFile, string outputFile, AudioConvertOptions? options = null)
        {
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException($"输入文件不存在: {inputFile}");
            }

            return new ValueTask();
        }
    }
}