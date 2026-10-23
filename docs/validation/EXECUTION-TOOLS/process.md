# Slice 1：ProcessRunner
基线：6e99dd4b703a194d7480c5e7b771b20cffabd653；实现与验收完成。
范围：Processes 类库、媒体内部适配器与引用、ProcessFixture、Processes.Tests、根解决方案及状态文档。
验收命令：
- dotnet test test/MS.Microservice.Processes.Tests -c Release -p:RestoreSources="$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false --verbosity quiet
- dotnet build MS.Microservice.Media/src/MS.Microservice.Media.Inference -c Release -p:RestoreSources="$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false --verbosity quiet
结果：8 passed / 0 failed / 0 skipped；build 0 warnings / 0 errors。
输出：[测试](process-tests.txt)、[构建](media-build.txt)。在线恢复因 NuGet TLS/访问失败；离线验证不含漏洞审计。未验证真实 FFmpeg/Python 工作负载及跨平台进程树行为。
