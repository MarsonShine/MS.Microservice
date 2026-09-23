# 音频格式探测的缓冲区

`AudioFileFormatDetector.DetectFormatFromStream` 读取流的开头，先检查 WAV 和 ID3，再在前 1024 字节内查找 MP3 帧头。文件校验、流源创建和音频流信息读取都会调用它。原实现按所走分支创建 12、10、4 字节数组；没有提前识别格式时还创建最多 1024 字节的扫描数组。单次文件处理的主要成本仍可能是 I/O 和编解码，此处只测量探测方法本身。

这些缓冲区大小固定或被 1024 字节限制，因此改为局部 `Span<byte>` 栈内存。探测顺序、帧头扫描范围及调用前后的流位置保持不变。扫描长度现在先取 `Math.Min(1024L, stream.Length)` 再转为 `int`；原先先转换长度，大于 2 GiB 的流可能溢出。该改动不使用反射、动态代码或共享缓冲区，也不引入跨调用状态。

使用 [基准程序](../../benchmarks/MS.Microservice.Audio.Benchmarks/Program.cs)对预先创建的 `MemoryStream` 运行五轮、每轮 100,000 次，取中位数。环境为 Windows x64、.NET 10.0.12、Release；优化前源码为提交 `0c4b8ca`。数据只代表内存流上的格式探测，不代表真实文件或完整音频处理吞吐。

| 输入 | 修改前 | 修改后 | 分配修改前 | 分配修改后 |
| --- | ---: | ---: | ---: | ---: |
| WAV 头 | 31.3 ns/次 | 28.2 ns/次 | 40 B/次 | 0 B/次 |
| ID3 MP3 头 | 49.0 ns/次 | 35.1 ns/次 | 80 B/次 | 0 B/次 |
| 未知格式，扫描 1024 字节 | 570.0 ns/次 | 505.3 ns/次 | 1160 B/次 | 0 B/次 |

可运行 `dotnet run --project benchmarks/MS.Microservice.Audio.Benchmarks/MS.Microservice.Audio.Benchmarks.csproj -c Release` 复测。后续若要优化整条音频处理路径，应先用代表性文件测量 I/O、重采样和编码开销；本次数据不足以支持改动那些部分。

合并音频时写入静音的内存问题与测量结果见[静音写入](silence-buffer-reuse.md)。
