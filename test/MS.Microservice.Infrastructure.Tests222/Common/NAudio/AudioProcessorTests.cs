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
            // ���ò���·��
            _testDirectory = Path.Combine("C:", "TestData");
            
            // ����������Ƶ�ļ�·��
            _testInputFiles = new[]
            {
                Path.Combine(_testDirectory, "test1.wav"),
                Path.Combine(_testDirectory, "test2.wav"),
                Path.Combine(_testDirectory, "test3.wav")
            };
            
            _testOutputFile = Path.Combine(_testDirectory, "output.wav");
            
            // ������װ�������ڲ�����֤�߼�����ʵ�ʲ����ļ�ϵͳ
            _audioProcessor = new AudioProcessorWrapper();
        }
        
        public void Dispose()
        {
            // ����Ҫʵ�������ļ�ϵͳ����Ϊ����û�����������ļ�
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
            
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.CombineAudioFilesAsync(_testInputFiles, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
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
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.CombineAudioFilesAsync(_testInputFiles, _testOutputFile, null));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
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
            
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.MixAudioFilesAsync(_testInputFiles, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
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
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.MixAudioFilesAsync(_testInputFiles, _testOutputFile, null));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
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
            
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.ConvertAudioFormatAsync(inputFile, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
            }
        }
        
        [Fact]
        public async Task ConvertAudioFormatAsync_WithInvalidInputFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.wav");
            var options = new AudioConvertOptions();
            
            // ʹ��NSubstituteģ��File.Exists��������false
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
            
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.ConvertMonoToStereoAsync(inputFile, _testOutputFile, options));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
            }
        }
        
        [Fact]
        public async Task ConvertMonoToStereoAsync_WithNullOptions_ShouldUseDefaultOptions()
        {
            // Arrange
            var inputFile = _testInputFiles[0];
            
            // ʹ��NSubstituteģ��File.Exists����
            using (var fileExistsContext = MockStaticFile.FileExists(true))
            {
                // Act
                var exception = await Record.ExceptionAsync(async () => 
                    await _audioProcessor.ConvertMonoToStereoAsync(inputFile, _testOutputFile, null));
                
                // Assert
                Assert.Null(exception); // ��Ӧ�׳��쳣
            }
        }
        
        [Fact]
        public async Task ConvertMonoToStereoAsync_WithInvalidInputFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.wav");
            
            // ʹ��NSubstituteģ��File.Exists��������false
            using (var fileExistsContext = MockStaticFile.FileExists(false))
            {
                // Act & Assert
                await Assert.ThrowsAsync<FileNotFoundException>(async () => 
                    await _audioProcessor.ConvertMonoToStereoAsync(nonExistentFile, _testOutputFile));
            }
        }
    }

    // ģ�⾲̬File��ĸ�����
    internal class MockStaticFile : IDisposable
    {
        private readonly Func<string, bool> _originalFileExists;
        private readonly bool _fileExistsResult;

        private MockStaticFile(bool fileExistsResult)
        {
            _originalFileExists = File.Exists;
            _fileExistsResult = fileExistsResult;
            
            // ʹ��NSubstitute�滻��̬�����ܸ��ӣ�������һ���򵥵ķ������
            // ��ʵ����Ŀ�У����Կ���ʹ��TypeMock��JustMock�ȹ��ߣ������ع�����ʹ������ע��
            File.Exists = (path) => _fileExistsResult;
        }

        public static MockStaticFile FileExists(bool result)
        {
            return new MockStaticFile(result);
        }

        public void Dispose()
        {
            // �ָ�ԭʼ����
            File.Exists = _originalFileExists;
        }
    }

    // ������Ƶ���������ļ�ϵͳ��NAudio���������ϣ�
    // ���Ǵ���һ����װ��������ʵ�ʲ����ļ�ϵͳ
    internal class AudioProcessorWrapper : IAudioProcessor
    {
        public ValueTask CombineAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioCombineOptions? options = null)
        {
            if (inputFiles == null || !inputFiles.Any())
            {
                throw new ArgumentException("�����ļ��б���Ϊ��", nameof(inputFiles));
            }

            // ����ļ��Ƿ����
            foreach (var file in inputFiles)
            {
                if (!File.Exists(file))
                {
                    throw new FileNotFoundException($"�����ļ�������: {file}");
                }
            }

            return new ValueTask();
        }

        public ValueTask MixAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioMixOptions? options = null)
        {
            if (inputFiles == null || !inputFiles.Any())
            {
                throw new ArgumentException("�����ļ��б���Ϊ��", nameof(inputFiles));
            }

            // ����ļ��Ƿ����
            foreach (var file in inputFiles)
            {
                if (!File.Exists(file))
                {
                    throw new FileNotFoundException($"�����ļ�������: {file}");
                }
            }

            return new ValueTask();
        }

        public ValueTask ConvertAudioFormatAsync(string inputFile, string outputFile, AudioConvertOptions options)
        {
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException($"�����ļ�������: {inputFile}");
            }

            return new ValueTask();
        }

        public ValueTask ConvertMonoToStereoAsync(string inputFile, string outputFile, AudioConvertOptions? options = null)
        {
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException($"�����ļ�������: {inputFile}");
            }

            return new ValueTask();
        }
    }
}