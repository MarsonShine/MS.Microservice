# 音频剪辑、音量与淡入淡出

`AudioEditor` 位于现有 Audio 项目及命名空间 `MS.Microservice.Infrastructure.Common.NAudio`，复用 NAudio 和 NAudio.Lame。已有 `IAudioProcessor` 的合并、混音、转换接口保持原有职责。

```csharp
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

`Start` 默认零，`Duration` 默认保留至文件末尾；指定时长须大于零，超过剩余长度时截到末尾。时间均向下量化到完整采样帧，一帧包含各声道的一个样本。起点须位于输入末尾之前，选段至少包含一帧。

`Gain` 为有限非负倍数：零为静音，一为原音量。各声道按同一帧增益处理，乘法使用双精度中间值，最终统一限幅到 [-1, 1]。不支持 NaN 或无穷样本；遇到此类浮点 WAV 输入会失败并清理输出。

淡入和淡出默认关闭，时长不得为负，量化后各自不得超过选段。淡入第一帧为零、该淡入最后一帧为完整增益；淡出反向变化，最后一帧为零。一帧长的淡入或淡出将该帧静音。重叠区域将两个系数相乘，且各声道保持同步。

输出由扩展名选择 `.wav` 或 `.mp3`，保留输入采样率和声道数，不执行重采样或声道转换。WAV 输出为 32 位 IEEE float。MP3 支持单声道和立体声，通过 `Mp3Preset` 选择已安装 LAME 的编码预设。

解码沿用 NAudio `AudioFileReader`。标准 PCM/float WAV 不需要 Windows 媒体解码器；MP3 解码及其他压缩格式依赖环境中的 Windows ACM/Media Foundation。MP3 编码需要 NAudio.Lame 附带的相应架构原生库。压缩音频定位精度受解码器限制；MP3 还存在有损编码、编码器延迟与末尾补帧，输出解码时长不保证逐帧等于 WAV 选段。

处理使用固定大小样本缓冲区，在后台线程按块解码、变换、写入并检查取消。单次原生编解码调用无法被强制中断。可选 `IProgress<double>` 返回已处理帧的比例，1 代表处理循环完成，编码器收尾和文件发布仍可能失败；建议由宿主在 `EditAsync` 成功返回后标记任务成功。同步进度回调异常会中止处理；使用异步派发的 `Progress<T>` 时，回调异常属于其派发上下文。

输入输出的规范化路径必须不同，输出必须不存在。先以 `CreateNew` 创建同目录随机临时文件，完整关闭编码器、输入和输出后，检查取消并用不覆盖目标的移动发布。目标在处理期间被创建也不会覆盖。复制、处理或取消失败只清理本次拥有的临时文件；调用方文件与输入保留。目录须由宿主保证可信且稳定，此 API 不隔离恶意本机文件系统修改，也不检测不同路径的硬链接别名。

强制结束进程或无法删除文件时可能留下临时文件，宿主负责保留期清理。文件已发布后的取消不撤销成功结果。
