# MS.Microservice.Tasks

统一任务中心提供持久任务提交、执行历史、进度、取消、重试及单实例恢复。Coravel 驱动执行，Dashboard 为可选的可视化管理组件。

执行器在终态落库失败时保留原容量槽，等待存储恢复并持续核对原执行，不重跑已经结束的处理器。停机结束在线收敛，遗留持久执行仍遵循启动恢复规则，详见下方执行设计。

打开 [MS.Microservice.Tasks.slnx](MS.Microservice.Tasks.slnx)：源码位于 `src`，测试位于 `test`，技术文档位于 `docs`。在本目录执行：

```powershell
dotnet test MS.Microservice.Tasks.slnx -c Release
```

| 组件 | 用途与接入说明 |
| --- | --- |
| [Tasks](src/MS.Microservice.Tasks/README.md) | 任务契约、状态与单实例执行器，不依赖数据库或 Coravel |
| [Tasks.EFCore](src/MS.Microservice.Tasks.EFCore/README.md) | 复用宿主 DbContext，支持独立提交和现有事务内暂存 |
| [Tasks.Coravel](src/MS.Microservice.Tasks.Coravel/README.md) | 复用宿主 Scheduler，驱动任务执行和中断恢复 |
| [Tasks.Dashboard](src/MS.Microservice.Tasks.Dashboard/README.md) | 挂载任务管理 UI，查看 Coravel 注册计划 |

实现细节见 [执行与恢复](docs/execution-design.md)、[持久化与事务](docs/persistence-design.md)、[面板与计划观察](docs/dashboard-design.md)。消息事务测试通过独立解决方案的 Dependencies 引用已有 Messaging 组件，不复制消息实现。

生产宿主负责数据库迁移、认证 Scope、处理器幂等、并发预算及保留期。可选 `IJobRetentionStore` 按 Scope、终态和截止时间执行有界清理，成功和失败可使用不同保留期；默认不注册自动清理服务。物理删除结束该 JobId 的提交幂等保留期，详见 [EF Core 保留清理](src/MS.Microservice.Tasks.EFCore/README.md#终态保留清理)。当前执行部署限单实例；可运行的 API 和界面示例见 [Lab](../samples/Lab/MS.Microservice.Lab/README.md#任务中心可视化面板)。源码导出与本地包消费见 [组件接入](../docs/components/consumption.md)。
