# Laboratory host

This host contains local-identity, file, functional and event-sourcing examples. It does not run inside the production reference host. The default launch profile uses the Lab environment and port 5210.

Supply `LabTokenIssuer__SigningKey` with an externally generated key of at least 32 ASCII characters. `LabTokenIssuer:Issuer`, `Audience` and `LifetimeSeconds` define the active local issuer; validation automatically trusts that active tuple. Optional `IdentityOptions:JwtBearerOption` arrays add trusted issuers/audiences/rotation keys rather than choosing the signing key by array position.

The local account/password login is for this laboratory. Password request Base64 is an encoding, not encryption; company services use the separate reference host's external JWT/OIDC integration. Existing Activation/EventStore practice databases remain separate from reference databases.

可靠消息实验通过 LabMessaging:Enabled=true 显式启用，使用
ConnectionStrings:LabMessagingDatabase 和 Messaging 配置；默认 SelfManaged，也可选择 Wolverine。
使用 Reference.DatabaseMigrator 显式初始化此独立数据库（将 ReferenceDatabase 环境变量指向实验库）。
实验入口为 POST /lab/messaging/profiles、GET /lab/messaging/audit，要求 Lab 身份；
失败查询与重放额外要求 Manage 权限。业务服务和处理器直接复用 Reference 类库，两个 Host 互不引用。

旧 Activation 数据库、Inbox/Outbox 表及迁移历史保留。旧练习保存不再自动产生消息，
旧待发送和死信数据不会自动迁移或派发；需要处置时先备份并人工核对。
新的消息实验与旧练习数据库必须分离。领域事件的跨边界映射见 Reference.Application。
