# 对照 AudioProcessor 源码理解缓冲复用、短片段合并和混音写入

## 为什么同一个 CopyAudioData 循环可以少分配 127 个数组？

打开 `AudioProcessor.cs`，先找 `CombineAudioSourcesAsMp3`，再从它的 `foreach` 跟进 `CopyAudioData`。外层每次处理**一段音频**，内层的 `while` 每次搬运这段音频的**一块数据**。假设要合并两段音频，哪一行 `new` 会执行两次？[外层的分配](../AudioProcessor.cs#L357) · [传入数组的调用点](../AudioProcessor.cs#L369) · [内层的复制方法](../AudioProcessor.cs#L697)

### 把调用点和方法体放到一起

以下摘出基线 `4a735c7` 与当前源码中相关的行，省略每段读取器、重采样器和静音的处理。

```diff
 // CombineAudioSourcesAsMp3：一次合并进入这个方法一次
+var buffer = new byte[CopyBufferSize];              // 4096 字节
 foreach (var source in sources)
 {
     // ……为这一段创建 resampler……
-    CopyAudioDataToMp3(resampler, mp3Writer);
+    CopyAudioData(resampler, mp3Writer, buffer);
 }
```

```diff
-private static void CopyAudioDataToMp3(IWaveProvider source, LameMP3FileWriter destination)
+private static void CopyAudioData(IWaveProvider source, Stream destination, byte[]? buffer = null)
 {
-    var buffer = new byte[4096];
+    buffer ??= new byte[CopyBufferSize];
     int bytesRead;
     while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
     {
         destination.Write(buffer, 0, bytesRead);
     }
 }
```

旧版虽然把 `new` 写在 `while` 外面，每次从外层进入方法时仍会执行它。第一段音频即使很长，读了十块，也只创建数组 A。第一段复制完，方法返回。第二段再进来，又创建数组 B。

新版先在 `foreach` 前创建 A。C# 传递 `byte[]` 参数时复制的是引用，调用方和方法参数都指向 A。`buffer ??= new byte[CopyBufferSize]` 相当于 `if (buffer is null) buffer = new byte[CopyBufferSize];`。处理第一段、第二段时都传入了 A，两次都会跳过右侧的 `new`。

| 假设一次合并有两段 | 进入复制方法几次 | 第一次使用 | 第二次使用 | 创建几个读缓冲 |
| --- | ---: | --- | --- | ---: |
| 旧版 | 2 | 新建 A | 新建 B | 2 |
| 新版 | 2 | 调用方的 A | 调用方的 A | 1 |

这里有个可以对照的真实调用。单文件转换仍写着 `CopyAudioData(source, mp3Writer)`，省略了第三个参数，`??=` 就会创建数组。这个调用和旧版一样分配一次。把两个复制方法合并并改名，可以让 WAV 和 MP3 共用同一个读写循环。省掉重复分配，还需要把数组建在多次调用的外面，每次传入同一个数组。[单文件转换的调用点](../AudioProcessor.cs#L653)

**第二段为什么可以覆盖第一段？** 假设第一段最后一块有 300 字节，第二段只读到 100 字节。数组后面可能还有第一段留下的数据，但 `destination.Write(buffer, 0, bytesRead)` 这次只取前 100 字节。前一次同步写入已经返回，下一段读入时可以覆盖 A。若改成写 `buffer.Length`，就会把旧数据也写进输出。[实际写出长度](../AudioProcessor.cs#L704)

### 省掉的是哪笔开销

仓库基准把 128 段单声道 PCM16 拼成一个 WAV 文件，每段 127～254 帧，总音频数据只有 48,768 字节。旧版每段都创建 4096 字节的读缓冲，累计申请 `128 × 4096 = 524,288` 字节的数据区。新版申请一次 4096 字节，单算数组数据区少了 **520,192 字节**。这是整个操作期间累计创建的量。旧数组用完后就可以被 GC 回收，不能据此推断进程峰值也下降了 520,192 字节。[基准输入](../../../benchmarks/MS.Microservice.Audio.Benchmarks/ProcessingBenchmarks.cs#L28)

可以单独验证这个差别。在 .NET 11 x64 Release 下，用同一个 `CopyAudioData` 连续复制 128 段，每段 4 字节，预先为输出流留足容量。预热后用 `GC.GetAllocatedBytesForCurrentThread()` 测量整个循环，把外层创建数组也计入。每次传 `null` 得到 **527,360 B**，每次传同一个数组得到 **4,120 B**。恰好是 128 倍，两边输出的 512 字节完全相同。这里一个数组占 4,120 B，其中 4,096 B 是数据区，其余是这台运行时上的对象开销。

少建数组也省去了反复分配和初始化的工作，可能减轻 GC 压力。至于一次合并快了多少，还取决于读写和编码花的时间。该 WAV 基准的端到端耗时从 2.174 ms 降到 1.854 ms，同时还启用了下面的合批写入。这个数字只能说明两处改动一起运行时的效果。MP3 也复用了读缓冲，本次仅对其输出做了行为验证。[完整测量与验证](../../../docs/validation/AUDIO-PROCESSING-PERF/summary.md)

### 换成自己的代码，怎么判断能否这样写？

假设要统计每张答题卡上有几个 B。原本可以在每张卡的循环里 `new int[4]`，分别数 A、B、C、D。先追踪上一张卡的数据，看看输出究竟留下了什么。

```csharp
string[] papers = ["ABBA", "AAAA"];
var counts = new int[4];                 // 整批答题卡共用一个计数数组
foreach (var paper in papers)
{
    Array.Clear(counts);                 // 每张卡都从 0 开始计数
    foreach (var answer in paper)
        counts[answer - 'A']++;

    Console.WriteLine(counts[1]);        // 先输出 2，再输出 0
}
```

第一张卡输出整数 2 以后，下一张卡可以覆盖这四个计数。这和音频写完一段再读下一段是同一个理由。有一处细节不同：计数用 `++`，会读到上一轮留下的值，所以必须清零。音频的 `Read` 会填入新字节，只要 `Write` 用实际长度，就可以直接覆盖。

现在把需求改成“最后一起展示每张卡的四项计数”。如果把输出行换成 `results.Add(counts)`（`results` 是 `List<int[]>`），两条记录都会指向同一个数组，最终看到的都是第二张卡的 `[4, 0, 0, 0]`。此时每张卡需要自己的数组，或在保存时复制一份快照。并发处理也要看使用时间是否重叠：如果上一次异步写入还在读取数组，就要等它完成，或让并发任务各用自己的缓冲。

下次看到循环调用辅助方法，可以先顺着调用点找到 `new`，数一次业务操作会走到它几次。然后问自己，**下一轮覆盖这块内存时，还有谁需要上一轮的内容？** 如果只调用一次，把 `new` 移出去仍然分配一次。如果上一轮已经用完，再按照读写方式判断需要清零还是只需记录有效长度。像四个整数这样小的数组，是否值得改还要看真实调用量。

## 合并 WAV 时，读端已有 4 KiB，写端为什么还要一个缓冲？

先看 `CombineAudioSourcesAsWav` 中**输入格式已经匹配**的分支。对照基线 `4a735c7` 和当前代码，留意传给复制方法的第二个参数。两个版本都有 WAV 写入器，新版让变量 `destination` 决定谁先收到数据。[选择输出流](../AudioProcessor.cs#L390) · [把输出流传给复制方法](../AudioProcessor.cs#L410)

```diff
-using var waveWriter = CreateWaveWriter(output, targetFormat);
+var waveWriter = CreateWaveWriter(output, targetFormat);
+using Stream destination = output is string && sources.Length > 1
+    ? new BufferedStream(waveWriter, CopyBufferSize)
+    : waveWriter;
+var buffer = new byte[CopyBufferSize];

 foreach (var source in sources)
 {
     // ……创建 reader，此处展示同格式分支……
-    CopyAudioDataToWav(reader, waveWriter);
+    CopyAudioData(reader, destination, buffer);

     // ……配置了静音时……
-    WriteSilence(waveWriter, targetFormat, options.SilenceDuration);
+    WriteSilence(destination, targetFormat, options.SilenceDuration);
 }
```

跟进 `CopyAudioData`，能看到为什么单靠读缓冲凑不满 4 KiB。读到多少，下一行就立即交出去。第一段只有 300 字节时，`Read` 返回 300，`Write` 就收到 300。第一段结束后，下一段又从数组的下标 0 开始读。即使把数组加大到 64 KiB，这两行仍会把第一段的 300 字节单独交出去。[读写循环](../AudioProcessor.cs#L702)

```csharp
while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
{
    destination.Write(buffer, 0, bytesRead);
}
```

假设顺序合并 A、B、C 三段 PCM，分别是 300、500、200 字节，关闭静音。两种写法都用同一个 4096 字节读数组，执行相同的三次 `destination.Write`。下表只数**音频数据进入 `WaveFileWriter.Write`** 的次数。

| 此时写到哪里 | 读数组开头的有效内容 | 直接交给 WAV 写入器 | 先交给 `BufferedStream` |
| --- | --- | --- | --- |
| A 写完 | A 的 300 字节 | 收到 `Write(300)` | 暂存 A，共 300 字节 |
| B 写完 | B 的 500 字节 | 收到 `Write(500)` | 追加 B，共 800 字节 |
| C 写完 | C 的 200 字节 | 收到 `Write(200)` | 追加 C，共 1000 字节 |
| 关闭输出流 | 读数组已用完 | 合计收到 3 次 | 把 A、B、C 一次交出，收到 `Write(1000)` |

**两块内存在 B 到来时各有用处。** 读数组开头已经被 B 覆盖，写缓冲还留着 A，能把 B 接在 A 后面。这里用一块额外空间和短片段的拷贝，换取较少的 WAV 写入器调用。静音也写入同一个 `destination`，所以仍然排在对应音频片段后面。关闭输出流时会排出最后一批数据，随后由 WAV 写入器完成文件头。[写入静音的位置](../AudioProcessor.cs#L415)

**为什么包在写入器上面？** NAudio 2.2.1 的 `WaveFileWriter.Write` 会读取底层流的长度，检查加上这批数据后是否超过 WAV 大小上限，再写数据并更新已写字节数。把 3 次入口调用合成 1 次，这项检查也从 3 次减到 1 次。要是只在 WAV 写入器下面给文件流加缓冲，3 次检查在数据到达下层前就已经做完了。这里数的是库方法调用，操作系统怎样缓存、实际落盘几次是另一个观测点。[NAudio 的块写实现](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/Wave/WaveOutputs/WaveFileWriter.cs#L229)

上表用实际写入器验证过，两条路径产生的完整 WAV 逐字节相同。用仓库基准的 128 种短片段长度再验证，48,768 字节原先分 128 次进入写入器，加缓冲后是 12 次（11 × 4096，最后 3712）。实际代码只给**多个输入合并到 WAV 文件**包装这层缓冲。遇到类似代码，可以先找每条记录在哪一行提交，再找下游对每次提交做了什么。如果业务要求每条记录立刻可见，延迟到下一批才交出就会改变行为。

## 混音从 WriteSamples 换成 Write，省掉了哪一步？

从 `MixAudioFilesAsync` 看起。混音器已经生成按左、右声道交错排列的 IEEE 32 位浮点样本，写入器也使用这个格式。旧版 WAV 分支进入 `WriteMixedAudioToWav`，新版进入和声道转换共用的 `WriteSampleProviderToWav`。沿着样本从 `Read` 到 `Write` 的路径，可以把两处改动放在一起看。[混音器的输出格式](../AudioProcessor.cs#L211) · [混音的 WAV 分支](../AudioProcessor.cs#L549) · [单声道转立体声的调用点](../AudioProcessor.cs#L294)

```diff
 // 旧方法中的 mixer 和新方法中的 sampleProvider 在混音路径上指向同一个混音器
-var buffer = new float[options.BufferSize];
+var bytes = ArrayPool<byte>.Shared.Rent(byteCount);
+var samples = new WaveBuffer(bytes).FloatBuffer;

-while ((samplesRead = mixer.Read(buffer, 0, buffer.Length)) > 0)
+while ((samplesRead = sampleProvider.Read(samples, 0, samplesPerRead)) > 0)
 {
-    waveWriter.WriteSamples(buffer, 0, samplesRead);
+    if (BitConverter.IsLittleEndian)
+        waveWriter.Write(bytes, 0, samplesRead * sizeof(float));
+    else
+        waveWriter.WriteSamples(samples, 0, samplesRead);
 }
```

这里省略了容量计算和租还数组的 `try/finally`。`samplesPerRead` 的单位是样本，默认的 4096 个 `float` 占 **16 KiB**。这与上一节搬运 PCM 字节所用的 4 KiB 读数组属于不同路径。[租用字节数组](../AudioProcessor.cs#L677) · [清零归还](../AudioProcessor.cs#L693)

**旧代码一行调用，库内还有一层循环。** 在 NAudio 2.2.1 中，`WriteSamples(float[], …)` 遍历这批样本，对每一个调用 `WriteSample(float)`。后者根据目标位深和编码选择写法。在本项目的浮点 WAV 分支，每次都调用 `BinaryWriter.Write(float)` 写一个值，再把音频数据长度加 4。混音结果已经是浮点数，这条分支原样保存它。重复工作发生在逐样本判断格式、写单值和更新长度上。[NAudio 的样本循环](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/Wave/WaveOutputs/WaveFileWriter.cs#L282) · [浮点分支](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/Wave/WaveOutputs/WaveFileWriter.cs#L264)

**新版如何拿到可以直接写的字节？** `new WaveBuffer(bytes).FloatBuffer` 给同一块内存提供了浮点视图。混音器通过 `samples` 填入第一个结果 `1.5f` 时，在这台小端机器上，`bytes` 的前四个字节就已经是 `00 00 C0 3F`。这正是浮点 WAV 需要的字节排列，可以一次提交整批有效字节。块写仍会检查 WAV 长度并按本批字节数更新计数。样本相加、声道排列和 WAV 文件头都由原来的组件处理。[浮点视图](../AudioProcessor.cs#L681) · [按实际字节数写入](../AudioProcessor.cs#L686)

用回归测试的六个样本走一遍。三帧立体声为 `(1.5, -1.25)`、`(0.25, -0.125)`、`(-0.25, 0.5)`，每批最多读四个样本。两种写法都读取两批。[混音尾块测试](../../../test/MS.Microservice.Audio.Tests/AudioProcessorTests.cs#L214)

| 混音器本次返回 | 旧版到达底层流的音频写入 | 新版到达底层流的音频写入 |
| --- | --- | --- |
| 4 个样本 | 4 次，每次 4 字节 | 1 次，16 字节 |
| 2 个样本 | 2 次，每次 4 字节 | 1 次，8 字节 |

用实际 NAudio 写入器记录底层流的调用，音频数据区两边都是 24 字节，写完文件头的两个 WAV 也逐字节相同。第二批虽沿用同一数组，但只写 `samplesRead × 4 = 8` 字节。若误用数组容量，就会多写尾部的数据。

判断能否迁移这个做法，先核对**内存里的表示与目标格式是否一致**。例如目标改成 PCM16，写入器需要把浮点值换算成 2 字节整数，直接提交一个 `float` 的四个字节就会写错。项目也为大端机器保留了 `WriteSamples` 路径。

旧版的 `new float[options.BufferSize]` 在循环外，每次混音只分配一次。按块写省去逐样本输出，数组池则让预热后的调用可以复用这约 16 KiB 的工作空间。实测两路 WAV 混音从 41.005 ms 降到 17.564 ms，单声道转立体声从 21.581 ms 降到 11.744 ms，两者每次都少分配 16,376 字节。这些耗时覆盖完整调用。[完整数据与复测命令](../../../docs/validation/AUDIO-PROCESSING-PERF/summary.md)

五组基准在修改前后都产生了逐字节相同的输出。另一个候选改动也曾降低编辑器的分配量，但增益与淡变的端到端耗时没有稳定改善，最终保留了原实现。

沿其他入口看同类问题，可读[格式探测与文件大小](format-detection-design.md)和[编辑、静音、批量处理与转换边界](audio-api-walkthrough.md)。
