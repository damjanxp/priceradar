# How AI tools were used

PriceRadar was built with **Claude Code** (an AI coding agent in the terminal) as a pair programmer. This file records
how, and above all where its output was wrong and what caught it. No other AI coding tool was used.

## Workflow

1. **Spec first.** Before any code, Claude Code drafted the specification (`docs/spec/PriceRadar-Spec.md`) from my
   brief. I answered its open questions (scope, sources, phone access) and decided the ethics rules myself, e.g. that
   KupujemProdajem stays a fixture because its terms forbid copying.
2. **One day per session.** Each session started with the day's section of the spec as context, ended with the day's
   "How to test this phase" commands actually run, and a commit I asked for after reading the diff.
3. **Measure, then claim.** Numbers in the README come from runs in `docs/benchmarks/results/`, the golden-set test
   and CI output. Where something could not be verified (no real Claude API call was made), the docs say so.

## Prompts that worked

- *"Read spec §4.3, §4.4, §5 and DAY-1. Scaffold the solution and the EF Core model, then run the Day 1 test commands
  and report each Definition of Done item."* It works because the spec defines "done" as commands with expected output,
  so the agent has to run things instead of declaring success.
- *"Predict the plan shape and logical reads before running the benchmark, then compare."* A wrong prediction is a
  signal that the model of the query is wrong, which is how the 50k-read worktable in the price-history query was
  found (it was not the table scan that everyone expects).
- *"Mark missing numbers as TODO instead of inventing them."* Used for every document with measurements.

## Where the AI output was wrong, and how it was caught

| # | What the AI produced | What caught it | Fix |
|---|---|---|---|
| 1 | A benchmark for parameter sniffing that captured plans with `SET STATISTICS XML`, concluding that every book compiles its own plan | The result contradicted the plan cache; `sys.dm_exec_query_stats` showed `execution_count = 2` for one plan. Capturing the plan had forced the recompiles. | Re-done through the plan-cache DMVs (`db/benchmarks/02-parameter-sniffing.sql`); the misleading result was deleted (DAY-6, `119e1f9`) |
| 2 | Synthetic data with a plain Zipf distribution | Checking the hottest book before benchmarking: one book held 15 % of all 500k snapshots, which would make the "hot book" query unrepresentative | Zipf–Mandelbrot with an offset; hottest book now 8,275 rows (DAY-2/6) |
| 3 | A first fix for the API crash after SQL Server restarts (wait for `state_desc = 'ONLINE'`) | The next restart failed again with error 4060: ONLINE is reported before connections succeed | Also require a successful connection before migrating (DAY-7, `b15975f`) |
| 4 | EF Core queries that compiled but threw at runtime (`GroupBy` over a navigation property; `OrderBy` on a constructor-projected record) | Endpoint checks returned 500; the ProblemDetails `traceId` led to the log line | Group by the FK column; project to an anonymous type first. Integration tests now call both endpoints (DAY-5, `a81bbb8`) |
| 5 | Candidate generation that only looked up rare title tokens, and a normaliser without volume numbers | The 169-row golden set: one-word typo duplicates were never offered as candidates, and "Seobe II" matched volume 1 | Same-author fallback and N7b roman numerals; precision 100 %, recall 88.7 % (DAY-7, `b15975f`) |
| 6 | Pipeline integration test data in which no listing landed in the ambiguous band | Checking which match outcomes the pipeline test produced: none was ambiguous, so the Claude-resolution path in `ResolveJob` was never exercised end to end | A dedicated `kp/test-ambiguous.json` fixture (DAY-7) |
| 7 | A Hangfire test expecting a run to be marked Failed after one `FailedState` | The test failed; Hangfire's `AutomaticRetry` filter turns a `FailedState` into a scheduled retry until the `RetryCount` job parameter reaches the limit, so the filter under test was right and the test was wrong | The test models exhausted retries and covers both paths (DAY-3, `72ab198`) |
| 8 | `.editorconfig` with `end_of_line = lf` and a `_camelCase` rule that also matched static fields | `dotnet format --verify-no-changes`: 731 line-ending and 106 naming errors | Line endings left to `.gitattributes`; PascalCase rules for static/const fields (DAY-8, `0f81d4b`) |
| 9 | A Playwright screenshot script using `wait_for_function` | It failed against the page's own CSP (`'unsafe-eval'` not allowed), which is the CSP working as intended | Wait on responses and selectors instead (DAY-9) |

## What was not delegated

- **Ethics and scope:** which sites may be scraped, and how (robots.txt read literally; KP as fixtures only).
- **Thresholds:** the 0.90 / 0.65 / 0.05 matching thresholds were chosen from the measured grid, trading recall for
  zero wrong auto-matches, because a wrong price verdict is worse than an "ambiguous" one.
- **Interpreting benchmarks:** every before/after claim was checked against the raw result files and the plans.
- **Commits and publishing:** nothing was committed without being asked, and nothing was pushed by the agent.
