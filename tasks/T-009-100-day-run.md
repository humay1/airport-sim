# T-009 — Run 100 sim-days in under 60s, identical across runs

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` (harness/integration, `tools/SimHarness`) |
| Assigned role | worker |
| Depends on | T-006, T-007, T-008 |
| Spec source | `spec/00-overview.md` Phase 0 kill gate; `spec/02-determinism.md` "Gates"; `spec/11-interfaces-schedule.md` §11.10 (fixture) |
| Blocked by | — |

## Writable paths

```
tools/SimHarness/**, tests/sim/core/**
```

Registers `sim.schedule` (T-008) and `sim.flow` (T-007) into the harness
built by T-001/T-006 and runs the Phase 0 fixture from
`tests/fixtures/schedule/phase0-200.csv` for 100 sim-days.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/02-determinism.md`,
`spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`spec/09-interfaces-flow.md`, `spec/11-interfaces-schedule.md`

## Interface to implement

No new interface. Drives the existing `ISimHost.Step`, registering
`IScheduleSystem` (registry position 2, §11.7) and `IFlowSystem` (registry
position 4, `08-interfaces-core.md` §8.5) together for the first time.

## Events

Emitted: none new
Consumed: none new — this task is the first place `FlightPlanPublished` /
`FlightMilestoneReached{PlanPublished}` actually reach a registered
`IFlowSystem.Inject` call end-to-end.

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect: a 100-sim-day run (`14400 * 100` ticks
against the provisional `SIM_SECONDS_PER_TICK` from Q-002 — do not author
golden hashes per that question's standing status) against the fixed
`phase0-200.csv` fixture, asserting (a) wall-clock completion under 60s on
the reference machine (`spec/03-module-map.md` "How a budget is measured"),
and (b) identical per-checkpoint world hash across two independent runs of
the same seed — this is `determinism_same_process` at Phase 0 scale, reusing
T-006's harness subcommand. **Do not edit them.**

## Performance budget

Wall-clock ceiling: 60s for 100 sim-days on the reference machine
(`spec/00-overview.md`). This is the Phase 0 kill-gate measurement, distinct
from but consistent with the per-tick budgets already asserted by T-007's and
T-008's own module tests.

## Done when

- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green (not `--fast`)
- [ ] Budget met, or the human owner has been escalated to (this task sits
      directly under the T-011 kill gate in the build order)
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

Per `spec/11-interfaces-schedule.md` §11.6 "Running without `sim.flow`",
`sim.schedule`'s own tests (T-008) must already pass without `sim.flow`
registered. This task is the first to exercise both together — if the
combined hash disagrees with the sum of each module's own behaviour, the
bug is in the `Inject` call site, not in either module's isolated tests.
