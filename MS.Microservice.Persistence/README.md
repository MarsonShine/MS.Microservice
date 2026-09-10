# MS.Microservice.Persistence

持久化模块把可复用 EFCore / SqlSugar 能力及其测试放在一起，不包含具体用户或订单模型。

- [EFCore](src/MS.Microservice.Persistence.EFCore/README.md)：规格查询、Include、软删除查询与 UTC 审计辅助。
- [SqlSugar](src/MS.Microservice.Persistence.SqlSugar/README.md)：通用仓储、查询、序列化、工作单元辅助与 scoped 客户端注册。

打开 [MS.Microservice.Persistence.slnx](MS.Microservice.Persistence.slnx)，在本目录执行：

    dotnet build MS.Microservice.Persistence.slnx -c Release
    dotnet test MS.Microservice.Persistence.slnx -c Release

[设计说明](docs/design.md)解释为什么业务 DbContext、映射和迁移留在应用中。
Dependencies 只列出通用 Core / Domain.Primitives；不需要打开整个 Lab 才能修改组件。

Reference 的实际业务库位于 samples/Reference/Persistence 项目，
旧身份库和用户分片实验位于 samples/Lab/Persistence 项目。
共同目录表示它们属于持久化这一能力类别，不表示应用应同时启用两种 ORM。
