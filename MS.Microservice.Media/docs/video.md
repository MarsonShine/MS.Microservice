# 视频信息与进程生命周期

`VideoProcessor.ProbeAsync` 使用宿主安装的 ffprobe，读取本地文件中第一个非封面视频轨道的尺寸、编码、帧率、时长与音轨存在性。宽高考虑元数据中的 90/270 度旋转；未知时长和帧率保留为 null，不虚构零值。尺寸是旋转后的像素栅格尺寸，尚未乘以非正方形像素的 SAR。

`FfmpegOptions` 配置可信的 ffmpeg/ffprobe 可执行文件和单次进程超时，默认命令名及 30 分钟。可设 `Timeout.InfiniteTimeSpan` 取消时间限制。组件不下载或捆绑 FFmpeg；所选构建需提供实际使用的编解码器，分发条件取决于该构建。

启动使用 `ProcessStartInfo.ArgumentList`，不经过命令行 shell。标准输出和错误并行读取，各自限制 262,144 字符，避免死锁和无限诊断缓存。取消、超时或输出限额失败时终止本次子进程树并等待其退出。调用方取消保留取消异常，进程超时为 `TimeoutException`，非零退出为带退出码和受限诊断的 `MediaProcessException`。诊断可能包含文件路径，日志记录由宿主决定。

单元测试验证元数据边界。实际 FFmpeg 验证须显式设置 `RUN_MEDIA_FFMPEG_TESTS=true` 并安装相应工具；未启用时集成用例跳过，不能视为运行验证通过。
