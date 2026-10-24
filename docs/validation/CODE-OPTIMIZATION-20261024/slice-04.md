# Slice 04：音频资源所有权

Status: complete

范围：src/MS.Microservice.Audio/AudioProcessor.cs、AudioProcessorExtensions.cs、相关Audio测试与必要README；任务状态/证据。范围外：编解码、取消API、输出覆盖策略、文件发布机制。

根因：转换文件reader经IWaveProvider返回后缺释放；混音和文件转流在try外批量打开，后续失败使前面的资源逃离finally。不变量：本方法取得的输入资源，在成功、处理失败、部分取得失败时都确定性释放；返回的结果流仍归调用方。

验收：`dotnet test test/MS.Microservice.Audio.Tests/MS.Microservice.Audio.Tests.csproj --no-restore -c Release`。同格式WAV成功、输出锁冲突、第二输入锁冲突/缺失后立即FileShare.None重开首输入，不依靠GC或忽略清理错误。

结果：新增4项资源回归在旧实现全部失败，均由操作后立即FileShare.None重开输入触发IOException；未调用GC或吞掉清理错误。修复后完整Audio项目源码重建92/92通过、0失败、0跳过，退出码0。

复核：双参私有工厂返回WaveStream使using静态表达所有权；混音和文件转流逐项获取进入原有finally保护。返回结果流仍由调用方释放，公开API和编解码逻辑保持。git diff --check通过。

红测进程结束后只删除已记录的4个GUID临时目录；绿测自动清理，任务临时目录剩余0。当前验证平台Windows；不据此承诺其他平台原生编解码兼容。
