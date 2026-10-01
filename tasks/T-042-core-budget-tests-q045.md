# T-042 — `sim.core` budget tests: conform to the `03` window and arithmetic rule

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` tests |
| Assigned role | test-author |
| Depends on | none open (spec PR #68, `7c9a373`, merged) |
| Spec source | `spec/03-module-map.md` "Budget tests: window and arithmetic" (Q-044, Q-045), "Measured" (`sim.core` sample); `spec/07-conventions.md` L11, L11a |
| Blocked by | — |

## Writable paths

```
tests/sim/core/**
```

No other file. No `src/`, `spec/`, `ci/` or `.github/` edit. The Test Author
opens its own PR. `tests/sim/core/**` has no other open writer: T-009's (merged #74), T-013's and
T-030's tests moved to `tests/tools/simharness/**` (`19` §19.6, Q-041; synced
2026-10-01). Check the queue before release anyway.

## Why this task exists

PR #68 made `03` binding on "every xUnit test that asserts a time against a
per-tick budget of the table above". The `sim.core` tests below do not follow
it. Per the team lead's audit: two timed tests in
`tests/sim/core/BudgetTests.cs` (the empty-day and busy-day tests that compare
one whole-day total against `14400L * BudgetMicrosPerTick`), and one in
`tests/sim/core/CommandQueueTests.cs` (the day-total
`Stopwatch.Frequency * TicksPerDay / 4000` check). The Test Author confirms the
list by reading every `Budget`-trait test in `tests/sim/core/**` and rewriting
each that asserts a time. A `Budget` test that asserts only allocation is out
of scope: "does **not** bind a `Budget`-trait test that asserts only
allocation".

## What this task does

Make each timed `Budget` test follow `03` exactly. "Any other timed check that
does not follow these rules carries no `Budget` trait, and it does not satisfy
a budget."

1. **Sample.** `sim.core` "has no `Tick` (Q-044). Its sample is one
   `ISimHost.Step(1)` call on a host whose registered systems are the test's
   probes, with checkpoint ticks included." Keep the probes' work to what
   exercises the loop, commands and event dispatch.
2. **Window.** "exactly `n = TICKS_PER_SIM_DAY` (14 400) consecutive ticks, one
   sample per tick", after any warm-up, which is not sampled. "No shorter
   window satisfies a budget." Several windows are judged one by one, not as a
   union, and do not overlap.
3. **Arithmetic, `long` only** (no `Int128`, no floating point, no `TimeSpan`).
   `f = Stopwatch.Frequency`, `B` the budget in whole microseconds (250),
   `C = B × n + 1`. "If `d > (long.MaxValue − f + 1) / 1 000 000`, then
   `u = C`. Otherwise `u = min((d × 1 000 000 + f − 1) / f, C)`."
   - mean: "it passes iff `Σu ≤ B × n`";
   - p99: "Sort the `u` ascending, and `p99 = u[(99 × n + 99) / 100 − 1]` in
     integer division ... It passes iff `p99 ≤ 2 × B`";
   - report `(Σu + n − 1) / n` and the p99 to test output on every run.
4. Keep names, fixtures and probes, the budget value (250 us) and the
   allocation assertions unchanged. Do not widen, average away, retry or skip.
   Sample storage is allocated before the measured loop.

## Slow tag (L11a)

Rule (a): a window of 14 400 `Step(1)` calls plus a warm-up is far under 144 000
ticks, unless a test measures more than 9 windows or warms up for more than
10 sim-days in total. Rule (b): judge from the first authoritative CI
duration (over 5 s means `Slow`, added beside `Budget`). The window is never
shortened to avoid the tag.

## Tests to pass

This task is the Test Author's own change. No separate grant.

```
tests/sim/core/**
```

## Performance budget

Unchanged: `sim.core` 0.25 ms/tick (`03`).

## Done when

- [ ] Every timed `Budget` test in `tests/sim/core/**` samples one `Step(1)` per tick over exactly 14 400 consecutive ticks
- [ ] `long` arithmetic only, per `03` Q-045; mean `Σu ≤ B × n`, p99 by nearest rank `≤ 2 × B`
- [ ] Reported mean `(Σu + n − 1) / n` and p99 written on every run, pass or fail
- [ ] Budget values, test names and allocation assertions unchanged
- [ ] Slow tag decided by L11a from CI duration
- [ ] `ci/run-checks.sh` green; tests pass on repeated CI runs
- [ ] No writes outside `tests/sim/core/**`
- [ ] Reviewer approved

## Worker notes

No worker: tests only. A failure that survives the rewrite is a real finding
for the team lead, not a test edit.
