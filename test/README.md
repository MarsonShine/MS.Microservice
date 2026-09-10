# 测试地图

- Core.Tests：通用 Core 行为，不引用 Lab 或正式 Host。
- Architecture.Tests：共享组件依赖边界、正式部署依赖闭包、业务消息契约隔离。
- Lab.Tests：本地身份、实验端点、旧练习模型和持久化。
- Audio.Tests、Excel.Tests、Observability.Tests：对应可选组件。
- Messaging.*.Tests：公开契约和适配器行为；Messaging.IntegrationTests：真实 PostgreSQL/RabbitMQ。
- Reference.*.Tests：正式服务分层、身份、业务和迁移。
- Samples.EducationalImages.Tests：教育图像场景；AI.*.Tests：共享 AI 组件和 Provider。

运行单个项目可用 dotnet test test/<项目名> -c Release。
消息集成测试需要显式 RUN_MESSAGING_INTEGRATION_TESTS=true；开启后环境缺失必须失败。

旧 Infrastructure.Tests 已按归属拆分。未纳入解决方案、使用旧 Audio API 和固定 C 盘目录的
Infrastructure.Tests222 原型已删除，历史可在提交 419622a 查看；当前音频回归以 Audio.Tests 为准。
