# 视频截图、封面与音轨提取

功能继续放在现有 `VideoProcessor`，复用 FFmpeg 进程管理、视频探测和临时文件原子发布，不需要新项目。

```csharp
var video = new VideoProcessor();
await video.CaptureFrameAsync("input.mp4", "cover.jpg", TimeSpan.FromSeconds(2),
    cancellationToken: cancellationToken);

AudioExtractionResult audio = await video.ExtractAudioAsync("input.mp4", "speech.wav",
    new AudioExtractionOptions
    {
        AudioTrackIndex = 0,
        Start = TimeSpan.FromSeconds(1),
        Duration = TimeSpan.FromSeconds(10)
    }, cancellationToken);
```

截图输出由 `.png`、`.jpg`／`.jpeg`、`.webp` 扩展名确定。不传图片选项时自动选择对应格式；传入 `ImageProcessingOptions` 时，其 `Format` 必须与扩展名一致。裁剪、缩放、旋转、翻转、质量和 JPEG 背景复用 `ImageProcessor`。先检查方向校正后视频尺寸及图片像素限制，再让 FFmpeg 输出一帧无损 PNG；同一临时文件经图片处理后替换成最终格式，不发生 JPEG 二次有损编码。

时间戳从零开始，必须落在已知时长的半开区间 `[0, duration)`。采用 FFmpeg 精确寻址，选择不早于请求时刻的第一帧；靠近结尾且后面没有帧时返回读取错误，不凭平均帧率猜测或向前夹取一帧。容器总时长可能比视频轨道长，因此时间检查通过仍可能没有可用帧。

中间 PNG 重新解码时也受 `ImageLimits.MaximumEncodedBytes` 限制。大幅面、高噪声帧可能在像素限制内但超出该压缩字节预算，此时失败并清理临时文件；宿主需要按内存与磁盘预算设置限制。这不是对 FFmpeg 原生内存或全部磁盘写入量的硬配额。图片处理及编码仍使用现有的内存缓冲。

音轨按音频流自身的零基索引选择，不是文件中的绝对流索引。`.wav` 输出有符号 16 位 PCM，`.mp3` 使用 MP3，`.m4a` 使用 AAC，`.flac` 使用 FLAC；显式固定源采样率和声道，输出探测后再次核对，避免 FFmpeg 暗中重采样或混音。MP3／AAC 默认请求 192 kbps。MP3 只接受单声道或立体声；编码器不支持源采样率时失败，可以改用 WAV 或 FLAC。所需编码器由宿主 FFmpeg 构建提供，缺失时返回进程错误。

音频 `Start` 必须非负并早于已知的所选音轨结束时刻；`Duration` 必须为正，超过余下时长时自然截至结尾。源轨道时长未知时使用容器时长作为可用检查信息。输出再经 ffprobe 解码计数，确认存在可解码音频帧后才发布；该验证会读取输出音频。返回实际编码、采样率、声道数和可获得的时长。无音轨、轨道不存在、纯音频输入都会明确失败；本入口用于视频音轨提取，普通音频编辑继续由已有 Audio 模块承担。

输入输出必须是不同的本地路径，已有目标文件或目录不会覆盖。只有完整生成并通过探测／图片处理后，才将本次唯一临时文件移动为目标；失败或取消仅删除本次临时文件。父目录可能按需创建，失败时不递归删除父目录。参数使用 `ArgumentList`，含中文、空格或引号的路径不会被拼成 shell 命令。取消、超时及清理错误规则与现有视频处理一致。

实际集成测试需要设置 `RUN_MEDIA_FFMPEG_TESTS=true`，并安装支持 PNG、JPEG、WebP、PCM、MP3、AAC、FLAC 和测试用 FFV1 的 FFmpeg／ffprobe。
