# 媒体处理

引用 `MS.Microservice.Media` 可处理静态图片、添加图片／视频水印、生成和识别二维码、编辑与拼接视频。图片自动去水印与图片／视频手动修复使用可选的 `MS.Microservice.Media.Inference`。两者均使用 .NET 10。

| 入口 | 功能 |
| --- | --- |
| `ImageProcessor.ProcessAsync` | EXIF 方向校正、裁剪、旋转、翻转、缩放，输出 PNG／JPEG／WebP |
| `ImageWatermarker.AddTextAsync` / `AddLogoAsync` | 图片文字／Logo 水印；透明 PNG 叠加图可供其他流程复用 |
| `CodeProcessor.GenerateAsync` / `DecodeAsync` | 八种二维码／条码生成及多码识别 |
| `WatermarkRemover.RemoveAsync`（可选） | 默认自动检测并修复图片，也可提供手动蒙版 |
| `MiganInpainter.InpaintAsync`（可选） | 按二值蒙版修复指定区域 |
| `VideoWatermarkRemover.RemoveAsync`（可选） | 手动蒙版／矩形关键帧视频修复，生成 FFV1 MKV 及预览包 |
| `VideoProcessor.ProbeAsync` / `ProcessAsync` | 视频信息、剪辑、画面变换、MP4／MKV／WebM 转码 |
| `AddTextWatermarkAsync` / `AddLogoWatermarkAsync` | 视频水印及显示时间范围 |
| `ConcatenateAsync` | 按顺序拼接不同尺寸、帧率、编码和音轨的视频 |
| `CaptureFrameAsync` / `ExtractAudioAsync` | 封面截图和指定音轨提取 |

## 图片编辑与水印

```csharp
using MS.Microservice.Media.Images;

await using var input = File.OpenRead("input.jpg");
await using var output = File.Open("scaled.webp", FileMode.CreateNew, FileAccess.Write);
await new ImageProcessor().ProcessAsync(input, output,
    new ImageProcessingOptions
    {
        Resize = new(1280, 720),
        ResizeMode = ImageResizeMode.Contain,
        Format = ImageOutputFormat.Webp,
        Quality = 85
    }, cancellationToken);
```

| `ImageProcessingOptions` 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Crop` | 不裁剪 | 使用 EXIF 校正后的整数像素坐标，必须位于图片内 |
| `Resize` / `ResizeMode` | 不缩放 / `Contain` | `Contain` 保持比例适配，`Cover` 居中裁剪填满，`Stretch` 拉伸 |
| `RotationDegrees` | 0 | 只接受 0、90、180、270 度，顺时针旋转 |
| `FlipHorizontal` / `FlipVertical` | `false` | 在旋转后画面上翻转 |
| `Format` / `Quality` | PNG / 90 | 质量范围 0–100；PNG 无损，质量参数不控制其文件大小 |
| `JpegBackground` | 白色 | JPEG 的不透明合成底色；PNG／WebP 保留透明度 |
| `Limits` | 16,384 单边 / 40M 像素 / 64 MiB 输入 | 输入和输出画布均受尺寸、像素预算约束 |

添加图片水印时，传入 `TextWatermark` 或 Logo 流。文字的 `FontSize` 默认 32，颜色默认白色；中文应指定已部署的 `FontFile`。默认字体缺字会报错，不会悄悄换字体。Logo 可用 `ImageSize` 指定等比例适配框。

| `WatermarkPlacement` 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Anchor` / `MarginX` / `MarginY` | 右下 / 16 / 16 | 九宫格锚点及四周内边距，按旋转后的外框定位 |
| `Opacity` / `RotationDegrees` | 0.35 / 0 | 透明度 0–1，水印角度可为任意有限值 |
| `Tile` / `TileGap` / `MaximumTiles` | `false` / 32 / 10,000 | 平铺时按外框加间隔排布，边缘可裁剪；非平铺必须完整放入画面 |

文字限一行、4096 个 UTF-16 单元，支持单个文本 run 的 HarfBuzz 塑形；复杂混合双向文字应先由排版组件生成透明 Logo。静态图片入口拒绝多帧动画，输出不复制原 EXIF 等元数据。

## 二维码与条形码

```csharp
using MS.Microservice.Media.Codes;

var codes = new CodeProcessor();
await using (var png = File.Open("code.png", FileMode.CreateNew, FileAccess.Write))
    await codes.GenerateAsync("你好，微服务", png,
        new CodeGenerationOptions { Format = CodeFormat.Qr, Width = 256, Height = 256 }, cancellationToken);
await using var codeInput = File.OpenRead("code.png");
var found = await codes.DecodeAsync(codeInput, cancellationToken: cancellationToken);
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Format` | QR | 另支持 Code128、EAN13、EAN8、UPC-A、Code39、DataMatrix、PDF417 |
| `Width` / `Height` | 256 / 256 | 输出 PNG 尺寸；过小会失败，不压缩码元来凑尺寸 |
| `Margin` | `null` | 使用格式默认值；DataMatrix 为一模块静区，显式值的单位因格式而异 |
| `QrErrorCorrection` | `Medium` | QR 的四档纠错级别 |
| `Foreground` / `Background` | 黑 / 白 | 均须不透明，前景应比背景暗 |
| `MaximumTextCharacters` | 4096 | 编码内容的 UTF-16 单元上限 |
| `Formats`（识别） | 全部 | 可缩小码制范围；默认尝试旋转、反色及更充分搜索 |

未找到码返回空集合；定位点属于 EXIF 校正后的图片。其他码制的字符、长度和校验位约束由 ZXing 验证，低对比度或静区不足会影响识别。

## 视频处理

先由宿主安装带所需编码器的 FFmpeg 和 ffprobe。可配置绝对路径，不能把外部请求作为可执行文件路径。

```csharp
using MS.Microservice.Media.Images;
using MS.Microservice.Media.Video;

var video = new VideoProcessor(new FfmpegOptions
{
    FfmpegPath = "ffmpeg", FfprobePath = "ffprobe"
});
await video.ProcessAsync("input.mp4", "edited.mp4", new VideoEditOptions
{
    Start = TimeSpan.FromSeconds(2), Duration = TimeSpan.FromSeconds(5),
    Resize = new(1280, 720)
}, cancellationToken);
await video.AddTextWatermarkAsync("edited.mp4", "marked.mp4",
    new TextWatermark("内部资料") { FontFile = "fonts/NotoSansSC-Regular.ttf", FontSize = 28 },
    new VideoWatermarkOptions { Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(4) },
    cancellationToken);
await using var logo = File.OpenRead("logo.png");
await video.AddLogoWatermarkAsync("edited.mp4", "with-logo.mp4", logo, new(120, 120),
    cancellationToken: cancellationToken);
await video.ConcatenateAsync(new[] { "edited.mp4", "marked.mp4" }, "joined.mp4",
    new VideoConcatenationOptions { Canvas = new(1280, 720), FrameRate = 30 }, cancellationToken);
await video.CaptureFrameAsync("joined.mp4", "cover.jpg", TimeSpan.FromSeconds(1),
    cancellationToken: cancellationToken);
await video.ExtractAudioAsync("joined.mp4", "audio.wav", cancellationToken: cancellationToken);
```

示例中的字体文件由宿主提供。MP4／MKV 输出 H.264 + AAC，WebM 输出 VP9 + Opus，须有对应编码器；FFmpeg 的分发条件取决于所选构建。

| 参数组 | 默认值 | 说明 |
| --- | --- | --- |
| `FfmpegOptions.Timeout` | 30 分钟 | 单次进程超时，可设 `Timeout.InfiniteTimeSpan` |
| 编辑 `Start` / `Duration` | 0 / 到末尾 | 非负起点、正时长；图片变换选项的坐标以方向校正后的画面为准 |
| `ConstantRateFactor` / `Speed` | 23 / `Fast` | CRF 0–51；速度另有 `Balanced`、`Slow` |
| 视频水印 `Start` / `End` | 0 / 到末尾 | 显示区间 `[Start, End)`，不延长源视频；位置用 `Placement` |
| 拼接 `Canvas` / `FrameRate` | 取首段 | 画布补为偶数；平均帧率未知时须指定，范围为 `(0, 240]` |
| 拼接 `MaximumInputs` | 32 | 允许单段和重复段，要求已知正时长 |
| 视频 `MaximumDimension` / `MaximumPixels` | 16,384 / 40M | 输入、画布及补齐外框预算；水印使用 `Placement.Limits` |
| 提取 `AudioTrackIndex` | 0 | 音频轨自身的零基索引；WAV 为 PCM16，另支持 MP3／M4A／FLAC |

视频文件入口不覆盖已有目标，成功校验后才发布。YUV420 输出会在右侧／底部将奇数尺寸补齐一像素；重新编码有损，不保证原像素、HDR 色彩或绝对时间戳保留。编辑与水印不强制恒定帧率；拼接统一为恒定帧率，并将每段音视频各自首帧归零，**不保留源音画起始偏移**。任一片段有声音时，拼接输出一个 48 kHz 立体声音轨，仅取各段第一音轨；其他音轨和字幕不属于拼接保留范围。

截图选择不早于指定时刻的第一帧，末尾没有后续帧时会报错。音轨提取保留源采样率和声道，MP3 仅接受单／双声道；所选编码器不支持该采样率时失败，可改用 WAV／FLAC。

## 图片自动去水印

选装 [Media.Inference](src/MS.Microservice.Media.Inference/README.md) 后，`RemoveAsync` 默认自动检测并修复，同时返回可调整蒙版；手动模式接受白色编辑、黑色保留的二值蒙版。需要本地模型和 Python 环境，运行时不下载权重或上传图片。

当前检测模型针对重复文字水印，会误选正常文字、字幕或场景 Logo。结果始终建议人工检查，应保留原图、展示蒙版并允许手动纠正。每次自动调用有独立 Python 冷启动和模型加载开销，应按后台任务规划；修复生成的是合理内容，不能恢复被遮挡的真实像素。当前入口只处理图片，不代表已提供视频去水印。

## 所有权与维护

图片、二维码和修复接口从当前位置读写流并保持打开；失败或取消可能留下部分输出，宿主应先写临时结果再发布。图片编码输出大小没有按输入文件大小保证，并发、总内存和保留策略由宿主控制。Skia／HarfBuzz 原生库及字体须适配目标系统，像素预算不等于进程内存硬配额。

使用者接入见[组件消费](../docs/components/consumption.md)。实现与回归说明：[图片管道](docs/images.md)、[图片水印](docs/watermarks.md)、[Codes 坐标](docs/codes.md)、[视频进程与编辑](docs/video.md)、[视频水印](docs/video-watermarks.md)、[截图与音轨](docs/video-assets.md)、[视频拼接](docs/video-concatenation.md)。
