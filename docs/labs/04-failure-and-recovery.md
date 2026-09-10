# 实验四：故障与恢复

目标：用可核对的状态确认恢复，不靠“多等几秒应该好了”。
先完成[公共准备](README.md)。

## Broker 中断

按实验三暂停 Broker，在中断期间提交一个新档案，记录返回的 Id。
恢复 Broker 后等待相同版本的审计出现，并检查待发积压清空。
每次故障实验结束都恢复专用 Broker，避免影响后续实验。

## 进程终止的四个窗口

[进程恢复矩阵](../../test/MS.Microservice.Messaging.IntegrationTests/ProcessRecoveryTests.cs)
使用独立子进程和 EF 屏障：

1. 保存后、提交前：业务和出站一起回滚。
2. 提交后、发送前：已提交工作在新进程中恢复，Id 不变。
3. 消费保存后、提交前：业务与后续出站回滚，重新处理后只有一个效果。
4. 消费提交后：断连和重投不会产生第二个业务效果。

先单独运行不需要 Docker 的协议检查：

    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release --filter FullyQualifiedName~FaultProtocolTests

具备支持的容器环境后，才能运行真实恢复用例：

    $env:RUN_MESSAGING_INTEGRATION_TESTS = 'true'
    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release --filter FullyQualifiedName~ProcessRecoveryTests

测试收到 BARRIER 后先查询数据库，确认到达目标阶段，再终止那个子进程。
测试夹具使用独立数据库，不会修改当前 Lab 数据库或注入正式服务 API。

## 毒消息、重试与重放

    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release --filter FullyQualifiedName~SharedContractMatrixTests

观察失败消费如何回滚后续事件、毒消息如何与健康消息隔离，
以及修正处理条件后如何以原 Id 重放。瞬时失败有重试，永久错误进入可诊断失败状态。

真实传输边界的补充用例：

    dotnet test test/MS.Microservice.Messaging.IntegrationTests -c Release --filter FullyQualifiedName~BrokerConfirmationTests

确认丢失夹具会观察并扣留真实 basic.ack 帧，再断开代理连接。
客户端不知道结果时不能标记已发布，即使 Broker 实际上已收到了消息。

当前 Lab 若存在失败记录，可通过实验五的授权接口查询并重放。
没有失败记录时返回空列表是正常结果，不要编造 failureId。
这些容器命令在本机未执行，不能将编译成功当成恢复验收通过。
