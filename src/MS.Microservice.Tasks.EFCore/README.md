# EF Core 任务存储

此包把任务和每次执行记录保存到宿主的 EF Core 模型，提供独立提交、业务事务内暂存、进度、取消与显式重试。任务参数及状态说明见 [Tasks](../MS.Microservice.Tasks/README.md)。当前包负责存储，不会自行领取或执行任务。

先为宿主配置关系型数据库的 scoped DbContext，再注册任务存储：

```csharp
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Tasks;
using MS.Microservice.Tasks.EFCore;

services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
services.AddEfCoreJobStore<AppDbContext>();

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.AddTaskCenter(); // 可传 schema；SQLite 不支持 schema。
        // 保留宿主已有实体配置。
    }
}
```

需要 `MS.Microservice.Tasks.EFCore` 命名空间，以及宿主选择的 EF Core 数据库 Provider。`AddTaskCenter` 加入 `Jobs` 和 `JobAttempts` 两张表；迁移由宿主创建、审查并应用，组件不在启动时自动修改数据库。

独立提交适用于只创建任务的请求：

```csharp
var request = JobSubmission.Create(Guid.NewGuid(), currentUserScope,
    "report.export", 1, new { ReportId = "r-123" });
var created = await jobs.SubmitAsync(request, cancellationToken);
```

`SubmitAsync` 使用新的 DI scope 和 DbContext，不保存请求作用域中其他待写入的业务实体。宿主正在持有事务时应使用 `StageAsync`，否则独立操作会拒绝执行。

如果业务数据、消息和任务需要一起成功，复用宿主已注册的 `MS.Microservice.Messaging.IUnitOfWork`：

```csharp
using MS.Microservice.Messaging;

await unit.ExecuteAsync(async token =>
{
    // 在当前作用域的同一个 DbContext 中修改业务数据，暂存已有 Outbox 消息。
    await jobs.StageAsync(request, token);
}, cancellationToken);
```

这里 `unit` 与任务存储必须使用同一个 `AppDbContext`。`StageAsync` 要求该 DbContext 已有事务，只追踪任务，不调用 `SaveChanges` 或提交；最外层工作单元负责统一保存、提交或回滚。未使用 Messaging 时，宿主也可以自行开启事务、调用 Stage、SaveChanges 并提交。

| 操作 | 调用者需要注意的约束 |
|---|---|
| `GetAsync` / `ListAsync` / `GetAttemptsAsync` | 传认证身份决定的 Scope；列表默认 50 条，最多 1000 条 |
| `GetDueAsync` / `TryStartAsync` | 前者只返回候选；后者成功返回执行令牌后才拥有执行资格 |
| `ReportProgressAsync` | 保存 0～100 的单调进度；取消后或令牌失效返回 `false` |
| `CompleteAsync` / `FailAsync` / `InterruptAsync` | 使用当前令牌更新任务和执行记录；更早提交的取消请求优先生效 |
| `CancelAsync` | 排队任务立即取消；运行任务保持 Running，等待执行方响应 |
| `RetryAsync` | 只重新排队，不执行；可指定新的延后时间，历史记录继续保留 |

结束操作的 `true` 表示本次状态更新已提交；若取消请求先提交，最终状态会是 `Cancelled`，即使调用的是 `CompleteAsync`。需要展示结果时重新查询任务，只有成功状态才读取其结果引用。

结果引用是宿主理解的文件键或业务标识，最多 4096 字符；大文件仍保存到现有文件存储。失败代码最多 128 字符，进度消息最多 1024 字符。宿主应避免把异常堆栈、文件字节或凭据写入任务参数和消息；对外状态接口只返回必要字段。

宿主必须使用同类型的 scoped DbContext；独立操作会检查错误共享的上下文、已有事务和待保存变更。Stage 所在事务失败后应回滚并丢弃或清理该上下文，沿用宿主工作单元已有的异常处理。

不同 DI scope 解析出的任务 DbContext 必须连接同一任务数据库。按请求切换数据库的宿主，需要单独配置稳定的任务存储，不能依赖请求中的租户状态来选择后台任务库。

首版部署约定为单实例，没有分布式租约、自动恢复、自动重试和保留期清理。当前使用 SQLite 验证存储行为；其他关系型数据库应随宿主迁移和集成一起验证。实现细节见 [持久化设计](docs/persistence-design.md)。
