# Slice 11：架构断言检查Reference宿主

Status: complete

范围：ArchitectureTests.ProductionWeb_ShouldNot_DependOn_LabHost；Media/Messaging/Persistence三个模块.slnx依赖清单；状态/证据。范围外：生产项目引用、其余边界规则与测试重写。

根因：名为ProductionWeb的检查实际获取Lab.AccountController程序集，检查Lab是否引用自身，不能验证Reference边界。将typeof目标改为ReferenceHost，断言保留。既有ComponentBoundaryTests对Reference递归依赖图已有效，此片修正错误目标，不声称此前没有边界验证。

验收：`dotnet test test/MS.Microservice.Architecture.Tests/MS.Microservice.Architecture.Tests.csproj --no-restore -c Release`。

首次完整验收：39通过、3失败、0跳过。三项失败均为ModuleSolutionContainsOwnedProjectsAndTheirCompleteDependencyGraph；Media缺Processes，Messaging与Persistence缺Threading。逐个读取当前及基线883b907的.slnx和其包含项目的ProjectReference，确认两者缺项相同；本次没有更改这些生产ProjectReference。

范围调整（编辑清单前声明）：把上述三个.slnx的Dependencies补齐纳入本片，修复同一架构验收发现的依赖图不完整，不改断言或降低完整验收。只增加已存在的依赖项目成员，不引入新的生产依赖。

最终结果：同一完整验收命令（追加--verbosity quiet减少输出）42/42通过，0失败0跳过，退出码0；git diff --check通过。三个模块Dependencies现包含各自已引用的项目，现有检查逐项目验证完整闭包。不修改包源码，复用前片API/消费者证据。
