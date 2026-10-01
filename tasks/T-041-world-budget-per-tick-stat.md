# T-041 — `sim.world` budget test: per-tick mean and p99 (flake fix)

| Field | Value |
|---|---|
| Status | QUEUED (approved by the owner 2026-09-29; test-author, own PR) |
| Module | `sim.world` tests |
| Assigned role | test-author |
| Depends on | none open (T-012 merged; T-037 merged) |
| Spec source | `spec/03-module-map.md` "Budget tests: window and arithmetic" (Q-044, Q-045; PR #68, `7c9a373`); `spec/07-conventions.md` L11, L11a |
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

Change only the measuring and asserting part of the test, to `03` "Budget
tests: window and arithmetic" (PR #68). That section is binding and replaces
this task's earlier 2000-tick design and its test-local p99 rank choice.

1. **Window (Q-044).** "The samples are exactly `n = TICKS_PER_SIM_DAY`
   (14 400) consecutive ticks, one sample per tick. Any 14 400 consecutive
   ticks after warm-up form a window, and the window need **not** start on a
   sim-day boundary." "No shorter window satisfies a budget." "Warm-up ticks
   before the window are allowed and are not sampled." Keep the existing
   unasserted warm-up pass, which the spec permits; the asserted pass is one
   window of 14 400 ticks. If more than one window is measured, the pass
   condition applies to each on its own, never to their union, and windows do
   not overlap.
2. **Measure each tick separately.** The raw sample `d` is "the
   `Stopwatch.GetTimestamp()` difference around one tick's measured work"
   (`Tick` plus the queries), stored in a `long[]` of 14 400 allocated before
   the measured loop. No `TimeSpan`, no floating point, no `Int128` (L11).
3. **Arithmetic (Q-045), `long` only.** With `f = Stopwatch.Frequency`,
   `B = 100` and `n = 14 400`: `u` is `d` converted to whole microseconds
   rounding up, capped at `C = B x n + 1`. "If `d > (long.MaxValue - f + 1) /
   1 000 000`, then `u = C`. Otherwise `u = min((d x 1 000 000 + f - 1) / f,
   C)`."
   - **Mean:** "it passes iff `Σu <= B x n`."
   - **p99:** nearest rank. "Sort the `u` ascending, and `p99 = u[(99 x n +
     99) / 100 - 1]` in integer division." It "passes iff `p99 <= 2 x B`"
     (200 us).
4. **Log on every run, pass or fail.** Inject `ITestOutputHelper` through the
   class constructor (as `FlowStressBudgetTests` does). Write the reported
   mean `(Σu + n - 1) / n` and the reported p99 `p99`, with the two limits,
   before the assertions run.
5. **Keep everything else unchanged:**
   - `BudgetMicrosPerTick = 100`, the same graph, seed and query draws, and
     the same work per tick: 64 `CanReach`, 64 `CanReachVia` and 16 `PathVia`
     at max tier, plus `Tick`;
   - the `sink > 0` assertion and the RNG-touch assertion
     (`Assert.Equal(0, rngService.Touches)`);
   - test name, `[Fact]` and `[Trait("Category", "Budget")]`.
6. **Do not change any budget value.** Do not widen, average away, retry, or
   skip. A failure that survives this change is a real failure and is
   reported, not tuned around.

## Slow tag (L11a)

Checked against the new window, not decided by the Test Author's guess:

- **Rule (a), work.** A Slow test steps "more than 144 000 ticks in total".
  One 14 400-tick window plus a warm-up that is itself at most 14 400 ticks
  is at most 28 800 ticks, well under 144 000. Not Slow by rule (a).
- **Rule (b), time.** An untagged test is Slow only if the duration xUnit
  reports in the PR's normal CI test job is "over 5 s". At the budget mean of
  100 us a 14 400-tick window costs about 1.44 s, and the warm-up pass about
  the same, so about 2.9 s at the budget limit and less for a healthy module.
  So the test starts **untagged**. If the first authoritative CI measurement
  is over 5 s, the Test Author adds `[Trait("Category", "Slow")]` in the same
  PR (a test may carry both `Budget` and `Slow`), and never shortens the
  window to avoid it, because "No shorter window satisfies a budget."

## Tests to pass

This task is the Test Author's own change; there is no separate test grant.

```
tests/sim/world/**
```

## Performance budget

Unchanged: 0.10 ms/tick at max tier (`03`, `18` §18.4), asserted as `Σu <= B x n`
over one 14 400-sample window, with p99 <= 0.20 ms. `tools/SimHarness budget` remains the authoritative measurement
(L11).

## Done when

- [ ] One window of exactly 14 400 per-tick samples after warm-up, `Stopwatch.GetTimestamp`, `long` arithmetic, no `TimeSpan`, floating point or `Int128`
- [ ] `u` computed by the `03` Q-045 rule; passes iff `Σu <= B x n` and `u[(99 x n + 99) / 100 - 1] <= 2 x B`
- [ ] Reported mean `(Σu + n - 1) / n` and p99 written to test output on every run, pass or fail
- [ ] Same work per tick, same RNG-touch assertion, same name; budget value unchanged
- [ ] Slow tag decided by the L11a rules above from the first CI measurement
- [ ] `ci/run-checks.sh` green; the test passes on repeated CI runs
- [ ] No writes outside `tests/sim/world/**`
- [ ] Reviewer approved

## Worker notes

No worker: test-only. `tests/sim/world/**` has no other open branch as of
2026-09-29 (T-037, the last world-test writer, is merged). If the test still
fails on `main` code after this change with a p99 or mean clearly above budget,
that is a `sim.world` performance finding for the team lead, not a test edit.
