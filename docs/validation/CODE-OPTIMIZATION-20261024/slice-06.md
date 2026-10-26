# Slice 06：公共 API 与包消费者校验

Status: complete

范围：build/api-baseline.json、API验证/自检脚本、现有模块消费脚本与明确消费者文件、必要module-tools辅助；.github/workflows/dotnet-ci.yml；docs/components相关说明；任务状态/证据。运行输出在本次独立临时目录及artifacts/validation，生成包和源码快照不提交。

缺口：既有当前版本导出/类型加载不能证明相对历史API兼容。新门禁必须比较固定基线，缺基线明确失败，并能区分真正的破坏诊断与环境/编译错误。

方案：固定883b907c733d75093f79a0fa20639023ac3167fd为初始基线，SDK原生PackageValidation；当前工作区通过新的artifacts-path构建，包含未跟踪的新编译输入且不复用旧校验semaphore。包消费维持tracked导出契约，明确拒绝遗漏的未跟踪构建输入，必要时只暂存本片授权源码再消费。禁止自动更新基线或自动抑制兼容错误。

保护入口：Core、AI.Core、AspNetCore、Tasks.EFCore、EventSourcing、Audio、Logging.AspNetCore、Messaging.SelfManaged.EFCore、Threading、Caching、Http.Resilience及共享依赖闭包。

验收：API完整配置正向校验；独立微型fixture验证相同/新增成员通过、删除公开成员得到CP0002且非零退出；从生成NuGet包执行Threading/Caching/HTTP resilience和已有Messaging消费者。报告记录基线、当前HEAD/dirty、SDK、模块、包hash与结果。

结果：SDK 10.0.402，固定基线的11入口/16个包API比较全部通过，主脚本退出码0。自检baseline/unchanged/additive退出码0；removed明确CP0002、退出码1，自检总结果passed。配置与校验没有自动刷新基线或抑制诊断。

完整消费者脚本在最新代码上从头运行成功：5项目独立构建0警告0错误、5个本轮包、8个实际调用检查通过（Threading/Caching/HTTP各2，Messaging两个上下文）。独立csproj无ProjectReference。本轮包映射仅local；SQLite宿主显式复用既有Sqlite10.0.6与bundle3.0.2，未更改被测库依赖。

命令：
- `./build/validate-api-compatibility.ps1`
- `./build/validate-api-compatibility-self-test.ps1`
- `./build/validate-module-consumption.ps1 -Modules @('MS.Microservice.Threading','MS.Microservice.Caching','MS.Microservice.Http.Resilience','MS.Microservice.Messaging.SelfManaged.EFCore') -ReportPath artifacts/validation/module-consumption-slice06.json`

补充负证据：独立临时Git fixture中的未跟踪src/Fixture/Untracked.cs使消费脚本退出1、明确列路径，且导出未启动；没有操作真实仓库index。此分支后续未变，复用该结果。

[持久结果摘要](slice-06-results.json)保留每包hash、自检退出码和消费检查。已重新核对37个API/消费包文件hash、当前消费脚本hash、removed日志的CP0002及消费者无项目引用。详细运行报告位于artifacts/validation/api-compatibility.json、api-compatibility-self-test.json、module-consumption-slice06.json；临时包/快照保留供诊断，不提交。

验证过程中先发生沙箱NuGet连接失败；提升运行后主脚本又暴露裸-p参数绑定缺陷，修正引用后完整16包重跑通过。消费者最初独立宿主下载旧SQLite原生依赖停滞，显式采用仓库已有宿主pin后完成恢复，最后新脚本完整重跑通过；早期失败与手工恢复未冒充最终整体验收。

边界：已接入CI但未推送/运行远端CI；包消费者使用SQLite和本地HTTP handler，不替代真实Broker/PostgreSQL或所有运行行为验证。
