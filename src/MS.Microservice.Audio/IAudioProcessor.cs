using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Common.NAudio
{
    /// <summary>
    /// 音频处理器接口
    /// </summary>
    public interface IAudioProcessor
    {
        /// <summary>
        /// 合并多个音频文件为一个文件
        /// </summary>
        /// <param name="inputFiles">输入文件路径集合</param>
        /// <param name="outputFile">输出文件路径</param>
        /// <param name="options">合并选项</param>
        /// <returns>异步任务</returns>
        ValueTask CombineAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioCombineOptions? options = null);

        /// <summary>
        /// 合并多个音频流为一个文件
        /// </summary>
        /// <param name="inputStreams">输入音频流集合</param>
        /// <param name="outputFile">输出文件路径</param>
        /// <param name="options">合并选项</param>
        /// <returns>异步任务</returns>
        ValueTask CombineAudioStreamsAsync(IEnumerable<Stream> inputStreams, string outputFile, AudioCombineOptions? options = null);

        /// <summary>
        /// 合并多个音频流并返回结果流
        /// </summary>
        /// <param name="inputStreams">输入音频流集合</param>
        /// <param name="outputFormat">输出格式</param>
        /// <param name="options">合并选项</param>
        /// <returns>合并后的音频流</returns>
        ValueTask<Stream> CombineAudioStreamsAsync(IEnumerable<Stream> inputStreams, AudioFormat outputFormat, AudioCombineOptions? options = null);

        /// <summary>
        /// 合并多个音频字节数组为一个文件
        /// </summary>
        /// <param name="inputAudioData">输入音频字节数组集合</param>
        /// <param name="outputFile">输出文件路径</param>
        /// <param name="options">合并选项</param>
        /// <returns>异步任务</returns>
        ValueTask CombineAudioDataAsync(IEnumerable<byte[]> inputAudioData, string outputFile, AudioCombineOptions? options = null);

        /// <summary>
        /// 合并多个音频字节数组并返回结果字节数组
        /// </summary>
        /// <param name="inputAudioData">输入音频字节数组集合</param>
        /// <param name="outputFormat">输出格式</param>
        /// <param name="options">合并选项</param>
        /// <returns>合并后的音频字节数组</returns>
        ValueTask<byte[]> CombineAudioDataAsync(IEnumerable<byte[]> inputAudioData, AudioFormat outputFormat, AudioCombineOptions? options = null);

        /// <summary>
        /// 混音多个音频文件
        /// </summary>
        /// <param name="inputFiles">输入文件路径集合</param>
        /// <param name="outputFile">输出文件路径</param>
        /// <param name="options">混音选项</param>
        /// <returns>异步任务</returns>
        ValueTask MixAudioFilesAsync(IEnumerable<string> inputFiles, string outputFile, AudioMixOptions? options = null);

        /// <summary>
        /// 转换音频格式
        /// </summary>
        /// <param name="inputFile">输入文件路径</param>
        /// <param name="outputFile">输出文件路径</param>
        /// <param name="options">转换选项</param>
        /// <returns>异步任务</returns>
        ValueTask ConvertAudioFormatAsync(string inputFile, string outputFile, AudioConvertOptions options);

        /// <summary>
        /// 将单声道转换为立体声
        /// </summary>
        /// <param name="inputFile">输入文件路径</param>
        /// <param name="outputFile">输出文件路径</param>
        /// <param name="options">转换选项</param>
        /// <returns>异步任务</returns>
        ValueTask ConvertMonoToStereoAsync(string inputFile, string outputFile, AudioConvertOptions? options = null);
    }
}