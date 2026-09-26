# T-011 — Stress: 30,000 daily passengers within frame budget

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-010 |
| Spec source | `spec/00-overview.md` Phase 0 kill gate; `spec/09-interfaces-flow.md` §9.10; `spec/03-module-map.md` "How a budget is measured" |
| Blocked by | — |

## Writable paths

```
src/sim/flow/**
```

**Correction (Q-021, supersedes the earlier "URGENT path-guard" fix on this
file):** the stress fixture and its budget test are `tests/sim/flow/**`,
which is the Test Author's territory exclusively
(`07-conventions.md` "Solution layout and build") — the path guard already
blocks a worker from writing there, so this task's earlier grant of
`tests/sim/flow/**` (its *only* grant) is dropped rather than corrected in
place. **This task's writable path is now `src/sim/flow/**` only, and this
task expects to write nothing there in the ordinary case:** the fixture and
measurement are the Test Author's deliverable per `00-overview.md`'s
kill-gate framing; a worker is released against this task only if the
Test Author's budget test fails and a real fix in `src/sim/flow/**`
(T-007/T-010's code) is needed to meet it. Do not add a new fixture or test
file under `tests/**` yourself, no matter how tempting — file back to the
Test Author instead.

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
`spec/00-overview.md` — "if T-011 cannot meet budget, the architecture is
redesigned here — not later. Escalate to the human owner."

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
