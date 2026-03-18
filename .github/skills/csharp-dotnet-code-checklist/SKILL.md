---
name: csharp-dotnet-code-checklist
description: Review C#, ASP.NET Core, and .NET 10 pull requests or code diffs with a practical checklist focused on correctness, nullability, async/await, EF Core, DI, security, performance, observability, and tests. Use this skill whenever the user asks for a C#/.NET code review, PR review, diff review, checklist-based review, best-practices validation, 架构审查, or wants actionable findings instead of style-only comments.
compatibility: Best when a git diff, PR patch, changed files, or file paths are available. Can also review full files when no diff is provided.
---

# C# / .NET 10 Code Checklist

Use this skill to review `C#`, `ASP.NET Core`, and `.NET 10` changes like a pragmatic senior engineer. The goal is to catch real defects, risky patterns, and maintainability issues that matter in production, not to flood the user with formatter-level nits.

Default to **PR / diff review**. Read changed code first, then pull only the nearby context needed to verify behavior, contracts, and framework usage.

## Review workflow

1. **Scope the review before judging the code.**
   - Prefer the diff, changed files, or the specific snippets the user highlighted.
   - If repository context is available, inspect project signals before making style or framework recommendations:
     - `*.csproj`
     - `Directory.Build.props`
     - `.editorconfig`
     - analyzer packages and severity configuration
     - nullable settings
     - ASP.NET Core / EF Core usage patterns

2. **Identify the code shape.**
   - API endpoint / controller / minimal API
   - domain or application service
   - background worker
   - data access / EF Core
   - infrastructure / integration code
   - tests

3. **Prioritize high-risk review lenses first.**
   - correctness and domain behavior
   - nullability and contracts
   - async / concurrency / cancellation
   - resource lifetime and disposal
   - security and data exposure
   - performance on likely hot paths

4. **Only report evidence-based findings.**
   - Anchor each finding to the changed code or directly adjacent context.
   - If context is missing, state the assumption instead of presenting a guess as fact.
   - Prefer comments that explain impact and a likely fix.

5. **Respect project conventions unless they are actively harmful.**
   - Do not force new syntax just because it exists.
   - Favor platform defaults, analyzers, and the repository's established patterns.
   - If the project is clearly using modern `.NET 10` / `C#` features, align with that direction.

6. **Respond in the user's language.**
   - If the user writes in Chinese, review in Chinese.
   - If the user writes in English, review in English.

## What counts as a strong finding

Raise issues when they are likely to cause one of these:

- incorrect behavior or broken invariants
- runtime failures, null bugs, race conditions, or deadlocks
- disposal / lifetime bugs
- security leaks or missing access control
- expensive queries or avoidable hot-path regressions
- hard-to-operate behavior such as weak logging, missing cancellation, or swallowed exceptions
- fragile tests or behavior changes without coverage

Do **not** over-report:

- formatting-only differences already handled by the repo
- speculative micro-optimizations outside meaningful paths
- large architecture rewrites unrelated to the change
- personal preference comments without a real maintenance or correctness payoff

## Severity rubric

Use these severities:

- `blocker`: likely production outage, security breach, data corruption, or guaranteed runtime failure
- `high`: strong correctness, reliability, or security risk
- `medium`: important maintainability, performance, or operability concern
- `minor`: only include when the user asked for exhaustive review

## Output format

Always use this structure:

### Review summary
- Scope:
- Overall risk:
- Top themes:

### Findings
1. `[severity]` Short title
   - Where: `path:line` or `snippet`
   - Why it matters:
   - Evidence:
   - Suggested change:

### Checks performed
- correctness / invariants
- contracts / validation
- nullability
- async / cancellation / concurrency
- disposal / lifetimes
- data access
- security
- performance
- diagnostics / tests

### Residual risks / follow-ups
- Mention missing context, unanswered questions, or areas not fully verified.

If there are no meaningful findings, say so clearly and still list the checks you performed plus any residual uncertainty.

## Checklist lenses

Apply these lenses as relevant to the code under review:

1. **Correctness and domain behavior**
   - Validate input and boundary conditions.
   - Watch for incorrect defaults, missing state transitions, and hidden behavior changes.

2. **API contracts and validation**
   - Public methods and endpoints should have clear contracts.
   - Prefer DTOs and typed responses over leaking persistence entities or raw exceptions.

3. **Nullability and contracts**
   - Respect nullable reference types.
   - Treat casual `!` suppression as suspicious unless the proof is obvious.
   - Call out missing `ArgumentNullException`, incorrect nullability, or weak `Try*` contracts.

4. **Async, concurrency, and cancellation**
   - Flag `.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `async void`, fire-and-forget work, and missing `CancellationToken` propagation.
   - Watch for shared mutable state across requests or background loops.

5. **Resource lifetime and disposal**
   - Look for incorrect `IDisposable` / `IAsyncDisposable` usage, per-call `HttpClient`, leaking streams, and scoped services captured by singletons.

6. **DI, configuration, and options**
   - Prefer explicit dependencies and validated options over deep `IConfiguration` lookups or service locator patterns.
   - Check service lifetimes for `Transient` / `Scoped` / `Singleton` mismatches.

7. **EF Core and data access**
   - Watch for early materialization, missing `AsNoTracking()` on read paths, `SaveChanges` in loops, N+1 patterns, and missing cancellation tokens.

8. **ASP.NET Core concerns**
   - Review auth, authorization, `ProblemDetails`, input validation, pagination/streaming, and structured logging.

9. **Security and secrets**
   - Flag secret leakage, unsafe raw SQL, missing access checks, path handling problems, or internal exception text returned to clients.

10. **Performance and allocation**
    - Focus on real costs: large object graphs, repeated serialization setup, unbounded concurrency, unnecessary allocations in loops, and trim/AOT-hostile reflection when the project signals NativeAOT or trimming.

11. **Diagnostics and test coverage**
    - Failures should be observable.
    - Important behavior changes should have useful tests, not only happy-path checks.

For deeper examples and review anchors, load `references/review-dimensions.md`.

## .NET 10 and modern C# guidance

Use modern platform defaults when they improve correctness, clarity, or operational safety:

- nullable reference types
- `await using` and `IAsyncDisposable`
- `CancellationToken` propagation
- `IAsyncEnumerable<T>` where streaming is appropriate
- `ProblemDetails` / typed results in web APIs
- `IOptions<T>` / validated options for configuration
- `System.Text.Json` and other built-in platform capabilities before custom infrastructure
- `TimeProvider` for time-sensitive logic and tests when it reduces flakiness

Treat new language features, including `C# 14` features, as optional tools rather than mandatory style rules. Recommend them only when they reduce accidental complexity or make contracts clearer.

## Review style

- Be concise and specific.
- Quote only the minimum code needed.
- Prefer "this is risky because..." over "this is wrong."
- Offer concrete fixes, not vague advice.
- Praise good patterns briefly when it helps build trust, but keep the focus on actionable review feedback.
