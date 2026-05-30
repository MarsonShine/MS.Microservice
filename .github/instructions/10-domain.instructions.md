---
applyTo: "**/*.Domain/**/*.cs"
---

## Domain Layer (DDD) Rules
- Domain 只表达业务模型与规则：实体/值对象/聚合/领域服务/领域事件/规格或规则对象
- Domain 禁止依赖：Web、Infrastructure、ORM、数据库、HTTP、序列化框架细节
- 聚合根负责一致性边界：修改状态通过方法完成，维护不变量
- 值对象不可变（immutable），以相等性（Value Equality）为核心
- 不要在实体里注入 Repository/DbContext/HttpClient 等基础设施依赖
- 领域事件仅描述“发生了什么”，不直接做 IO；由外部处理器（AppService/Infrastructure）订阅处理
- 业务规则失败优先用领域异常或领域错误对象表达，不返回 HTTP/DTO

## Keep It Simple
- 若只是简单字段封装、无复杂规则：允许用较轻量的模型，不强行引入聚合/事件/服务