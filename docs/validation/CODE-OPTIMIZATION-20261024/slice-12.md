# Slice 12：重试耗时单调计时

Status: complete

范围：Core RetryContext、Core.Tests对应时钟回归、Resilience README、状态/证据。范围外：Executor、策略/条件、尝试次数/等待、HTTP/AI重试及新时钟模块。

根因：TotalElapsed直接DateTime.UtcNow减StartTime，墙钟校正影响TimeoutRetryStrategy的持续时间判断。不变量：经过时间只由单调timestamp决定；StartTime继续提供创建时的UTC诊断时间。

方案：公开无参构造与原属性保持；内部TimeProvider构造供现有Core.Tests友元使用；保存起始timestamp并调用GetElapsedTime。不把WithTimeout改为硬截止或单次操作取消。

验收：`dotnet test test/MS.Microservice.Core.Tests/MS.Microservice.Core.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~Retry' --verbosity quiet`；`./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.Core') -ReportPath artifacts/validation/api-compatibility-slice12.json`。独立推动UTC前后跳变及timestamp，断言持续时间与1秒策略边界；既有长延迟取消/兼容用例同跑。

结果：Retry筛选完整87/87通过，0失败0跳过、退出码0，包含既有85项与新增UTC跳变2项；Core/Domain.Primitives/Threading三个包API基线比较通过，报告artifacts/validation/api-compatibility-slice12.json，退出码0。git diff --check通过。

测试TimeProvider分别独立提供UTC和1000Hz timestamp，覆盖UTC先前跳或先后跳、999毫秒与一秒边界；StartTime仍是初始UTC。既有Retry/F/长延迟取消测试同次执行。未修改系统时钟，不把无法注入旧实现的新增内部构造编译失败当红测。

公开无参构造、属性与内部setter保留；新增clock入口仅internal，不增加外部配置或依赖。未改变Slice06四入口消费者的受测输入，复用其证据。
