# T-041 — `sim.world` budget test: per-tick mean and p99 (flake fix)

| Field | Value |
|---|---|
| Status | QUEUED (approved by the owner 2026-09-29; test-author, own PR) |
| Module | `sim.world` tests |
| Assigned role | test-author |
| Depends on | none open (T-012 merged; T-037 merged) |
| Spec source | `spec/03-module-map.md` "How a budget is measured" (Statistic, Measured); `spec/07-conventions.md` L11 |
| Blocked by | — |

## Writable paths

```
tests/sim/world/**
```

No other file. No `src/`, `spec/`, `ci/` or `.github/` edit. The Test Author
opens its own PR. In practice the only file that changes is
`tests/sim/world/WorldBudgetTests.cs`.

## Why this task exists

`WorldBudgetTests.test_world_budget_tick_and_queries_at_max_tier_within_point_one_ms`
failed in CI three times in two days and passed on every rerun:

- 102 us, run 36479217271;
- 150 us, run 36509227314;
- a third failure, run 36621043280, attempt 1, on `main` after PR #64.

The test times 2000 ticks as ONE aggregate and asserts aggregate / 2000 <= 100
us. One scheduling hiccup inside that window moves the whole mean, and the test
prints its timing only when it fails, so CI never shows the real margin. The
spec's statistic is different: **mean <= budget AND p99 <= 2x budget**, taken
over per-tick samples (`03` "Statistic"). The test does not assert what the spec
states. This task makes it do so.

## What this task does

Change only the measuring and asserting part of the test.

1. **Measure each tick separately.** Take `Stopwatch.GetTimestamp()` before and
   after each tick's work (`Tick` plus the queries) and store the per-tick
   elapsed value in a pre-allocated `long[]` sized to the tick count. Use
   `Stopwatch.Frequency` and `long` arithmetic only: no `TimeSpan`, no floating
   point (L11). Sample storage is allocated before the measured loop.
2. **Assert the spec's statistic** on the asserted pass:
   - mean <= `BudgetMicrosPerTick` (100 us);
   - p99 <= 2 x `BudgetMicrosPerTick` (200 us).
   Compute both in `long` arithmetic from the stored samples. Keep the mean
   as total elapsed over the tick count, converted once, so no per-tick
   rounding is lost.
3. **Log the result on every run, pass or fail.** Inject `ITestOutputHelper`
   through the test class constructor (as `FlowStressBudgetTests` does) and
   write the measured mean and p99 in microseconds, and the two limits, before
   the assertions run, so a failing run and a passing run both show the margin.
4. **Keep everything else unchanged:**
   - `BudgetMicrosPerTick = 100`, and the two-pass structure with pass 0 as the
     unasserted warm-up;
   - the same graph, seed and query draws, and the same work per tick: 64
     `CanReach`, 64 `CanReachVia` and 16 `PathVia` at max tier, plus `Tick`;
   - the `sink > 0` assertion and the RNG-touch assertion
     (`Assert.Equal(0, rngService.Touches)`);
   - test name, `[Fact]` and `[Trait("Category", "Budget")]`. Do not add a
     `Slow` trait: 2000 ticks at about 100 us is far below L11a's limits.
5. **Do not change any budget value.** Budgets are spec, not this task's to
   change. Do not widen, average away, retry, or skip. A failure that survives
   this change is a real failure and is reported, not tuned around.

## Spec mismatch: report, do not improvise

`03` says the statistic is taken "across the day's ticks" on "the max-tier
fixture ... run for one full sim-day". A sim-day is 14 400 ticks
(`08` `TICKS_PER_SIM_DAY`). This module test runs **2000** ticks of a
caller-sized query batch, not a full day of `IWorldSystem` use. Also, `03` does
not define how the 99th percentile is computed from N samples (the rank rule).

The Test Author must therefore:

- keep the existing 2000 ticks per pass; do **not** resize the run to 14 400 or
  any other count;
- take p99 as the sample at zero-based index `ceil(0.99 * N) - 1` of the sorted
  samples, in integer arithmetic (`(99 * N + 99) / 100 - 1`), and say so in a
  one-line comment. This is a test-local choice, not a spec statement;
- state both gaps (tick count versus one sim-day; percentile rank rule) in the
  PR description and in the report, so the team lead can route them to the
  Architect as an open question. Do not edit `spec/open-questions.md`.

If the Test Author finds that keeping 2000 ticks cannot support a meaningful p99
(for example the values are dominated by timer resolution), it stops and
reports; it does not change the sample count on its own.

## Tests to pass

This task is the Test Author's own change; there is no separate test grant.

```
tests/sim/world/**
```

## Performance budget

Unchanged: 0.10 ms/tick at max tier (`03`, `18` §18.4), asserted as mean, with
p99 <= 0.20 ms. `tools/SimHarness budget` remains the authoritative measurement
(L11).

## Done when

- [ ] Per-tick samples via `Stopwatch.GetTimestamp`, `long` arithmetic, no `TimeSpan`, no floating point
- [ ] Asserts mean <= 100 us and p99 <= 200 us; budget value and warm-up pass unchanged
- [ ] Mean and p99 written to test output on every run, pass or fail
- [ ] Same work per tick, same RNG-touch assertion, same name and traits
- [ ] The tick-count and percentile-rank gaps stated in the PR body and the report
- [ ] `ci/run-checks.sh` green; the test passes on repeated CI runs
- [ ] No writes outside `tests/sim/world/**`
- [ ] Reviewer approved

## Worker notes

No worker: test-only. `tests/sim/world/**` has no other open branch as of
2026-09-29 (T-037, the last world-test writer, is merged). If the test still
fails on `main` code after this change with a p99 or mean clearly above budget,
that is a `sim.world` performance finding for the team lead, not a test edit.
