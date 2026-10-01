# T-045 — `tools.simharness`: `budget --tier max` rounding to the `03` rule

| Field | Value |
|---|---|
| Status | QUEUED (do not release until T-009 has merged **and** the Test Author has confirmed a deterministic done-test exists; see Worker notes) |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-009, T-013, T-014, T-030 |
| Spec source | `spec/19-interfaces-harness.md` §19.4 (PR #68, `7c9a373`); `spec/03-module-map.md` "Budget tests: window and arithmetic" (Q-045); `spec/07-conventions.md` L11 |
| Blocked by | — |

## Writable paths

```
tools/SimHarness/**
```

Test Author paths (the worker writes none of these):

```
tests/tools/simharness/**
```

Serialised after T-009, T-013, T-014 and T-030, which all write
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

No new public interface. `HarnessCli.Run`'s `budget` subcommand behaviour and
stdout line change only in the rounding and the verdict arithmetic.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/tools/simharness/**
```

Written by the Test Author, after T-009's tests. **Do not edit them.**

## Performance budget

Not applicable to the harness itself. The gate is 6 ms/tick whole-sim,
`B = 6000`.

## Done when

- [ ] `RunBudget` rounds each sample up and computes mean and p99 by the `03` Q-045 rule, in `long` arithmetic only
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green (not `--fast`)
- [ ] No writes outside `tools/SimHarness/**`
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

**Possible spec gap, reported to the team lead, not decided here.** `19` gives
the harness no seam for injecting timer samples, and `19` §19.3 says no CLI seam
is added beyond the form. A black-box test through `HarnessCli.Run` sees real
timings only, so the floor-versus-ceiling difference is not reliably
observable from it. If the Test Author cannot state a deterministic, passing
test of the rounding without inventing a seam (for example an internal
`BudgetStatistic` function that takes `long[]` samples and a frequency, which
would need the Architect to name it and say whether tests may see it), this task
becomes a spec gap for the Architect and is not released. The Test Author
reports; it does not invent the seam.

Tests of the Phase 0 composition stay as T-009's Test Author wrote them
(`test_harness_cli_budget_phase0_day_passes` is renamed there).
