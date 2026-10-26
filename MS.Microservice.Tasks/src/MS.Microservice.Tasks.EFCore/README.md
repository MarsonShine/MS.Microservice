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

执行器的进度和控制查询使用独立短事务。SQLite 同库只允许一个写者，处理器持有业务写事务时不要同步等待进度写入；应在事务提交后汇报进度。

## 终态保留清理

`AddEfCoreJobStore` 同时注册可选的 `IJobRetentionStore`，在同一个 DI scope 中与 `IJobStore` 指向同一个存储实例。它提供显式维护操作；宿主决定保留期、可信 Scope 和调用时机，组件注册不会启动清理 worker。

以下示例中的 `retention` 由 DI 解析为 `IJobRetentionStore`，`clock` 为宿主的 `TimeProvider`。成功和失败可以使用不同保留期：

```csharp
var now = clock.GetUtcNow();
await retention.PruneAsync(authorizedScope, JobState.Succeeded, now.AddDays(-7),
    take: 100, cancellationToken: cancellationToken);
await retention.PruneAsync(authorizedScope, JobState.Failed, now.AddDays(-30),
    take: 100, cancellationToken: cancellationToken);
```

每次只清理指定 Scope 和终态，允许的状态为 `Succeeded`、`Failed`、`Cancelled`、`Interrupted`。完成时间必须严格早于 `finishedBeforeUtc`；等于截止时间、完成时间为空、Queued 和 Running 的记录都会保留。截止时间不得晚于当前时间。`take` 默认为 100，范围 1–1000，限制本次候选数量；返回值是实际删除的 Job 数，不包含级联删除的 Attempt 数。竞争可能让候选失去资格，因此返回 0 不保证库中没有其他到期记录。

清理复用独立 fresh context 规则，拒绝调用方已有事务和 ambient transaction，不提交调用方其他更改。每条删除再次核对状态、截止时间和候选版本：Retry 先提交则跳过，删除先提交则 Retry 返回 NotFound。Job 与其 Attempt 通过现有外键级联一起删除；一个批次失败或取消时可能已完成部分删除，可以安全重试。此操作只处理任务记录，结果引用对应的文件生命周期仍由宿主管理。

过期但未物理删除的任务仍保留原提交身份；删除后，同一 `Scope + JobId` 会作为新提交，允许新的参数，执行历史从新任务开始。保留期应覆盖调用方依赖的重试窗口，不提供删除之后的永久去重保证。

清理使用已有模型与外键，不新增索引或启动 DDL。候选和删除数量受 `take` 限制，数据库筛选、排序的扫描成本仍取决于宿主数据规模与现有执行计划。

当前执行部署约定为单实例，没有分布式租约。单独使用存储包不会自行恢复或重试；[Coravel 接入](../MS.Microservice.Tasks.Coravel/README.md)提供调度及按处理器策略恢复中断的执行。当前使用 SQLite 验证存储行为；其他关系型数据库应随宿主迁移和集成一起验证。实现细节见 [持久化设计](../../docs/persistence-design.md)。
