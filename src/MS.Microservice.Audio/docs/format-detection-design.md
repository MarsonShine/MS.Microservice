# 对照源码读懂格式探测与文件信息查询

## 已经读了 12 字节，为什么还要回到文件头？

从 `DetectFormatFromStream` 看起。WAV 的 `RIFF` 在字节 0～3，`WAVE` 在 8～11，所以第一次读取 12 字节。判 MP3 用的 `ID3` 则在 0～2，已经包含在手里这块数据中。[读文件头](../AudioFileFormatDetector.cs#L63) · [判断 ID3](../AudioFileFormatDetector.cs#L68)

下面只摘出从 WAV 未命中到判断 ID3 的相关语句。旧版判断 ID3 的三次字节比较位于 `IsMp3Format` 内。

```diff
-stream.Position = 0;
-if (IsMp3Format(stream))
+if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
     return AudioFormat.Mp3;
```

旧版的 `IsMp3Format` 先重新读 10 字节。ID3 也未命中时，再回到起点读至多 1024 字节，逐字节找 MP3 同步字。找不到时又回到起点读 4 字节，检查 FLAC、Ogg，两种标记和最后的默认分支都返回 `Auto`。表中为同一个 1024 字节内存流的实测，每次读取都能给足请求的长度。

| 输入 | 旧版依次读取 | 新版依次读取 | 返回 |
| --- | --- | --- | --- |
| RIFF/WAVE | 12 | 12 | `Wav` |
| ID3 | 12 + 10 | 12 | `Mp3` |
| 全零，未知头 | 12 + 10 + 1024 + 4 | 12 + 1012 | `Auto` |

这里少读了哪些字节，可以从 `HasMp3Frame` 的切片看出来。开头那 12 字节从已读的栈缓冲复制到 `probe[0..12]`，下一次读从流的位置 12 直接填 `probe[12..]`。这样还保留了一个容易漏掉的情况：假设下标 11 是 `FF`、12 是 `E2`，两个字节分别来自两次读取，拼起来正好符合现有的同步字规则。窗口最多 1024 字节，所以下标 1023 的 `FF` 不能借窗口外的字节命中。[拼接窗口](../AudioFileFormatDetector.cs#L85) · [跨边界和短读测试](../../../test/MS.Microservice.Audio.Tests/AudioFileFormatDetectorTests.cs)

```csharp
header.CopyTo(probe);
stream.ReadExactly(probe[header.Length..]);
```

找同步字时先用 `IndexOf(0xff)` 跳过首个候选之前的数据，再顺序检查后一个字节的高三位。全零输入可以一次排除。若输入是反复出现的 `FF 00`，每一个都是假候选，此时顺序检查避免反复启动搜索。换成别的协议也可以沿着这个例子检查两件事：两个字段是否来自已读过的前缀，以及一个合法标记能否跨过分块边界。[候选搜索](../AudioFileFormatDetector.cs#L88)

调用方仍需传可定位、有长度的流。即使一次 `Read` 只能提供三个字节，`ReadExactly` 也会继续读够所需长度。完成或读取失败后恢复调用者进入时的位置。同步字命中只负责选解码器，完整校验仍由解码阶段负责。

## 文件入口为什么还会分配内存？

`stackalloc` 省的是探测算法自己的托管数组。旧版 `DetectActualFormat` 先 `File.Exists`，再创建一个带默认 4 KiB 缓冲的 `FileStream`。这个流最多只用来探测前 1 KiB，随后就关闭。新版直接尝试打开文件，把流缓冲设为探测上限。只有打开失败才查询路径，以保留缺失路径与其他读写错误的区别。[打开文件](../AudioFileFormatDetector.cs#L33)

本机正常 WAV 文件探测的托管分配从 **4368 B 降至 1296 B**，差额 3072 B 与流缓冲缩小 3 KiB 对应。传现成 `MemoryStream` 的入口在修改前后都是 **0 B/次**。上表记录的是探测器向流读取的数据量，文件系统缓存和实际磁盘读取是另一层。[本轮耗时、分配和测量条件](../../../docs/validation/AUDIO-INSPECTION-PERF/summary.md)

## 查 WAV 大小时，哪个 Length 才是调用者要的？

看 `ValidateAudioFile` 验证一个存在的 WAV。旧版先查一次 `File.Exists`，再进入公开的 `GetAudioFileInfo`，它又查一次。读取器打开文件、取出格式后，还会通过 `new FileInfo(filePath).Length` 按路径查询文件大小。新版两个公开入口各自保留对缺失路径的处理，成功路径直接进入同一个私有方法。验证返回 `false`，而显式查询缺失文件会抛出 `FileNotFoundException`。[验证入口](../AudioProcessorExtensions.cs#L170) · [元数据入口](../AudioProcessorExtensions.cs#L192)

以下是 WAV 分支前后实际用到的语句。新版由调用方打开文件，把同一个流交给 WAV 或 MP3 读取器。

```diff
-var fileInfo = GetAudioFileInfo(filePath);
+var fileInfo = GetExistingAudioFileInfo(filePath);

-using var reader = new WaveFileReader(filePath);
+using var stream = File.OpenRead(filePath);
+return GetWavOrMp3Info(stream, format, filePath);

-FileSize = new FileInfo(filePath).Length
+FileSize = stream.Length
```

以基准的 1 秒 WAV 为例，8000 Hz、单声道、16 位 PCM 的音频数据是 `8000 × 1 × 2 = 16,000` 字节。这份 NAudio 生成的文件还有 46 字节文件头，`stream.Length` 实测为 16,046 字节。`reader.Length` 对 WAV 返回音频数据区长度，用它会把完整文件大小报小。采样率和时长仍取自解码器。这里值得复用的是已经打开的**原始文件流**，它同时对应同一份数据及其容器长度。[打开的流](../AudioProcessorExtensions.cs#L201) · [文件大小的来源](../AudioProcessorExtensions.cs#L240) · [文件与流的对照测试](../../../test/MS.Microservice.Audio.Tests/AudioResourceOwnershipTests.cs#L20)

文件入口仍根据扩展名选择读取器，大小写均可。未知扩展名保留 `AudioFileReader` 的按路径解码。流入口从内容探测格式，WAV／MP3 直接解码，未知格式会复制整个流到临时文件后再打开。把 `ValidateAudioFile` 换成只看 12 字节的探测会改变含义：一个只剩 `RIFF....WAVE` 的截断文件能命中文件头，却读不出有效音频信息。[未知流的临时文件](../AudioProcessorExtensions.cs#L267)

## 此前的栈缓冲测量

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

本轮 .NET 11 数据使用当时已改用栈缓冲的版本作基线。原始 .NET 10 结果用于保留数组改栈缓冲这一步的历史对照。
