---
name: csharp-dotnet-code-checklist
description: "Review existing C#/.NET changes for ASP.NET Core, EF Core, and runtime defects. Complements general diff review; not for implementation or architecture-only advice."
---

# C# / .NET Code Checklist

Review changed .NET behavior using the project's actual target frameworks and conventions. When used with `../review-code-change/SKILL.md`, that skill owns scope and reporting; this one supplies specialist evidence.

## Workflow

1. Start from the diff or highlighted snippets and trace the affected contracts and callers.
2. When target frameworks, nullable settings, analyzers, dependencies, or test/CI conventions are needed and not already known, run the bundled collector (Node.js 22+):

   ```powershell
   node --no-warnings --experimental-strip-types "{baseDir}\scripts\inspect_dotnet_repo.ts" --path "<repo>"
   ```

   Reuse its JSON during the review. Missing runtime or repository context limits those checks; continue reviewing the available code and state material gaps.
3. Prioritize correctness, nullability, async/cancellation, concurrency, disposal and DI lifetimes, security, data access, and measured or demonstrable hot-path regressions.
4. Read `references/review-dimensions.md` for affected areas such as EF Core, ASP.NET Core, DI, tests, trimming/AOT, or modern C# features. Validate suspected defects with focused evidence.

## Output

Use the user's language and requested review format. Otherwise lead with actionable findings ordered by impact: location, defect, consequence, evidence, and smallest credible correction. For a standalone review, use `blocker`, `high`, `medium`, or `minor`; with a general review workflow, use its severity convention.

If no defects are substantiated, say so. Report material unverified areas and checks actually performed; do not emit a checklist of every review dimension. Omit formatting nits, speculative micro-optimizations, and missing-test findings without a concrete behavior risk.
