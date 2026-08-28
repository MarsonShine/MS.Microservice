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

Do not add a hardcoded value, special case, or narrow conditional solely to make the reported example pass. A conditional is acceptable only when it represents a genuine business, domain, security, or compatibility rule and is covered by tests.

Prefer restoring the violated invariant in shared logic, data models, state transitions, normalization, validation, APIs, or architectural boundaries. Do not over-generalize or refactor unrelated code without evidence.

## Implementation and validation

- Add or update a regression test that captures the violated invariant.
- Validate a relevant case matrix, including:
  - The original failure
  - Variations of the original input or state
  - Boundary cases
  - Opposite or inverse cases when applicable
  - Normal/common cases
  - Previously working behavior that could regress
  - At least three relevant unseen cases
- Run focused tests and the appropriate broader test suite.
- Actively identify an unseen case that could still fail.
- If a related variant fails, stop stacking patches. Re-evaluate the root cause and fix layer.

## Completion criteria

The fix is complete only when the invariant is restored, the broader failure class is handled, relevant unseen cases pass, existing valid behavior does not regress, and no case-specific workaround was introduced without a justified domain rule.

In the final report, summarize the root cause, invariant, fix layer, changed behavior, tests performed, and any remaining uncertainty.
