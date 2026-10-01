# T-044 — `sim.schedule` budget tests: conform to the `03` window and arithmetic rule

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.schedule` tests |
| Assigned role | test-author |
| Depends on | T-040 (merged #64; wrote `tests/sim/schedule/ScheduleHashTests.cs`) |
| Spec source | `spec/03-module-map.md` "Budget tests: window and arithmetic" (Q-044, Q-045; PR #68, `7c9a373`); `spec/11-interfaces-schedule.md` §11.9; `spec/07-conventions.md` L11, L11a |
| Blocked by | — |

## Writable paths

```
tests/sim/schedule/**
```

No other file. No `src/`, `spec/`, `ci/` or `.github/` edit. The Test Author
opens its own PR.

## Why this task exists

Per the team lead's audit of PR #68, two tests in
`tests/sim/schedule/BudgetTests.cs` do not conform. The Test Author confirms by
reading every timed `Budget` test in `tests/sim/schedule/**`. `03` notes the
window "generalises `11` §11.9's rule (Q-031)", so a window need not start on a
sim-day boundary, but must cover 14 400 consecutive ticks, which includes each
bank. T-040 tags `ScheduleHashTests`, a different file in the same directory, so
this task is serialised after it to keep the paths unshared. (historical, resolved: T-040 merged #64)

## What this task does

Make each timed `Budget` test follow `03` exactly. "Any other timed check that
does not follow these rules carries no `Budget` trait, and it does not satisfy
a budget."

1. **Sample.** The module's `Tick` only, excluding fixture setup and the
   checkpoint phase (`03` "Measured"), one sample per tick: `d` is "the
   `Stopwatch.GetTimestamp()` difference around one tick's measured work".
2. **Window.** "exactly `n = TICKS_PER_SIM_DAY` (14 400) consecutive ticks, one
   sample per tick." Any 14 400 consecutive ticks after warm-up form a window,
   which need not start on a sim-day boundary. "No shorter window satisfies a
   budget." Several windows are judged one by one, never as a union, and do
   not overlap. Warm-up ticks are not sampled.
3. **Arithmetic, `long` only** (no `Int128`, no floating point, no
   `TimeSpan`). `f = Stopwatch.Frequency`, `B` the budget in whole
   microseconds, `C = B × n + 1`. "If `d > (long.MaxValue − f + 1) /
   1 000 000`, then `u = C`. Otherwise `u = min((d × 1 000 000 + f − 1) / f,
   C)`."
   - mean: "it passes iff `Σu ≤ B × n`";
   - p99: "Sort the `u` ascending, and `p99 = u[(99 × n + 99) / 100 − 1]` in
     integer division ... It passes iff `p99 ≤ 2 × B`";
   - report `(Σu + n − 1) / n` and the p99 to test output on every run.
4. Keep names, fixtures, work per tick, allocation assertions and the budget
   values unchanged. Do not widen, average away, retry or skip. Sample
   storage is allocated before the measured loop.

## Slow tag (L11a)

Rule (a): one window plus a warm-up is far under 144 000 ticks. Rule (b): at the
`sim.schedule` budget of 0.1 ms/tick, a window costs about 1.44 s at the limit.
Judge from the first authoritative CI duration: over 5 s means add `Slow`
beside `Budget`. Never shorten the window.

## Tests to pass

This task is the Test Author's own change. No separate grant.

```
tests/sim/schedule/**
```

## Performance budget

Unchanged: `sim.schedule` 0.10 ms/tick at max tier (`03`), asserted as `Σu ≤ B × n` with `B = 100`, p99 ≤ 200 us.

## Done when

- [ ] Every timed `Budget` test in the owned paths samples per tick over exactly 14 400 consecutive ticks
- [ ] `long` arithmetic only (no `Int128`, no floating point, no `TimeSpan`), per `03` Q-045; mean `Σu ≤ B × n`, p99 by nearest rank `≤ 2 × B`
- [ ] Reported mean `(Σu + n − 1) / n` and p99 written on every run, pass or fail
- [ ] Budget values, test names and allocation assertions unchanged
- [ ] Slow tag decided by L11a from CI duration
- [ ] `ci/run-checks.sh` green; tests pass on repeated CI runs
- [ ] No writes outside the owned paths
- [ ] Reviewer approved

## Worker notes

No worker: tests only. Setup and fixture loading are not sampled (`03` "Measured"). A failure that
survives the rewrite is a real finding for the team lead, not a test edit.
