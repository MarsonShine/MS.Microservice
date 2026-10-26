# Slice 03：Outbox 即时认领与租约进展

Status: complete

范围：Messaging.SelfManaged.EFCore 的 OutboxStore/SelfManagedPublisher；SelfManaged.Tests 及必要测试夹具；模块说明；任务状态/证据。范围外：数据库方言、迁移、并行发布、Broker协议、重试策略。

根因：整批共用旧now且全部认领完成后才发布，后续消息租约在队列等待中耗尽。不变量：无发布容量的候选不提前占有租约，每条基于认领时的新时间取得所有权。

方案：有界候选ID快照，惰性逐条条件UPDATE/读取/yield；Publisher await foreach处理完前一条才认领下一条。保留预发送Renew、续租独立scope、确认和失效token保护。不减少现有约1+2N认领往返。

验收：`dotnet test MS.Microservice.Messaging/test/MS.Microservice.Messaging.SelfManaged.Tests/MS.Microservice.Messaging.SelfManaged.Tests.csproj --no-restore -c Release`。慢候选查询后新租约、前条跨租约不影响后条、旧所有者与晚确认回归。

结果：旧代码定向回归 2 失败、0 通过、0 跳过。慢候选读取后租约期望 00:00:06、实际 00:00:01；前条跨租约后，后条发布成功数期望 1、实际 0。修复后完整 SelfManaged 项目源码重建验收 54/54，通过且无失败/跳过，退出码 0。

新增证据还验证首条发送时其余候选仍 Pending，提前结束枚举不会占有剩余候选；既有旧所有者、晚确认、重试与重放用例保持通过。git diff --check 通过。生产仅改两个内部实现文件，BCL ToListAsync 无新增依赖。

边界：单次认领本身慢于租约仍可能失效，由预发送续租与旧所有者检查拒绝；不声明往返数或吞吐提升。真实 PostgreSQL/RabbitMQ 故障矩阵未执行。
