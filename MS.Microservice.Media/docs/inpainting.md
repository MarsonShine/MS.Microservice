# 本地 MI-GAN 图片区域修复

`MS.Microservice.Media.Inference` 是可选组件，将 ONNX Runtime 原生 CPU 依赖与基础图片、二维码、视频工具隔离。`MiganInpainter` 按调用方提供的二值蒙版修复图像区域；水印识别由检测组件负责。

```csharp
await using var inpainter = await MiganInpainter.CreateAsync(new MiganInpaintingOptions
{
    ModelPath = configuredModelPath,
    CpuThreads = 4
});
await using var image = File.OpenRead("input.png");
await using var mask = File.OpenRead("mask.png");
await using var output = File.Create("result.png");
var result = await inpainter.InpaintAsync(image, mask, output, cancellationToken);
```

服务应复用实例，并在宿主停止时异步释放。每个实例在解码、推理、编码期间串行处理请求；默认 CPU 线程数为处理器数量与 4 的较小值。多个实例各自拥有原生会话和内存，创建实例数由宿主控制。

## 模型部署

只支持经过验证的 MI-GAN-512-Places2 pipeline-v2：

- 发布者：`andraniksargsyan/migan`。
- 固定 revision：`406830d0fa60666da0071c342ad2fbc8f30c5c64`。
- 文件：`migan_pipeline_v2.onnx`，28,079,181 字节。
- SHA-256：`6f1f3530a1a2324b19752018ce756088b07973cda8d7d890034ace5c8a48c40b`。
- [作者提供的固定文件](https://huggingface.co/andraniksargsyan/migan/resolve/406830d0fa60666da0071c342ad2fbc8f30c5c64/migan_pipeline_v2.onnx)。部署阶段由使用者放到本地目录，配置 `ModelPath`。

创建实例时将固定大小的模型读入快照并验证摘要，再加载同一快照，防止校验后重新打开路径导致模型替换。启动暂时增加约 28 MB 托管内存。随后验证 ONNX 元数据：uint8 `image[1,3,H,W]`、uint8 `mask[1,1,H,W]`，以及单一 uint8 `result` 输出。不会在请求期间下载模型或访问网络，也不允许替换为未验证哈希后沿用当前后处理协议。

运行库采用官方 CPU 包 `Microsoft.ML.OnnxRuntime` 1.31.0。模型[代码许可](https://github.com/Picsart-AI-Research/MI-GAN/blob/2b793c5ece43f4253e32d4afc257120a5deed6f5/LICENSE)与独立[权重许可](https://github.com/Picsart-AI-Research/MI-GAN/blob/2b793c5ece43f4253e32d4afc257120a5deed6f5/LICENSE-WEIGHTS)均为 MIT；分发模型时保留许可通知。

## 蒙版与像素契约

输入图片和蒙版分别由基础组件的共享 `ImageCodec` 解码并按自身 EXIF 方向规范化。蒙版尺寸必须精确等于规范化后的图片尺寸，不自动缩放；每个蒙版像素必须是不透明的灰度 0 或 255：**白色编辑，黑色保留**。中间灰度、彩色和透明蒙版均拒绝。支持共享解码器接受的静态图格式，建议无损 PNG；没有表头、坐标或亮度阈值的隐式猜测。

保留的是 `ImageCodec` 规范化解码后的 RGBA 像素，不是原始文件字节、EXIF、色彩配置或 PNG 完全透明像素的隐藏 RGB。该解码器使用 premultiplied alpha，半透明通道可能已经量化。输出固定为 PNG，以保证规范化基准中的蒙版外 RGBA 一致；所有像素的 alpha 均保留。模型只修复 RGB，不生成新的 alpha。需要 JPEG、WebP 或后续编辑时，再调用 `ImageProcessor`。

原始导出管道在后处理阶段使用 known-mask maxpool3 与 Gaussian5。这会羽化蒙版，并使细笔画水印残留。因此推理蒙版与最终编辑蒙版分开：对编辑洞按源算子的支持半径 `1 + 2 = 3` 向外扩展，供模型推理；最终只在原始白色蒙版内取模型 RGB，其余像素完全保留。该规则来自固定模型[官方管道源码](https://github.com/Picsart-AI-Research/MI-GAN/blob/2b793c5ece43f4253e32d4afc257120a5deed6f5/scripts/create_onnx_pipeline.py)，不扩大调用方授权编辑范围。

非空修复要求宽高均至少为 3，满足 Gaussian5 的 reflection-padding2 约束。空蒙版跳过模型执行，直接输出规范化身份 PNG。返回结果中的 `SelectedPixelCount` 是蒙版选中数，`EditedPixelCount` 是实际发生变化的规范化 RGBA 像素数，两者可以不同。

## 资源与取消

默认限制为单边 4096、400 万像素、单个输入编码 32 MiB；图片和蒙版分别校验。宿主可以配置更严格的 `ImageLimits`。工作内存与当前解码图及张量尺寸相关，并包含较大的原生模型内存；限制并不等于原生运行库的硬内存配额。

每实例仅允许一个请求进行包括解码在内的重操作，避免排队请求先分配所有图像。张量、推理结果、图像和会话均按所有权释放；释放实例会等待活跃操作完成，已经排队的后续调用观察到已释放状态。重复异步释放安全。

取消用于流 I/O、排队等待和像素处理，并在原生推理前后检查。同步原生 ONNX 执行不能通过此接口强制中断，取消后可能仍需等待当前执行返回；取消不会提前释放原生执行中的张量或会话。输入、蒙版、输出流始终由调用方持有，组件不关闭它们；错误或取消可能留下部分输出，建议先写临时文件，完整成功后再发布。

## 验证与质量边界

常规单元测试覆盖灰度二值校验、三像素上下文、边缘裁切、空蒙版、RGB 平面、alpha 与最终像素合成、限额和哈希失败。真实模型集成测试需要显式配置本地文件：

```powershell
$env:MS_MEDIA_MIGAN_MODEL = (Resolve-Path './models/migan_pipeline_v2.onnx').Path
dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Inference.Tests/MS.Microservice.Media.Inference.Tests.csproj
```

未设置该变量时，真实模型测试明确跳过，不下载权重，不用伪推理替代。实际模型验证包含不同分辨率、最小尺寸、半透明图片、蒙版外 PNG 像素保护，以及正在执行和已排队请求的释放行为。

内容修复生成合理背景，不能还原不可见的真实像素。已有 CPU 实验显示中强度合成文字水印可显著减弱，但边缘可能出现修复痕迹，弱水印可能变差，错误蒙版会损伤正常内容。此接口不承担水印语义判断，应保留原图、蒙版、预览及结果供调整；图片模型逐帧调用也不代表已经实现视频时序一致性。
