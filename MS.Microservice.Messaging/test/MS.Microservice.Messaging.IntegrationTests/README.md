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

SharedContractMatrixTests 使用同一个 ProbeEvent / ProbeHandler 覆盖两种 Provider：
中文与空值、decimal/enum、嵌套和构造器属性、独立订阅、消费失败与后续事件回滚、
原 Id 重放、毒消息隔离、瞬时重试以及并发物理重复投递。
ProbeLocalTests 使用 SQLite 执行相同处理器的本地事务验证，不替代原生 Provider 验收。

InvalidMessageTests 将不支持的版本、无效 JSON、信封/正文 Id 冲突投递到真实订阅队列，
检查可诊断失败且没有业务效果。DependencyRecoveryTests 验证 Broker/数据库中断后同一宿主恢复。

BrokerConfirmationTests 覆盖 mandatory return、满队列 reject-publish 产生的 nack，
以及“Broker 已确认但客户端未收到”的不确定结果。ConfirmLossProxy 只存在于夹具中：
它解析 AMQP 帧，观察到真实 basic.ack 后扣留该帧，测试收到信号后断开代理连接。
测试会验证消息已到达 Broker，且发布任务不能报告成功；测试自身的等待超时不能当作传输失败。

每个真实依赖用例有 180 秒上限；状态等待有 90 秒上限。CI 必须显式开启矩阵，
并保留测试结果和失败进程输出。本地只运行指定的 ProbeLocalTests / FaultProtocolTests 时不启动容器。
