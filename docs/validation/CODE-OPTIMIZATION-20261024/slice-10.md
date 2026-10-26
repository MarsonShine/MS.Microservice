# Slice 10：终态保留清理

Status: complete

范围：Tasks独立保留接口、EFCore存储/DI、Tasks.Tests与必要夹具、模块说明、状态/证据。范围外：schema/index、自动清理worker、ResultReference文件清理、Dashboard按钮、多实例执行。

根因/能力缺口：终态与attempt没有保留出口，持续增长；删除若未复核状态/version会误删刚Retry的任务。不变量：只清理指定scope/state且严格早于cutoff的终态，候选确定后仍须满足原版本条件；attempt由已有FK cascade随父行原子删除。

方案：新增可选IJobRetentionStore，不给IJobStore添加成员；独立fresh DbContext，沿用拒绝caller/ambient transaction的存储约束。每次take 1..1000候选，逐行条件删除，返回实际删除数；按不同终态调用以配置不同保留期。拒绝未来cutoff。物理删除结束该JobId幂等保留期，后续同ID新提交允许新payload，旧attempt token仍不得写入。

验收：`dotnet test MS.Microservice.Tasks/test/MS.Microservice.Tasks.Tests/MS.Microservice.Tasks.Tests.csproj --no-restore -c Release`；`./build/validate-api-compatibility.ps1 -Modules @('MS.Microservice.Tasks.EFCore') -ReportPath artifacts/validation/api-compatibility-slice10.json`。覆盖真实SQLite级联、Retry竞争、scope/终态/cutoff、批次、重新提交及事务隔离。

边界：take限制候选/父行删除数，不保证扫描、排序和cascade工作量上限；不在缺少真实负载与迁移归属时增加索引。取消可发生在部分行已提交之后。

结果：完整Tasks.Tests 29/29通过，0失败0跳过，退出码0（原20+新增9）。EFCore生产项目Release构建0警告0错误；Tasks及Tasks.EFCore两个包API基线比较通过，报告artifacts/validation/api-compatibility-slice10.json，退出码0。git diff --check通过。

证据：真实SQLite四终态/跨scope/strict UTC cutoff、自然null活跃项、take=2返回2/1/0；多attempt外键级联；DELETE前Retry获胜与UPDATE前删除获胜由DbCommandInterceptor握手固定顺序；同JobId新payload/attempt1，旧token完成/进度均失败；首DELETE提交后取消保留其余两项；caller脏业务实体未保存、显式/ambient事务被拒绝。新功能没有伪造旧实现红测。

复核：两次只读独立复核未发现具体缺陷，候选只投影Id/Version，逐DELETE重验完整eligible表达式。无IJobStore成员、模型或迁移变化。未在真实PostgreSQL/大规模数据验证执行计划；本片未改变Slice06四入口消费者的受测输入，复用其证据。
