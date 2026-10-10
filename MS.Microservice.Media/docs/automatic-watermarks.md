# 自动水印检测与修复编排

使用、安装、模型清单和参数见 [Inference README](../src/MS.Microservice.Media.Inference/README.md)。`WatermarkRemover` 将本地 repeated-text 分割器和 MI-GAN 组合，自动与手动模式共用修复及结果校验，不在检测失败时隐式转为手动。

## 输入快照与坐标

每实例的单并发范围包括原始流快照、Python 检测、MI-GAN、输出蒙版和工作目录清理。先按编码限额保存原始字节，不提前重编码为 PNG，防止高度压缩输入因中间格式膨胀而改变输入预算的含义。

Python 用 Pillow EXIF 变换产生规范化坐标的检测蒙版，.NET 继续以 `ImageCodec` 解码同一原始字节。修复后核对检测尺寸与选中数，避免两个后端的方向或蒙版协议不一致。自动蒙版为二值 PNG；手动模式直接保存并返回原蒙版编码和自身方向元数据，不声称原文件已重编码规范化。

## 检测模型协议

worker、依赖文件和 manifest 随构建／发布复制到 `media-inference`，脚本固定从 `AppContext.BaseDirectory` 下该目录启动。可执行路径是可信宿主配置，不能让请求指定 Python 或任意脚本。

权重先读为有大小限制的快照并验证固定摘要，再调用 `torch.load(BytesIO(snapshot), weights_only=True)`。创建严格的 `UnetPlusPlus/efficientnet-b4`，加载全部 key，不执行远程代码或下载 encoder 权重。worker 校验 `segmentation-models-pytorch`／`timm` 的固定版本，记录其他实际运行库版本。

默认 `RgbZeroToOne` 把规范化 RGB 双线性缩为 512×512，再除以 255。显式 `ImageNet` profile 使用 mean `[0.485,0.456,0.406]`、std `[0.229,0.224,0.225]`。logits 经 sigmoid，双线性还原到源尺寸，再按阈值产生二值选区。没有按角落、字幕位置、文字内容、连通域大小或测试样本追加过滤。

发布者未公开完整训练预处理和可靠验证指标，当前 profile 是可重复执行的协议，不是最佳归一化结论。`MaximumProbability`／`MeanProbability` 是未校准输出，不是质量分数。元数据记录模型版本、摘要、profile、阈值、尺寸、选中／改动数、推理时间及含冷启动的 worker 时间，不构造 IoU 或未测量的效果分数。

## 误检与调整

自动结果固定建议 review。已知合成负样本中，默认 RGB profile 会把普通字幕选中；只提高阈值或排除某个位置，不能由一个样本推断通用修复。保留原图、检测蒙版和结果，让应用支持删除误选和补选笔画，再传人工蒙版重跑。

当前验证只覆盖 repeated-text 检测器，不代表同一发布者其余五种模型或视频检测均已接入。无水印负样本和误检样本都应成为可见测试结果，不能只保留成功演示。

## 资源、失败与冷启动

图片与蒙版分别受修复组件的编码、单边和像素限制。很低的编码预算可能容纳源图却容纳不了生成蒙版，此时明确失败；输出 PNG 容量与发布策略由宿主承担。实例串行不等于固定内存配额，ONNX 会话、Python/Torch 和当前图像均占资源。

每次自动调用新建一个 CPU worker。已记录的四线程调用中，模型推理约 1.27 秒，包含冷启动、加载和推理的 worker 合计约 17.30 秒；较早常驻 proof 的约 0.72 秒单次推理不包含同样开销，不能相互替代作为请求延迟。这些是已记录环境的局部数据，不是吞吐保证。

进程超时、取消、诊断限额和进程树终止复用[视频进程管理](video.md)。工作区是可信暂存根目录下经过绝对路径校验的独占随机子目录，POSIX 仅当前用户可访问。正常及失败都会尝试删除，仅删除本调用拥有的目录；原处理已失败时不让清理错误覆盖它，独立清理失败则正常传播。

四个流必须互不相同，从当前位置读写并保持打开。结果校验可能发生在修复 PNG 已写出之后，两份输出不是事务。实例释放等待活跃调用完成，排队取消不读取输入，重复释放安全；ONNX 仍须等待当前同步执行结束。

## 集成入口与回归

```powershell
$env:MS_MEDIA_MIGAN_MODEL = 'absolute/path/migan_pipeline_v2.onnx'
$env:MS_MEDIA_WATERMARK_PYTHON = 'absolute/path/media-inference-env/Scripts/python.exe'
$env:MS_MEDIA_WATERMARK_MODEL = 'absolute/path/segmenter_repeated_text.pth'
dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Inference.Tests/MS.Microservice.Media.Inference.Tests.csproj
```

[WatermarkRemovalTests](../test/MS.Microservice.Media.Inference.Tests/WatermarkRemoval/WatermarkRemovalTests.cs) 运行真实 Python→蒙版→MI-GAN，并覆盖重复文字、无水印、可见字幕误检、EXIF、精确字节预算、错误摘要、worker 超时／取消、调用方流与独占目录清理。常规测试不下载权重。合成控制样本不提供自然照片准确率或连续帧质量结论。
