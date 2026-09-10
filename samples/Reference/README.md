# Reference：组件接入的参考应用

Reference 展示“如何把可复用组件组合成一个实际服务”，不是底层核心库，
也不是其他组件的运行前置条件。只使用 AI、消息、日志或持久化组件时，无需引用这个应用。

场景是外部身份对应的本地用户档案、应用角色和审计。身份来自标准 JWT/OIDC；
本地账号密码和令牌签发属于 Lab。

阅读顺序：Domain → Application → Persistence → Web → DatabaseMigrator。
前两层说明业务与用例；Persistence 负责应用自己的数据模型；
Web 是选择具体组件的组合根；Migrator 负责显式结构变更。

- [总体设计、拆分理由和代价](../../docs/Architecture-Overview.md)
- [Web 配置与接口](MS.Microservice.Reference.Web/README.md)
- [持久化模型](MS.Microservice.Reference.Persistence/README.md)
- [迁移入口](MS.Microservice.Reference.DatabaseMigrator/README.md)
- [运行步骤](../../docs/Getting-Started.md)

这里的分层是参考选择，小应用可以合并程序集，但应保留职责与事务边界。
Lab 会复用 Application/Persistence 示例类库；两个 Host 互不引用。
