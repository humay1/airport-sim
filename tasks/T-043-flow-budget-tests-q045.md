# T-043 — `sim.flow` budget tests: conform to the `03` window, arithmetic and handler-timing rules

| Field | Value |
|---|---|
| Status | MERGED (PR #85) |
| Module | `sim.flow` tests |
| Assigned role | test-author |
| Depends on | T-039, T-040, T-023 (all merged: #63, #64, #71) |
| Spec source | `spec/03-module-map.md` "Budget tests: window and arithmetic" (Q-044, Q-045; PR #68, `7c9a373`); `spec/07-conventions.md` L11, L11a; `spec/03-module-map.md` "How a budget is measured" (Q-061 update path, Q-064 handler timing; PRs #73, #75) |
| Blocked by | — |

## Writable paths

```
tests/sim/flow/**
```

No other file. No `src/`, `spec/`, `ci/` or `.github/` edit. The Test Author
opens its own PR.

## Why this task exists

Per the team lead's audit of PR #68, these `sim.flow` tests do not conform:
`FlowBudgetTests`, `FlowStressBudgetTests` (it uses `Int128`, which `03` forbids)
and `PromotionBudgetTests`. All are under `tests/sim/flow/`. The Test Author
confirms the list by reading every timed `Budget` test in `tests/sim/flow/**`
and rewrites each that asserts a time. `PromotionAllocationTests` and any other
test that asserts only allocation are out of scope.

**Serialised after T-039 and T-040** (T-023 has merged). T-040 adds the `Slow`
trait to `PromotionBudgetTests` and `FlowStressBudgetTests` methods and T-039
adds a file, both in `tests/sim/flow/**`. Never release two tasks that write the
same paths. (historical, resolved: T-039, T-040 and T-023 merged; T-043 merged #85)

**Folded in (2026-10-01, post-#73/#75 sync): two more `tests/sim/flow/**`
rewrites that would otherwise need their own serialised tasks.** They touch the
same files this task already rewrites, so a separate task would only add a
fourth writer to the path. They are parts 5 and 6 below.

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
4. **Handler timing (Q-064, `03` "Timing a module's handlers").** Add handler
   timers to every timed `Budget` test in `FlowStressBudgetTests`,
   `SecurityLaneBudgetTests` and `PromotionBudgetTests` (and `FlowBudgetTests`):
   build `sim.flow` with a `SystemServices` whose `Events` and `Commands` wrap
   each handler it registers, during construction, in a non-allocating timing
   shim. Tick `t`'s sample is the sum of the `Stopwatch.GetTimestamp()`
   differences around `Tick` and around every shimmed call in phases 1 to 3
   (`SetServersOpen` `Apply` included); steps 1 to 3 then apply to that sum
   unchanged. "No other way of timing handlers satisfies a budget." The shim
   and its sample accumulator are allocated before the loop. If `sim.flow` has
   no event handler, only the `Apply` is shimmed, and the test says so.
5. **Allocation window covers `Apply` (Q-061, `03` "How a budget is
   measured").** `test_security_lane_switching_every_tick_allocates_nothing_in_flow_tick`
   meters only `sim.flow`'s `Tick` through the `Timed` wrapper
   (`SecurityLaneBudgetTests.cs`), so the `SetServersOpen` `Apply` runs outside
   it. Re-meter it so the command's `Apply` runs inside the metered window for
   each `Step(1)` (the T-037 meter over the whole step, or a direct rig that
   delivers the command in the window), after a warm-up that already ran it.
   Assert exactly 0. Where another flow allocation test (`FlowBudgetTests`,
   `FlowStressBudgetTests`, `PromotionAllocationTests`) leaves a flow handler or
   `Apply` outside its window, widen it the same way; the Test Author lists
   which in the PR. An allocation test needs no `Budget` trait (Q-061 revision).
6. Keep names, fixtures, work per tick, allocation assertions and the budget
   values unchanged. Do not widen, average away, retry or skip. Sample
   storage is allocated before the measured loop.

## Slow tag (L11a)

Rule (a): one window is 14 400 ticks, but `sim.flow`'s budget is 2.5 ms/tick, so
a window at the budget mean takes about 36 s. Rule (b) is therefore expected to
apply (over 5 s on CI), and the already-tagged `Slow` methods (T-040:
`PromotionBudgetTests...every_node_promoted`, `FlowStressBudgetTests` x2) stay
tagged. For each other timed test the Test Author decides from the first
authoritative CI duration: over 5 s means add `Slow` beside `Budget`. A tagged
test whose latest CI duration is under 2.5 s loses the tag at the next Test
Author change. A PR that adds `Slow` needs the manual pre-merge `slow-tests`
run green. The window is never shortened to avoid the tag.

## Tests to pass

This task is the Test Author's own change. No separate grant.

```
tests/sim/flow/**
```

## Performance budget

Unchanged: `sim.flow` 2.5 ms/tick at max tier (`03`), asserted as `Σu ≤ B × n` with `B = 2500`, p99 ≤ 5000 us.

## Done when

- [ ] Every timed `Budget` test in the owned paths samples per tick over exactly 14 400 consecutive ticks
- [ ] `long` arithmetic only (no `Int128`, no floating point, no `TimeSpan`), per `03` Q-045; mean `Σu ≤ B × n`, p99 by nearest rank `≤ 2 × B`
- [ ] Reported mean `(Σu + n − 1) / n` and p99 written on every run, pass or fail
- [ ] Handler timers in the timed flow tests (Q-064): each sample is `Tick` plus shimmed handler and `Apply` time
- [ ] The `SetServersOpen` `Apply` runs inside the metered window of the lane-switching allocation test, asserting 0 (Q-061)
- [ ] Budget values and test names unchanged; allocation assertions stay at exactly 0
- [ ] Slow tag decided by L11a from CI duration
- [ ] `ci/run-checks.sh` green; tests pass on repeated CI runs
- [ ] No writes outside the owned paths
- [ ] Reviewer approved

## Worker notes

No worker: tests only. `FlowStressBudgetTests` and `PromotionBudgetTests` also
assert cohort bounds and zero allocation; those assertions are unchanged. A
failure that survives the rewrite is a real finding for the team lead, not a
test edit.
