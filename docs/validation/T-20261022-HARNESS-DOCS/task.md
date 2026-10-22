# 当前任务

Status: completed
Task: harness 必需文档检查与恢复试点
Task ID: T-20261022-HARNESS-DOCS

## 契约

- 来源：可观测性方案评审发现现有 sync/check 不覆盖辅助文档；ai-skills 已复现并修复。
- 行为：必需辅助文档缺失或空白时检查失败；同步补建模板；保留已有非空项目文档。
- In scope：本任务卡、状态页、harness 恢复说明、单任务验收证据与临时副本演练。
- Out of scope：业务代码、全链路追踪、通用记录器、全局流程约束、真实依赖故障矩阵。
- 依赖：ai-skills 修复提交 837c3dd；Python、Git。
- 验收命令：从仓库根运行 `python docs/validation/T-20261022-HARNESS-DOCS/replay.py --skills-root <ai-skills-checkout>`，并运行该 checkout 的 `harness.py check --root .`。
- 证据：[单任务摘要](summary.md)。

## 验收项

| 编号 | 通过标准 | 证据 | 结果 |
| --- | --- | --- | --- |
| A1 | 实际项目通过修复后引擎的检查 | P1 | 通过 |
| A2 | 临时副本缺任务卡时旧引擎漏检，新引擎拒绝；同步后恢复 | P2 | 通过 |
| A3 | 状态页、恢复说明、PR 模板内容保持字节不变 | P2 | 通过 |
| A4 | 证据可复跑，任务卡和状态页引用统一摘要 | P3 | 通过 |

同一时间只允许一个 `in_progress`。完成后保留本次契约供审查；下一任务替换前将结果保存在证据摘要中。
