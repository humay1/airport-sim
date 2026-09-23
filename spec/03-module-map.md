# 03 — Module map

One worker agent per module. A worker writes only inside its own directory plus
its own test directory. Everything else is read-only to it.

| Module | Directory | Depends on | Owns |
|---|---|---|---|
| `sim.core` | `src/sim/core` | — | Tick loop, RNG service, command queue, event bus, fixed-point math, state hashing |
| `sim.save` | `src/sim/save` | core | Serialisation, snapshots, migrations |
| `sim.world` | `src/sim/world` | core | Grid, construction, rooms, navigation graph, flow fields |
| `sim.schedule` | `src/sim/schedule` | core, flow | Flight schedule, slots, seasons, published timetable |
| `sim.airside` | `src/sim/airside` | core, world, schedule | Runways, taxiways, stands, aircraft movement, wind/active direction |
| `sim.flow` | `src/sim/flow` | core, world | Passenger cohorts, queue nodes, promotion/demotion, corridors |
| `sim.turnaround` | `src/sim/turnaround` | core, airside, staff | Handling jobs, ground vehicles, job scheduling |
| `sim.baggage` | `src/sim/baggage` | core, world, flow | Belt network, sorters, carousels, mishandled-bag model |
| `sim.delay` | `src/sim/delay` | core, all above (read-only via events) | Delay attribution tree |
| `sim.staff` | `src/sim/staff` | core | Roles, rosters, shifts, fatigue, training |
| `sim.economy` | `src/sim/economy` | core, schedule, flow | Revenue, costs, loans, capex, regulated caps |
| `sim.policy` | `src/sim/policy` | core | Policy state, approval workflow, effect application |
| `sim.reputation` | `src/sim/reputation` | core, policy | Four reputation meters |
| `sim.incident` | `src/sim/incident` | core | Incident definitions, triggers, contingency policy execution |
| `sim.progression` | `src/sim/progression` | core, economy, reputation | Tiers, unlocks, ARFF categories |
| `app.render` | `src/app/render` | sim (read-only) | Rendering, cameras, overlays |
| `app.ui` | `src/app/ui` | sim (read-only), render | Screens, advisor, delay tree view |
| `app.host` | `src/app/host`, `unity/AirportSim` | sim, render, ui | Composition root, frame loop, Unity project shell and player build |
| `content` | `data/` | — | Aircraft, airlines, objects, incidents, policies |

## Communication rules

- **Downward calls only**, following the dependency column. No module calls a
  module that depends on it.
- **Upward information flows as events** on the event bus. `sim.delay` learns about
  everything exclusively this way — it never queries other modules directly.
- Events are immutable value types, defined in `sim.core`.
- Any interface not listed in this spec does not exist. If a worker needs one, it
  files a question; the Architect adds it here first.

## Published interfaces

| Module | Interface spec |
|---|---|
| `sim.core` | `08-interfaces-core.md` |
| `sim.flow` | `09-interfaces-flow.md` |
| `sim.schedule` | `11-interfaces-schedule.md` |
| `sim.airside` | `12-interfaces-airside.md` |
| `sim.turnaround` | `13-interfaces-turnaround.md` |
| `sim.delay` | `14-interfaces-delay.md` (principles in `06-delay-attribution.md`) |
| `app.render` | `15-interfaces-render.md` (Phase 1: headless scene layer, promotion controller, tick pacer; engine backend is a contract only) |
| `app.host` | `16-interfaces-host.md` (composition root, frame loop, Unity project; module construction pending Q-009) |
| event catalogue (all emitters, consumed by `sim.delay`) | `10-events.md` |
| everything else | not yet specified — a worker may not start without one |

## Per-module performance budgets

Total is **6 ms per tick at max tier** (`01-architecture.md`, locked). The named
splits there are reproduced unchanged; this table only apportions that document's
"everything else combined 0.8" line and states how the budget is measured.

| Module | Budget (ms/tick at max tier) | Source |
|---|---|---|
| `sim.flow` | 2.50 | `01-architecture.md` |
| `sim.baggage` | 1.00 | `01-architecture.md` |
| `sim.airside` | 0.80 | `01-architecture.md` |
| `sim.turnaround` | 0.50 | `01-architecture.md` |
| `sim.delay` | 0.40 | `01-architecture.md` |
| `sim.core` (loop, commands, event dispatch) | 0.25 | apportioned |
| `sim.world` (per-tick queries; recompute excluded) | 0.10 | apportioned |
| `sim.schedule` | 0.10 | apportioned |
| `sim.staff` | 0.08 | apportioned |
| `sim.economy` | 0.05 | apportioned |
| `sim.incident` | 0.05 | apportioned |
| `sim.policy` | 0.03 | apportioned |
| `sim.progression` | 0.02 | apportioned |
| `sim.reputation` | 0.02 | apportioned |
| `sim.save` (per-tick observation only) | 0.02 | apportioned |
| **unallocated reserve** | **0.08** | apportioned |
| **Total** | **6.00** | |

The reserve is not free capacity. It is the Architect's only room to absorb a
module that overruns without reopening the locked split, and it is handed out by
amendment, never claimed by a worker.

### Off-tick budgets

Work excluded from the per-tick budget, because it is amortised and off the hot
path. Each still has a ceiling and a test.

| Work | Budget | Source |
|---|---|---|
| `sim.world` flow-field recompute | 50 ms per construction change | `01-architecture.md` |
| Checkpoint hashing, whole world | 20 ms per checkpoint, every `HASH_CHECKPOINT_TICKS` | this file — **LOW CONFIDENCE** |
| Snapshot write (`sim.save`) | 250 ms, off the tick path | this file — **LOW CONFIDENCE** |

Hashing every sim-hour at 20 ms adds roughly 0.03 ms/tick amortised, which the
reserve covers. If the Verifier measures worse, the checkpoint interval is the
dial to turn, not the budget.

### How a budget is measured

A budget that is not measured identically everywhere is not a budget. Binding on
the Test Author and the Verifier:

- **Reference machine:** the minimum spec of `01-architecture.md`, or the CI agent
  with a recorded scaling factor. The factor is recorded per run, never applied
  retroactively to make a past failure pass.
- **Load:** the max-tier fixture — 800 daily movements, 90 000 daily passengers,
  60 stands, 3 runways — run for one full sim-day.
- **Statistic:** the module passes if **mean ≤ budget** and **p99 ≤ 2× budget**
  across the day's ticks. The mean protects the frame; the p99 catches the peak
  that only shows up at the 07:00 bank, which is exactly when the player is
  watching.
- **Measured:** the module's `Tick` only, excluding fixture setup and excluding
  the checkpoint phase, which is billed separately above.
- **Allocation:** zero bytes allocated in the update path, asserted as well as
  timed. A GC pause does not appear in a mean and ruins a frame anyway.

Budgets are asserted in each module's own tests (`07-conventions.md`,
"Performance"), so a regression fails the owning module's suite rather than an
integration suite nobody reads.

### The soak fixture

HUMAN DECISION — owner (delegated), 2026-09-23 (Q-003, D3). The nightly
`soak_500_days` gate (`02-determinism.md`) runs a **mid-tier** fixture, not
the max-tier one:

- The fixture is sized so that the whole sim's mean cost is **under
  0.1 ms per tick** on the reference machine above. 500 sim-days are
  7.2 M ticks, so the run takes about 12 minutes. The bound is on cost, not
  on movement or passenger counts. The Test Author chooses the counts, and
  they are fixture sizing, not balance.
- Every system registered in the build under test is registered in the soak,
  with a `repeat_daily` schedule so that every day carries load.
- The soak proves long-run determinism: drift, counter overflow, unbounded
  growth of retained state. It does **not** prove max-tier performance. The
  per-module budget tests above prove that, on the max-tier fixture.
- If a merged module pushes the soak fixture's mean over 0.1 ms per tick, the
  fixture is shrunk by the Test Author and its golden re-authored. The gate is
  never shortened, sampled or disabled.
- The fixture lives at `tests/fixtures/soak/**` and its golden hash at
  `tests/golden/`.

## Module brief template

Every worker task brief must contain:

```
Module:          sim.<name>
Writable paths:  src/sim/<name>/**, tests/sim/<name>/**
Readable specs:  spec/00, spec/01, spec/02, spec/03, spec/<module-specific>
Interface:       <exact signature list from the spec, citing its section>
                 e.g. "spec/09-interfaces-flow.md §9.7 IFlowSystem"
Events emitted:  <list>
Events consumed: <list>
Tests to pass:   tests/sim/<name>/** (written by the Test Author, do not edit)
Budget:          <ms/tick>
```
