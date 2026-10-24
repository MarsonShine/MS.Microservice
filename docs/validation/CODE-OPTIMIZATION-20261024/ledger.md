# 代码优化分片清单

基线：`883b907c733d75093f79a0fa20639023ac3167fd`。依据：[审计报告](../CODE-AUDIT-20261024/report.md)。用户授权在同一请求中完成各片并分别提交，供逐提交审核；不推送、不改写已有提交。

每次仅一片 in_progress。实现与必要回归证据同片；审查预算通常为 8 个生产文件或约 400 行非生成生产变更，紧耦合构造链可说明后保持同片。验收保存在对应 slice-NN.md，提交哈希由 Git 历史确认。

| 片 | 目的与拟提交标题 | 范围与验收 | 依赖 | 状态 |
| --- | --- | --- | --- | --- |
| 00 | `docs: record code audit and optimization slices` | 审计/清单/状态；harness、链接、diff 检查 | 无 | complete |
| 01 | `fix(ai): share provider concurrency across capabilities` | AI.Core/Provider/测试；dotnet test MS.Microservice.AI/MS.Microservice.AI.slnx --no-restore -c Release | 00 | complete |
| 02 | `feat(ai): enforce bounded response and SSE reads` | AI 预算/解析/测试；AI 解决方案测试及超限/取消/释放回归 | 01 | complete |
| 03 | `fix(messaging): claim outbox work when ready to publish` | SelfManaged.EFCore/测试；dotnet test MS.Microservice.Messaging/test/MS.Microservice.Messaging.SelfManaged.Tests --no-restore -c Release | 00 | complete |
| 04 | `fix(audio): release readers on success and partial failure` | AudioProcessor/扩展/测试；dotnet test test/MS.Microservice.Audio.Tests --no-restore -c Release | 00 | complete |
| 05 | `fix(logging): record final HTTP outcomes` | Logging/宿主管线；Logging.AspNetCore.Tests 与 Reference.Web.Tests 管线验证 | 00 | complete |
| 06 | `build: validate public API and package consumer contracts` | build/API 基线/消费者/CI；正向、删除公开成员负向、包消费者执行 | 00 | complete |
| 07 | `fix(core): unify HTTP helper ownership and query handling` | Core HTTP/测试；HTTP/JSON 测试和兼容检查 | 06 | complete |
| 08 | `refactor(auth): move reference policies to composition root` | AspNetCore/宿主/测试；身份测试与兼容检查 | 06 | complete |
| 09 | `fix(tasks): reconcile failed terminal state writes online` | Tasks 执行器/测试；终态恢复且 handler 不重跑 | 00 | complete |
| 10 | `feat(tasks): add bounded terminal job retention cleanup` | Tasks/EFCore/测试；清理、Retry 竞争、JobId 契约和兼容检查 | 06,09 | pending |
| 11 | `test(architecture): target the reference host boundary` | 架构测试；dotnet test test/MS.Microservice.Architecture.Tests --no-restore -c Release | 00 | pending |
| 12 | `fix(core): measure retry elapsed time monotonically` | Core Retry/测试；Retry 回归与兼容检查 | 06 | pending |
| 13 | `fix(eventsourcing): bound projection batches and reuse tracked models` | EventSourcing/Lab/测试；持久投影、架构与兼容检查 | 06 | pending |

最终验收：根解决方案 Release 构建与测试、API/包消费检查、harness closeout、工作区及提交清单。真实后端/原生依赖或环境门禁不可用时保留证据，不写成通过。

条件性扩展按原规划：多实例租约、缓存广播、密钥轮换没有触发条件时不在当前实现集合内。范围不含业务功能、部署平台、第二套调度器或新加密算法。
