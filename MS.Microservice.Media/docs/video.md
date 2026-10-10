# 视频探测、过滤与进程管理

调用、配置和输出格式见 [Media README](../README.md)。本页维护 `ProbeAsync`、编辑过滤链及所有视频入口共用的进程／发布边界。

## 探测模型

ffprobe 返回的第一条非封面视频轨作为主画面，保留未知时长和平均帧率为 null。帧率按分子／分母解析，不先截成整数。优先使用 side data 的 rotation，缺失时才读 rotate tag；90／270 度交换像素宽高，SAR 不折算进返回的栅格尺寸。

当前 `VideoInfo` 读取的是旋转标量，不验证完整 DISPLAYMATRIX，不能据此断言没有翻转、缩放或其他仿射变换。需要严格矩阵约束的上层流程应独立验证完整矩阵，不复用一个角度字段代替。

时长优先取容器，再回退视频轨；数值须有限、非负并可由 `TimeSpan` 表示。缺少有效视频轨或尺寸时报错，不用封面图片或虚构零值满足结果类型。

## 过滤链与编码

编辑顺序为 FFmpeg 自动应用输入方向 → 时间剪辑 → exact 裁剪 → 显式直角旋转 → 翻转 → 缩放 → 偶数补齐 → 编码。裁剪在方向校正后的栅格内检查，`exact=1` 防止奇数位置被悄悄舍弃。

Contain 适配边界框，Cover 放大后居中裁剪，Stretch 直接拉伸。无缩放时保留源 SAR；指定目标尺寸后设 `setsar=1`，视觉比例需由调用方选择合适目标。YUV420 要求偶数宽高，右侧／底部只补一像素，不裁原边缘；目标框及补齐尺寸均在编码前检查，结果探测后再核对预算。

MP4／MKV 使用 libx264、AAC，WebM 使用 libvpx-vp9、Opus。视频选择 `0:V:0`，音轨映射 `0:a?`；视频 `fps_mode=passthrough` 不主动转为 CFR，但重新编码和封装不保证原始像素、色彩采样或绝对时间戳不变。超过剩余时长的截取交给 FFmpeg，最终仍须能探测有效画面。

复杂过滤图可能把输入 DISPLAYMATRIX 继续传到输出。像素已完成自动方向变换后，过滤链统一删除该 side data 并清空 rotate tag，防止播放器再次旋转；升级或新增编码图不能只在普通编辑路径清理。

## 进程生命周期

`MediaProcessRunner` 使用 `ArgumentList` 与 `UseShellExecute=false`，参数不拼接成 shell。stdout／stderr 并行读取，各自最多 262,144 个字符；达到限额会终止操作，不能等待写满管道的子进程自行退出。

取消、超时或读取错误时终止本次进程树、等待退出并观察两个读取任务，保留原失败。调用方取消保留取消异常；只有自身 deadline 到期才转换为 `TimeoutException`。非零退出产生含退出码和受限 stderr 的 `MediaProcessException`，诊断可能包含路径，由宿主决定如何记录。

## 发布与回归

统一发布方法先在目标目录 `CreateNew` 创建独占暂存文件，编码／后处理和探测成功后再次检查取消，再不覆盖地移动到目标。处理期间目标被竞争者创建也不覆盖。仅清理本次文件；处理与清理同时失败时保留两者，不删除父目录。该规则依赖可信稳定目录，不覆盖崩溃后的保留期清理。

[VideoProbeTests](../test/MS.Microservice.Media.Tests/Video/VideoProbeTests.cs) 和 [VideoEditingTests](../test/MS.Microservice.Media.Tests/Video/VideoEditingTests.cs) 覆盖元数据、真实编码、旋转、VFR、预算及发布失败。集成用例需显式启用：

```powershell
$env:RUN_MEDIA_FFMPEG_TESTS = 'true'
dotnet test MS.Microservice.Media/test/MS.Microservice.Media.Tests/MS.Microservice.Media.Tests.csproj
```

未启用时真实工具用例跳过。所选 FFmpeg 构建须具备测试及调用使用的编码器；本组件不安装工具，分发条件随构建而异。
