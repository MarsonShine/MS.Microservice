# 格式探测的栈缓冲与已有基准

`AudioFileFormatDetector.DetectFormatFromStream` 从流开头检查 WAV、ID3，再在前 1024 字节查找 MP3 帧头，最后检查其他文件头；未知类型返回 `Auto`。调用要求流支持长度和定位，结束时恢复原位置；文件头命中不表示媒体可完整解码。

这些工作区有固定上限，使用局部 `Span<byte>` 栈缓冲。原实现按路径创建 12、10、4 字节数组，未提前识别时还创建最多 1024 字节扫描数组。扫描长度先取 `Math.Min(1024L, stream.Length)` 再转成 int，避免对超过 2 GiB 的流先转换长度而溢出。探测顺序和原位置恢复规则保留，没有跨调用的共享游标。

## 已记录的测量

[基准程序](../../../benchmarks/MS.Microservice.Audio.Benchmarks/Program.cs)对预先创建的 `MemoryStream` 运行五轮，每轮 100,000 次，记录中位数。环境为 Windows x64、.NET 10.0.12、Release；优化前源码为提交 `0c4b8ca`。

| 输入 | 修改前 | 修改后 | 分配修改前 | 分配修改后 |
| --- | ---: | ---: | ---: | ---: |
| WAV 头 | 31.3 ns/次 | 28.2 ns/次 | 40 B/次 | 0 B/次 |
| ID3 MP3 头 | 49.0 ns/次 | 35.1 ns/次 | 80 B/次 | 0 B/次 |
| 未知格式，扫描 1024 字节 | 570.0 ns/次 | 505.3 ns/次 | 1160 B/次 | 0 B/次 |

数据只描述当时的内存格式探测，不代表真实文件 I/O 或完整音频处理吞吐。可用仓库基准程序重新测量：

```powershell
dotnet run --project benchmarks/MS.Microservice.Audio.Benchmarks/MS.Microservice.Audio.Benchmarks.csproj -c Release
```

后续性能修改应保留短文件、未知格式、MP3 扫描边界及流位置恢复测试，并先区分探测、I/O、解码和重采样的耗时。静音写入的另一份局部测量见[静音缓冲设计](silence-buffer-design.md)。
