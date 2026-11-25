# Media 条码与抠图的整图缓冲验收

2026-11-24，在 Windows 10 x64、Intel Core i5-9400、.NET SDK 11.0.100、.NET 11.0.0 上，用同一份 Release 驱动分别调用 `30f704b` 和优化后的程序集。条码测量包含 PNG 解码、亮度转换、ZXing 识别和定位点映射。抠图测量从 `RemoveAsync` 进入，覆盖解码、推理、像素合成和两份 PNG 编码。[条码实现说明](../../../MS.Microservice.Media/docs/codes.md) · [抠图实现说明](../../../MS.Microservice.Media/docs/background-removal-design.md) · [基准源码](../../../MS.Microservice.Media/benchmarks/MS.Microservice.Media.Benchmarks/Program.cs)

## 彩色二维码

| 输入 | 旧 ms/次 | 新 ms/次 | 旧托管 B/次 | 新托管 B/次 |
| --- | ---: | ---: | ---: | ---: |
| 256×256 | 4.54 | 0.891 | 368400 | 171726 |
| 1280×720 | 60.3 | 11.5 | 3965336 | 1199996 |

每版三个进程，按旧新、新旧、旧新顺序运行。每进程预热一次，每个尺寸测七批，每批十六次识别，先取批次中位数再取三个进程的中位数。分配由 `GC.GetTotalAllocatedBytes(true)` 取整个进程的差值。新旧驱动均关闭分层编译。输入按指定尺寸生成一次，识别仅启用 QR 并关闭外层旋转和反色。默认多码制、EXIF 旋转及反色另由模块测试覆盖。

两版均识别出 `Media benchmark: color QR 42`。256×256 的四个点为 `(62,194) (62,62) (194,62) (176,176)`；1280×720 为 `(430.5,568.5) (430.5,150.5) (848.5,150.5) (791.5,511.5)`。后者每次少分配约 2.77 MB，与取消整帧 RGB24 的 2,764,800 字节有效载荷吻合。计时涵盖消除 RGB 写入再读取、将位图步长移出像素循环及提前释放位图的合计效果。

## BiRefNet 真实 CPU 推理

以 512×600 的 Grace Hopper 人像为输入，启用软蒙版。新旧版本各启动一个进程，每版建立一次双线程 CPU 会话，预热一次，再测三次完整调用，取中位数。输入内容相同，两条输出流在预热后复用容量；模型加载和输出哈希核对在计时外。

| 每次抠图并编码两份 PNG | 旧版 | 新版 |
| --- | ---: | ---: |
| 总耗时 | 15.2 s | 15.2 s |
| 托管分配 | 135053560 B | 130697536 B |

独立概率数组的有效载荷为 4,194,304 字节。完整调用的托管分配中位数相差 4,356,024 字节，还包含该进程其他线程及编解码的分配。在这次测量的精度下，端到端耗时相同。

新旧版本每次都得到 181,078 个非透明像素，平均 alpha 为 `0.5783146446078431`。跨版本逐项比对输出 SHA-256 也相同。

| 输出 | SHA-256 |
| --- | --- |
| 前景 PNG | `1D6EEDCAAD6A1080C5EFAAB7DE0C95A41B6CD2FD8F7FCB055FF989FD88CD7ABD` |
| 软蒙版 PNG | `1F1FE6C3D2A93162E546A3E7A9F2877D29CE7814F710B1014FD122016843B093` |

## 复现

在仓库根目录运行。模型参数指向仓库约定的本地 BiRefNet-lite 权重。

```powershell
$bench = 'MS.Microservice.Media/benchmarks/MS.Microservice.Media.Benchmarks'
$portrait = 'MS.Microservice.Media/test/MS.Microservice.Media.Inference.Tests/BackgroundRemoval/Fixtures/grace-hopper.jpg'
$model = '.tools/media-models/birefnet-lite.onnx'
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project $bench
dotnet run -c Release --no-build --project $bench -- --birefnet $model $portrait
```

旧版使用 `git archive 30f704b` 导出独立目录，将当前基准项目的 `.csproj` 和 `Program.cs` 复制到该目录的对应位置，用相同 SDK、模型和人像运行上述命令。模型和人像路径可传绝对路径。各组比较的是进程内托管分配，模型的原生激活、Skia 像素以及进程峰值工作集需另外测量。

## 回归

```powershell
$env:MS_MEDIA_BIREFNET_MODEL = (Resolve-Path '.tools/media-models/birefnet-lite.onnx').Path
$env:RUN_MEDIA_FFMPEG_TESTS = 'true'
dotnet test MS.Microservice.Media/MS.Microservice.Media.slnx -c Release -m:1
```

| Release 测试 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| MS.Microservice.Media.Tests | 254 | 0 | 0 |
| MS.Microservice.Media.Inference.Tests | 139 | 0 | 61 |

条码测试逐字节比较了两行带对齐填充的预乘 RGBA 像素和 ZXing 的 RGB24 参考结果，并复用了八种码制、EXIF 和旋转定位点用例。抠图测试覆盖极值、非有限值、错误形状和取消；真实模型还验证了人像、透明边缘、EXIF、软蒙版、排队取消与会话释放。其余 61 项是本次未启用的 MI-GAN、Swin2SR、OCR、表格及图片／视频去水印的可选集成用例。
