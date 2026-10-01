# T-011 — Stress: 30,000 daily passengers within frame budget

| Field | Value |
|---|---|
| Status | MERGED (PR #59) |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-010 |
| Spec source | `tasks/queue.md` "Phase 0 — feasibility spike (the kill gate)" and its Gate note; `spec/09-interfaces-flow.md` §9.10; `spec/03-module-map.md` "How a budget is measured" |
| Blocked by | — |

**Correction (this cycle): the "Spec source" line above was wrong.**
`spec/00-overview.md` contains no kill-gate text at all — no "Phase 0",
"feasibility" or "kill" anywhere in it. The kill-gate framing ("if T-011
cannot meet budget, the architecture is redesigned here — not later.
Escalate to the human owner") lives in `tasks/queue.md`'s "Phase 0 —
feasibility spike (the kill gate)" heading and its "**Gate:**" line, not in
any spec file — the Planner's own queue framing, not the Architect's. Cited
correctly above; every other reference to `00-overview.md`'s "kill gate" in
this file (the Performance budget section, the worker notes) is likewise
this queue framing, not a spec section.

## Writable paths

```
src/sim/flow/**
tests/sim/flow/**
```

**Correction (Q-021, supersedes the earlier "URGENT path-guard" fix on this
file):** the stress fixture and its budget test are `tests/sim/flow/**`,
which is the Test Author's territory exclusively
(`07-conventions.md` "Solution layout and build") — the path guard already
blocks a **worker** from writing there (`ci/check-paths.sh`'s
`protected_for_role` blocks `tests/` for every role but `test-author`),
so this doesn't reopen Q-021's worker restriction. **`tests/sim/flow/**` is
granted here only so the Test Author's own PR passes the path guard**: the
guard resolves a branch's role and task id from its name (`<role>/<task-id>
-<slug>`), so a `test-author/T-011-*` branch is checked against *this*
task's writable paths, same as `test-author/T-036-alloc-measure-fix` needed
T-037 filed before it could pass (PR #49/#51's flake fix hit the identical
failure). **This task's `src/sim/flow/**` grant is unchanged and expects no
write in the ordinary case:** the fixture and measurement are the Test
Author's deliverable per the kill-gate framing above; a worker is released
against this task only if the Test Author's budget test fails against
`main`'s code and a real fix in `src/sim/flow/**` (T-007/T-010's code) is
needed to meet it. Do not add a new fixture or test file under `tests/**`
yourself, no matter how tempting — file back to the Test Author instead.

**Release order, binding (this cycle):** T-011 depends on T-010, which is
not yet merged. (historical, resolved: T-010 merged #56; this task merged #59) The Test Author's tests are done
(`test-author/T-011-stress-30k-tests`, `a885923`:
`tests/sim/flow/StressDay.cs`, `FlowStressBudgetTests.cs`) and **pass the
budget against `main` today** — mean `0.50`–`0.63` ms, p99 `2.60`–`3.26` ms
against the `2.5`/`5.0` ms budget, `0` bytes allocated, a peak of `252` live
cohorts. That PR merges only after **T-010** merges, same as every other
open `tests/sim/flow/**` branch (see `tasks/queue.md`'s T-038 release-order
note) — do not merge it ahead of T-010 even though it is green today,
since T-010 itself lands in this same directory. **A worker is needed only
if the budget fails once T-010's code is in** — as things measure now, on
pre-T-010 `main`, it holds with margin, but T-010 changes `sim.flow`'s own
update path (cohort→agent promotion/demotion) and could change the cost.
(historical, resolved: that PR merged after T-010 (#56); T-011 merged #59 and no worker was needed)

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/03-module-map.md`, `spec/09-interfaces-flow.md`

## Interface to implement

No new interface. Exercises the existing `IFlowSystem` (§9.7) under load.

## Events

Emitted: none new
Consumed: none

## Tests to pass

```
tests/sim/flow/**
```

Written by the Test Author. A fixture generating a full sim-day of cohort
traffic summing to 30,000 daily passengers through a representative node
graph (sources → corridors → queues → gates → sinks), asserting:

- mean tick cost ≤ `2.5` ms, p99 ≤ `5.0` ms, measured per
  `spec/03-module-map.md` "How a budget is measured" (module `Tick` only,
  excluding fixture setup and checkpoint phase)
- zero bytes allocated in the update path
- a ceiling on live cohort count (mandatory merging, §9.3, is what bounds
  this — the test is what proves it)

**Do not edit them.**

## Performance budget

`2.5` ms/tick mean, `5.0` ms/tick p99, at the 30,000-passenger fixture (a
sub-max-tier point on the way to the 90,000-passenger max-tier fixture used
by the module's steady-state budget test). This is the Phase 0 kill gate: see
`tasks/queue.md`'s **Gate:** note — "if T-011 cannot meet budget, the
architecture is redesigned here — not later. Escalate to the human owner."

**Measured on `main`, this cycle** (`test-author/T-011-stress-30k-tests`,
`a885923`): mean `0.50`–`0.63` ms, p99 `2.60`–`3.26` ms, `0` bytes
allocated, peak `252` live cohorts. Well inside budget as of today; T-010
has not yet merged, so this is not the final measurement — see the Release
order note above. (historical, resolved: T-010 merged #56)

## Done when

- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met, or the human owner has been escalated to per the gate note
      above
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

A failing budget here is not this task's failure to fix by any means
necessary — CLAUDE.md's "no confidently wrong agent" rule applies. If the
architecture cannot meet budget, stop and escalate; do not quietly change the
node model to hide the cost.

The stress fixture now exercises the Q-012 routing rule
(`09-interfaces-flow.md` §9.6): cost is O(out-degree × gates × path length)
per released cohort. If this fixture is the one that shows that cost needs
caching, the fix belongs in `sim.flow` (`src/sim/flow/**`, T-007's module),
not in `sim.world`, which stays load-time-only.
