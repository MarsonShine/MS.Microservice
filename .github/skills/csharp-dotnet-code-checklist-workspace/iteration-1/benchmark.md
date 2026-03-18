# Skill Benchmark: csharp-dotnet-code-checklist

**Model**: gpt-5.4
**Date**: 2026-03-18T02:47:21Z
**Evals**: 1, 2, 3 (1 runs each per configuration)

## Summary

| Metric | With Skill | Without Skill | Delta |
|--------|------------|---------------|-------|
| Pass Rate | 100% ± 0% | 83% ± 29% | +0.17 |
| Time | 41.7s ± 15.6s | 25.0s ± 2.0s | +16.7s |
| Tokens | 3076 ± 512 | 1510 ± 557 | +1566 |

## Notes

- The skill improved total pass rate from 83.3% to 100% in this iteration, with the clearest gain on the EF Core review eval where the skill expanded the review beyond the obvious query regression.
- Eval 1 and Eval 3 were non-discriminating in this run: both with-skill and baseline outputs satisfied all current assertions, so those evals validate minimum coverage more than unique skill value.
- With-skill runs were slower and longer on average because the skill encouraged broader, more structured reviews. The added cost bought more complete issue coverage on Eval 2.
- The benchmark's token column is an output-size proxy derived from `metrics.json` because `read_agent` did not expose total_tokens for these runs. Duration values are real elapsed times from the completed agents.