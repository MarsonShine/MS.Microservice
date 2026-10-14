# 任务面板接入

`MS.Microservice.Tasks.Dashboard` 提供按 Scope 隔离的任务查询、取消／重试接口，以及 Coravel 定时计划目录。组件只依赖任务契约、ASP.NET Core 和 Coravel，不绑定 EF Core 或 Lab，不注册第二个调度器。

```csharp
using MS.Microservice.Tasks.Dashboard;

// IJobStore、Coravel Scheduler 与宿主认证已注册。
services.AddTaskDashboard(options =>
{
    options.ResolveScope = context => context.User.FindFirst("task_scope")?.Value;
    options.AuthorizationPolicy = "TaskDashboard";
    options.ControlAuthorizationPolicy = "TaskOperators";
    options.SchedulesAuthorizationPolicy = "TaskOperators";
});

app.UseAuthentication();
app.UseAuthorization();
app.MapTaskDashboard("/task-center");

// 替换原来的 UseScheduler 调用，保留原有计划配置。
app.Services.UseSchedulerWithTaskDashboard(scheduler =>
{
    scheduler.Schedule<DailyReport>().DailyAt(9, 0)
        .PreventOverlapping("daily-report").WithDashboardName("每日业务报表");
});
```

`ResolveScope` 必须与任务提交时使用的 Scope 一致，只从宿主认证／授权信息生成。没有 Scope 的认证用户返回 403；组件不从 URL 或请求体接受 Scope，也不返回载荷 JSON、Scope 或内部执行令牌。宿主仍负责 `IJobStore` 的注册与生命周期。

| 接口（相对于挂载路径） | 行为 |
| --- | --- |
| `GET /api/session` | 返回当前操作权限与 ASP.NET Core 防伪令牌 |
| `GET /api/jobs?state=Running&take=100` | 当前 Scope 最近任务，默认 100、最多 200；`hasMore` 表示还有更早结果 |
| `GET /api/jobs/{id}` / `attempts` | 单任务及执行历史；可以直接按 ID 查询较早任务 |
| `POST /api/jobs/{id}/cancel` / `retry` | 调用现有持久状态转换，保留取消、次数和幂等约束 |
| `GET /api/schedules` | 通过观察入口注册的 Coravel 计划配置，只读 |

控制请求须先取得 session 防伪令牌，并携带相应 Cookie 与 session 返回的防伪 Header；宿主可额外指定控制权限。`ReadOnly=true` 禁用取消和重试。三个 Policy 留空时使用宿主默认认证策略；Schedules 是宿主级计划目录，需限制查看者时配置 `SchedulesAuthorizationPolicy`。

Coravel 6.0.2 没有公开计划枚举接口，`UseSchedulerWithTaskDashboard` 在调用原生 fluent API 时同步记录成功配置。不通过此入口注册的计划不会显示；目录记录注册配置，不表示运行状态，一次性计划执行移除后仍可能保留该配置。自定义条件不在查看时执行，不推算下一次运行，也不提供计划编辑、暂停或手动执行。

任务列表和统计只覆盖当前选中状态的最近窗口，不承诺全库搜索、精确总数或分页扫描。Coravel 计划目录在内存中随宿主重新注册，任务及执行历史沿用 `IJobStore` 的持久化。实现边界见 [面板设计](docs/dashboard-design.md)。
