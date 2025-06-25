using MS.Microservice.Infrastructure.Common.NAudio;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Tests.Common.NAudio
{
    // ���ɲ�����Ҫʵ�ʵ���Ƶ�ļ���NAudio����
    // ��Щ����Ĭ�ϻᱻ��������Ϊ������Ҫʵ�ʵ��ļ�ϵͳ����Ƶ�ļ�
    // �����в���ǰ����׼���ò�����Ƶ�ļ�
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
            
            // ע��: ���д˲���ǰ��Ҫ�ֶ�׼��������Ƶ�ļ�
            // ���ﲻ�ᴴ����ʵ����Ƶ�ļ�����Ϊ��Ҫ��ʵ����Ƶ����
        }
        
        public void Dispose()
        {
            // ������Դ������ļ�
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
                        // �����������
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
                    // �����������
                }
            }
        }
        
        [Fact(Skip = "��Ҫʵ����Ƶ�ļ�")]
        public async Task CombineAudioFilesAsync_WithRealFiles_ShouldCreateCombinedOutput()
        {
            // Arrange - ������Ҫָ��ʵ�ʴ��ڵĲ�����Ƶ�ļ�
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
        
        [Fact(Skip = "��Ҫʵ����Ƶ�ļ�")]
        public async Task MixAudioFilesAsync_WithRealFiles_ShouldCreateMixedOutput()
        {
            // Arrange - ������Ҫָ��ʵ�ʴ��ڵĲ�����Ƶ�ļ�
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
        
        [Fact(Skip = "��Ҫʵ����Ƶ�ļ�")]
        public async Task ConvertAudioFormatAsync_WithRealFile_ShouldConvertFormat()
        {
            // Arrange - ������Ҫָ��ʵ�ʴ��ڵĲ�����Ƶ�ļ�
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
        
        [Fact(Skip = "��Ҫʵ����Ƶ�ļ�")]
        public async Task ConvertMonoToStereoAsync_WithRealMonoFile_ShouldConvertToStereo()
        {
            // Arrange - ������Ҫָ��ʵ�ʴ��ڵĵ�����������Ƶ�ļ�
            var inputFile = @"TestData\mono.wav";
            var outputFile = Path.Combine(_testDirectory, "stereo.wav");
            _createdFiles.Add(outputFile);
            
            // Act
            await _audioProcessor.ConvertMonoToStereoAsync(inputFile, outputFile);
            
            // Assert
            Assert.True(File.Exists(outputFile));
            
            // ��֤�����������
            using (var reader = new AudioFileReader(outputFile))
            {
                Assert.Equal(2, reader.WaveFormat.Channels);
            }
        }
    }
}