# 当前状态

更新：2026-10-22
代码基线：`master` @ `b8b2d75`；本次仅更新 harness 试点文档与复跑夹具
本次变更：T-20261022-HARNESS-DOCS 验收证据试点；历史业务验证范围保持下述限制

## 仓库

- 分支：`master`；此记录随试点 slice 提交，工作区状态以 `git status` 为准。
- 活动任务：无；T-20261022-HARNESS-DOCS 已完成，保留契约见 [docs/tasks/active.md](tasks/active.md)。`Files` 与 `DataTransfer` 的 3 个 slice 已完成，见 [Files-DataTransfer-TODO.md](Files-DataTransfer-TODO.md)。
- 其余未完成事项以 [framework-optimization-roadmap.md](framework-optimization-roadmap.md) 为准；该文件只记录仍未完成且需要证据的事项。
- 决策记录：[docs/decisions](decisions/README.md)。

## 验证

- 当前试点：[harness 文档恢复验收](validation/T-20261022-HARNESS-DOCS/summary.md)：实际项目检查、临时副本故障演练及已有内容保留检查通过；不改变既有业务验收边界。

- 本次未运行 `dotnet restore/build/test`；变更仅涉及 Markdown 和 Python 演练夹具，未修改 .NET 代码或构建配置。
- 技能副本：`.github/skills/csharp-dotnet-code-checklist` 的 4 个文件与 ai-skills `a69b026` 哈希一致；已提交的评测工作区已移除。
- harness：`harness.py check` 通过（基线 v1，`AGENTS.md` 与 `.github/copilot-instructions.md` 的托管块哈希一致）。
- 可追溯证据：[module-consumption.json](validation/module-consumption.json)（`9c32e98`，2026-09-10），只覆盖模块消费，不含发布验收。
- CI：[dotnet-ci.yml](../.github/workflows/dotnet-ci.yml) 覆盖构建、测试、集成测试、容器冒烟和漏洞扫描；最近一次运行结果在 GitHub Actions 查看，本文件不复制日志。

## CI 摘要

| 项 | 值 |
| --- | --- |
| workflow | `dotnet-ci` |
| 最近记录 | 无（本次未运行，需在 GitHub Actions 查看 `master` 最近一次运行） |
| 记录格式 | commit + 结论 + 日期；不复制日志 |

## 阻塞

- 真实 PostgreSQL／RabbitMQ 容器环境与部署环境未提供，roadmap 中的故障矩阵、发布验收和数据迁移验证仍无法执行。
- 无本次变更引入的阻塞。

## 下一步

1. 本次试点未发现需要通用 trace 记录器的缺口；按摘要中的复跑命令复查即可。后续任务按需要采用验收项与证据映射。
2. 为 roadmap 第一项准备支持的容器环境，执行真实依赖故障矩阵并记录结果。
3. 按实际需求选择下一项候选能力；没有触发条件时不提前开 slice。
4. 合并或发布前更新本文件，并写明验证范围与未跑项。

## 任务与完成率

- VCR：未统计；本次试点只报告验收项结果，不新增完成率口径。
- 范围外改动：0
- 活动任务：无

跨会话约定见 [worktree-handoff.md](worktree-handoff.md)，harness 恢复见 [harness.md](harness.md)。
