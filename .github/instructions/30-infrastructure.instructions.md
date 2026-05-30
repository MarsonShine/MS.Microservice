---
applyTo: "**/*.Infrastructure/**/*.cs"
---

## Infrastructure Layer Rules
- 只在 Infrastructure 里出现：ORM/DbContext、EF 配置、迁移、第三方 SDK、消息中间件实现等
- 实现上层定义的接口（Repository/Gateway），不要把实现细节泄漏到 Domain
- 不要在 Infrastructure 定义业务规则；业务规则属于 Domain/AppService
- 映射清晰：持久化模型与领域模型的映射应集中管理，避免散落各处
- 如果使用 EF：领域模型尽量不被 EF 特性/注解污染（优先 Fluent 配置）

## Reliability
- 外部调用要考虑：超时、重试（适度）、取消令牌、异常包装（转为上层可理解的错误）