# 数据库就绪检查为什么需要组件探针

`/health/ready` 只是运行检查的入口。原来的 `PlatformHealthChecks` 提供端点和 `ready` 标签，但没有可复用的数据库检查。Reference 在自己的宿主里查询档案表、检查迁移和消息存储；Lab 则单独实现了一个 PostgreSQL `SELECT 1` 检查。另一个使用数据库的宿主如果只映射就绪端点、忘记注册探针，数据库断开时仍可能得到 `200`。

现在宿主可显式调用 `AddDbConnectionCheck`。它接收一个创建 `DbConnection` 的工厂，不绑定 EF Core 或某个数据库驱动。Lab 的接入方式如下：

```csharp
var connection = configuration.GetConnectionString("ActivationConnection") ?? string.Empty;
services.AddHealthChecks()
    .AddDbConnectionCheck("postgresql", _ => new NpgsqlConnection(connection));
```

这个注册只在调用它的宿主生效。`/health/live` 不执行数据库检查；`/health/ready` 执行带 `ready` 标签的检查。每次检查创建并释放自己的连接，执行 `OpenAsync` 和 `SELECT 1`；打开失败、命令失败或缺少连接配置都会得到 `Unhealthy`，默认检查期限为五秒。查询字符串可以由宿主显式指定，以适配不同 SQL 方言。工厂必须返回由本次检查独占、可释放的连接，不能返回业务代码正在使用的同一个连接。

仅能连通数据库，不代表业务表和迁移都已就绪。Reference 仍使用宿主专属检查，因为它还要判断所选消息提供者的迁移和消息存储；再注册一次通用连接检查只会重复访问同一数据库。未来若多个宿主需要相同的迁移检查，应先明确它们共用的迁移契约，再抽出独立适配，而不是把 Reference 的表名放进通用组件。

使用 `DbConnection` 工厂让组件不必引用 Npgsql、Sqlite 或 EF Core，也不用扫描程序集或反射构造连接。健康检查是低频 I/O 路径，这次修改没有性能基线，也不声称减少检查耗时。测试用 SQLite 验证 ready 才打开连接、`SELECT 1` 可执行、连接或命令失败时返回不健康；Lab 测试验证缺少 PostgreSQL 配置时不健康。默认 HTTP 响应只写健康状态，不输出异常细节；宿主若自定义响应写入方法，也不应直接序列化检查异常。
