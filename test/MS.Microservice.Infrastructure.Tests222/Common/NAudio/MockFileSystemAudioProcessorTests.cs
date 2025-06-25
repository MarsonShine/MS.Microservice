using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Lame;
using NAudio.Wave;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Tests.Common.NAudio
{
    /// <summary>
    /// 这个测试类提供了一个使用NSubstitute来模拟IAudioProcessor的示例
    /// 注意：由于NAudio实际上直接访问文件系统，这些测试仍然无法完全运行，仅作为参考
    /// </summary>
    public class MockFileSystemAudioProcessorTests
    {
        private readonly IAudioProcessor _mockProcessor;
        private readonly MockFileSystem _fileSystem;
        private readonly string _testDirectory;
        private readonly string[] _testFiles;
        private readonly string _outputFile;
        
        public MockFileSystemAudioProcessorTests()
        {
            _mockProcessor = Substitute.For<IAudioProcessor>();
            _fileSystem = new MockFileSystem();
            
            _testDirectory = Path.Combine("C:", "TestAudio");
            _fileSystem.AddDirectory(_testDirectory);
            
            _testFiles = new[]
            {
                Path.Combine(_testDirectory, "file1.wav"),
                Path.Combine(_testDirectory, "file2.wav"),
                Path.Combine(_testDirectory, "file3.wav")
            };
            
            // 添加模拟文件
            foreach (var file in _testFiles)
            {
                _fileSystem.AddFile(file, new MockFileData(new byte[1024]));
            }
            
            _outputFile = Path.Combine(_testDirectory, "output.wav");
        }
        
        [Fact]
        public async Task CombineAudioFilesAsync_ShouldCallProcessorWithCorrectParameters()
        {
            // Arrange
            var options = new AudioCombineOptions();
            _mockProcessor.CombineAudioFilesAsync(
                Arg.Any<IEnumerable<string>>(), 
                Arg.Any<string>(), 
                Arg.Any<AudioCombineOptions>()
            ).Returns(new ValueTask());
            
            // Act
            await _mockProcessor.CombineAudioFilesAsync(_testFiles, _outputFile, options);
            
            // Assert
            await _mockProcessor.Received(1).CombineAudioFilesAsync(
                Arg.Is<IEnumerable<string>>(files => files.SequenceEqual(_testFiles)),
                Arg.Is<string>(output => output == _outputFile),
                Arg.Is<AudioCombineOptions>(opts => ReferenceEquals(opts, options))
            );
        }
        
        [Fact]
        public async Task MixAudioFilesAsync_ShouldCallProcessorWithCorrectParameters()
        {
            // Arrange
            var options = new AudioMixOptions();
            _mockProcessor.MixAudioFilesAsync(
                Arg.Any<IEnumerable<string>>(), 
                Arg.Any<string>(), 
                Arg.Any<AudioMixOptions>()
            ).Returns(new ValueTask());
            
            // Act
            await _mockProcessor.MixAudioFilesAsync(_testFiles, _outputFile, options);
            
            // Assert
            await _mockProcessor.Received(1).MixAudioFilesAsync(
                Arg.Is<IEnumerable<string>>(files => files.SequenceEqual(_testFiles)),
                Arg.Is<string>(output => output == _outputFile),
                Arg.Is<AudioMixOptions>(opts => ReferenceEquals(opts, options))
            );
        }
        
        [Fact]
        public async Task ConvertAudioFormatAsync_ShouldCallProcessorWithCorrectParameters()
        {
            // Arrange
            var options = new AudioConvertOptions();
            var inputFile = _testFiles[0];
            _mockProcessor.ConvertAudioFormatAsync(
                Arg.Any<string>(), 
                Arg.Any<string>(), 
                Arg.Any<AudioConvertOptions>()
            ).Returns(new ValueTask());
            
            // Act
            await _mockProcessor.ConvertAudioFormatAsync(inputFile, _outputFile, options);
            
            // Assert
            await _mockProcessor.Received(1).ConvertAudioFormatAsync(
                Arg.Is<string>(input => input == inputFile),
                Arg.Is<string>(output => output == _outputFile),
                Arg.Is<AudioConvertOptions>(opts => ReferenceEquals(opts, options))
            );
        }
        
        [Fact]
        public async Task ConvertMonoToStereoAsync_ShouldCallProcessorWithCorrectParameters()
        {
            // Arrange
            var options = new AudioConvertOptions();
            var inputFile = _testFiles[0];
            _mockProcessor.ConvertMonoToStereoAsync(
                Arg.Any<string>(), 
                Arg.Any<string>(), 
                Arg.Any<AudioConvertOptions>()
            ).Returns(new ValueTask());
            
            // Act
            await _mockProcessor.ConvertMonoToStereoAsync(inputFile, _outputFile, options);
            
            // Assert
            await _mockProcessor.Received(1).ConvertMonoToStereoAsync(
                Arg.Is<string>(input => input == inputFile),
                Arg.Is<string>(output => output == _outputFile),
                Arg.Is<AudioConvertOptions>(opts => ReferenceEquals(opts, options))
            );
        }
        
        [Fact]
        public async Task CombineAudioFilesAsync_WithException_ShouldPropagateException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("测试异常");
            _mockProcessor.CombineAudioFilesAsync(
                Arg.Any<IEnumerable<string>>(), 
                Arg.Any<string>(), 
                Arg.Any<AudioCombineOptions>()
            ).Returns(Task.FromException(expectedException).AsValueTask());
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                await _mockProcessor.CombineAudioFilesAsync(_testFiles, _outputFile, null));
            
            Assert.Same(expectedException, exception);
        }
    }
}