# 0004：接入 Harness v1 基线

状态：已采纳
日期：2026-10-21

## 背景

- 工作协议（WIP=1、任务契约、完成证据）此前散落在对话和个别文档中，换项目或换 agent 就要重新交代。
- 本仓库既是生产参考，也是学习轨迹，需要在没有任何 AI 工具时仍能恢复规则。
- 各宿主读取的指令文件不同：Codex/DSH 读 `AGENTS.md`，Claude 读 `CLAUDE.md`，Copilot 读 `.github/copilot-instructions.md`。

## 决策

采用 ai-skills 的 harness v1 基线：

- `AGENTS.md` 保存 canonical 托管块与 `## Project` 项目事实。
- `CLAUDE.md` 通过 `@AGENTS.md` 导入。
- `.github/copilot-instructions.md` 顶部嵌入同一段托管块。
- `docs/status.md` 记录状态与 VCR，`docs/tasks/active.md` 只保留一个活动任务。
- `docs/harness.md` 记录四层结构、同步命令和离线恢复步骤。

托管块由 `~/.agents/harness/harness.py` 生成与校验，不手工编辑。

## 否决方案

- 只放在用户级全局指令：仓库评审和 CI 看不见，换机器就丢失。
- 每个宿主各维护一份完整规则：必然漂移。
- 在项目内单独维护一份规则：与 ai-skills 基线脱节。
- 引入 `.harness.json` 清单：没有 agent 会读，增加工具专属感。

## 后果

- 基线升级需要在本仓库运行 `sync --write`，diff 只应出现在托管块。
- `check` 失败表示托管块被手改；版本落后只警告。
- README 继续作为命令事实来源，托管块只链接，不复制。
- 没有 Python 或 AI 工具时，按 `docs/harness.md` 手工恢复，并在 `docs/status.md` 记录“未运行 harness check”。

## 复审条件

宿主开始原生读取其他指令文件时，增加适配器而不是复制规则；基线出现 v2 时，按同一流程同步并更新本记录。
