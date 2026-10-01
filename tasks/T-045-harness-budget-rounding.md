# T-045 — `tools.simharness`: `budget --tier max` rounding to the `03` rule

| Field | Value |
|---|---|
| Status | QUEUED (spec gap closed by Q-058, PR #70; releasable after T-009 (merged #74), T-013, T-014 and T-030 have merged and the Test Author's six tests are authored) |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-009 (merged #74), T-013, T-014, T-030 (the last three not merged) |
| Spec source | `spec/19-interfaces-harness.md` §19.1, §19.4 and §19.7 (PR #68, `7c9a373`; Q-058, PR #70, `b00adcb`); `spec/03-module-map.md` "Budget tests: window and arithmetic" (Q-045); `spec/07-conventions.md` L11 |
| Blocked by | — |

## Writable paths

```
tools/SimHarness/**
```

Test Author paths (the worker writes none of these):

```
tests/tools/simharness/**
```

Serialised after T-009 (merged #74), T-013, T-014 and T-030, which all write
`tools/SimHarness/**` and `tests/tools/simharness/**`. It goes last because it
is the one at risk of a spec gap (Worker notes): if it were earlier it could
stall the others, and later it holds nothing up. None of the others reads
`budget`'s rounding. Never release two of these together.

## Why this task exists

`19` §19.4 now says: "The samples, the pass condition and the reported numbers
are exactly `03` 'Budget tests: window and arithmetic' (Q-045), with `B = 6000`
and `n = TICKS_PER_SIM_DAY`: each sample is rounded **up** to whole
microseconds, `mean_us` is the rounded-up mean, and `p99_us` is the
nearest-rank value. It passes if `mean_us ≤ 6000` and `p99_us ≤ 12000`, which
is exactly `03`'s condition. This replaces the earlier flooring of the samples
and of the mean, which accepted a mean just over 6000 µs."

`tools/SimHarness/HarnessCli.cs` `RunBudget` (T-006) still floors. This task
brings it to the rule, in `long` arithmetic only:

- `f = Stopwatch.Frequency`, `B = 6000`, `n = TICKS_PER_SIM_DAY` (14 400),
  `C = B × n + 1`. "If `d > (long.MaxValue − f + 1) / 1 000 000`, then
  `u = C`. Otherwise `u = min((d × 1 000 000 + f − 1) / f, C)`."
- "Mean: it passes iff `Σu ≤ B × n`." Reported `mean_us = (Σu + n − 1) / n`.
- "p99: nearest rank. Sort the `u` ascending, and `p99 = u[(99 × n + 99) / 100
  − 1]`." Passes iff `p99 ≤ 2 × B`. Reported `p99_us = p99`.
- The output line format of `19` §19.3 is unchanged:
  `<PASS|FAIL> budget ticks=<n> mean_us=<m> p99_us=<p>`.

## Readable specs

`CLAUDE.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/19-interfaces-harness.md`

## Interface to implement

One new pure public member (Q-058, `19` §19.1 and §19.4):

```
HarnessGates.BudgetFromSamples(IReadOnlyList<int64> samples, int64 frequency) -> GateResult
```

It runs nothing and reads no clock. It applies the rule above with
`B = 6000` and `n = samples.Count`; `Report` is the §19.3 budget line. It
throws for a count other than `TICKS_PER_SIM_DAY`, a negative sample, or a
frequency outside `03`'s bound, and does not modify `samples`.
`budget --tier max` collects its 14 400 samples in tick order and makes
exactly one call, with `Stopwatch.Frequency`; it prints `Report` and exits 0
iff `Passed`, else 1. The CLI computes no part of the verdict or line itself
(a Reviewer checks this). No CLI form passes samples in, and none may be
added. `HarnessCli.Run`'s other behaviour is unchanged.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/tools/simharness/**
```

Written by the Test Author, after T-009's tests: the six `19` §19.7 tests
(five with an exact `Report`, one argument test), including
`test_harness_gates_budget_from_samples_rounds_each_sample_up` (every sample
60 001 ticks at 10^7 Hz, which fails under rounding up and passes under
flooring). **Do not edit them.**

## Performance budget

Not applicable to the harness itself. The gate is 6 ms/tick whole-sim,
`B = 6000`.

## Done when

- [ ] `HarnessGates.BudgetFromSamples` rounds each sample up and computes mean and p99 by the `03` Q-045 rule, in `long` arithmetic only
- [ ] `RunBudget` makes exactly one `BudgetFromSamples` call and computes nothing itself
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green (not `--fast`)
- [ ] No writes outside `tools/SimHarness/**`
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

**Spec gap closed (Q-058, spec PR #70, `b00adcb`).** The seam is
`HarnessGates.BudgetFromSamples`, which gives the Test Author a deterministic
done-test. The ordering after T-009 (merged #74), T-013, T-014 and T-030 is unchanged.

Tests of the Phase 0 composition stay as T-009's Test Author wrote them
(`test_harness_cli_budget_phase0_day_passes` is renamed there).
