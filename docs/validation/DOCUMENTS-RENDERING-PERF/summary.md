# 文档处理的性能验收

2026 年 11 月 24 日，在 Windows 10 x64、Intel Core i5-9400、.NET SDK 11.0.100 上，用从 Git `f4c88f4` 单独构建的旧模块与本次修改作对照。以下 MB 采用十进制，MiB 表示 1048576 字节。[Documents 代码点评](../../../MS.Microservice.Documents/src/MS.Microservice.Documents/docs/performance-review.md)

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project benchmarks/MS.Microservice.Documents.Benchmarks
```

相同驱动分别加载新旧模块，以固定内容构造输入。每个场景预热一批，再测七批，取每个进程的中位数后再按版本取中位数。每批在计时外校验末次结果。`GC.GetTotalAllocatedBytes(true)` 的差值计入被测进程所有线程的托管分配；不同进程的分配不会自动相加。[基准入口](../../../benchmarks/MS.Microservice.Documents.Benchmarks/Program.cs) · [输入与校验](../../../benchmarks/MS.Microservice.Documents.Benchmarks/Fixtures.cs)

## Documents 公开入口

每版三个进程，按旧新、新旧、旧新交错。正文提取核对完整字符串；模板回读并核对控件内容及原有图片；PDF 按场景核对页数、页面尺寸、文本和嵌入图片的字节。12 个页眉场景还包含 11 个短节段落。

| 单次操作 | 旧 µs | 新 µs | 旧 B | 新 B |
| --- | ---: | ---: | ---: | ---: |
| 提取 6 个码元正文，无页眉 | 128.850 | 96.946 | 645917 | 317166 |
| 提取短正文及 12 个页眉 | 955.629 | 656.971 | 3120553 | 1716636 |
| 提取 851968 个码元正文 | 11147.100 | 9773.950 | 13468540 | 8895776 |
| 填充一个控件，小模板 | 2943.400 | 2882.988 | 1345366 | 1097400 |
| 填充一个控件，含 3670168 B JPEG | 18284.400 | 14235.800 | 24017912 | 16289432 |
| 填充一个控件，保留 851968 个码元正文 | 65612.300 | 61453.200 | 19969320 | 19713600 |
| 检查 859 B PDF | 121.517 | 105.479 | 152304 | 69392 |
| 同一 PDF，不可寻址，每次最多读 17 B | 116.877 | 114.500 | 153344 | 153344 |
| 从 859 B PDF 选页 | 249.028 | 235.991 | 223337 | 137903 |
| 检查 3671273 B PDF | 4389.262 | 2044.425 | 17910589 | 7422947 |
| 从 3671273 B PDF 选页 | 8642.875 | 5459.475 | 36340912 | 22180028 |

两个局部探针解释这些结果。对同一含约 1 MiB XML 的包，给 `WordPackage.CheckAsync` 的压缩输入流加字节计数，包大小 1002656 B，新旧分别读取 1006870 和 2009296 B。另在含 `hello` 和 524288 个尾部换行的 `StringBuilder` 上比较裁尾，在计时前建好分块。逐字符缩短再生成字符串耗时约 3735 µs，分配 6912192 B；逐块定位后生成结果约 76 µs、688 B。后者的选择依据是跨块长尾情况，普通正文的整体效果以上表公开入口为准。

## 回归测试

| Release 命令 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| `dotnet test MS.Microservice.Documents/test/MS.Microservice.Documents.Tests -c Release` | 126 | 0 | 0 |
