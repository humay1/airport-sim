# T-021 — One runway, taxiway graph, four contact stands

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.airside` |
| Assigned role | worker |
| Depends on | T-008, T-026 |
| Spec source | `spec/00-overview.md` build order #3; `spec/12-interfaces-airside.md` (answers Q-005) |
| Blocked by | — |

## Writable paths

```
src/sim/airside/**, tests/sim/airside/**, tests/fixtures/airside/**
```

`sim.airside` depends on `core`, `world` and `schedule` (`spec/03-module-map.md`),
but this task must build and pass **without** calling into `sim.world` at all —
`spec/12-interfaces-airside.md` §12.1 ("On `sim.world`") is binding: the
taxiway/runway/stand graph is self-owned, loaded directly by this module, not
queried from `sim.world`. It must also build and pass **without**
`sim.turnaround` registered — §12.8's fallback is what this task actually
exercises; do not add a hard dependency on T-022 merging first.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/12-interfaces-airside.md`,
`spec/11-interfaces-schedule.md` §11.7 (`IScheduleSystem` queries this module
calls), `spec/09-interfaces-flow.md` §9.7 (the `Inject`/`Absorb` contract this
module calls), `spec/10-events.md` §10.4, §10.6 ("From sim.airside")

## Interface to implement

```
struct RunwayId   { uint16 Value }
struct StandId    { uint16 Value }
struct TaxiNodeId { uint16 Value }
struct TaxiEdgeId { uint16 Value }

enum TaxiNodeKind { RunwayThreshold, Junction, StandPosition }

readonly struct RunwayDef {
  RunwayId   Id
  TaxiNodeId ThresholdNode
  int32      ActiveDirectionDeg
  int32      DeclaredCapacityPerHour
  uint32     OccupancyTicks
}

readonly struct TaxiNodeDef { TaxiNodeId Id; TaxiNodeKind Kind }

readonly struct TaxiEdgeDef {
  TaxiEdgeId Id
  TaxiNodeId From
  TaxiNodeId To
  uint32     TraversalTicks
  bool       Bidirectional
}

readonly struct StandDef {
  StandId    Id
  TaxiNodeId Node
  ContentId  MaxAircraftSizeCategory
  NodeId     DepartureSinkNode
}

readonly struct AirsideLayout {
  IReadOnlyList<RunwayDef>   Runways
  IReadOnlyList<TaxiNodeDef> Nodes
  IReadOnlyList<TaxiEdgeDef> Edges
  IReadOnlyList<StandDef>    Stands
}

interface IAirsideLayoutLoader { AirsideLayout Load(AirsideLayout raw) }

enum AircraftLegPhase {
  AwaitingApproach, HeldForRunway, OnRunway, HeldOnTaxiway, Taxiing,
  OnStand, AwaitingPushbackClearance, Departed
}

readonly struct AircraftTrack {
  FlightId         Flight
  MovementKind     Kind
  AircraftLegPhase Phase
  TaxiNodeId?      AtNode
  TaxiEdgeId?      OnEdge
  Fx               EdgeProgress
  StandId?         Stand
  RunwayId?        Runway
  Tick             PhaseEnteredAt
  Tick             DueAt
}

readonly struct StandState { StandId Id; FlightId? Occupant }

interface IAirsideSystem : ISimSystem {
  bool   TryGetTrack(FlightId flight, out AircraftTrack track)
  bool   TryGetStand(StandId id, out StandState stand)
  IReadOnlyList<StandId> FreeStands()
  int32  RunwayQueueLength(RunwayId runway)
  IReadOnlyList<FlightId> TrackedFlights()
  AirsideLayout Layout()                                    // the validated layout of §12.4, immutable
}
```

**`AtNode` and `OnEdge` together** (§12.9, amended by Q-008): while `OnEdge`
is set, `AtNode` holds the node the aircraft **entered the edge from** (not
cleared during traversal, as an earlier reading might assume), and
`EdgeProgress` runs from 0 at `AtNode` to 1 at the edge's other endpoint. For
a `Bidirectional` edge this is the only way to know direction of travel.
While `OnEdge` is unset, `AtNode` is the node the aircraft is at (including
holding at a node, §12.6), or unset if the aircraft is off-graph.

`Layout()` returns the layout `IAirsideLayoutLoader` validated at load. It is
load-time data, not runtime state, so it is **not hashed** (§12.12). It
exists for `app.render` (T-020, `spec/15-interfaces-render.md` §15.4), which
must not load the airside fixture a second time on its own.

Binding, copied from `spec/12-interfaces-airside.md`, not paraphrased:

- **Milestone ownership** (§12.3): `sim.airside` emits `InboundAirborne`,
  `Landed`, `OffRunway`, `OnStand`, `DoorsOpen`, `DoorsClosed`, `Pushback`,
  `TakeoffRoll`, `Airborne`. It does **not** emit `DeboardComplete`,
  `ReadyToBoard` or `BoardingComplete` — those stay `sim.turnaround`'s.
- **`PlannedTick` per milestone** (§12.3, added by the Q-007 amendment —
  binding, schedule-anchored and cumulative per `10-events.md` §10.4;
  `RouteTicks(a,b)` is the precomputed unimpeded taxi time, holds excluded):

  | Milestone | `FlightId` | `PlannedTick` |
  |---|---|---|
  | `InboundAirborne` | arrival | `STA − CRUISE_LEAD_TICKS` |
  | `Landed` | arrival | `STA` |
  | `OffRunway` | arrival | `STA + OccupancyTicks` |
  | `OnStand` | arrival | `STA + OccupancyTicks + RouteTicks(threshold, stand)` |
  | `DoorsOpen` | arrival | planned `OnStand` + the fixed door delay |
  | `OnStand` | departure | `STD − MinTurnaround` |
  | `DoorsClosed` | departure | `STD` |
  | `Pushback` | departure | `STD` |
  | `TakeoffRoll` | departure | `STD + RouteTicks(stand, threshold)` |
  | `Airborne` | departure | planned `TakeoffRoll` + `OccupancyTicks` |

  `sim.delay` measures only `Landed`, arrival `OnStand`, and departure
  `OnStand`/`Pushback`/`Airborne` (`spec/14-interfaces-delay.md` §14.4); every
  row above is still binding, for the UI and for later checkpoints.
- **Layout and routing** (§12.4): load-time validation (every node/edge
  reference resolves, graph connected, ids unique within the layout) is a
  hard failure naming the offending id. Threshold-to-stand routing is
  computed once at load, least-`TraversalTicks`, ties by ascending
  `TaxiEdgeId` at the first diverging edge — never per-tick, never per-agent.
- **Runway model** (§12.5): pacing (`minSeparationTicks =
  ceil(TICKS_PER_SIM_HOUR / DeclaredCapacityPerHour)`, tracked via
  `NextSlotTick`, shared by arrivals and departures) and physical occupancy
  (`Occupant`, one aircraft at a time) are independent checks, both must pass.
  Failing either holds the aircraft, queued by ascending `EventId` of the hold
  request, emitting `AircraftHeldForRunway`; releases at the head, in queue
  order, the first tick both checks pass, emitting
  `AircraftHeldForRunwayReleased` with `Cause` set to the hold event.
- **Taxiway model** (§12.6): each edge holds exactly one aircraft
  (`TAXI_EDGE_CAPACITY = 1`). Same hold/release/`Cause` pattern as runways,
  event names `AircraftHeldOnTaxiway`/`AircraftHeldOnTaxiwayReleased`. An
  aircraft blocked at the next edge holds at the node, never mid-edge.
  `InboundAirborne` fires at `ScheduledTick - CRUISE_LEAD_TICKS` (1200 ticks),
  always exactly on time — Planned and Actual are equal, no upstream delay
  model exists yet. `Landed` never fires before `ScheduledTick`.
- **Stands** (§12.7): compatible and free, earliest-declared, ties by
  ascending `StandId`. No stand free: hold at the threshold node,
  `StandUnavailable{Flight, Stand:null, occupying:null}`, re-evaluate every
  tick, `StandAssigned` on success with `Cause` set to the freeing `Pushback`.
  Occupies `OnStand` through `Pushback` inclusive. **Arrivals inject zero
  passengers at Phase 0/1** — `FlightRecord.PaxCount` is departures-only
  (`spec/11-interfaces-schedule.md` §11.4); do not call `Inject` at
  `DoorsOpen`, the call is a documented no-op until a future amendment adds
  arrival demand data. At `DoorsClosed`, call
  `IFlowSystem.Absorb(stand.DepartureSinkNode, flight)` once, unconditionally.
- **Two `FlightId`s per rotation, one stand handoff** (§12.3 "Which
  `FlightId` gets which milestone", §12.7 "Rotation-less flights", §12.8):
  an arrival and its linked departure are **separate `FlightId`s**
  (`spec/11-interfaces-schedule.md` §11.3), each with its own
  `AircraftTrack`. The arrival's track carries `InboundAirborne` through
  `DoorsOpen`; the departure's track is created directly in `OnStand` phase
  at the same `Stand` and carries `DoorsClosed` through `Airborne`. A
  rotation-less arrival (`HasRotation = false`) never gets a departure track
  and stays on stand indefinitely — intentional, not a bug. A rotation-less
  departure is assumed already on stand at `ScheduledTick - MinTurnaround`
  and creates its own track there directly.
- **The `sim.turnaround` handshake and its fallback** (§12.8, five-step
  sequence): subscribe to `FlightMilestoneReached{Milestone=DeboardComplete}`
  for the **arrival** — on the next `Tick`, if `HasRotation`
  (`IScheduleSystem.TryGetRotation`), create the departure's track in
  `OnStand` phase at the same `Stand`, reassign `StandState.Occupant`, and
  fire `FlightMilestoneReached{OnStand}` for the departure. Separately,
  subscribe to `FlightMilestoneReached{Milestone=BoardingComplete}` for the
  **departure** — on the next `Tick`, call `Absorb` and fire `DoorsClosed`
  for the departure, then `Pushback`. **If `sim.turnaround` is not
  registered in this build** — the case this task actually ships and tests
  — neither `DeboardComplete` nor `BoardingComplete` is ever emitted
  (`10-events.md` §10.3 forbids `sim.airside` emitting another module's
  events), so both steps above collapse into one: at `DoorsOpen tick +
  MinTurnaround` (arrival's `MinTurnaround`), if `HasRotation`, do the
  handoff (create departure track, reassign occupant, fire departure
  `OnStand`), call `Absorb`, and fire departure `DoorsClosed` — all in the
  same tick.
- **Registry position 3** (§12.9), after `sim.schedule` (2), before
  `sim.flow` (4) and `sim.turnaround` (5).
- **`ReassignStand` command** (§12.10): only while `Phase == OnStand`;
  rejected `NotPermitted` if the target stand is occupied or incompatible;
  takes effect at the next tick boundary; no milestone re-fires.
- **No RNG at Phase 0/1** (§12.12). Do not add a stream speculatively.

## Events

Emitted: `FlightMilestoneReached` (for the nine milestones listed above),
`AircraftHeldForRunway`/`AircraftHeldForRunwayReleased`,
`AircraftHeldOnTaxiway`/`AircraftHeldOnTaxiwayReleased`,
`StandUnavailable`/`StandAssigned`

Consumed: `FlightMilestoneReached{Milestone=DeboardComplete}` and
`FlightMilestoneReached{Milestone=BoardingComplete}` (both from
`sim.turnaround`, when registered)

## Tests to pass

```
tests/sim/airside/**
```

Written by the Test Author, against a companion fixture at
`tests/fixtures/airside/**` whose binding requirements are
`spec/12-interfaces-airside.md` §12.13, run against
`tests/fixtures/schedule/phase0-200.csv` with `sim.turnaround` absent. Expect
at least:

- `test_layout_rejects_disconnected_graph`
- `test_runway_pacing_holds_when_declared_capacity_exceeded`
- `test_taxi_edge_single_occupant_holds_second_aircraft`
- `test_stand_assignment_prefers_lowest_id_among_compatible_free_stands`
- `test_doors_close_after_min_turnaround_when_turnaround_absent`
- `test_stand_hands_off_from_arrival_to_departure_flightid_without_freeing`
- `test_rotationless_arrival_stays_on_stand_indefinitely`
- `test_arrival_pax_count_zero_skips_inject`
- `test_airside_tick_consumes_no_rng`

**Do not edit them.** If a test contradicts `spec/12-interfaces-airside.md`,
file an open question and stop.

## Performance budget

`0.80` ms/tick at max tier (`spec/03-module-map.md`,
`spec/12-interfaces-airside.md` §12.12). Per-tick work is O(tracked aircraft +
runways + held edges); `FreeStands()` is O(stands), bounded and cheap at max
tier (60 stands). No allocation in the update path; the routing table is
computed once at load, off the tick path.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`sim.airside` makes **no calls into `sim.world`** at Phase 0/1 — the
taxiway/runway/stand graph is this module's own data, loaded via
`IAirsideLayoutLoader`, independent of any landside representation
(`spec/12-interfaces-airside.md` §12.1). Do not wire a dependency on
`sim.world` that doesn't exist yet.

`DoorsOpen`/`DoorsClosed` belong to this module, not `sim.turnaround` —
`spec/10-events.md` §10.4 was corrected to say so explicitly when this spec
was written; do not follow an older mental model where "stand milestones"
meant all five door/service transitions.

Q-007 and Q-008 amendments (both same-cycle, before this file's own branch
merges) add the `PlannedTick` table above and the `Layout()` query plus the
`AtNode`-stays-set-during-`OnEdge` clarification. If this task is picked up
from an older local checkout, re-pull this file before starting.

`RunwayId`, `StandId`, `TaxiNodeId` and `TaxiEdgeId` (used above and in
`AircraftTrack`/`RunwayDef`/`TaxiNodeDef`/`TaxiEdgeDef`/`StandDef`) are now
authored in `src/sim/core/**` by T-026, not here — they are event-payload
types by `03-module-map.md`'s rule that events are defined in `sim.core`,
and this task may write only `src/sim/airside/**`. Reference them from
`sim.core`; do not redeclare them locally. T-026 must merge before this task
is released.

Q-006 is now answered (`spec/13-interfaces-turnaround.md`); the handshake this
task implements — `DeboardComplete` triggers the stand handoff to the
departure's `FlightId`, `BoardingComplete` triggers `DoorsClosed` — is the
one `sim.turnaround`'s own spec commits to producing, so no further amendment
is expected here. If a future change to either spec needs a different
handshake, that is a coordinated amendment to both `§12.8` and
`13-interfaces-turnaround.md` §13.6, not a local workaround in either
module's code.
