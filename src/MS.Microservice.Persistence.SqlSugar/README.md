# SqlSugar 可选组件

依赖 Core、Domain.Primitives、SqlSugarCore。此包提供通用仓储、查询、序列化和工作单元辅助，
不包含 UserDemo、按用户分片、数据库密码或自动建表。

通过 services.AddSqlSugarClient<SqlSugarClient>(options, configurationFactory, clientFactory)
注册。每个 DI scope 创建并拥有一个客户端；同一 scope 内复用实例。配置工厂在解析时执行，
不得返回已被其他 scope 拥有的客户端。宿主注入连接串并显式管理数据库结构。
PrintLog 仅输出命令类型和参数数量，不输出 SQL 或参数值。

UserDemo 和按用户分片示例位于 samples/Lab/MS.Microservice.Lab.Persistence/SqlSugar；
教学注册为 AddMicroserviceSqlSugarPersistence。旧拼写 Microsoft.Extension.DependencyInjection
下的 AddSqlSugarService 转发已移除。分片客户端解析不再隐式建库建表，DDL 由实验者显式执行。
