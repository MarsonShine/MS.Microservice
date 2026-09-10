# Persistence 模块的设计思路

## 通用能力与业务模型分开

持久化组件应该能被另一套业务 DbContext 使用，不应为了提供查询或审计而依赖 User、Role 或 Order。
因此通用 EFCore 包保留规格/Include、软删除与审计辅助；应用持有具体 DbContext、业务映射和迁移。

Reference.Persistence 展示两种可靠消息实现如何复用业务模型，
Lab.Persistence 保留旧身份库、旧迁移和用户分片教学。
把这些类库移动到应用目录，并不意味着通用仓储必须引用它们。

## 两种 ORM 为什么并列

EFCore 与 SqlSugar 是可选接入路径，放在同一模块是为了集中维护持久化主题，
不是要求同一业务同时使用两个 ORM，更不是隐含一个跨 ORM 原子事务。
选择哪个实现由应用决定；跨数据库或外部系统的副作用也不在本地事务保证中。

## 生命周期为什么重要

EF DbContext 是工作单元的一部分，不能被多个并发操作共享。
SqlSugar 的 scoped 注册也必须在每个 scope 中创建客户端，
否则仅仅把一个预先创建的实例注册成 Scoped，仍会跨作用域共享和重复释放。

审计辅助分别识别 ICreatedAt 与 IUpdatedAt，因为实体可能只实现其中一个。
一次保存使用同一个 UTC 时刻；修改实体不覆盖创建时间，未改变或删除实体不被误写更新时间。
辅助方法不自行提交事务，不清理业务事件。

## 数据库结构由谁管理

迁移是应用的部署数据契约，不能靠通用包或依赖注入解析偷偷建表。
Reference 的 Migrator 负责显式 SQL 导出/应用，Lab 有自己的显式迁移入口。
旧练习数据和迁移历史默认保留，不通过删库修复结构差异。

阅读入口：[EFCore](../src/MS.Microservice.Persistence.EFCore/README.md)、
[SqlSugar](../src/MS.Microservice.Persistence.SqlSugar/README.md)、
[总体架构](../../docs/Architecture-Overview.md)。
