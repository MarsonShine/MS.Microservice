# Root-Cause Bug Fix Protocol

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
