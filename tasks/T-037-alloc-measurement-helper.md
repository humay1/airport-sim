# T-037 — Shared allocation-measurement helper for zero-allocation tests

| Field | Value |
|---|---|
| Status | MERGED (PR #51) |
| Module | `sim.core` test infrastructure (plus `sim.world` tests) |
| Assigned role | test-author |
| Depends on | T-036 (merged) |
| Spec source | `spec/07-conventions.md` "Performance"; `spec/08-interfaces-core.md` §8.6 "Allocation" (Q-035) |
| Blocked by | — |

## Writable paths

```
tests/sim/core/**
tests/sim/world/**
```

No other file, and no other module. This is a test-only fix: no `src/`,
`spec/`, `ci/` or `data/balance/` edit.

## Why this task exists

T-036's zero-allocation tests (`BusAllocationTests`, and the pre-existing
`BudgetTests`, `RandomServiceTests`) are flaky under `GC.GetAllocated-
BytesForCurrentThread`: the CI path guard scoped T-036's writable paths to
`src/sim/core/EventBus.cs`/`Channel.cs` only, so a fix that touches
`tests/sim/core/**` and `tests/sim/world/**` on a `test-author/T-036-*`
branch is rejected by that same guard — the branch name resolves to T-036,
whose task file grants no `tests/` path. The fix needs its own task so a
`test-author/T-037-*` branch can carry it.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md` "Performance",
`spec/08-interfaces-core.md` §8.6 "Allocation"

## What this task does

A shared `Allocation.Start()`/`Allocation.Since()` meter that forces a full
GC (`GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()`) before the
baseline read, because `GC.GetAllocatedBytesForCurrentThread` over-reports
when the runtime retires a partly used allocation context mid-window —
observed as spurious non-zero deltas (up to one ~8 KB allocation quantum)
with no corresponding allocation. The helper lives in
`tests/sim/core/CoreTestKit.cs`, with a copy in
`tests/sim/world/WorldTestKit.cs` (the two test projects share no code).
Every `GC.GetAllocatedBytesForCurrentThread` assertion in `tests/` is
switched to it. Assertions stay exactly `0`: no retries, no tolerances,
nothing skipped. `spec/07-conventions.md`'s "Performance" section
prescribes no measurement method, so a test-only meter is this task's
choice to make, not a spec gap.

Files changed, exactly:

```
tests/sim/core/BudgetTests.cs
tests/sim/core/BusAllocationTests.cs
tests/sim/core/CommandQueueTests.cs
tests/sim/core/CoreTestKit.cs
tests/sim/core/FxTests.cs
tests/sim/core/RandomServiceTests.cs
tests/sim/core/RandomStreamTests.cs
tests/sim/core/StateHasherTests.cs
tests/sim/world/WorldQueriesTests.cs
tests/sim/world/WorldTestKit.cs
```

## Tests to pass

```
tests/sim/core/**
tests/sim/world/**
```

This task **is** the Test Author's own change; there is no separate test
grant to satisfy. It adds one new test:

- `test_allocation_meter_real_allocation_is_counted` — asserts the meter
  still sees a real allocation (allocates a buffer inside the measured
  window and checks the delta reflects it), so the fix cannot mask a
  genuine regression by over-collecting.

Every existing zero-allocation assertion (`BudgetTests`,
`BusAllocationTests`, `CommandQueueTests`, `FxTests`, `RandomServiceTests`,
`RandomStreamTests`, `StateHasherTests`, `WorldQueriesTests`) must still
assert exactly `0` bytes, now measured through `Allocation.Start()`/
`Since()` instead of a raw `GC.GetAllocatedBytesForCurrentThread` pair.

## Performance budget

Not applicable — this task changes no timed or budgeted code path, only
how an existing zero-allocation assertion is measured.

## Done when

- [ ] `Allocation.Start()`/`Since()` exists in both `CoreTestKit.cs` and
      `WorldTestKit.cs`
- [ ] Every `GC.GetAllocatedBytesForCurrentThread` pair in `tests/` is
      replaced with it
- [ ] `test_allocation_meter_real_allocation_is_counted` passes
- [ ] Every zero-allocation test still asserts exactly `0`, with no
      tolerance or retry
- [ ] `ci/run-checks.sh` green, including repeated runs showing no flake
- [ ] No writes outside `tests/sim/core/**`/`tests/sim/world/**`
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`test-author/T-036-alloc-measure-fix` is this task's branch; it predates
this task file because the flake was found and root-caused while T-036 was
in flight (see that branch's own PR for the measurement evidence: 20
instrumented full-solution runs isolating the cause to allocation-context
retirement, not sim code, JIT/OSR/type-loading, or thread hopping). No code
change to T-036's own `EventBus.cs`/`Channel.cs`, or to any other task's
`src/`, is needed or permitted here.
