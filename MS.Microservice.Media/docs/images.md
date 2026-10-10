# 图片处理

`MS.Microservice.Media.Images.ImageProcessor` 通过流读取静态图片，使用 SkiaSharp 真实解码，并输出 PNG、JPEG 或 WebP。公共 API 不暴露 Skia 类型。

```csharp
var result = await new ImageProcessor().ProcessAsync(input, output,
    new ImageProcessingOptions
    {
        Crop = new(10, 20, 400, 300),
        RotationDegrees = 90,
        FlipHorizontal = true,
        Resize = new(800, 600),
        ResizeMode = ImageResizeMode.Contain,
        Format = ImageOutputFormat.Jpeg,
        Quality = 90
    }, cancellationToken);
```

顺序固定为：解码并校正 EXIF 方向 → 裁剪 → 顺时针旋转 → 在旋转后坐标中翻转 → 缩放 → 编码。共享内部 `ImageCodec` 规范化全部八种 EXIF origin，后续图片路径可使用同一解码和编码规则。输出不复制原 EXIF、文本及其他附加元数据。

裁剪使用方向校正后的整数像素坐标，矩形必须完整位于图片内。旋转只接受 0、90、180、270 度；任意角度不在此 API 范围。翻转分别围绕当前显示的水平与垂直方向。

缩放指定宽高均为正的 `ImageSize`：

- `Contain` 保持比例，适配目标边界框，返回实际尺寸，不填充留白；尺寸四舍六入五成双到整数像素，最小一像素。
- `Cover` 保持比例并从中心裁剪，输出目标宽高。
- `Stretch` 输出目标宽高，允许改变比例。

缩放允许放大，采用双线性采样。裁剪、方向校正和直角旋转使用最近邻采样，保留像素位置。

PNG 和 WebP 保留透明度。JPEG 先在指定的不透明 `JpegBackground` 上合成，默认白色，再编码。`Quality` 范围 0–100；JPEG/WebP 使用编码器的质量参数，PNG 使用无损编码而不以该参数控制文件大小。压缩结果大小取决于图像内容，不保证降质或转格式后文件更小。

输入采用当前 Skia 编译包支持的静态格式。创建 codec 后先检查宽高、像素数和帧数，再申请像素内存；多帧动画拒绝处理，不隐式丢弃后续帧。像素解码必须返回成功，codec 报告损坏或不能完整解码时抛出 `InvalidDataException`。解码器可能容忍不影响像素恢复的容器缺失，例如当前版本可以完整恢复缺少 IEND 的 PNG；该 API 校验可解码像素，不是严格容器标准验证器或文件安全扫描器。

默认限制为单边 16,384 像素、总计 40,000,000 像素、编码输入 64 MiB。输入、EXIF 校正和每个输出画布都检查像素限制；目标框本身也须符合限制。实现另外限制单个 RGBA 栅格不超过 `int.MaxValue` 字节。可通过 `ImageLimits` 收紧预算，宿主仍须控制并发与总内存。

编码输入先读入有界内存流，再复制到 native `SKData`；解码与变换期间最多同时保留当前及下一张 RGBA 栅格，JPEG 扁平化另需一张栅格，编码输出也暂存在 native 内存。原始文件尺寸不能代表解码内存；40M 像素的一张 RGBA 栅格约需 160 MB。

调用方输入输出流始终保持打开，从当前位置读写，支持不可定位的分段输入。禁止将同一流实例用作输入与输出；调用方也不应以不同包装器引用同一底层数据做原地处理。取消在读取、native 调用前后与输出复制中检查，单次 native 解码/绘制/编码不能被强制中断。输出失败或取消可能留下部分字节，需完整发布时应由宿主使用临时文件。方法成功不关闭或刷新调用方输出流。
