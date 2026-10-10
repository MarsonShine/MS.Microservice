# MI-GAN 修复协议与像素边界

模型部署、调用及配置见 [Inference README](../src/MS.Microservice.Media.Inference/README.md)。本组件把 ONNX CPU 依赖隔离在可选项目中，基础媒体工具不需要加载模型。

## 固定模型与输入

实现只接受已验证的 MI-GAN-512-Places2 pipeline-v2。创建实例时读取固定大小的模型快照，校验 SHA-256 后加载同一份字节，避免校验后重新打开路径的替换窗口。启动临时增加约 28 MB 托管内存，随后验证 uint8 `image[1,3,H,W]`、uint8 `mask[1,1,H,W]` 及单一 uint8 `result` 的 ONNX 元数据。

固定模型与许可只有[部署入口](../src/MS.Microservice.Media.Inference/README.md)一份清单；切换权重不能只放宽摘要，而应重新验证整个输入与后处理协议。运行时不下载模型，也不访问网络。

图片、蒙版分别按自身 EXIF 规范化，尺寸须相同。蒙版只允许不透明灰度 0／255，不缩放、不猜阈值。保留基准是 `ImageCodec` 解码后的 premultiplied RGBA，不是原文件字节、元数据或透明像素隐藏 RGB；半透明通道可能已量化。

## 推理蒙版与最终编辑范围

原始 pipeline-v2 后处理使用 known-mask maxpool3 和 Gaussian5，直接输入细笔画蒙版会羽化边缘并残留水印。实现根据[固定官方导出管道](https://github.com/Picsart-AI-Research/MI-GAN/blob/2b793c5ece43f4253e32d4afc257120a5deed6f5/scripts/create_onnx_pipeline.py)的算子支持半径 `1 + 2 = 3`，把推理洞向外扩展三像素，并裁到图像边界。

推理蒙版和最终编辑蒙版因此不同：只在原始白色选区内采用模型 RGB，其余规范化 RGBA 完全保留，所有 alpha 也保持。扩大推理上下文不会扩大调用方授权的编辑范围。

非空选区须满足宽高至少 3，以支持 Gaussian5 的 reflection-padding2；空蒙版不执行模型，直接输出规范化身份 PNG。`SelectedPixelCount` 描述选区，`EditedPixelCount` 描述实际改变的像素，两者无需相等。

## 会话与取消

实例持有原生 ONNX 会话，并以单并发覆盖解码、张量、推理和编码，排队请求不会先分配图像。多个实例有独立模型内存，局部图片预算不能替代宿主的总内存与并发控制。

流 I/O、队列和像素处理支持取消；同步 ONNX 执行只在前后检查，取消后仍可能等待当前运行返回，不能先释放正在使用的张量。异步释放停止接收新操作，等待活跃操作，并让排队调用观察已释放状态；重复释放安全。

输入、蒙版和输出由调用方持有。native 图像、张量、结果和会话按所有权释放；失败可能已写出部分 PNG，宿主负责暂存与发布。

## 验证与质量证据

常规测试覆盖二值蒙版、三像素邻域、边缘、空蒙版、RGB 平面、alpha、合成及模型摘要失败。实际模型用例显式设置本地文件后运行：

```powershell
$env:MS_MEDIA_MIGAN_MODEL = (Resolve-Path './models/migan_pipeline_v2.onnx').Path
dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Inference.Tests/MS.Microservice.Media.Inference.Tests.csproj
```

未配置时真实模型用例明确跳过，不下载或伪造推理。实际验证包含不同分辨率、最小尺寸、半透明、EXIF 坐标、蒙版外像素与活跃／排队释放，见[MiganInpainterTests](../test/MS.Microservice.Media.Inference.Tests/MiganInpainterTests.cs)。

已有 CPU 实验中，中强度合成文字水印可明显减弱，边缘仍可能出现痕迹，弱水印也可能变差；这些不是自然照片准确率评估。错误蒙版会损伤正常内容，模型也不能恢复不可见的真实背景。逐帧调用图片模型不构成视频时序质量证据。
