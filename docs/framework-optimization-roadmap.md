# 后续工作与验证边界

当前结构与已实现职责以[总体架构](Architecture-Overview.md)、
[模块入口](../README.md)和[Lab 文档](labs/README.md)为准。
这份路线图只记录仍需处理的事项，不把未执行的验证写成完成状态。

| 未完成事项 | 现有基础 | 后续需要的证据 |
|---|---|---|
| 真实依赖故障矩阵执行 | [消息测试夹具](../MS.Microservice.Messaging/test/MS.Microservice.Messaging.IntegrationTests/README.md)已提供 | 支持的 PostgreSQL/RabbitMQ 容器环境中的执行结果与失败排查记录 |
| 发布与部署验收 | Reference、显式 Migrator、本地启动脚本 | 实际镜像、运行平台、配置、迁移和恢复验证；此项尚未执行 |
| 存量业务数据的 Provider 切换 | 两种具体 DbContext、独立迁移集、[独立库对照实验](labs/06-replacement.md) | 停写、备份、数据迁移、在途/死信处置及切换结果核对 |
| 可重复性能报告 | 组件和业务测试可以作为用例起点 | 明确环境、数据规模、吞吐、延迟、分配与积压恢复测量，不预设性能承诺 |
| 历史专题的可执行示例核对 | 理论与早期教程保留在 docs | 按实际使用逐篇确认路径、接口与适用版本；当前接入优先使用新入口 |

多实例、Saga、多租户和 Kubernetes 不属于当前参考实现的完成范围。
本地构建、组件测试和源码/包消费验证通过，不能替代上述真实环境与发布验证。

旧分片路线图的文字保存在[历史快照](history/framework-optimization-roadmap-legacy.txt)。
其中旧目录、Infrastructure 门面和旧 Inbox/Outbox 状态描述不再是当前接入说明。
