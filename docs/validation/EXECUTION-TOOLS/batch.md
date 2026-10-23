# Slice 2：BatchProcessing
基线：6e99dd4b703a194d7480c5e7b771b20cffabd653；第一片验收通过后开始，实现与验收完成。
范围：Threading 类库与测试、音频批量入口和项目引用、Audio README、根 README/解决方案、状态文档。
验收：dotnet test test/MS.Microservice.Threading.Tests -c Release --no-restore --verbosity quiet
依赖恢复使用本机缓存（RestoreSources=$env:USERPROFILE/.nuget/packages，NuGetAudit=false）。
结果：6 passed / 0 failed / 0 skipped，见 [原始输出](batch-tests.txt)。
覆盖：并发上限、输入顺序、逐项失败、取消等待在途且不派发后续项、空输入、非法并发、两个音频旧入口及失败优先于取消。
限制：音频兼容入口通过替身测试，未重新执行真实编解码工作负载。输入与结果在内存；取消依赖处理函数配合，旧音频转换不支持在途取消。

两片文件清单：
- Slice 1：src/MS.Microservice.Processes/、test/MS.Microservice.Processes.Tests/、test/ProcessFixture/、MS.Microservice.Media/src/MS.Microservice.Media/Video/MediaProcessRunner.cs、对应 Media.csproj。
- Slice 2：src/MS.Microservice.Threading/、test/MS.Microservice.Threading.Tests/、src/MS.Microservice.Audio/AudioProcessorExtensions.cs、Audio.csproj 与 README。
- 共享接入和记录：MS.Microservice.slnx、README.md、docs/status.md、docs/tasks/active.md、本目录。
