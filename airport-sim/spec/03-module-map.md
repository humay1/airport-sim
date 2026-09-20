# 03 — Module map

One worker agent per module. A worker writes only inside its own directory plus
its own test directory. Everything else is read-only to it.

| Module | Directory | Depends on | Owns |
|---|---|---|---|
| `sim.core` | `src/sim/core` | — | Tick loop, RNG service, command queue, event bus, fixed-point math, state hashing |
| `sim.save` | `src/sim/save` | core | Serialisation, snapshots, migrations |
| `sim.world` | `src/sim/world` | core | Grid, construction, rooms, navigation graph, flow fields |
| `sim.schedule` | `src/sim/schedule` | core | Flight schedule, slots, seasons, published timetable |
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
| `content` | `data/` | — | Aircraft, airlines, objects, incidents, policies |

## Communication rules

- **Downward calls only**, following the dependency column. No module calls a
  module that depends on it.
- **Upward information flows as events** on the event bus. `sim.delay` learns about
  everything exclusively this way — it never queries other modules directly.
- Events are immutable value types, defined in `sim.core`.
- Any interface not listed in this spec does not exist. If a worker needs one, it
  files a question; the Architect adds it here first.

## Per-module performance budgets

Filled in by the Architect once the frame budget in `01-architecture.md` is set.
Each module's tests assert its budget at max tier.

| Module | Budget (ms/tick at max tier) |
|---|---|
| `sim.flow` | TBD — expected largest consumer |
| `sim.world` (flow field recompute) | TBD — amortised, not per tick |
| `sim.baggage` | TBD |
| all others | TBD |

## Module brief template

Every worker task brief must contain:

```
Module:          sim.<name>
Writable paths:  src/sim/<name>/**, tests/sim/<name>/**
Readable specs:  spec/00, spec/01, spec/02, spec/03, spec/<module-specific>
Interface:       <exact signature list from the spec>
Events emitted:  <list>
Events consumed: <list>
Tests to pass:   tests/sim/<name>/** (written by the Test Author, do not edit)
Budget:          <ms/tick>
```
