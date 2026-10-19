# Project Skills

Copilot loads project skills from this directory. They are copies, not the source of truth.

| 技能 | 来源 | 同步版本 | 说明 |
| --- | --- | --- | --- |
| `csharp-dotnet-code-checklist` | ai-skills `skills/csharp-dotnet-code-checklist` | `a69b026`，2026-10-19 | 完整复制：`SKILL.md`、`references/`、`scripts/`、`evals/` |
| `baoyu-translate` | 外部 | 未记录 | 第三方，不在此仓库修改 |
| `baoyu-url-to-markdown` | 外部 | 未记录 | 第三方，不在此仓库修改 |
| `mcp-builder` | 外部（自带 LICENSE.txt） | 未记录 | 第三方，不在此仓库修改 |
| `prompt-builder` | 外部 | 未记录 | 第三方，不在此仓库修改 |
| `skill-creator` | 外部（自带 LICENSE.txt） | 未记录 | 第三方，含评测脚本，不在此仓库修改 |

评测输出不属于技能。`csharp-dotnet-code-checklist-workspace/` 已移除；需要重放时在 ai-skills 的 `workspaces/` 下生成，并记录基线 commit。

## 同步规则

1. 自研技能只在 ai-skills 修改，然后复制到这里并更新上表。
2. 第三方技能不在此仓库内修改；替换时记录来源和日期。
3. 删除技能前先确认没有文档、工作流或指令文件引用它。
