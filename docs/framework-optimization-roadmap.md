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
| 可重复性能报告 | [缓存／敏感字段本地对照](components/cache-sensitive-performance.md)及各组件基准 | 继续按真实工作负载报告环境、吞吐、延迟、分配与恢复；本地微基准不替代真实后端容量验收 |
| 历史专题的可执行示例核对 | 理论与早期教程保留在 docs | 按实际使用逐篇确认路径、接口与适用版本；当前接入优先使用新入口 |
| 旧密码与 RSA 兼容路径退出 | Lab 登录时仍可验证旧 HMAC 密码；[RSA 解密](../src/MS.Microservice.Core/Security/Cryptology/rsa-oaep.md)仍读取旧密文 | 统计未升级密码记录并安排重置；确认外部 RSA 密文及调用方迁移完成，再分别删除两条只读兼容路径 |

多实例、Saga、多租户和 Kubernetes 不属于当前参考实现的完成范围。
本地构建、组件测试和源码/包消费验证通过，不能替代上述真实环境与发布验证。

旧分片路线图的文字保存在[历史快照](history/framework-optimization-roadmap-legacy.txt)。
其中旧目录、Infrastructure 门面和旧 Inbox/Outbox 状态描述不再是当前接入说明。

## 已规划：导入导出任务编排

状态：**待后续实现，尚未开始**。面向需要后台导入／导出、处理进度和错误报告的业务系统，复用 [Tasks](../MS.Microservice.Tasks/README.md)、[Excel／CSV](../src/MS.Microservice.Excel/README.md) 与 [文件存储](../src/MS.Microservice.Core/README.md)。组件由现有宿主接入，Coravel 继续驱动任务，管理入口沿用任务中心，不新增独立应用或另一套任务队列。

| 规划范围 | 后续实现与验收要求 |
| --- | --- |
| 导入流程 | 私有文件上传 → 持久任务 → 分批校验／业务处理 → 进度与汇总 → 错误报告下载；错误保留源行列定位，报告内容遵守敏感字段策略 |
| 导出流程 | 提交明确的查询条件 → 后台分批读取／写入 → 成功后发布私有结果文件 → 下载；失败和取消时不发布部分结果 |
| 业务接入 | 通用编排管理任务与文件生命周期；字段映射、业务校验、每批事务及部分成功／整体回滚策略由业务处理器明确提供 |
| 取消与重试 | 复用 Tasks 的进度、取消和执行历史；只有业务满足幂等条件才开放自动重试，已提交批次不因取消自动回滚 |
| 检查点与恢复 | 是否支持断点续跑取决于稳定输入、检查点和业务幂等契约；首版不得把重新执行任务宣传为任意文件的断点恢复 |
| 资源预算 | CSV 按记录流式处理；现有 NPOI Excel 会加载工作簿，不因分批写库就具备恒定内存；分别规定格式、大小、行数、并发与结果保留期 |
| 权限与示例 | 输入、任务、错误报告和结果按宿主授权 Scope 隔离；Lab 提供正常导入、查看进度／报告、导出下载的核心流程 |
| 性能证据 | 固定文件规模和环境，报告吞吐、峰值内存、取消响应及数据库批处理成本；范围内验证失败清理、重试幂等和结果下载授权 |

## 按实际应用触发的候选能力

下面的候选项尚未实现，也没有默认接入 Reference。只有出现表中的实际需求并能准备验证环境时，才为它开新 slice；每项都应先确认现有 .NET 或部署平台能力是否足够。
新增附加能力时，宿主默认不接入，并提供明确的启用选项；关闭状态应不注册该能力专用的后台服务，也不要求其专用存储。若数据迁移或就绪检查仍依赖该能力，需要在同一 slice 说明并验证这个边界。

| 触发条件 | 可做的能力 | 现有基础与最小验收 |
| --- | --- | --- |
| 出现第二个同步调用的服务，且目标地址会随环境变化 | 服务发现适配 | 已有可选 [HTTP 韧性客户端](../src/MS.Microservice.Http.Resilience/README.md)。先用配置或部署平台的服务名连接两个测试实例；验证地址变化、一个实例失联、总超时与取消，不自建注册中心。 |
| 应用需要保存由本框架加密的数据，并要求在不停读旧数据时轮换密钥 | 密钥来源和密钥 ID | [AES-GCM v1](../src/MS.Microservice.Core/Security/Cryptology/authenticated-encryption.md) 只有格式版本，没有密钥 ID。先确定实际密钥管理服务，再设计新封套版本、旧新密钥并存期限及错误密钥测试；不能把密钥写进仓库配置。 |
| 多实例服务反复读取同一批数据，且能说明失效规则 | 共享缓存后端与失效广播验证 | [Caching](../src/MS.Microservice.Caching/README.md) 已统一键、策略、回源预算与监测，旧 Core／Lab 缓存已移除。显式 JSON 元数据入口已提供，但仍需选定真实后端，验证跨实例读取、过期、回源、主动失效、故障和所需的 AOT 发布；广播适配尚未实现。 |
| 业务文件必须跨实例访问，不能继续保存在单机目录 | 对象存储适配 | Lab 上传目前使用本地 `StorageDirectory`。先选定对象存储及权限模型，再验证大小限制、流式上传、取消、失败清理和下载授权；不把 Lab 文件接口直接升为通用 API。 |

定时与后台业务任务已有 [Tasks／Coravel](../MS.Microservice.Tasks/README.md) 组件，后续编排优先复用其持久任务与执行能力。Saga、多租户等扩展仍需具体应用用例和独立验收条件。
