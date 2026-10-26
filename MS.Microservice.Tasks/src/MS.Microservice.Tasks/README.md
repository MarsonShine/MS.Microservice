# 任务契约

`MS.Microservice.Tasks` 定义可持久保存的任务提交、状态、进度、取消和执行记录，并提供单实例执行器。业务代码可以依赖本包；EF Core 存储见 [Tasks.EFCore](../MS.Microservice.Tasks.EFCore/README.md)，后台调度接入见 [Tasks.Coravel](../MS.Microservice.Tasks.Coravel/README.md)。核心依赖通用 DI 和日志接口，不依赖 Coravel、数据库或业务模型。

```csharp
using MS.Microservice.Tasks;

var request = JobSubmission.Create(
    jobId: Guid.NewGuid(),
    scope: "tenant-42:user-7",
    handlerName: "report.export",
    handlerVersion: 1,
    payload: new { ReportId = "r-123", Format = "pdf" });

JobSnapshot created = await jobs.SubmitAsync(request, cancellationToken);
JobSnapshot? current = await jobs.GetAsync(request.Scope, created.JobId, cancellationToken);
```

`jobs` 是 `IJobStore`，由宿主选择存储实现。`Create` 立即序列化参数，后续修改原对象不会改变任务。处理器名称和版本是稳定的业务契约；参数应保存文件键、业务 ID 等必要数据，不保存 Stream、请求对象或密钥。

| 参数 | 默认值／限制 | 用途 |
|---|---|---|
| `JobId` | 非空 GUID | 调用者生成一次并保存；重复提交沿用同一 ID |
| `Scope` | 非空，最多 256 字符，有效 Unicode | 租户／用户等隔离边界，按原值区分大小写 |
| `HandlerName` / `HandlerVersion` | 名称最多 128 个 ASCII 字母、数字、`.`、`-`、`_`；版本 ≥ 1 | 标识参数对应的处理器契约 |
| `PayloadJson` | 最多 256 KiB UTF-8 JSON | 冻结的任务参数；也可以直接调用构造函数传入 JSON |
| `Queue` | `default`，最多 64 个同上 ASCII 字符 | 供宿主区分执行资源组 |
| `MaximumAttempts` | 3，范围 1～100 | 允许开始执行的总次数，包含首次 |
| `NotBeforeUtc` | 空，立即可领取 | 首次可执行时间；输入会转换为 UTC |

同一 `Scope + JobId` 再次提交相同请求会返回已有任务。处理器、JSON 字符串、队列、执行次数或首次调度时间不同，会抛出 `JobSubmissionConflictException`；JSON 的空白和属性顺序也参与比较。创建时间、当前状态和后来重试的时间不参与提交身份判断。

可选 `IJobRetentionStore` 支持宿主按 Scope、终态和完成时间分批清理，使用方式见 [EF Core 任务存储](../MS.Microservice.Tasks.EFCore/README.md#终态保留清理)。物理清理会删除任务及其执行历史，也结束该 JobId 的提交幂等保留期；之后相同 Scope、JobId 被视为新任务。已有 `IJobStore` 实现无需实现此可选接口。

| 状态 | 含义 |
|---|---|
| `Queued` | 已保存，等待领取 |
| `Running` | 当前工作或终态持久化尚未结束，可能已请求取消 |
| `Succeeded` / `Failed` | 本次执行成功／失败 |
| `Cancelled` | 排队时取消，或运行中的取消请求已在结束时生效 |
| `Interrupted` | 执行中断，例如宿主停止；由执行方明确记录 |

`CancelAsync` 对运行中的任务只记录取消请求，不会强制终止线程或进程。`RetryAsync` 只接受失败、中断或取消的任务，保留 `JobId` 和历史；用完执行次数后拒绝重试。启用执行器后，普通失败和超时仍等待显式重试；只有注册为 `SafeToRetry` 的处理器允许中断后自动重新排队。外部调用产生的副作用不承诺只执行一次。

`TryStartAsync` 返回当前执行的 `JobAttemptToken`；进度和结束更新必须携带它。进度为 0～100 的整数，同一次执行只能增加。执行入口和跨 Scope 的 `GetDueAsync` 应仅供可信后台代码调用；对外接口应从认证身份取得 Scope，再调用按 Scope 查询或控制的方法。

宿主注册 `IJobHandler<TPayload>`，按明确名称和版本匹配任务；参数可使用普通 DTO 或 JsonElement。每次执行创建独立 DI scope，按 Queue 限制并发，观察持久取消请求并传递 CancellationToken。处理器必须配合取消，且在返回前等待自己启动的子任务结束；执行器不会提前释放仍在工作的资源槽。

终态落库暂时失败时，执行器保留原容量槽，在同一宿主内持续重试存储并核对原 Attempt Token；处理器不会因此重跑。存储恢复后可以继续收敛，无需为此重启。停机取消重试等待，未完成的持久记录仍由下次启动恢复；具体提交结果以存储查询为准。

`AddTaskExecution` 可配置终态重试间隔：`InitialFinalizationRetryDelay` 默认 1 秒，`MaximumFinalizationRetryDelay` 默认 30 秒，要求 `25ms <= initial <= maximum <= 5min`。间隔指数增加并封顶，每个存储操作继续使用独立的 `StorageOperationTimeout`（默认 10 秒）。

存储模型、原子状态转换和事务边界见 [持久化设计](../../docs/persistence-design.md)，处理器执行及恢复见 [执行设计](../../docs/execution-design.md)。
