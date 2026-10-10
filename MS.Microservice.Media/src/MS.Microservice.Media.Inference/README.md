# 本地媒体推理工具

引用可选项目 `MS.Microservice.Media.Inference`，可自动检测并修复图片／视频水印、手动修复指定区域、识别文字与表格、抠图，以及进行 2 倍图片增强。基础图片编辑、加水印和视频工具见[Media README](../../README.md)。

## 自动检测并修复

先准备下文的本地 Python 环境和两个固定模型。将 `pythonPath`、`detectorModelPath`、`repairModelPath` 替换为可信宿主配置，Python 路径必须为已安装可执行文件的绝对路径。

```csharp
using MS.Microservice.Media.Inference;
using MS.Microservice.Media.Inference.WatermarkRemoval;

await using var remover = await WatermarkRemover.CreateAsync(new WatermarkRemovalOptions
{
    Repair = new MiganInpaintingOptions { ModelPath = repairModelPath },
    Detector = new PythonWatermarkDetectorOptions
    {
        PythonExecutablePath = pythonPath,
        ModelPath = detectorModelPath
    }
});
await using var original = File.OpenRead("original.jpg");
await using var imageOutput = File.Open("result.png", FileMode.CreateNew, FileAccess.Write);
await using var maskOutput = File.Open("detected-mask.png", FileMode.CreateNew, FileAccess.Write);
var result = await remover.RemoveAsync(original, imageOutput, maskOutput,
    cancellationToken: cancellationToken);
```

`RemoveAsync` 默认 `Automatic`；未配置检测器时明确报错，不隐式改成手动模式。当前模型针对重复文字水印，普通文字、字幕和场景 Logo 也可能被选中。结果的 `ReviewRecommended` 固定为 true：应展示原图、蒙版和结果，让用户删去误选或补选遗漏。概率字段是未校准的模型分数，不能当成质量保证。

## 手动修复

只做手动处理时无需 Python。配置 `Repair` 后，给 `RemoveAsync` 传 `WatermarkRemovalMode.Manual` 和 `manualMask`；图片、两个输出和手动蒙版必须是不同的流。

```csharp
await using var inpainter = await MiganInpainter.CreateAsync(
    new MiganInpaintingOptions { ModelPath = repairModelPath });
await using var image = File.OpenRead("input.png");
await using var mask = File.OpenRead("mask.png");
await using var output = File.Open("repaired.png", FileMode.CreateNew, FileAccess.Write);
var result = await inpainter.InpaintAsync(image, mask, output, cancellationToken);
```

蒙版须是严格不透明的二值灰度图：**白色编辑，黑色保留**，尺寸与各自 EXIF 校正后的图片一致。中间灰度、颜色、透明或尺寸不符均报错，不自动缩放或猜阈值。修复输出固定 PNG，保留规范化解码基准中的蒙版外像素和全部 alpha。非空修复要求宽高至少为 3；空蒙版直接输出规范化身份 PNG。

自动模式返回二值 PNG 蒙版；`RemoveAsync` 手动模式返回蒙版的原编码和方向元数据，不能一律声明为 PNG。要继续裁剪、压缩或转格式，再调用基础 `ImageProcessor`。

## 自动视频去水印

```csharp
using MS.Microservice.Media.Inference.VideoWatermarkRemoval;

await using var videoRemover = new VideoWatermarkRemover(new VideoWatermarkRemovalOptions
{
    PythonExecutablePath = pythonPath,
    MiganModelPath = repairModelPath,
    Detector = new VideoWatermarkDetectorOptions { ModelPath = detectorModelPath }
});
var videoResult = await videoRemover.RemoveAsync("input.mp4", "new-auto-result-directory", cancellationToken);
```

不传手动请求时默认自动检测，必须配置 `Detector`。检测器与图片自动模式共用固定 repeated-text 权重，`Normalization` 默认 `RgbZeroToOne`、`Threshold` 默认 0.5；CPU、进程超时和视频预算使用下表配置。除 `video-requirements.txt` 外，还需部署图片检测的 Torch、SMP／timm 依赖。

模型可能漏选水印，也会误删普通字幕、文字和场景 Logo。`ReviewRecommended` 始终为 true，应保留原片、检查蒙版和结果，再用手动入口纠正。输出为下述 FFV1 MKV 预览包；资源实测、分数含义和真实负样本见 [自动视频检测设计](../../docs/automatic-video-watermarks.md)。

## 手动视频区域修复

```csharp
using MS.Microservice.Media.Inference.VideoWatermarkRemoval;

await using var videoRemover = new VideoWatermarkRemover(new VideoWatermarkRemovalOptions
{
    PythonExecutablePath = pythonPath,
    MiganModelPath = repairModelPath
});
var videoResult = await videoRemover.RemoveAsync("input.mp4", "new-result-directory",
    new VideoWatermarkRemovalRequest { StaticMaskPath = "mask.png" }, cancellationToken);
```

提前安装 `media-inference/video-requirements.txt` 中的 Python 依赖并准备同一个 MI-GAN 模型。此入口需要 PyAV、OpenCV、Python ONNX Runtime 和 FFmpeg；只做手动图片修复时仍无需 Python。

| 参数组 | 默认值／要求 |
| --- | --- |
| `StaticMaskPath` / `RectangleKeyframes` | 二选一；静态二值 PNG，或从零开始、同索引线性插值的矩形关键帧 |
| 坐标 | 视频显示方向校正后的像素，矩形须完整位于画面内 |
| `FfmpegExecutablePath` / `CpuThreads` / `ProcessTimeout` | `ffmpeg` / 最多默认 4 线程 / 每个进程 10 分钟 |
| `Limits.MaximumInputBytes` / `MaximumOutputBytes` | 每份输入 256 MiB / 完整结果包 512 MiB |
| `MaximumFrames` / `MaximumDurationSeconds` | 300 帧 / 120 秒 |
| `MaximumFramePixels` / `MaximumFrameDimension` / `MaximumTotalPixels` | 1920×1080 / 8192 / 256M |
| `MaximumAudioTracks` / `MaximumAudioSampleValues` | 8 条 / 50M 样本值 |
| `MaximumMaskKeyframes` / `MaximumRectanglesPerKeyframe` / `MaximumMaskRectangles` | 512 / 128 / 65,536，复制请求前检查 |
| `Flow` | 相邻帧光流可靠性门限；调整门限不保证质量提升 |

输出目录必须不存在。结果为 FFV1 MKV、蒙版预览视频、逐帧 JSON 和首帧原图／蒙版／结果 PNG；整包校验后发布，可再用 `VideoProcessor.ProcessAsync` 明确转为 MP4。该入口是图片模型加光流辅助，不能恢复被遮挡的真实内容，结果需要人工复核。

仅支持 RGB8 SDR、方形像素及标准直角旋转。保留 VFR 时间间隔，输出量化到 1 ms；负时间戳、碰撞、无法确定末帧长度或音轨存在时间跳变时会拒绝处理。音频转为 float32 PCM，保留实际样本和通道顺序，源扬声器角色另行记录，播放器不保证恢复角色。超过两声道且角色未知的音轨不支持。

宿主应限制同时运行及排队的任务数，保持输入文件稳定。取消或验证失败不发布结果；发布后的暂存清理错误可能在完整目录已存在时报告，应核对目标再处理。流程、时间线和质量边界见[视频区域修复设计](../../docs/video-watermark-removal.md)。

## 图片文字识别

提前安装发布目录 `media-inference/ocr-requirements.txt` 中的依赖，并将 [OCR 模型清单](worker/ocr-modelmanifest.json) 中的两个固定 ONNX 文件和字典放入同一可信目录。运行接口不下载模型；文件大小和 SHA-256 必须匹配清单。

```csharp
using MS.Microservice.Media.Inference.Ocr;

var ocr = new OcrProcessor(new OcrOptions
{
    PythonExecutablePath = pythonPath,
    ModelDirectory = modelDirectory,
    TemporaryDirectory = scratchDirectory
});
await using var document = File.OpenRead("document.png");
var recognized = await ocr.RecognizeAsync(document, cancellationToken);
foreach (var region in recognized.Regions)
    Console.WriteLine(region.Text);
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `PythonExecutablePath` / `ModelDirectory` | 必填 | 已部署 Python 的绝对路径及模型目录 |
| `TemporaryDirectory` | 系统临时目录 | 已存在、由宿主可信管理的暂存目录 |
| `Limits` | 16,384 单边 / 40M 像素 / 64 MiB 输入 | 静态图片的尺寸及编码大小上限 |
| `MaximumIntermediateBytes` | 128 MiB | 规范化 PNG 的独立大小上限 |
| `DetectionMaximumSide` / `MaximumRecognitionWidth` | 1280 / 2048 | 检测最长边及识别行宽度上限 |
| `MaximumRegions` / `MaximumTextCharacters` | 128 / 16,384 | 区域数和全部文字的 UTF-16 单元上限；超额报错 |
| `DetectionThreshold` / `BoxThreshold` / `MinimumConfidence` | 0.3 / 0.5 / 0.5 | 检测像素、文字框和识别结果的过滤门限 |
| `CpuThreads` / `Timeout` | `min(4, CPU数)` / 2 分钟 | 单次 CPU worker 的线程数及进程超时 |

返回 EXIF 方向校正后的尺寸、文字及四角坐标，角点顺序为左上、右上、右下、左下。适用于正常方向的横排中英文印刷体和适度倾斜文字；不提供表格结构、多栏阅读顺序、180°纠正或手写识别保证。分数未校准，空结果只表示当前配置未识别出文字，低对比度、小字或复杂背景可能漏检。

输入从当前位置读取并保持打开。每次调用启动独立进程和两份模型会话，同一实例允许并发，宿主须限制并行任务数。处理流程、模型许可和边界验证见 [OCR 设计](../../docs/ocr-design.md)。

## 带线框表格识别

复用 OCR 的 Python 环境与三个固定模型文件，无需另外部署表格模型。

```csharp
using MS.Microservice.Media.Inference.Tables;
using MS.Microservice.Media.Inference.Ocr;

var tables = new TableRecognizer(new TableRecognitionOptions
{
    Ocr = new OcrOptions
    {
        PythonExecutablePath = pythonPath,
        ModelDirectory = modelDirectory,
        TemporaryDirectory = scratchDirectory
    }
});
await using var tableImage = File.OpenRead("table.png");
var recognized = await tables.RecognizeAsync(tableImage, cancellationToken);
foreach (var table in recognized.Tables)
    foreach (var cell in table.Cells)
        Console.WriteLine($"({cell.Row},{cell.Column}) {cell.RowSpan}×{cell.ColumnSpan}: {cell.Text}");
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Ocr` | 必填 | 沿用 OCR 的本地模型、图片、推理、暂存和输出预算 |
| `MinimumLineLength` | 40 px | 最短线段及每个基础格横／纵间距下限，范围 8–512 |
| `LineTolerance` | 3 px | 线位置聚类容差，范围 1–8 |
| `MinimumBoundaryCoverage` | 0.9 | 单元格边界所需线覆盖率，范围 0.75–1 |
| `MaximumTables` | 8 | 表格数上限 |
| `MaximumRows` / `MaximumColumns` | 64 / 64 | 每表行列数上限 |
| `MaximumGridCells` | 512 | 全部表格合并前基础格总数；超额报错 |

支持轴对齐、闭合外框、至少两个基础格的直线网格，以及矩形合并单元格。默认每格横纵间距至少 40 px；降低线长可接收小格，也会增加文字或图形误检。孤立 1×1 框、无框表格、旋转和透视版面不在支持范围。完整缺失的内部边线会被解释为合并，完全消失的行列不能恢复；网格状字形也可能误识别，`ReviewRecommended` 始终为 true。

行列索引从零开始，坐标属于 EXIF 校正后的图片。`Cell.RegionIndices` 引用 `recognized.Ocr.Regions`；跨格或表外文字保留在 `UnassignedRegionIndices`。单元格文字按几何顺序换行，不承诺语义阅读顺序；擦除表格线也可能影响相邻文字识别。流保持打开，并发由宿主限制。算法、唯一归属和验证矩阵见 [表格识别设计](../../docs/table-recognition-design.md)。

## 抠图与换背景

提前部署 [BiRefNet FP32 模型清单](worker/background-removal-modelmanifest.json) 中的固定文件，约 224 MB；此入口使用 .NET CPU 推理，无需 Python。

```csharp
using MS.Microservice.Media.Inference.BackgroundRemoval;
using MS.Microservice.Media.Images;

await using var backgroundRemover = await BiRefNetBackgroundRemover.CreateAsync(new()
{
    ModelPath = backgroundModelPath
});
await using (var portrait = File.OpenRead("portrait.jpg"))
await using (var foreground = File.Open("cutout.png", FileMode.CreateNew, FileAccess.Write))
await using (var softMask = File.Open("foreground-mask.png", FileMode.CreateNew, FileAccess.Write))
{
    var cutoutResult = await backgroundRemover.RemoveAsync(portrait, foreground, softMask, cancellationToken);
}

await using var cutout = File.OpenRead("cutout.png");
await using var colored = File.Open("colored-background.png", FileMode.CreateNew, FileAccess.Write);
await new ImageProcessor().ProcessAsync(cutout, colored,
    new ImageProcessingOptions { BackgroundColor = new(240, 240, 255) }, cancellationToken);
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `ModelPath` | 必填 | 固定模型本地文件，大小与 SHA-256 须匹配清单 |
| `CpuThreads` | `min(2, CPU数)` | 每实例 CPU 线程数 |
| `Limits` | 4096 单边 / 4M 像素 / 32 MiB 输入 | 源静态图片预算，输出为 EXIF 校正后的相同尺寸 |
| `softMaskPng` | 不输出 | 可选不透明灰度 PNG，其灰度值等于结果的最终 alpha |

抠图输出透明 PNG，保留规范化 RGB，并把估计前景概率乘上源 alpha。软蒙版已经包含源 alpha，使用时不要再乘一次；白色为完全不透明，黑色为透明，中间值为软边缘。`ReviewRecommended` 始终为 true，复杂背景、发丝或原边缘颜色污染可能影响效果，组件不反演前景颜色。

**这是高内存模型**：本机 2 线程实测单任务／复用峰值约 6.1–6.4 GiB，会话创建约 10 秒、一次人像处理约 18 秒。宿主需另留内存余量并限制全局重任务并发；权重名称中的 lite、文件大小和小源图均不表示低运行内存，也不能把这些观测当作硬上限。

应复用并异步释放实例。流须不同且保持打开，PNG 编码容量由调用方管理；失败或取消可能留下部分输出，两份输出不构成事务，先写私有暂存结果再发布。预处理、alpha、取消和内存取舍见 [抠图设计](../../docs/background-removal-design.md)。

## 2 倍图片增强

本功能用已训练的 Swin2SR 模型将图片宽高各放大 2 倍，例如 640×480 变为 1280×960，像素数变为 4 倍。输出纹理由模型预测，不能保证还原原图缺失的真实细节。

提前部署 [Swin2SR 模型清单](worker/super-resolution-modelmanifest.json) 中的固定 ONNX 文件；此入口通过 ONNX Runtime 的 C# 接口在本机 CPU 上执行模型，无需 Python、GPU 或自行训练。模型须由宿主提前下载，运行接口不会下载。

```csharp
using MS.Microservice.Media.Inference.SuperResolution;

await using var upscaler = await Swin2SrUpscaler.CreateAsync(new Swin2SrOptions
{
    ModelPath = superResolutionModelPath
});
await using var lowResolution = File.OpenRead("small.png");
await using var enlarged = File.Open("enhanced.png", FileMode.CreateNew, FileAccess.Write);
var enhanced = await upscaler.UpscaleAsync(lowResolution, enlarged, cancellationToken);
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `ModelPath` | 必填 | 已部署的固定 2 倍 Swin2SR 模型，大小和哈希须匹配清单 |
| `CpuThreads` | `min(4, CPU数)` | 每实例 CPU 线程数 |
| `Limits` | 4096 单边 / 4M 像素 / 32 MiB 输入 | 静态图片输入预算 |
| `MaximumOutputDimension` / `MaximumOutputPixels` | 8192 / 16M | 方向校正后两倍画布的预算 |
| `TileSize` / `ContextPixels` | 128 / 16 | 核心块边长及邻域，均为 8 的倍数；较大值增加计算和内存 |

输出为宽高各两倍的 PNG，原图 alpha 用双线性插值放大。模型可能合成原图没有的纹理、扭曲文字或产生分块接缝，不能作为真实细节恢复；`ReviewRecommended` 始终为 true。半透明边缘可能受白底合成影响。

`CreateAsync` 加载模型并创建可复用会话，`UpscaleAsync` 使用该会话处理图片；应复用并异步释放实例，每实例串行处理，多实例并发由宿主控制。流从当前位置读写并保持打开，输出编码大小由调用方管理；失败或取消可能留下部分输出，先写暂存文件再发布。模型、张量等概念与代码的对应关系，以及分块、透明度和取消实现，见 [超分辨率设计](../../docs/super-resolution-design.md)。

## 图片处理配置

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Repair.ModelPath` | 必填 | 下文 MI-GAN 固定模型的本地文件 |
| `Repair.CpuThreads` | `min(4, CPU数)` | 每实例 ONNX CPU 线程数 |
| `Repair.Limits` | 单边 4096 / 4M 像素 / 每输入 32 MiB | 图片、蒙版分别校验；非进程内存硬配额 |
| `Detector.PythonExecutablePath` / `ModelPath` | 自动模式必填 | 可信 Python 绝对路径与固定分割权重 |
| `Detector.Normalization` | `RgbZeroToOne` | 可显式选 `ImageNet`，不同 profile 可能改变选区 |
| `Detector.Threshold` | 0.5 | 有限值且 `0 < Threshold < 1`，调高不保证消除误检 |
| `Detector.CpuThreads` | `min(4, CPU数)` | Python worker 线程数 |
| `Detector.Timeout` | 2 分钟 | 含冷启动、模型加载及检测的进程超时 |
| `TemporaryDirectory` | 系统临时目录 | 宿主选择的可信暂存目录 |

应复用并异步释放实例。每实例串行执行重操作，但每次自动调用仍启动独立 Python worker，有冷启动和加载开销；多实例各有模型内存，宿主应限制并发。运行期间不安装包、不下载权重、不上传图片。

## Python 与模型部署

worker、`requirements.txt`、`modelmanifest.json` 会复制到应用输出／发布目录的 `media-inference`。自动模式从这里启动固定脚本，不能以请求参数替换脚本或可执行文件。

Windows 新环境示例：

```powershell
python -m venv .tools/media-inference-env
.tools/media-inference-env/Scripts/python.exe -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cpu
.tools/media-inference-env/Scripts/python.exe -m pip install -r path/to/media-inference/requirements.txt
```

Linux/macOS 使用相应 venv 的 `bin/python`。锁定依赖为 `segmentation-models-pytorch==0.5.0`、`timm==1.0.22`；NumPy、Pillow、Torch、torchvision 须可用。已有兼容 CPU Torch 环境可建 `--system-site-packages` venv，再安装这两个包。已验证环境是 Python 3.12、Torch 2.13.0+cpu、torchvision 0.28.0+cpu、Pillow 12.3.0，其他组合需在目标环境运行集成测试。

部署时显式下载并校验固定模型，运行接口不会代为下载：

| 模型 | 固定下载 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| MI-GAN pipeline-v2 | [migan_pipeline_v2.onnx](https://huggingface.co/andraniksargsyan/migan/resolve/406830d0fa60666da0071c342ad2fbc8f30c5c64/migan_pipeline_v2.onnx) | 28,079,181 B | `6f1f3530a1a2324b19752018ce756088b07973cda8d7d890034ace5c8a48c40b` |
| repeated-text 分割器 | [segmenter_repeated_text.pth](https://huggingface.co/christophernavas/watermark-remover/resolve/509fb6833823f4e9c519f8e4f87a9053567c779c/segmenter_repeated_text.pth) | 84,036,934 B | `bfd26cffb00d49541438b66e5723edaa733b60e61b09bf6abca9a5ba1b738d0e` |

MI-GAN [代码](https://github.com/Picsart-AI-Research/MI-GAN/blob/2b793c5ece43f4253e32d4afc257120a5deed6f5/LICENSE)与[权重](https://github.com/Picsart-AI-Research/MI-GAN/blob/2b793c5ece43f4253e32d4afc257120a5deed6f5/LICENSE-WEIGHTS)为 MIT，检测权重的[模型卡](https://huggingface.co/christophernavas/watermark-remover/blob/509fb6833823f4e9c519f8e4f87a9053567c779c/README.md)声明 Apache-2.0；分发时保留相应通知。修复运行库为 ONNX Runtime CPU 1.31.0，需验证目标平台的原生运行库。

## 图片修复的结果和失败边界

修复生成合理背景，不能还原被遮挡的真实像素，边缘可能有痕迹，错误蒙版会损伤正常内容。图片入口的逐帧调用不保证视频连续性，视频应使用前述专用入口并检查结果。

输入输出从当前位置读写并保持打开，流对象不同也必须对应不同底层文件或资源；组件无法识别任意包装流的别名。失败、取消或结果校验错误可能留下部分输出，两份输出不构成事务；宿主应先写私有临时结果，全部成功后再发布。输入字节预算也适用于生成蒙版，过低的预算可能容纳压缩原图却容纳不了蒙版；输出 PNG 容量由调用方管理。

取消可终止 Python 进程树；同步 ONNX 推理仍可能要等当前调用返回。实例释放会等待活跃操作完成。协议、像素合成、已记录冷启动数据和测试入口见[自动检测设计](../../docs/automatic-watermarks.md)与[区域修复设计](../../docs/inpainting.md)。
