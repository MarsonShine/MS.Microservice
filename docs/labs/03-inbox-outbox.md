# 实验三：自研 Inbox/Outbox

目标：用持久化状态解释可靠消息，不只观察 HTTP 成功。
本实验使用默认 SelfManaged；先完成[公共准备](README.md)。

## 让待发状态可见

先暂停专用 Broker，再提交业务：

    Invoke-LocalCompose stop rabbitmq
    $state = Invoke-LabExercise $settings -BaseUrl $labUrl
    $state.Profile

业务保存应成功。查询元数据，不查询消息正文：

    $sql = 'SELECT "Id", "ContractName", "ContractVersion", "State", "AttemptCount", "NextAttemptAtUtc", "LockedUntilUtc" FROM messaging."Outbox" ORDER BY "CreatedAtUtc" DESC LIMIT 10;'
    Invoke-LocalCompose exec -T postgres psql -U ms_reference -d ms_lab_messages_selfmanaged -c $sql

记录业务 Id 和消息 Id。恢复 Broker：

    Invoke-LocalCompose up -d --wait --wait-timeout 180 rabbitmq
    Wait-ReferenceAudit $state -BaseUrl $labUrl -AuditPath '/lab/messaging/audit'
    $sql = 'SELECT "MessageId", "Consumer", "State", "AttemptCount", "CompletedAtUtc" FROM messaging."Inbox" ORDER BY "ReceivedAtUtc" DESC LIMIT 10;'
    Invoke-LocalCompose exec -T postgres psql -U ms_reference -d ms_lab_messages_selfmanaged -c $sql

同一个消息 Id 应贯穿 Outbox、Inbox 和业务审计。

## 阅读状态与确认顺序

| 表 | 当前实现的状态值 |
|---|---|
| Outbox | 0 Pending，1 Publishing，2 Published，3 DeadLettered |
| Inbox | 0 Processing，1 Processed，2 Failed，3 DeadLettered |

这些数字是默认实现内部细节，不是业务契约，不能要求替换组件使用同样的表结构。

领取 Inbox 明确区分 Acquired、AlreadyProcessed、Busy。
只有 AlreadyProcessed 可以成功短路；Busy 等待后仍无法获取则重新入队，不消耗业务失败预算。
租约的确认、失败和续租必须带所有权 token；旧 worker 不能确认新 worker 的工作。

发布使用持久消息、稳定 MessageId、mandatory 和确认跟踪；
只有收到明确确认才进入 Published。消费提交后才返回手动 ACK 决策。
完整实现见[默认组件](../../src/MS.Microservice.Messaging.SelfManaged.EFCore/README.md)和
[RabbitMQ 传输](../../src/MS.Microservice.Messaging.RabbitMQ/README.md)。

## 保留期与幂等边界

已发布 Outbox 默认保留 7 天，已完成 Inbox 默认保留 30 天。
未完成与死信不会自动删除。超过去重保留期后，消息层不能承诺永久去重；
参考业务通过数据库唯一约束保护同一档案版本的永久性审计效果。

完成时应能说明：为什么发送确认与数据库提交之间仍可能发生重复投递，
以及为什么业务幂等仍然必要。
