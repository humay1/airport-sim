# T-009 — Run 100 sim-days in under 60s, identical across runs

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` (harness/integration, `tools/SimHarness`) |
| Assigned role | worker |
| Depends on | T-006, T-007, T-008, T-012 |
| Spec source | `spec/00-overview.md` Phase 0 kill gate; `spec/02-determinism.md` "Gates"; `spec/11-interfaces-schedule.md` §11.10 (fixture) |
| Blocked by | — |

## Writable paths

```
tools/SimHarness/**, tests/sim/core/**
```

Registers `sim.world` (T-012), `sim.schedule` (T-008) and `sim.flow` (T-007)
into the harness built by T-001/T-006 and runs the Phase 0 fixture from
`tests/fixtures/schedule/phase0-200.csv` and
`tests/fixtures/world/phase0-landside.*` for 100 sim-days. `sim.flow` is not
constructed without `sim.world` (`18` §18.4), so this task's composition
order is world, schedule, airside (absent at Phase 0), flow — registry
order per `08` §8.5.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/02-determinism.md`,
`spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`spec/09-interfaces-flow.md`, `spec/11-interfaces-schedule.md`

## Interface to implement

No new interface. Drives the existing `ISimHost.Step`, registering
`IWorldSystem` (registry position 1, `18` §18.4), `IScheduleSystem`
(registry position 2, §11.7) and `IFlowSystem` (registry position 4,
`08-interfaces-core.md` §8.5) together for the first time. Compose them
through `08` §8.11a's `ISimHostBuilder`/`SystemServices` and each module's
`<Module>Factory` (`WorldFactory`, `ScheduleFactory`, `FlowFactory`,
Q-009) — never by reaching into a module's internals.

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
at the now-confirmed `SIM_SECONDS_PER_TICK = 6`, D2 — golden hashes may be
authored) against the fixed `phase0-200.csv`/`phase0-landside.*` fixtures,
asserting (a) wall-clock completion under 60s on the reference machine
(`spec/03-module-map.md` "How a budget is measured"), and (b) identical
per-checkpoint world hash across two independent runs of the same seed —
this is `determinism_same_process` at Phase 0 scale, reusing T-006's harness
subcommand. **Do not edit them.**

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

`sim.world` (T-012) has no runtime state and no RNG (`18` §18.4); its
presence in the registered set changes the world hash only through its own
fixture hash, not through any tick behaviour. Its dependency direction is
one-way: `sim.flow` depends on it, it depends on nothing.
