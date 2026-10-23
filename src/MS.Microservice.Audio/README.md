# 音频处理

引用 `MS.Microservice.Audio`，可剪辑音频、调整音量、添加淡入淡出，以及合并、混音、转换格式和声道。命名空间为 `MS.Microservice.Infrastructure.Common.NAudio`，依赖 NAudio 和 NAudio.Lame。

| 入口 | 功能 |
| --- | --- |
| `AudioEditor.EditAsync` | 按时间剪辑、音量、淡入淡出，生成新的 WAV／MP3 |
| `AudioProcessor` / `IAudioProcessor` | 文件、流、字节数组合并，混音、格式转换、单声道转立体声 |
| `AudioProcessorExtensions` | 目录合并、批量转换、读取音频信息 |
| `AudioFileFormatDetector` | 探测 WAV／MP3；其他格式返回 `Auto`；流须可定位，仍需后续解码校验 |

## 音频编辑

```csharp
using MS.Microservice.Infrastructure.Common.NAudio;

await new AudioEditor().EditAsync(
    "input.wav", "edited.wav",
    new AudioEditOptions
    {
        Start = TimeSpan.FromSeconds(2),
        Duration = TimeSpan.FromSeconds(5),
        Gain = 0.8f,
        FadeIn = TimeSpan.FromMilliseconds(200),
        FadeOut = TimeSpan.FromMilliseconds(300)
    },
    cancellationToken: cancellationToken);
```

| `AudioEditOptions` 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Start` | 0 | 非负；必须早于输入末尾 |
| `Duration` | `null` | 保留到末尾；指定时必须为正，超过剩余长度时截到末尾 |
| `Gain` | 1 | 有限非负倍数；0 为静音，结果限幅到 [-1, 1] |
| `FadeIn` / `FadeOut` | 0 | 各自不得超过选段；重叠时两个系数相乘 |
| `Mp3Preset` | `STANDARD` | 仅 MP3 使用的 LAME 预设 |

时间向下对齐到完整采样帧，选段至少包含一帧。编辑保留采样率和声道，不重采样；WAV 输出为 **32 位 IEEE float**，MP3 只支持单声道或立体声。输入中的 NaN／无穷样本会报错。

输入与输出路径必须不同，目标不能已经存在。`EditAsync` 成功返回后文件才可作为完成结果；进度值 1 只表示处理循环结束。取消和失败会清理本次临时文件，强制结束进程后可能留下临时文件。

## 合并与转换

```csharp
var processor = new AudioProcessor();
await processor.CombineAudioFilesAsync(
    new[] { "first.wav", "second.wav" }, "combined.wav",
    new AudioCombineOptions { SilenceDuration = 0.5f });
```

合并默认按文件名排序；需要按传入顺序拼接时设置 `SortByFileName = false`。每个输入之后都会添加配置的静音，包括最后一个输入。`AudioProcessor` 的流合并入口会接管并释放输入流，返回的结果流由调用方释放；文件入口可能覆盖已有目标，需自行选择新路径。`AudioEditor` 的不覆盖保证不适用于这些旧入口。

## 运行要求

标准 PCM／float WAV 的编辑不需要 Windows 媒体解码器；MP3 解码、其他压缩格式和部分转换路径依赖 Windows ACM／Media Foundation。MP3 编码需要与进程架构匹配的 LAME 原生库。部署前应在目标平台验证实际使用的编解码路径。

MP3 有损编码和补帧会影响解码后的末尾时长；压缩音频的定位精度受解码器限制。`AudioEditor.EditAsync` 在处理块之间检查取消，单次原生调用无法强制中断；旧合并、混音和转换入口没有取消参数。输出目录由宿主保持可信且稳定。

源码或包的引用方式见[组件消费](../../docs/components/consumption.md)。维护说明：[编辑与发布](docs/audio-editor-design.md)、[格式探测与已有基准](docs/format-detection-design.md)、[静音缓冲](docs/silence-buffer-design.md)。

## 批量转换并发

BatchConvertAsync 与 BatchConvertAudioFormatAsync 保留原签名，共用受限并发实现，默认并发上限为 Environment.ProcessorCount。所有项结束后传播失败；失败优先于单项取消。

需要指定并发和取消时使用 BatchConvertAsync(inputFiles, outputDirectory, outputFormat, options, maxConcurrency, cancellationToken)。取消只停止后续派发，已开始的旧 ConvertAudioFormatAsync 不支持取消，仍需等待完成。输出文件名继续由输入叶子名称生成，调用方应提供不冲突的目标名称。

通用逐项结果见 [BatchProcessing](../MS.Microservice.Threading/README.md)。
