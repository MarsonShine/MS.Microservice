# 二维码的矩阵生成与坐标还原

调用和参数见 [Media README](../README.md)。`CodeProcessor` 使用 [ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11)，图像解码与 PNG 编码复用 `ImageCodec`；无需 System.Drawing 或额外图像绑定包。ZXing.Net 许可为 Apache-2.0。

## 编码与静区

先验证完整 UTF-16，再交给码制编码器，防止孤立代理项被悄悄替换；有效代理对和用户实际输入的 U+FFFD 均保留。QR 使用 UTF-8，数字码的长度、校验位和其他格式的字符范围由引擎负责，例如 EAN13 的 12 位输入可补校验位，13 位输入须已有正确校验位。

输出按整数码元直接绘制为不透明深色前景与浅色背景。引擎矩阵大于目标画布时拒绝，不能裁剪或平滑缩放码元来满足尺寸；较小矩阵居中补背景。低对比度、静区不足和过密内容仍可能无法识别。

DataMatrix 在默认 margin 下补一模块静区，因为引擎默认零。显式 margin 保留 ZXing 的格式语义：QR／DataMatrix 按模块，一维条码通常按像素。显式零值不被擅自改写，但其识别风险必须由调用方承担。

## 识别坐标

透明输入先合成白底，再生成 RGB／灰度读取缓冲。组件自行遍历四个旋转方向，把定位点逆变换回 EXIF 校正后的显示坐标，避免[通用自动旋转接口](https://github.com/micjahn/ZXing.Net/blob/v0.16.11.0/Source/lib/BarcodeReaderGeneric.cs)直接返回旋转后坐标。

[PDF417](https://github.com/micjahn/ZXing.Net/blob/v0.16.11.0/Source/lib/pdf417/PDF417Reader.cs)还会在内部旋转检测；必须在多码递归裁剪前，按其元数据把坐标还原。该规则只适用于 PDF417，一维码扫描反向的同名元数据不能重复应用。

读取采用首次成功方向的结果，混合不同角度的多个码、遮挡和复杂背景仍是尽力识别。关闭 `AutoRotate` 同时关闭一维读取器的隐式旋转，但 QR 自身的方向容忍能力仍在。UPC-A 和前导零 EAN13 可能是同一数据的两种表示，结果保留引擎实际格式和文本，不强制改回生成时的格式。正常图像未命中返回空集合，损坏或超限图像继续抛出解码错误。

## 资源与回归

编码文本、输入编码字节、图片尺寸／像素以及工作缓冲均有各自约束；识别另有 RGB 和灰度缓冲，宿主需按并发量配置图片预算。调用方流保持打开，取消在像素处理、I/O 和同步 ZXing 调用前后观察；引擎内部不能被中途打断。

[CodeProcessorTests](../test/MS.Microservice.Media.Tests/Codes/CodeProcessorTests.cs) 的跨格式旋转与定位点回归是升级 ZXing 的必要检查。还应保留整像素矩阵、默认／零静区、UTF-16 边界、EAN/UPC 等价格式和多码结果用例。批量调用由应用编排，不在该无状态组件中增加调度框架。
