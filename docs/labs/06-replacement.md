# 实验六：替换消息组件

目标：让业务代码与 Handler 保持不变，替换完整可靠消息机制。
先完成[公共准备](README.md)。

## 先观察默认实现

记录以下文件和当前结果：

- [公共消息契约](../../MS.Microservice.Messaging/src/MS.Microservice.Messaging.Abstractions/Contracts.cs)
- [ProfileService](../../samples/Reference/MS.Microservice.Reference.Application/ProfileService.cs)
- [ProfileAuditHandler](../../samples/Reference/MS.Microservice.Reference.Application/ProfileMessages.cs)
- 当前档案和审计结果、消息 Id、待发和处理中消息数量

SelfManaged 使用业务 DbContext 中的自研 Inbox/Outbox，再通过 RabbitMQ 传输。
Wolverine 使用原生可靠存储、事务中间件和恢复机制；不是只替换 Send 方法。

## 停写并检查旧状态

停止发送新请求，等待旧实现的待发、处理中和待重试工作结束：

    $sql = 'SELECT COUNT(*) AS pending FROM messaging."Outbox" WHERE "State" IN (0,1); SELECT COUNT(*) AS unfinished_inbox FROM messaging."Inbox" WHERE "State" IN (0,2);'
    Invoke-LocalCompose exec -T postgres psql -U ms_reference -d ms_lab_messages_selfmanaged -c $sql
    Invoke-LocalCompose exec -T rabbitmq rabbitmqctl list_queues -p ms-reference name messages_ready messages_unacknowledged

待发、处理中、Broker ready/unacknowledged 应分别核对。
死信不算已排空，需要单独查询、记录并处置。保留备份后结束旧宿主。

## 启动替换实现

    ./build/start-local.ps1 -Lab -Provider Wolverine

脚本使用 ms_lab_messages_wolverine 和独立队列前缀，显式初始化原生存储。
重新加载公共准备中的登录信息后，执行同一个 Invoke-LabExercise 和审计断言。
业务服务与 Handler 不需要引用 Wolverine Envelope 或自研表实体，也不需要改代码。

教学配置切换使用新库：它不会复制旧业务数据、在途消息或死信。
生产存量数据切换需要另行设计数据迁移与停写窗口，不属于这个独立库对照实验。

## 检查替换边界

- 默认值仍为 SelfManaged；未知 Provider 或重复注册必须立即失败。
- 每个宿主只启动选中的保存、消费持久化和派发链。
- Wolverine 在持久化前将集成事件 Id 映射到 Envelope Id。
- 中立 Handler 被排除出原生约定扫描，只经事务适配桥调用一次。
- 两种实现可以具有不同的表结构、内部状态和 Broker 确认顺序；共同断言是持久化业务效果和恢复能力。

无需容器的配置与发现回归：

    dotnet test test/MS.Microservice.Messaging.Wolverine.Tests -c Release
    dotnet test test/MS.Microservice.Lab.Tests -c Release --filter FullyQualifiedName~LabMessagingConfigurationTests

具备容器环境时，再运行两种 Provider 共用的 SharedContractMatrixTests。
不能用运行时热切换、双写或复制另一套自研实现来完成这个实验。
