# Root-Cause Bug Fix Protocol

<!-- harness:managed v1 sha256:4748970e5308f31520e3ca033395a1248dd88a993327b29cc9a56f36b9784f50 -->
## Working Agreement

- Keep exactly one task `in_progress`. Do not start a second task until the first has passing acceptance evidence.
- Before editing, state the task's in-scope paths, out-of-scope work, and acceptance command.
- A change outside the declared scope is a scope violation: revert it or split it into a new task.
- Done means the acceptance command passed and its output is recorded. Code inspection is not evidence.
- A one-file edit may skip the task card, but must still state its acceptance check in the commit or report.
- Record the current state in `docs/status.md` and the active task in `docs/tasks/active.md`.
- Project facts belong in `README.md` and the `## Project` section. Do not copy commands into this block.
- This block is generated. Change the baseline and run `harness sync`; do not edit it by hand.
<!-- /harness:managed -->

Current state: [docs/status.md](docs/status.md). Decisions: [docs/decisions/](docs/decisions/README.md). Remaining work: [docs/framework-optimization-roadmap.md](docs/framework-optimization-roadmap.md). Harness recovery: [docs/harness.md](docs/harness.md).

Start from the current-state and active-task links. Follow [session lifecycle](docs/worktree-handoff.md) for closeout and resumption; open historical material only when needed.

When handling a bug, failure, unexpected behavior, or edge case, do not optimize only for the reported example. Treat it as evidence of a potentially broader defect.

## Before editing code

1. Inspect the relevant implementation, call chain, data flow, existing tests, logs, and configuration.
2. Briefly report:
   - Root cause, separating evidence from hypotheses
   - Violated invariant or contract
   - Broader failure class and likely variants
   - Correct fix layer: the lowest appropriate layer that owns the invariant and is shared by affected paths
   - Proposed fix, expected interactions, and regression risks
3. Do not modify code until this analysis is complete.

## Anti-patch rule

Do not add a hardcoded value, special case, or narrow conditional solely to make the reported example pass. A conditional is acceptable only when it represents a genuine business, domain, security, or compatibility rule and has credible regression evidence.

Prefer restoring the violated invariant in shared logic, data models, state transitions, normalization, validation, APIs, or architectural boundaries. Do not over-generalize or refactor unrelated code without evidence.

## Implementation and validation

- Use the smallest sufficient verification for the affected contract; choose cases by root cause and impact, not fixed quotas.
- Reuse existing checks and valid results. Add tests only for concrete regression gaps worth maintaining.
- Preserve explicit testing requirements, established TDD, and required gates; expand checks only for concrete shared or high-risk impact.
- Stop when evidence is sufficient. Revisit the fix layer if related cases fail; disclose unavailable checks without prolonged tooling repair.

## Completion criteria

Complete the fix when the invariant is restored and credible evidence covers the affected behavior and relevant regression risks. Disclose unverified behavior; an unavailable check is not a pass.

Briefly report the root cause, invariant, fix layer, changed behavior, verification performed or reused, and remaining uncertainty.

## Markdown 文档配图

编写或更新本仓库的文档时，如果图能帮助读者看清数据如何流动、调用按什么顺序发生，或两组结果相差多少，就主动配图。根据读者要弄懂的问题，优先选择适合目标 Markdown 渲染器的格式：

- **Mermaid** 适合流程、调用时序、状态转换和组件关系。目标渲染器支持时，直接使用 `mermaid` 代码块，方便随正文一起修改。
- **Vega-Lite / Plotly** 适合用数据说明趋势、分布和性能对比。标明单位和测量条件。若目标渲染器无法直接显示图表，就导出静态 SVG 并用相对路径嵌入，同时保留可编辑的图表定义。
- **SVG** 适合需要精确布局的示意图，例如缓冲区中的字节如何排列、对象由谁持有，或优化前后哪一步消失了。将 SVG 放在文档旁的资源目录，用带替代文本的 Markdown 图片语法引用。

把图放在它解释的段落、代码或数据旁。图中沿用源码里的方法名、变量名和正文里的示例数值，用一句话指出读者应关注的变化。交付前在目标 Markdown 预览中检查图能否显示、标签是否清晰，并核对箭头方向、数值和单位。

## Project

- Stack: .NET 11 (`global.json` 11.0.100); PostgreSQL, RabbitMQ, and Keycloak for local runs.
- Build: `dotnet restore --configfile nuget.config`, then `dotnet build --no-restore -c Release`.
- Test: `dotnet test --no-build -c Release`; integration, container, and vulnerability checks are in `.github/workflows/dotnet-ci.yml`.
- Commands: [README](README.md) and [docs/Getting-Started.md](docs/Getting-Started.md) are authoritative; do not copy them here.
- Boundaries: shared modules must not depend on Reference, Lab, or concrete User/Order models; Web is the composition root; schema changes go through the explicit DatabaseMigrator, never startup DDL.
- State: [docs/status.md](docs/status.md); active task: [docs/tasks/active.md](docs/tasks/active.md); harness recovery: [docs/harness.md](docs/harness.md).
