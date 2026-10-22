# Harness 接入与恢复

版本：v1
更新时间：2026-10-21
本文件是本仓库的 harness 恢复记录。即使所有 AI 工具都不可用，按这里的步骤也能恢复工作协议。

## 四层结构

| 层 | 位置 | 作用 |
| --- | --- | --- |
| 全局触发 | `~/.dsh/AGENTS.md`、`~/.codex/AGENTS.md`、`~/.claude/CLAUDE.md` | 发现仓库缺少托管块时提醒初始化 |
| 引擎 | `~/.agents/harness/`（`harness.py` + `templates/`） | 执行 `init / adopt / sync / check` |
| 项目 | `AGENTS.md`、`CLAUDE.md`、`.github/copilot-instructions.md` | 托管块、适配器、项目事实 |
| 技能 | ai-skills 的 `bootstrap-harness` | 让 agent 知道何时调用引擎 |

规则只有一份：`AGENTS.md` 的 `harness:managed` 块。`CLAUDE.md` 用 `@AGENTS.md` 导入；`.github/copilot-instructions.md` 嵌入同一段正文，供不跟随指针的宿主使用。

## 本仓库接线

- `AGENTS.md`：canonical 托管块 + `## Project` 项目事实。
- `CLAUDE.md`：`@AGENTS.md`。
- `.github/copilot-instructions.md`：顶部同一托管块。
- [docs/status.md](status.md)：当前分支、验证、阻塞、下一步、VCR。
- [docs/tasks/active.md](tasks/active.md)：当前唯一任务契约。
- [.github/pull_request_template.md](../.github/pull_request_template.md)：任务、验收、范围检查。
- 队列与未完成事项：[framework-optimization-roadmap.md](framework-optimization-roadmap.md)。
- 设计取舍：[docs/decisions](decisions/README.md)。

## 命令

```powershell
python "$HOME/.agents/harness/harness.py" status
python "$HOME/.agents/harness/harness.py" init --write --auto
python "$HOME/.agents/harness/harness.py" adopt --write
python "$HOME/.agents/harness/harness.py" sync --write
python "$HOME/.agents/harness/harness.py" check
```

`init` 只用于新仓库或空 `AGENTS.md`；已有内容用 `adopt`。所有写操作先看 diff，不自动提交。

## 必需文档恢复与证据试点

使用包含 ai-skills `837c3dd` 修复的引擎时，`check` 会拒绝缺失或空白的任务卡、状态页、恢复指南和 PR 模板。`sync --write` 仅补建这些缺失／空白文件，保留已有非空项目文档；历史任务结果应从 Git 或备份恢复。

源模板更新不会迁移已有项目文档。新字段需要增量编辑，不能将 `harness check` 通过解释为证据有效或业务正确。

本仓库以 [T-20261022-HARNESS-DOCS](validation/T-20261022-HARNESS-DOCS/summary.md) 试点“验收项 → 可复查证据 → 判断”。任务卡和状态页引用同一摘要；实际输出与人工判断分开记录。该试点不要求所有任务建立 trace，也不增加 VCR 统计。

## 只读会话收尾检查

安装带 closeout 子命令的 ai-skills 引擎后，从仓库根运行：

```powershell
python "$HOME/.agents/harness/harness.py" closeout --root . --state docs/status.md --task docs/tasks/active.md
```

完成任务并归档后追加 `--complete`。有本次未提交文件时，用重复的 `--scope <路径>` 声明范围；`--base <会话基线>` 同时检查本次已提交改动。原有改动用 `--preserve "路径=保留原因"` 说明；只对明确属于本任务的临时路径使用 `--scratch <路径>`。

命令只读并输出 JSON：退出码 0 表示所选结构检查通过，1 表示检查失败。它检查 Status/Task/Next、状态页与任务卡一致性、内联文件链接及未说明的改动；不会验证业务正确性、远端 CI 或全部忽略文件，也不会删除文件、执行文档命令或修改托管块。blocked 交接还需 Blocker；未完成交接可通过普通检查，但不能通过 --complete。

任务完成后将契约和结果保存到 docs/validation/<task>/，再把状态页和活动入口设为 idle/none。详细生命周期见 [跨会话约定](worktree-handoff.md)。

## 没有 AI 工具时

1. 打开 `AGENTS.md`，保留 `## Working Agreement` 到 `<!-- /harness:managed -->` 的内容。
2. 托管块丢失时，从下方“基线快照”复制正文；标记和哈希可在下次运行 `sync --write` 时补上。
3. 确认 `CLAUDE.md` 含 `@AGENTS.md`。
4. 确认 `.github/copilot-instructions.md` 顶部有同一段托管块。
5. 在 `docs/status.md` 写当前分支、验证、阻塞、下一步。
6. 在 `docs/tasks/active.md` 只保留一个 `in_progress` 任务，并写验收命令。

如果连 Python 也没有，只能手工维护上述文件；此时在 `docs/status.md` 的验证一栏写明“未运行 harness check”。

## 基线快照（v1）

## Working Agreement

- Keep exactly one task `in_progress`. Do not start a second task until the first has passing acceptance evidence.
- Before editing, state the task's in-scope paths, out-of-scope work, and acceptance command.
- A change outside the declared scope is a scope violation: revert it or split it into a new task.
- Done means the acceptance command passed and its output is recorded. Code inspection is not evidence.
- A one-file edit may skip the task card, but must still state its acceptance check in the commit or report.
- Record the current state in `docs/status.md` and the active task in `docs/tasks/active.md`.
- Project facts belong in `README.md` and the `## Project` section. Do not copy commands into this block.
- This block is generated. Change the baseline and run `harness sync`; do not edit it by hand.

## 注意事项

- 不要手工编辑托管块；改基线后运行 `sync`。
- README 是项目事实，托管块只链接，不复制命令。
- 已有 `AGENTS.md` 只 `adopt`，不自动合并。
- 自动初始化只在新/空仓库、干净工作区执行，且不自动提交。
- 哈希不一致时 `check` 失败；版本落后只警告。
- 规则变更先改 ai-skills 的 `bootstrap-harness/templates`，再同步到本仓库，不要在项目里单独改一份。

## 为什么记录在这里

本仓库既是生产参考，也是学习轨迹。harness 的规则、适配器和恢复步骤都落在仓库里，而不是只存在于某个 AI 工具的全局目录，这样换工具、重装系统或完全离线时仍然可读、可恢复。设计原因见 [0004-adopt-harness-baseline.md](decisions/0004-adopt-harness-baseline.md)。
