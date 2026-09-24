# Laboratory host

This host contains local-identity, file, functional and event-sourcing examples. It does not run inside the production reference host. The default launch profile uses the Lab environment and port 5210.

Supply `LabTokenIssuer__SigningKey` with an externally generated key of at least 32 ASCII characters. `LabTokenIssuer:Issuer`, `Audience` and `LifetimeSeconds` define the active local issuer; validation automatically trusts that active tuple. Optional `IdentityOptions:JwtBearerOption` arrays add trusted issuers/audiences/rotation keys rather than choosing the signing key by array position.

The local account/password login is for this laboratory. Password request Base64 is an encoding, not encryption; company services use the separate reference host's external JWT/OIDC integration. Existing Activation/EventStore practice databases remain separate from reference databases.

`ApiEncryptOptions:IsEnabled` defaults to `false`. When enabled, `/api/lab/encryption/echo` accepts a new RSA-OAEP + AES-GCM envelope for its registered DTO; `/api/lab/encryption/plain` keeps plain JSON through `[NoEncrypt]`. Supply the RSA private key as `ApiEncryptOptions__PrivateKey` from the deployment environment. The binder belongs to the optional AspNetCore.Encryption component; see [加密请求模型绑定](../../../src/MS.Microservice.AspNetCore.Encryption/docs/encrypted-model-binding.md).

可靠消息实验通过 LabMessaging:Enabled=true 显式启用，使用
ConnectionStrings:LabMessagingDatabase 和 Messaging 配置；默认 SelfManaged，也可选择 Wolverine。
使用 Reference.DatabaseMigrator 显式初始化此独立数据库（将 ReferenceDatabase 环境变量指向实验库）。
实验入口为 POST /lab/messaging/profiles、GET /lab/messaging/audit，要求 Lab 身份；
失败查询与重放额外要求 LabMessagingOperations 权限（角色动作路径 lab/messaging/operations）。业务服务和处理器直接复用 Reference 类库，两个 Host 互不引用。

旧 Activation 数据库、Inbox/Outbox 表及迁移历史保留。旧练习保存不再自动产生消息，
旧待发送和死信数据不会自动迁移或派发；需要处置时先备份并人工核对。
新的消息实验与旧练习数据库必须分离。领域事件的跨边界映射见 Reference.Application。

旧实现已从可编译项目移除。早期状态机源码可在提交 2d2273c 查看；它不是当前接入示例。
当前组件回归分别位于 Messaging.SelfManaged.EFCore.Tests、Messaging.RabbitMQ.Tests、
Messaging.Wolverine.Tests。Messaging.IntegrationTests 对两种实现运行相同业务断言；
显式开启 RUN_MESSAGING_INTEGRATION_TESTS=true 时，缺少依赖会失败，不能以跳过代替验收。

演练账号由 Lab.DatabaseMigrator 的 --seed-lab-users 显式创建，并且仅允许 ms_lab_ 前缀数据库。
必须配置 LabBootstrap:OperatorPassword 和 ReaderPassword（至少 16 字符）。
lab-operator 获得消息运维动作，lab-reader 没有此动作；密码使用 PasswordHasher 存储。
重复执行不会覆盖已有账号的密码或权限。正式 Reference Host 没有此初始化入口。
