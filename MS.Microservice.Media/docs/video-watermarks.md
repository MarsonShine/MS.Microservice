# 视频文字和 Logo 水印

`VideoProcessor.AddTextWatermarkAsync` 和 `AddLogoWatermarkAsync` 复用图片的 `ImageWatermarker`，渲染与旋转元数据一致的完整画布透明 PNG，再通过本地 FFmpeg 叠加到视频。文字字体覆盖、整形、旋转、九宫格位置、透明度和平铺限制遵循 [图片水印](watermarks.md)。Logo 从调用者流当前位置读取，保持流打开。

```csharp
await new VideoProcessor().AddTextWatermarkAsync("input.mp4", "output.mp4",
    new TextWatermark("内部资料") { FontFile = fontFile, FontSize = 28 },
    new VideoWatermarkOptions
    {
        Start = TimeSpan.FromSeconds(2), End = TimeSpan.FromSeconds(10),
        Placement = new WatermarkPlacement { Anchor = WatermarkAnchor.BottomRight, Opacity = 0.4 }
    }, cancellationToken);
```

时间范围为 `[Start, End)`，默认从零到视频结束；超过视频结束的 End 不延长视频。时间依据 FFmpeg 默认归一化后的输入时间轴，保留视频帧时间间隔；不使用 `copyts`，不承诺保留原始容器的绝对时间戳。源旋转元数据应用到像素，输出旋转归零；奇数边长在右侧或下侧补齐一像素，以符合 YUV420 编码要求。非方形像素按源视频的像素栅格定位水印。

输出支持 MP4/MKV 的 H.264/AAC 和 WebM 的 VP9/Opus。映射第一条非封面视频和所有音轨；无音轨保持无音轨。视频和音频会重新编码，结果属于有损处理，颜色、像素值和编码延迟可能变化；字幕、数据轨和附件不保留。高位深/HDR 不作色调映射保证，选用专门的色彩管理流程处理这类素材。

不覆盖既有文件，也不接受输入路径作为输出。像素和边长预算在渲染前检查；原生进程使用参数数组、并行排空有界输出、可配置超时和取消后终止进程树。完整画布 PNG 和视频暂存文件位于输出目录，仅清理本次创建的文件。编码和结果检查通过、PNG 清理成功后才原子发布视频；失败时已有输入和目标不变。每个调用的内存随画布大小增长，调用方应控制并发。

真实 FFmpeg 集成测试覆盖三种容器、区间边界逐帧检查、声音保留、文字/Logo、透明水印、三种非零直角旋转元数据、预算、错误字体/编码器、取消和既有文件保护。方向规范化后的各编码滤镜统一移除已应用的显示矩阵，防止复杂图输出被播放器再次旋转。开启方式与 [视频基础](video.md) 相同：`RUN_MEDIA_FFMPEG_TESTS=true`；旋转测试的样本生成使用 FFmpeg 6 及以上的 `-display_rotation` 参数。

滤镜时间范围与叠加语义参考 [FFmpeg 官方滤镜文档](https://www.ffmpeg.org/ffmpeg-filters.html#overlay)。
