# 后续工作与验证边界

当前结构与已实现职责以[总体架构](Architecture-Overview.md)、
[模块入口](../README.md)和[Lab 文档](labs/README.md)为准。
这份路线图只记录仍需处理的事项，不把未执行的验证写成完成状态。

| 未完成事项 | 现有基础 | 后续需要的证据 |
|---|---|---|
| 真实依赖故障矩阵执行 | [消息测试夹具](../MS.Microservice.Messaging/test/MS.Microservice.Messaging.IntegrationTests/README.md)已提供 | 支持的 PostgreSQL/RabbitMQ 容器环境中的执行结果与失败排查记录 |
| HTTP 幂等的真实数据库并发验证 | [EF 幂等组件](../MS.Microservice.Idempotency/README.md)、Reference 的两套迁移和带键创建端点 | 在 PostgreSQL 下同时发送相同键/相同与不同正文，核对只有一次档案与 Outbox 效果，失败请求回滚后重放原 `201`；分别验证 SelfManaged 与 Wolverine |
| 入口策略的实际容量验证 | [限流](../src/MS.Microservice.AspNetCore/docs/rate-limiting.md)、[请求超时](../src/MS.Microservice.AspNetCore/docs/request-timeouts.md)已可选接入 | 用预期并发和慢依赖检查 `429`、`504`、健康豁免及取消传播；多实例时在部署入口决定是否需要共享配额 |
| 发布与部署验收 | Reference、显式 Migrator、本地启动脚本 | 实际镜像、运行平台、配置、迁移和恢复验证；此项尚未执行 |
| 存量业务数据的 Provider 切换 | 两种具体 DbContext、独立迁移集、[独立库对照实验](labs/06-replacement.md) | 停写、备份、数据迁移、在途/死信处置及切换结果核对 |
| 可重复性能报告 | 组件和业务测试可以作为用例起点 | 明确环境、数据规模、吞吐、延迟、分配与积压恢复测量，不预设性能承诺 |
| 历史专题的可执行示例核对 | 理论与早期教程保留在 docs | 按实际使用逐篇确认路径、接口与适用版本；当前接入优先使用新入口 |
| 旧密码与 RSA 兼容路径退出 | Lab 登录时仍可验证旧 HMAC 密码；[RSA 解密](../src/MS.Microservice.Core/Security/Cryptology/rsa-oaep.md)仍读取旧密文 | 统计未升级密码记录并安排重置；确认外部 RSA 密文及调用方迁移完成，再分别删除两条只读兼容路径 |

多实例、Saga、多租户和 Kubernetes 不属于当前参考实现的完成范围。
本地构建、组件测试和源码/包消费验证通过，不能替代上述真实环境与发布验证。

旧分片路线图的文字保存在[历史快照](history/framework-optimization-roadmap-legacy.txt)。
其中旧目录、Infrastructure 门面和旧 Inbox/Outbox 状态描述不再是当前接入说明。

## 按实际应用触发的候选能力

下面的候选项尚未实现，也没有默认接入 Reference。只有出现表中的实际需求并能准备验证环境时，才为它开新 slice；每项都应先确认现有 .NET 或部署平台能力是否足够。
新增附加能力时，宿主默认不接入，并提供明确的启用选项；关闭状态应不注册该能力专用的后台服务，也不要求其专用存储。若数据迁移或就绪检查仍依赖该能力，需要在同一 slice 说明并验证这个边界。

| 触发条件 | 可做的能力 | 现有基础与最小验收 |
| --- | --- | --- |
| 出现第二个同步调用的服务，且目标地址会随环境变化 | 服务发现适配 | 已有可选 [HTTP 韧性客户端](../src/MS.Microservice.Http.Resilience/README.md)。先用配置或部署平台的服务名连接两个测试实例；验证地址变化、一个实例失联、总超时与取消，不自建注册中心。 |
| 应用需要保存由本框架加密的数据，并要求在不停读旧数据时轮换密钥 | 密钥来源和密钥 ID | [AES-GCM v1](../src/MS.Microservice.Core/Security/Cryptology/authenticated-encryption.md) 只有格式版本，没有密钥 ID。先确定实际密钥管理服务，再设计新封套版本、旧新密钥并存期限及错误密钥测试；不能把密钥写进仓库配置。 |
| 多实例服务反复读取同一批数据，且能说明失效规则 | 共享缓存适配 | Core 已有 `IDistributedCache` JSON 辅助方法。使用运行时 JSON 元数据的实现若要用于 Native AOT，应先提供显式 `JsonTypeInfo<T>` 契约并验证裁剪发布；选定真实缓存后端后，再验证跨实例读取、过期、并发回源、主动失效和后端不可用。 |
| 业务文件必须跨实例访问，不能继续保存在单机目录 | 对象存储适配 | Lab 上传目前使用本地 `StorageDirectory`。先选定对象存储及权限模型，再验证大小限制、流式上传、取消、失败清理和下载授权；不把 Lab 文件接口直接升为通用 API。 |

定时业务任务、Saga 与多租户等能力，等有具体应用用例后再列验收条件。消息 Outbox 的后台轮询只处理可靠消息，不代表已经有通用任务调度器。
