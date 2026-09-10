# 消息恢复集成测试

仅在显式设置 RUN_MESSAGING_INTEGRATION_TESTS=true 时运行真实依赖矩阵。
测试使用独立 PostgreSQL / RabbitMQ 容器和每个用例独立的数据库、交换机与队列。
开启后环境缺失会导致失败，不会用全部跳过表示通过。

ProcessRecoveryTests 对 SelfManaged / Wolverine 都执行：

| 屏障 | 注入前证据 | 终止进程后的断言 |
|---|---|---|
| save-before-commit | EF 已完成 SaveChanges，事务尚未提交 | 业务与出站均回滚 |
| save-after-commit | EF Commit 回调，Broker 已停止 | 业务与出站保留，重启后以原 Id 消费 |
| consume-before-commit | 消费业务已 SaveChanges，尚未提交 | 消费业务回滚，恢复后产生一个效果 |
| consume-after-commit | 消费事务提交回调，随后停止 Broker | 重启后业务效果不重复 |

FaultWorker 是独立进程，屏障只存在于测试程序集的 EF 拦截器中。测试先收到 BARRIER，
再查询数据库确认目标状态，然后终止这个子进程。恢复验收同时检查业务数据、持久化消息积压、
Broker 队列和进程退出结果。轮询用于等待已定义的状态条件，不用于猜测故障注入时机。

SelfManaged 的 consume-after-commit 屏障位于返回手动 ACK 决策之前。
Wolverine 的 Broker 确认由原生持久 Inbox 管理；该屏障验证其原生事务提交后的进程恢复，
不能将它描述为两种实现具有相同的 Broker ACK 内部顺序。

非容器协议测试可以单独运行：

    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release --filter FullyQualifiedName~FaultProtocolTests

它只启动 --barrier-only 子进程，不构造容器夹具。真实矩阵运行：

    $env:RUN_MESSAGING_INTEGRATION_TESTS = 'true'
    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release

本地若按要求跳过 Docker，只能记录“编译通过，协议测试通过，真实依赖矩阵未执行”。
