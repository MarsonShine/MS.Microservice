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

## Project

- Stack: .NET 10 (`global.json` 10.0.401); PostgreSQL, RabbitMQ, and Keycloak for local runs.
- Build: `dotnet restore --configfile nuget.config`, then `dotnet build --no-restore -c Release`.
- Test: `dotnet test --no-build -c Release`; integration, container, and vulnerability checks are in `.github/workflows/dotnet-ci.yml`.
- Commands: [README](README.md) and [docs/Getting-Started.md](docs/Getting-Started.md) are authoritative; do not copy them here.
- Boundaries: shared modules must not depend on Reference, Lab, or concrete User/Order models; Web is the composition root; schema changes go through the explicit DatabaseMigrator, never startup DDL.
- State: [docs/status.md](docs/status.md); active task: [docs/tasks/active.md](docs/tasks/active.md); harness recovery: [docs/harness.md](docs/harness.md).
