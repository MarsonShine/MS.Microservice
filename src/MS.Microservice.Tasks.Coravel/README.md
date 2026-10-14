# Coravel 任务中心调度

使用宿主已有的 Coravel Scheduler 驱动持久任务执行，支持分组并发、进度、协作取消、超时和中断恢复。数据库仍由 [Tasks.EFCore](../MS.Microservice.Tasks.EFCore/README.md) 保存；组件不使用 Coravel 内存队列保存任务，也不额外启动调度服务。

```csharp
using Coravel;
using MS.Microservice.Tasks;
using MS.Microservice.Tasks.Coravel;
using MS.Microservice.Tasks.EFCore;

services.AddEfCoreJobStore<AppDbContext>(); // AppDbContext 已注册，并在模型中 AddTaskCenter。
services.AddTaskExecution(options => options.AddQueue("reports", 2));
services.AddJobHandler<ExportRequest, ExportHandler>("report.export", 1, options =>
{
    options.Queue = "reports";
    options.Timeout = TimeSpan.FromMinutes(5);
    options.SafeToRetry = false;
});
services.AddScheduler(); // 宿主已有注册时保留原来的一处即可。
services.AddCoravelTaskCenter();

// 在宿主已有的 UseScheduler 回调中加入这一行。
app.Services.UseScheduler(scheduler => scheduler.ScheduleTaskCenter());
```

`MS.Microservice.Tasks.Coravel` 是调度扩展的命名空间。`AddCoravelTaskCenter` 要放在 `AddTaskExecution` 后面；宿主统一管理 `AddScheduler` 和 `UseScheduler`。适配器的 HostedService 启动时等待恢复完成，随后 Tick 才能领取新任务；停机时停止领取并等待处理器结束。

处理器使用 `IJobHandler<TPayload>`，返回可选的私有结果引用。下面的 `ReportExporter` 是宿主自己的报表业务服务：

```csharp
public sealed record ExportRequest(string ReportId);

public sealed class ExportHandler(ReportExporter exporter) : IJobHandler<ExportRequest>
{
    public async Task<string?> ExecuteAsync(ExportRequest payload, JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        await context.ReportProgressAsync(10, "正在生成报表");
        return await exporter.ExportAsync(payload.ReportId, context.JobId, cancellationToken);
    }
}
```

提交时使用相同的名称、版本和队列：

```csharp
var request = JobSubmission.Create(Guid.NewGuid(), currentUserScope,
    "report.export", 1, new ExportRequest("r-123"), queue: "reports");
var job = await jobs.SubmitAsync(request, cancellationToken);
```

| 设置 | 默认值 | 说明 |
|---|---|---|
| Queue 并发 | `default` 为 1 | `AddQueue` 配置 1～1000，处理器只能使用已配置队列 |
| Tick 调度 | 每秒 | `ScheduleTaskCenter(intervalSeconds)` 可设 1～59，按 Coravel 的分钟内秒刻度执行 |
| 取消轮询 | 500 毫秒 | `CancellationPollInterval` 可设 25 毫秒～1 分钟 |
| 存储操作超时 | 10 秒 | `StorageOperationTimeout` 可设 25 毫秒～5 分钟 |
| 处理器超时 | 5 分钟 | `Timeout` 可设 1 毫秒～1 天，通过取消令牌通知处理器 |
| `SafeToRetry` | `false` | 仅允许中断后的自动重排，普通失败与超时仍需显式重试 |

处理器必须配合 CancellationToken，且返回前等待自己启动的工作结束。取消和超时不会强制终止线程，也不会提前释放并发槽。优雅停机或启动恢复会将旧执行记录标为 Interrupted；安全且有剩余次数的任务重新排队，其余保留人工处理。已请求取消的任务始终不会因此复活。

只有业务确实能幂等重跑时才开启 SafeToRetry，例如按稳定 JobId 生成结果、覆盖同一输出键。组件不能保证事务外的邮件或远程调用只发生一次。当前部署限单执行实例，不要让两个宿主同时连接同一个任务数据库并启用执行。

实现流程见 [执行设计](../MS.Microservice.Tasks/docs/execution-design.md)，事务与记录原子性见 [持久化设计](../MS.Microservice.Tasks.EFCore/docs/persistence-design.md)。Coravel API 约定参考 [官方 Scheduler 文档](https://docs.coravel.net/Scheduler/)。
