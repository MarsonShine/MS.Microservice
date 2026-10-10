# 二维码与条形码

`CodeProcessor` 直接放在现有 Media 的 `Codes` 命名空间，使用 [ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11) 编解码，并复用 SkiaSharp 和 `ImageCodec` 的图片限制、EXIF 方向处理及 PNG 输出。ZXing.Net 为 Apache-2.0 授权，不需要 System.Drawing 或额外图像绑定包。

```csharp
var codes = new CodeProcessor();
await codes.GenerateAsync("你好，微服务", output, new CodeGenerationOptions
{
    Format = CodeFormat.Qr,
    Width = 256,
    Height = 256,
    QrErrorCorrection = QrErrorCorrection.High
}, cancellationToken);

IReadOnlyList<DecodedCode> found = await codes.DecodeAsync(input,
    new CodeReadingOptions { Formats = [CodeFormat.Qr, CodeFormat.Code128] }, cancellationToken);
```

生成支持 QR、Code128、EAN13、EAN8、UPC-A、Code39、DataMatrix 和 PDF417。所有格式先验证字符串是完整的 UTF-16，拒绝孤立代理项，不允许编码器悄悄替换内容；有效代理对和用户实际传入的 `U+FFFD` 均保留。QR 使用 UTF-8，支持四档纠错。数字码的长度、校验位、Code128／Code39 的字符范围等约束由 ZXing 验证；例如 EAN13 可以传 12 位数字让引擎补校验位，13 位输入必须包含正确校验位。其他格式的可编码字符范围以引擎支持为准。

输出是指定尺寸的 PNG，只有不透明的深色前景和浅色背景。引擎按格式选择模块尺寸及留白；`Margin = null` 为 DataMatrix 添加所需的一模块静区（引擎默认为零），其他格式保留引擎默认值。显式值沿用 ZXing 的格式语义（QR 和 DataMatrix 按模块，普通条码按像素），不能假定所有格式单位相同；显式零值仍保留，但可能无法识别。输出按整像素绘制，不做平滑缩放。引擎需要比指定尺寸更大的矩阵时直接失败，不裁剪或压缩码元；较小矩阵居中补背景。低对比度、自定义过小留白、过密编码及劣质拍摄都可能降低识别率。

识别支持同一图片中的多个码，默认尝试全部支持格式、旋转及反色；可以缩小格式范围。正常图片中未找到码返回空集合，损坏或超限图片抛出对应读取错误。透明像素合成到白底后识别。返回文本、引擎实际识别的格式及定位点；UPC-A 与前导零的 EAN13 可能表示同一码，返回值保留引擎判定和对应文本，不强行改成生成时的格式。定位点针对 EXIF 校正后的输入图片。

组件自行管理检测时的四个旋转方向，并将定位点逆变换回显示坐标，避免 [ZXing 0.16.11 通用自动旋转接口](https://github.com/micjahn/ZXing.Net/blob/v0.16.11.0/Source/lib/BarcodeReaderGeneric.cs)返回旋转后坐标。识别采用引擎的首次成功方向结果；多个码混合不同角度、遮挡或复杂背景时，识别仍是尽力而为。关闭 `AutoRotate` 会同时禁用一维读取器的隐式旋转，但 QR 等格式本身的方向容忍能力仍保留。

同一读取边界还适配 [PDF417 的内部旋转坐标](https://github.com/micjahn/ZXing.Net/blob/v0.16.11.0/Source/lib/pdf417/PDF417Reader.cs)，在多码递归裁剪之前按其检测器旋转角度还原。该元数据规则仅适用于 PDF417；一维码扫描反向时的同名元数据含义不同，不能重复转换。升级 ZXing 时应保留跨格式旋转和坐标回归测试，并重新评估这些兼容处理。

输入从流的当前位置读取，输出写到当前位置，不关闭调用者流。限制包括 `ImageLimits` 的压缩输入字节数、像素数和尺寸，以及默认 4096 个 UTF-16 字符的编码内容上限。识别时还有 RGB 与灰度工作缓冲区，应按宿主内存预算设置图片上限。取消在输入输出、像素处理及引擎调用前后检查，ZXing 的同步编解码过程本身不能中途打断。写出异常或取消可能留下部分输出，由调用者丢弃该流或文件。

批量处理由应用逐个调用同一无状态组件；这里不引入后台任务或调度框架。编码矩阵及通用 RGB 读取接口见 [ZXing 官方项目](https://github.com/micjahn/ZXing.Net)。
