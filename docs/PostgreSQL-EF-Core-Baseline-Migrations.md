# PostgreSQL EF Core 基线迁移：本项目的生成过程、原理与操作手册

本文归档 commit `99e6ee39870133e4eb03fdbff10daca3f2189304` 中两组 PostgreSQL 基线迁移的实际生成过程，并解释 EF Core 为什么能够“自动”生成对应的建表、索引、外键和回滚代码。

这里的“自动生成”不是应用启动时自动改数据库，而是开发者显式执行 `dotnet ef migrations add` 后，由 EF Core 设计时工具生成 C# 迁移文件。生成文件提交到 Git，真正应用数据库是另一个独立步骤。

## 一、最终产物

| DbContext | 负责内容 | PostgreSQL schema | 基线迁移 |
| --- | --- | --- | --- |
| `ActivationDbContext` | Identity、Role、Action、Log | `fz_platform_activation` | `20260821072620_BaselineIdentityAndLog` |
| `EventStoreDbContext` | Event、Snapshot、Projection Checkpoint、Order Read Model | `event_sourcing` | `20260821072637_BaselineEventSourcing` |

迁移目录：

- `MS.Microservice.Persistence/MS.Microservice.Persistence.EFCore/src/MS.Microservice.Persistence.EFCore/Migrations/Activation`
- `src/MS.Microservice.Infrastructure/EventSourcing/Migrations`

设计时启动项目与可选的直接执行入口：

- `src/MS.Microservice.DatabaseMigrator`

> 生产默认路径是生成 SQL 后交给 DBA 执行，不是运行 `DatabaseMigrator`。后者会直接调用 `MigrateAsync()`，只保留给本地、集成测试或 DBA 明确批准的独立部署 Job。

## 二、生成前做了什么

### 1. 固定 EF CLI 版本

本机原有的全局 `dotnet-ef` 是 `5.0.10`，与项目的 EF Core `10.0.6` 不匹配。因此仓库增加了 `.config/dotnet-tools.json`，把 CLI 固定为 `10.0.6`：

```powershell
dotnet tool restore
dotnet tool run dotnet-ef --version
```

使用仓库本地工具可以避免不同开发者调用不同版本的全局 CLI。Microsoft 官方说明 `dotnet ef` 可以作为 local tool 通过 tool manifest 管理，并要求项目引用 `Microsoft.EntityFrameworkCore.Design`。[Microsoft：EF Core .NET CLI 工具参考](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)

### 2. 明确 PostgreSQL Provider 与 schema

两个 Context 都使用 Npgsql 的 `UseNpgsql()`，因此 EF Core 生成 PostgreSQL 方言的迁移操作，而不是 SQL Server、SQLite 或通用 SQL。Npgsql 官方说明 `Npgsql.EntityFrameworkCore.PostgreSQL` 是 PostgreSQL 的 EF Core Provider，`UseNpgsql()` 是 Provider 配置入口。[Npgsql：Entity Framework Core Provider](https://www.npgsql.org/efcore/)

本次显式设置：

```csharp
modelBuilder.HasDefaultSchema("fz_platform_activation");
```

以及：

```csharp
modelBuilder.HasDefaultSchema("event_sourcing");
```

所以生成的 `Up()` 中会先调用 `EnsureSchema`，再在对应 schema 中创建表。

### 3. 隔离两套迁移历史

两个 Context 都使用名为 `__MigrationsHistory` 的历史表，但放在各自 schema 中：

```csharp
npgsql.MigrationsHistoryTable(
    "__MigrationsHistory",
    ActivationDbContext.DEFAULT_SCHEMA);
```

```csharp
npgsql.MigrationsHistoryTable(
    "__MigrationsHistory",
    EventStoreDbContext.DefaultSchema);
```

历史表保存已经应用的 Migration ID。EF Core 据此判断哪些迁移尚未执行。官方文档说明可以通过 `MigrationsHistoryTable()` 修改历史表名称和 schema；如果数据库已经应用过迁移后才修改历史表位置，则迁移现有历史数据是使用方自己的责任。[Microsoft：自定义迁移历史表](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table)

### 4. 提供设计时 DbContext Factory

`dotnet ef` 不是在处理 HTTP 请求，它需要在“设计时”单独创建 DbContext，读取实体和 Fluent API 映射。因此本项目为两个 Context 提供：

- `ActivationDbContextDesignFactory`
- `EventStoreDbContextDesignFactory`

两者实现 `IDesignTimeDbContextFactory<TContext>`，只提供构建 Model 所需的 Npgsql 配置。生成迁移不需要访问真实数据库；默认设计连接串只是让 Provider 能完成配置，不是生产凭据。

EF Core 找到设计时 Factory 后，会使用它创建 Context，并跳过其他创建方式。[Microsoft：设计时创建 DbContext](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation)

### 5. 建立独立启动项目

新增 `MS.Microservice.DatabaseMigrator` 作为：

1. EF CLI 的 `--startup-project`；
2. 部署阶段显式应用迁移的独立程序。

EF CLI 中：

- `--project` 是迁移文件最终写入的目标项目；
- `--startup-project` 提供可执行运行时和设计时依赖；
- `--context` 在多个 DbContext 中选择本次处理对象；
- `--output-dir` 指定目标项目内部的迁移目录。

这些参数的定义见 [Microsoft：EF Core .NET CLI 工具参考](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)。

## 三、实际执行的生成命令

生成前先构建独立迁移项目：

```powershell
dotnet build src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --no-restore
```

### Activation：Identity + Log

```powershell
dotnet tool run dotnet-ef migrations add BaselineIdentityAndLog --project MS.Microservice.Persistence/MS.Microservice.Persistence.EFCore/src/MS.Microservice.Persistence.EFCore/MS.Microservice.Persistence.EFCore.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context ActivationDbContext --output-dir Migrations/Activation --no-build
```

### Event Sourcing

```powershell
dotnet tool run dotnet-ef migrations add BaselineEventSourcing --project src/MS.Microservice.Infrastructure/MS.Microservice.Infrastructure.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context EventStoreDbContext --output-dir EventSourcing/Migrations --no-build
```

`--no-build` 只因为命令执行前已经成功构建。修改 Model 后如果没有先构建，不应盲目加这个参数，否则 CLI 可能加载旧程序集并生成错误迁移。

## 四、EF Core 内部如何生成迁移

```mermaid
flowchart LR
    A[设计时 Factory 创建 DbContext] --> B[EF 构建当前 IModel]
    B --> C[读取上一次 ModelSnapshot]
    C --> D[ModelDiffer 计算结构差异]
    D --> E[Provider 生成 MigrationOperation]
    E --> F[写入 Up / Down / Designer / Snapshot]
```

具体过程：

1. EF CLI 通过设计时 Factory 创建指定的 DbContext。
2. DbContext 执行 `OnModelCreating` 和所有 Entity Configuration，形成当前 `IModel`。
3. EF 读取该 Context 上一次提交的 Model Snapshot。
4. EF 比较“旧 Snapshot”和“当前 Model”。
5. Npgsql Provider 把差异转换为 PostgreSQL 对应的迁移操作。
6. CLI 把操作生成为可审查、可修改、可提交 Git 的 C# 文件。

官方迁移概览说明，EF Core 会比较当前 Model 与旧 Model Snapshot，生成迁移源文件，并通过历史表记录已经应用的迁移。[Microsoft：EF Core Migrations Overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)

这次是第一组迁移，没有旧 Snapshot，因此差异相当于：

```text
空模型 → 当前完整模型
```

所以基线迁移包含全部 schema、表、主键、外键和索引。

## 五、三个生成文件分别有什么作用

每个 DbContext 会得到三类文件。

### 1. `<timestamp>_<MigrationName>.cs`

这是人工 review 最重要的文件：

- `Up()`：向前升级数据库；
- `Down()`：回滚该次迁移。

基线的 `Up()` 包含 `EnsureSchema`、`CreateTable`、`CreateIndex` 和外键；`Down()` 按依赖顺序删除表。

### 2. `<timestamp>_<MigrationName>.Designer.cs`

这是该迁移生成时的目标模型元数据，包含：

- `[DbContext]`；
- `[Migration]`；
- `BuildTargetModel()`。

EF 在生成 SQL、回滚和迁移元数据时会使用它。通常不手工编辑。

### 3. `<DbContext>ModelSnapshot.cs`

Snapshot 表示“当前最新模型”。下一次执行 `migrations add` 时，EF 会用它与新的 DbContext Model 比较，只生成增量差异。

Snapshot 必须与迁移一起提交 Git。只提交 migration 而漏掉 Snapshot，会破坏下一次差异计算。Microsoft 官方也明确说明 Snapshot 是 `migrations add` 生成并应纳入源代码管理的文件。[Microsoft：EF Core Migrations Overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)

## 六、这次具体生成了哪些数据库对象

### `fz_platform_activation`

- `Actions`
- `Logs`
- `Roles`
- `Users`
- `RoleActions`
- `UserRoles`
- User、Log、关联表所需索引和外键

### `event_sourcing`

- `event_store`
- `snapshots`
- `projection_checkpoint`
- `order_read_model`
- Event ID、Stream ID、Stream Version、Stream Type、CreatedAt 等索引

这些对象来自当前 Entity、DbSet、Fluent API 和 Npgsql 类型映射，不是从某个现有数据库反向读取出来的。

## 七、生成迁移不等于执行迁移

`migrations add` 只生成文件，不执行 `Up()`，也不会修改数据库。

本项目的独立 Migrator 可以调用：

```csharp
await context.Database.MigrateAsync();
```

但它不是默认生产发布方式。Web Host 的 `Program` 不调用 `Migrate()`、`MigrateAsync()` 或 `EnsureCreated()`。生产 Web 运行账号只应持有业务所需的 DML 权限，不应持有 `CREATE TABLE`、`ALTER TABLE`、`DROP TABLE` 等 DDL 权限。

推荐的权限与职责边界：

| 角色/程序 | 是否连接生产数据库 | 建议权限 | 职责 |
| --- | --- | --- | --- |
| Web Host | 是 | 业务 DML，无 DDL | 正常读写业务数据 |
| EF SQL 生成命令 | 否 | 不需要数据库账号 | 根据 Migration 生成 SQL 文件 |
| CI/CD | 默认否 | 只管理构建产物 | 归档 SQL、SHA-256、版本和变更单 |
| DBA | 是 | 受控、短期 DDL | review、备份、执行、验证和回滚 |

因此，线上数据库连接串和 DDL 凭据不需要交给开发者、Web 应用或普通 CI Job。生成 SQL 使用的是 Migration C# 文件、Snapshot、设计时 Model 和 Npgsql Provider，不需要打开生产数据库连接。

Microsoft 官方建议生产环境优先生成并审查 SQL 脚本；运行时自动迁移会带来权限、并发、回滚和未经 review 直接执行 SQL 等风险。[Microsoft：应用迁移](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)

## 八、如何验证迁移而不连接数据库

本次实际使用 `--no-connect` 列出两组迁移：

```powershell
dotnet tool run dotnet-ef migrations list --project MS.Microservice.Persistence/MS.Microservice.Persistence.EFCore/src/MS.Microservice.Persistence.EFCore/MS.Microservice.Persistence.EFCore.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context ActivationDbContext --no-build --no-connect
```

```powershell
dotnet tool run dotnet-ef migrations list --project src/MS.Microservice.Infrastructure/MS.Microservice.Infrastructure.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context EventStoreDbContext --no-build --no-connect
```

输出分别包含：

```text
20260821072620_BaselineIdentityAndLog
20260821072637_BaselineEventSourcing
```

`--no-connect` 是 EF CLI 的官方选项，用于列出迁移但不连接数据库。[Microsoft：EF Core .NET CLI 工具参考](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)

仓库中的 `BaselineMigrationTests` 还会：

1. 调用 `GetMigrations()` 确认迁移可发现；
2. 使用 `IMigrator.GenerateScript()` 生成幂等 PostgreSQL SQL；
3. 检查 schema 和核心表名是否存在。

## 九、生产发布前推荐生成可审查 SQL

这里所说的“专门生成迁移脚本”就是 `dotnet ef migrations script`。它和下面几个动作不同：

| 命令/调用 | 结果 | 是否直接改数据库 |
| --- | --- | --- |
| `dotnet ef migrations add` | 生成 C# Migration 与 Snapshot | 否 |
| `dotnet ef migrations script` | 生成 SQL 文件 | 否 |
| `dotnet ef database update` | 连接数据库并执行迁移 | 是 |
| `Database.MigrateAsync()` | 程序连接数据库并执行迁移 | 是 |

本项目线上默认只允许前两步进入开发/CI 流程，最终 SQL 由 DBA 执行。

先创建归档目录：

```powershell
New-Item -ItemType Directory -Force artifacts/migrations | Out-Null
```

Activation：

```powershell
dotnet tool run dotnet-ef migrations script 0 BaselineIdentityAndLog --idempotent --project MS.Microservice.Persistence/MS.Microservice.Persistence.EFCore/src/MS.Microservice.Persistence.EFCore/MS.Microservice.Persistence.EFCore.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context ActivationDbContext --output artifacts/migrations/activation.sql
```

Event Sourcing：

```powershell
dotnet tool run dotnet-ef migrations script 0 BaselineEventSourcing --idempotent --project src/MS.Microservice.Infrastructure/MS.Microservice.Infrastructure.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context EventStoreDbContext --output artifacts/migrations/event-sourcing.sql
```

幂等脚本会查询迁移历史表，只执行尚未登记的迁移。官方文档同时强调：生产 SQL 应先 review、测试和归档，再交给部署系统或 DBA 执行。[Microsoft：应用迁移与幂等脚本](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)

生成后计算校验和，并把 SQL、校验和、Git commit、目标 Migration ID 和变更单号一起归档：

```powershell
Get-FileHash artifacts/migrations/activation.sql -Algorithm SHA256
Get-FileHash artifacts/migrations/event-sourcing.sql -Algorithm SHA256
```

DBA 在执行前应核对校验和，避免 review 后的 SQL 被替换。

只有在 DBA 明确批准直接执行模式时，才允许选择独立 Migrator。它必须作为应用启动之前的独立部署 Job，使用短期、受控的 DDL 凭据，不能复用 Web Host 的长期业务连接账号：

```powershell
dotnet run --project src/MS.Microservice.DatabaseMigrator -- --context activation
dotnet run --project src/MS.Microservice.DatabaseMigrator -- --context eventstore
```

## 十、以后新增迁移的标准流程

以 Activation 为例：

1. 修改 Entity 或 Entity Configuration。
2. 构建项目。
3. 检查是否存在未归档的模型变化。
4. 生成命名清晰的迁移。
5. review `Up()`、`Down()` 和 Snapshot。
6. 生成幂等 SQL，在临时 PostgreSQL 上演练升级和回滚。
7. 迁移文件、Snapshot、测试在同一个 commit 提交。

检查模型变化：

```powershell
dotnet tool run dotnet-ef migrations has-pending-model-changes --project MS.Microservice.Persistence/MS.Microservice.Persistence.EFCore/src/MS.Microservice.Persistence.EFCore/MS.Microservice.Persistence.EFCore.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context ActivationDbContext
```

新增迁移：

```powershell
dotnet tool run dotnet-ef migrations add AddUserLastLoginAt --project MS.Microservice.Persistence/MS.Microservice.Persistence.EFCore/src/MS.Microservice.Persistence.EFCore/MS.Microservice.Persistence.EFCore.csproj --startup-project src/MS.Microservice.DatabaseMigrator/MS.Microservice.DatabaseMigrator.csproj --context ActivationDbContext --output-dir Migrations/Activation
```

如果迁移尚未应用且刚生成就发现错误，可以使用 `migrations remove`，修正 Model 后重新生成。迁移一旦进入共享环境或已经应用，不应删除或改写历史迁移，而应增加新的修正迁移。[Microsoft：EF Core .NET CLI 工具参考](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)

## 十一、已有数据库使用基线时的重大风险

当前基线首先面向空数据库。不能因为已有数据库“表结构看起来一样”，就直接执行基线。

对已有数据库直接执行会出现两类风险：

- 表已经存在，基线的 `CREATE TABLE` 失败；
- 人工向历史表插入基线记录，但真实列、约束或索引并不一致，后续迁移建立在错误前提上。

已有数据库接入基线前必须：

1. 完整备份并验证恢复；
2. 导出现有 schema；
3. 与基线生成 SQL 做逐项 diff；
4. 由 DBA 明确采用“修正数据库到基线”还是“创建空基线并登记现状”；
5. 在生产规格相近的副本上演练；
6. 记录历史表初始化方式和审计证据。

不要在没有 schema diff 和回滚方案时手工插入 `__MigrationsHistory`。历史表只证明“系统认为迁移已执行”，不证明数据库真实结构正确。

## 十二、Review 检查清单

- Migration 名称是否描述业务变化；
- `Up()` 是否误把 rename 生成为 drop + add；
- `Down()` 是否真的可回滚；
- nullable、default、最大长度、精度是否正确；
- 索引和唯一约束是否符合并发语义；
- 大表变更是否可能长时间锁表；
- 数据迁移是否需要分批或双写过渡；
- Snapshot 是否与 migration 一起提交；
- 两个 Context 是否写入正确的 schema 和历史表；
- SQL 是否已在临时 PostgreSQL 上演练；
- 生产备份和回滚方案是否就绪。

## 十三、官方参考资料

1. [Microsoft：EF Core Migrations Overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) — Model、Snapshot、Migration 与历史表的总体工作方式。
2. [Microsoft：EF Core .NET CLI 工具参考](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) — `--project`、`--startup-project`、`--context`、`--output-dir`、`--no-connect` 等参数。
3. [Microsoft：设计时创建 DbContext](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation) — `IDesignTimeDbContextFactory<TContext>` 的发现与调用规则。
4. [Microsoft：应用迁移](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) — SQL、幂等脚本、Migration Bundle、CLI 和运行时迁移的生产权衡。
5. [Microsoft：自定义迁移历史表](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/history-table) — 自定义历史表名称与 schema。
6. [Microsoft：独立迁移项目](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/projects) — Context、目标项目、启动项目和独立迁移程序集的组织方式。
7. [Npgsql：Entity Framework Core Provider](https://www.npgsql.org/efcore/) — PostgreSQL EF Core Provider 与 `UseNpgsql()` 配置入口。
