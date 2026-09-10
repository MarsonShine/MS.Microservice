# 实验二：事务与迁移

目标：区分保存、事务提交、消息派发和数据库结构迁移。
先完成[公共准备](README.md)。

## 看清事务边界

ProfileService 通过 IUnitOfWork.ExecuteAsync 组织一次业务操作。
SelfManaged 在同一个业务 DbContext 事务中提交档案与 Outbox；
消费端将业务效果、后续出站事件和 Inbox 完成标记一起提交。

以下本地测试在 SQLite 上验证“消费已暂存后续事件，但业务失败时全部回滚”：

    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release --filter FullyQualifiedName~ProbeLocalTests

检查 [ProbeHandler](../../MS.Microservice.Messaging/test/MS.Microservice.Messaging.IntegrationTests/ProbeHost.cs)：
处理器只修改实体和入队，不自行 SaveChanges 或 Commit。
失败分支已经执行过修改与 Enqueue，最终数据库仍不能留下这些部分结果。

## 导出当前迁移

默认导出不会连接业务数据库，也不会执行 DDL：

    dotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -c Release -- --provider SelfManaged --output artifacts/labs/migrations/self
    dotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -c Release -- --provider Wolverine --output artifacts/labs/migrations/wolverine

检查 SQL 与 manifest.json 中的目标迁移和 SHA-256。
Wolverine 目录除业务 SQL 外，还包含原生消息存储创建脚本；
原生表不由业务 EF 迁移重复创建。已有原生存储应使用 --diff 生成差异供检查。

需要对当前独立 Lab 消息库显式应用时：

    $configuration = Get-LabEnvironment $settings 'SelfManaged'
    $env:ConnectionStrings__ReferenceDatabase = $configuration.ConnectionStrings__LabMessagingDatabase
    dotnet run --project samples/Reference/MS.Microservice.Reference.DatabaseMigrator -c Release -- --provider SelfManaged --output artifacts/labs/migrations/self --apply

这一步是明确的管理命令，Web 启动不会替你执行它。
重复应用当前迁移应保持业务数据和迁移历史稳定。

## 理解上一练习版本的增量

旧 Activation 练习的基线与后续 Inbox/Outbox 迁移保留在
[历史迁移集](../../samples/Lab/MS.Microservice.Lab.Persistence/Migrations/Activation)。
可以只生成基线到当前版本的增量 SQL：

    dotnet tool restore
    dotnet tool run dotnet-ef migrations script 20260821072620_BaselineIdentityAndLog --context ActivationDbContext --project samples/Lab/MS.Microservice.Lab.Persistence --startup-project samples/Lab/MS.Microservice.Lab.DatabaseMigrator --configuration Release --output artifacts/labs/legacy-upgrade.sql

将旧练习库备份到新的练习数据库后，再在副本上检查并应用增量；
不要删除原库来模拟升级。旧消息表保留的是历史资料，不会被新运行链自动派发。

完成时应能区分：业务事务回滚、增量结构迁移、数据库重置。
外部 HTTP/邮件等副作用不受数据库回滚保护，应改为后续消息或对方支持的幂等操作。
