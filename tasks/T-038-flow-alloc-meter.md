# T-038 — `sim.flow` tests: switch to the forced-GC allocation meter

| Field | Value |
|---|---|
| Status | MERGED (PR #60) |
| Module | `sim.flow` tests |
| Assigned role | test-author |
| Depends on | T-037 (merged, PR #51, `7bb30b5`), T-007 (merged) |
| Spec source | `spec/07-conventions.md` "Performance"; `spec/08-interfaces-core.md` §8.6 "Allocation" (Q-035) |
| Blocked by | — |

## Writable paths

```
tests/sim/flow/**
```

No other file, and no other module. This is a test-only fix: no `src/`,
`spec/`, `ci/` or `data/balance/` edit.

## Why this task exists

T-037 (PR #51) added a shared, forced-GC `Allocation.Start()`/`Since()`
meter to `tests/sim/core/**` and `tests/sim/world/**`, because
`GC.GetAllocatedBytesForCurrentThread` over-reports when the runtime
retires a partly used allocation context mid-window, producing a spurious
non-zero delta with no real allocation behind it. `tests/sim/flow/**`
arrived on `main` (T-007, merged) with three zero-allocation assertions
that still read `GC.GetAllocatedBytesForCurrentThread` directly and are
exposed to the same flake: `BoardingTests.cs`, `FlowBudgetTests.cs`,
`OutstandingCostTests.cs`. T-037's own writable paths do not cover
`tests/sim/flow/**` (`sim.core`/`sim.world` only), so this is its own task.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md` "Performance",
`spec/08-interfaces-core.md` §8.6 "Allocation"

## What this task does

Switch `sim.flow`'s zero-allocation measurements to the same forced-GC
meter (a copy in the flow test kit, since test projects share no code):
assertions stay exactly `0`, no retries, no tolerances. Follow T-037's
pattern exactly — `Allocation.Start()` runs a full blocking collection
(`GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()`) before the
baseline read; `Allocation.Since(start)` reads the delta. Add the meter to
`sim.flow`'s test kit (the file already holding shared fixtures/helpers for
`tests/sim/flow/**`, following T-037's `CoreTestKit.cs`/`WorldTestKit.cs`
naming). Every `GC.GetAllocatedBytesForCurrentThread` pair in
`BoardingTests.cs`, `FlowBudgetTests.cs` and `OutstandingCostTests.cs` is
replaced with it.

## Tests to pass

```
tests/sim/flow/**
```

This task **is** the Test Author's own change; there is no separate test
grant to satisfy. Every existing zero-allocation assertion in
`BoardingTests.cs`, `FlowBudgetTests.cs` and `OutstandingCostTests.cs` must
still assert exactly `0` bytes, now measured through the meter instead of a
raw `GC.GetAllocatedBytesForCurrentThread` pair. No new test is required
beyond what T-037 already added
(`test_allocation_meter_real_allocation_is_counted`, `sim.core`) — this
task does not need its own copy of that test unless the reviewer finds the
flow test kit's copy of the meter itself needs coverage.

## Performance budget

Not applicable — this task changes no timed or budgeted code path, only
how existing zero-allocation assertions are measured.

## Done when

- [ ] The forced-GC meter exists in `sim.flow`'s test kit
- [ ] Every `GC.GetAllocatedBytesForCurrentThread` pair in
      `BoardingTests.cs`/`FlowBudgetTests.cs`/`OutstandingCostTests.cs` is
      replaced with it
- [ ] Every zero-allocation test still asserts exactly `0`, with no
      tolerance or retry
- [ ] `ci/run-checks.sh` green, including repeated runs showing no flake
- [ ] No writes outside `tests/sim/flow/**`
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

**Release order, binding:** do not release this task until both of its
dependencies are actually merged — **T-037 is now merged** (PR #51,
`7bb30b5`); **T-010** has since merged (#56), as has this task (#60). T-010's worker branch
(`worker/T-010-cohort-promotion-demotion`) and its Test Author's branch
(`test-author/T-010-cohort-promotion-tests`) both carry T-010's own
additions to `tests/sim/flow/**`, and the T-011 Test Author is concurrently
writing `tests/sim/flow/**` tests on
`test-author/T-011-stress-30k-tests`. Releasing T-038 before T-010 merges
risks a worker touching the same test files T-010 is about to land,
producing conflicts or clobbering T-010's tests — this task's own writable
path is exactly the shared surface those two are actively writing. Check
the queue for T-010's merge before starting.
