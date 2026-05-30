---
applyTo: "**/*.AppService/**/*.cs"
---

## Application Layer Rules (Use Cases)
- AppService 负责用例编排：调用聚合、协调事务、调用外部接口（通过抽象接口）
- 不要把业务规则“写死”在 AppService：规则应下沉到 Domain（除非只是流程编排）
- 对外部依赖（仓储、消息、第三方服务）只依赖抽象接口（放在 Core 或 AppService 合适位置）
- 返回应用结果（Result/DTO），不要返回基础设施类型（DbContext/EF Entity）
- 保持可测试：用例类尽量可被单元测试覆盖（mock 外部依赖）

## Anti-Overengineering
- 不强制 CQRS：若需求简单，可用一个 service 方法完成
- 只有当读写模型明显分离/性能要求/复杂查询时再引入 Query/Command 分离