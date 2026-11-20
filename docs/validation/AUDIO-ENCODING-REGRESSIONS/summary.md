# Audio 编码、静音和演示流回归验收

2026-11-20，Windows x64、.NET 11 Release。基线是包含[此前性能优化与四项复现](../AUDIO-INSPECTION-PERF/summary.md)的工作区。测试通过公开处理器入口生成文件，并使用读取器校验结果。

| 路径 | 修复前的证据 | 修复后的验收 |
| --- | --- | --- |
| 混音、单声道转立体声写 MP3 | 1 秒 PCM16 音调经两个默认入口读回约 2.038 秒。LAME 按 PCM16 解释浮点字节 | 写入器从实际样本提供方取得格式。两个默认入口均读回 1.0449 秒、44.1 kHz、双声道。声道转换指定 48 kHz 时实际重采样，读回 1.0320 秒。测试核对每个声道的 440 Hz 音调与振幅，时长容许编码补帧 |
| 立体声转换的直拷 | 浮点 WAV 被原样写为 `.mp3`，未知后缀也被放行。显式要求浮点 WAV 时，PCM16 输入仍被原样复制 | 跨容器输出可由 MP3 读取器解码。未知后缀抛出异常并且没有生成文件。PCM16 WAV 转浮点 WAV 保留帧数，原始格式确实匹配的浮点 WAV 则逐字节保留附加的 RIFF `JUNK` 块 |
| PCM8 合并静音 | 三种 PCM8 格式表示都在第一段之后写出 `0x00`，预期为 `0x80` | 每段追加 4097 个 `0x80`，覆盖一个完整缓冲和单字节尾块。普通 PCM8 的新增样本全部解码为零。显式及从 WAV 头读回的 WAVEFORMATEXTENSIBLE PCM8 均逐字节校验 |
| 无参流处理演示 | 第一次流合并后回卷输入，在 `FileStream.set_Position` 抛出 `ObjectDisposedException` | 每次流调用使用独立的只读流包装。真实演示生成全部六份 WAV，测试核对格式、样本顺序、尾部静音和长度 |

新增 15 个回归用例。针对四个问题的首批 9 例在旧实现上全部失败，随后补充的“立体声 PCM16 WAV 请求浮点 WAV”也在旧实现上失败。同格式 WAV 的附加块在修复前后均得以保留。兼容性检查还发现，始终重编码已经匹配的 MP3 会丢掉原标签。最终实现保留默认预设下符合直拷条件的真实 MP3，覆盖无前置标签及前置 2223 字节 ID3v2 的情况，均逐字节比较包含 ID3v1 尾标签的输出。显式更换 `INSANE` 预设时，编码后的音频帧达到 320 kbps；将 WAV 改名为 `.mp3` 则验证会真正编码成 MP3。

定向运行 15 项全部通过。完整验收命令 `dotnet test test/MS.Microservice.Audio.Tests -c Release` 通过 117 项，失败 0 项，跳过 0 项。MP3 回读验证运行于 Windows 的原生编解码环境。

用例见[编码与容器测试](../../../test/MS.Microservice.Audio.Tests/AudioProcessorTests.cs)、[PCM8 静音测试](../../../test/MS.Microservice.Audio.Tests/SilenceBufferTests.cs)和[演示回归测试](../../../test/MS.Microservice.Audio.Tests/AudioDemoTests.cs)。
