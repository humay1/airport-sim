# 00 — Overview

## Pillars

1. **Flow over placement.** A gate is only as good as the walk to it.
2. **Structural time pressure.** The published schedule is the clock. Bad decisions
   fail at peak, not instantly.
3. **Legible failure.** Every delayed minute traces to a visible cause.
4. **Airlines are partners with personalities**, not anonymous demand.

## Scope at 1.0

- ~40 aircraft types, 6 size categories
- 30 airline archetypes
- ~150 buildable objects
- 12 scenarios, 3 climate regions
- 4 progression tiers: regional → national → international → hub

## Explicit non-goals

Written here so no agent ever proposes them:

- **This is not an air traffic control simulator.** Sequencing and separation exist
  only as constraints on throughput. No vectoring, no radar screen, no voice comms.
- Not a flight simulator. Aircraft below the stand-and-taxiway level are abstract.
- Not a city builder. The world outside the airport perimeter is an abstraction.
- No multiplayer at 1.0.
- No real-world airline or aircraft licensing.

## Systems index

| System | Spec | Owner module |
|---|---|---|
| Tick, time, RNG | `02-determinism.md`, `08-interfaces-core.md` | `sim.core` |
| Event catalogue | `10-events.md` | `sim.core` (defined), all (emitted) |
| Schedule | `11-interfaces-schedule.md` | `sim.schedule` |
| Airside movement | `12-interfaces-airside.md` | `sim.airside` |
| Passenger flow | `03-module-map.md`, `09-interfaces-flow.md` | `sim.flow` |
| Turnaround jobs | `13-interfaces-turnaround.md` | `sim.turnaround` |
| Delay attribution | `06-delay-attribution.md`, `14-interfaces-delay.md` | `sim.delay` |
| Baggage | `03-module-map.md` | `sim.baggage` |
| Staff | `03-module-map.md` | `sim.staff` |
| Economy | `03-module-map.md` | `sim.economy` |
| Policy | `05-policy-system.md` | `sim.policy` |
| Reputation | `05-policy-system.md` | `sim.reputation` |
| Incidents | `03-module-map.md` | `sim.incident` |
| Progression | `03-module-map.md` | `sim.progression` |

## Build order (dependency order — do not reorder)

1. Deterministic tick and save format
2. Time and schedule
3. Aircraft movement and stands
4. Passenger flow and queues
5. Turnaround jobs
6. **Delay attribution**
7. Money
8. Staff
9. Policy and reputation
10. Incidents
11. Progression and unlocks
12. Tutorial and advisor

Delay attribution sits at 6 deliberately: it is built before the economy because
every balance decision afterwards depends on being able to see why things failed.
