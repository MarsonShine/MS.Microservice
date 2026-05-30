---
applyTo: "**/*.Web or .Api/**/*.cs"
---

## Web Layer Rules (Presentation)
- Web 只做：路由/鉴权/输入校验/DTO 映射/调用 AppService/把结果转 HTTP 响应
- 禁止把业务规则写在 Controller/Endpoint
- DTO 与领域模型隔离：不要直接暴露 Domain 实体到 API 响应
- 错误映射一致：应用/领域错误 -> 统一转换为合适的 HTTP 状态码与错误体
- 保持 endpoint 简短：复杂流程移到 AppService 用例

## Keep It Practical
- 逻辑极简单的 endpoint（例如纯健康检查）：允许就地实现，不强行引入用例类