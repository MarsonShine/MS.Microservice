# 图片自动去水印与手动调整

`WatermarkRemover.RemoveAsync` 默认执行真实的本地水印分割，再调用 MI-GAN 修复。调用方同时获得修复 PNG、检测蒙版和执行元数据，可以保留原图进行预览、调整蒙版并重新以手动模式执行。当前只支持图片和一个经过运行验证的 repeated-text 分割器，不表示已经完成其余五种检测器或视频时序修复。

```csharp
using MS.Microservice.Media.Inference;
using MS.Microservice.Media.Inference.WatermarkRemoval;

await using var remover = await WatermarkRemover.CreateAsync(new WatermarkRemovalOptions
{
    Repair = new MiganInpaintingOptions { ModelPath = configuredMiganModel },
    Detector = new PythonWatermarkDetectorOptions
    {
        PythonExecutablePath = configuredAbsolutePythonPath,
        ModelPath = configuredDetectorModel
    }
});
await using var original = File.OpenRead("original.jpg");
await using var resultPng = File.Create("result.png");
await using var editableMask = File.Create("mask.png");
var result = await remover.RemoveAsync(original, resultPng, editableMask, cancellationToken: cancellationToken);
```

未配置 `Detector` 时，默认自动调用抛出明确的配置错误，不使用占位检测、裁剪、模糊或隐式手动回退。只需要手动处理时可以不配置 Python：

```csharp
await using var remover = await WatermarkRemover.CreateAsync(new WatermarkRemovalOptions
{
    Repair = new MiganInpaintingOptions { ModelPath = configuredMiganModel }
});
var result = await remover.RemoveAsync(original, resultPng, returnedMask,
    WatermarkRemovalMode.Manual, manualMask, cancellationToken);
```

手动模式要求调用方提供严格不透明的二值灰度蒙版：白色编辑、黑色保留；尺寸与 EXIF 规范化后的图片一致。不启动 Python。输出蒙版保留用户提供的编码及自身方向元数据；不要把它一律标记为 PNG 或声称文件本身已经重新规范化。自动模式输出的是 EXIF 规范化坐标中的二值 PNG。修复图片始终输出 PNG。

## 部署 Python 环境与模型

Python 可执行文件是可信宿主配置，必须是已经安装的绝对路径，不能来自外部请求参数。worker、`requirements.txt` 和 `modelmanifest.json` 随项目引用及 NuGet 包复制到应用输出的 `media-inference` 目录，并复制到发布输出。默认从 `AppContext.BaseDirectory/media-inference/watermark_detector.py` 启动，不执行任意请求提供的脚本。

新环境可按下面的步骤准备；所有网络操作只发生在显式部署阶段：

```powershell
python -m venv .tools/media-inference-env
.tools/media-inference-env/Scripts/python.exe -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cpu
.tools/media-inference-env/Scripts/python.exe -m pip install -r path/to/media-inference/requirements.txt
```

使用已安装且兼容的 CPU Torch 环境时，也可以创建 `--system-site-packages` venv，再仅安装 `segmentation-models-pytorch==0.5.0`、`timm==1.0.22`。worker 对这两个版本进行检查；NumPy、Pillow、Torch 和 torchvision 必须已经可用。实际验证环境为 Python3.12、Torch2.13.0+cpu、torchvision0.28.0+cpu、Pillow12.3.0。其他版本应由宿主运行相同集成测试确认，执行元数据记录实际运行时版本。

分割模型固定为：

- [发布者模型卡](https://huggingface.co/christophernavas/watermark-remover/blob/509fb6833823f4e9c519f8e4f87a9053567c779c/README.md)，权重许可 Apache-2.0。
- revision `509fb6833823f4e9c519f8e4f87a9053567c779c`。
- `segmenter_repeated_text.pth`，84,036,934 字节。
- SHA-256 `bfd26cffb00d49541438b66e5723edaa733b60e61b09bf6abca9a5ba1b738d0e`。

可显式下载并校验：

```powershell
Invoke-WebRequest 'https://huggingface.co/christophernavas/watermark-remover/resolve/509fb6833823f4e9c519f8e4f87a9053567c779c/segmenter_repeated_text.pth' -OutFile 'segmenter_repeated_text.pth'
Get-FileHash -Algorithm SHA256 -LiteralPath 'segmenter_repeated_text.pth'
```

MI-GAN 修复模型的固定下载地址、大小与 SHA-256 见[本地区域修复](inpainting.md)。请求期间不安装包、不下载模型、不上传媒体、不调用付费服务。

worker 将分割权重读为限制大小的字节快照并校验长度与 SHA-256，再执行 `torch.load(BytesIO(snapshot), weights_only=True)`。严格创建 `UnetPlusPlus/efficientnet-b4` 并加载全部 key，不启用远程代码，也不下载额外 encoder 权重。

## 检测协议与结果含义

默认 profile 为 `RgbZeroToOne`：EXIF 规范化后的 RGB 双线性缩放为 512×512，再除以 255。logits 经 sigmoid 后双线性还原概率图，以固定默认阈值 0.5 产生编辑蒙版。`ImageNet` profile 可显式选择，使用 mean `[0.485,0.456,0.406]`、std `[0.229,0.224,0.225]`。阈值可由宿主明确配置为 0–1 之间的有限值；没有基于角落、字幕位置、文字内容、连通域大小或当前测试样例增加隐式过滤。

发布者未给出完整训练预处理和可靠验证指标。上述 profile 是已实际运行的显式协议，不是已证明的最佳归一化，也不保证选中内容都是水印。`MaximumProbability` 和 `MeanProbability` 是未经校准的模型输出，不能当作去水印质量分数或真实概率保证。结果同时提供模型版本、SHA-256、profile、阈值、尺寸、选中数、实际改动数、推理耗时及包含冷启动的 worker 耗时；不返回凭空构造的 IoU 或效果评分。

自动结果固定标记 `ReviewRecommended=true` 并提示正常文字、字幕和场景 Logo 可能被选中。已知合成负样本中，默认 RGB profile 将正常字幕误选为编辑区域；提高阈值或按位置排除并不能据一个样例证明通用修复。应展示原图、蒙版和结果，允许用户擦除误选区域或补选漏掉的笔画，再使用手动模式重跑。

## 文件、资源与取消

每实例单并发覆盖原始流快照、Python 检测、MI-GAN 修复及临时文件清理。输入只按配置编码上限流式保存原始字节，不先重新编码规范化 PNG，因此高度压缩的原图不会因中间 PNG 膨胀而突破输入预算。Python 用 Pillow 的 EXIF 变换生成规范化蒙版，MI-GAN 继续使用共享 `ImageCodec` 解码同一原始字节，并验证蒙版尺寸与选中数一致。

图片和手动／自动蒙版分别受 `Repair.Limits.MaximumEncodedBytes` 限制，像素数和单边尺寸使用相同 `Repair.Limits`。很低的自定义编码上限可能允许压缩原图，却不能容纳生成的高复杂度蒙版，此时明确失败。输出 PNG 不受输入编码字节上限限制；输出容量与发布策略由调用方管理。

每次自动调用启动一个本地 CPU worker，导入运行库、读取并加载模型后执行一次分割；没有常驻 HTTP 服务。4 线程实测神经网络推理约 1.27 秒，但该次 worker 的冷启动、加载和推理合计约 17.30 秒。这与之前常驻 proof 的约 0.72 秒单次推理不同。实例持有 MI-GAN 原生会话，worker 另有 Python/Torch 内存开销；并发实例数必须由宿主控制，不能把单并发误认为固定内存配额。

进程超时、取消、诊断上限和整个进程树终止复用基础组件的 `MediaProcessRunner`。临时目录是配置目录下经过绝对路径验证的独占随机子目录，POSIX 使用仅当前用户访问权限；正常完成和错误／取消均尝试删除，仅删除该调用拥有的目录。已有处理错误时清理失败不覆盖原异常，独立清理失败正常传播。

输入、手动蒙版和两个输出流必须互不相同，均从当前位置开始，组件不关闭调用方流、不修改原始内容。失败或取消可能产生部分输出，两份输出不构成事务；建议先写私有临时结果，全部成功后再发布。释放实例会停止接收新操作，等待活跃操作结束并释放内层 MI-GAN 会话；排队请求取消不会读取输入，重复异步释放安全。

## 实际集成测试

常规测试不下载权重。显式提供三个本地路径可启用真实 Python→蒙版→.NET MI-GAN 流程：

```powershell
$env:MS_MEDIA_MIGAN_MODEL = 'absolute/path/migan_pipeline_v2.onnx'
$env:MS_MEDIA_WATERMARK_PYTHON = 'absolute/path/media-inference-env/Scripts/python.exe'
$env:MS_MEDIA_WATERMARK_MODEL = 'absolute/path/segmenter_repeated_text.pth'
dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Inference.Tests/MS.Microservice.Media.Inference.Tests.csproj
```

测试包括合成重复文字、无水印场景、已知字幕误检的可见结果、EXIF 规范化与精确输入字节预算、错误模型摘要、实际 worker 超时／取消、调用方流所有权和独占临时目录清理。样本是合成控制，不是自然照片准确率评估；没有测试全部六种检测器，也没有视频连续帧质量结论。
