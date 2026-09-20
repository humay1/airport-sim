# 12 — Public interfaces: `sim.airside`

Implements the `sim.airside` row of `03-module-map.md`: runways, taxiways,
stands, aircraft movement. Answers `open-questions.md` Q-005. Notation and
binding rules are as in `08-interfaces-core.md`; where this file appears to
contradict `01-architecture.md` or `02-determinism.md`, those win and it is a
spec bug.

Reading order for a `sim.airside` worker: `01`, `02`, `07`, `08`,
`11-interfaces-schedule.md`, this file, then `09` §9.7 and `10` §10.4/§10.6.

---

## 12.1 What the module is, and what it is not

`sim.airside` owns the physical envelope of an aircraft from the moment it is
close enough to this airport to matter, to the moment it leaves the taxi graph
outbound. It is the only module that moves an aircraft.

`sim.airside` owns:

- the runway(s), their declared capacity and physical occupancy,
- the taxiway graph and its single-lane conflict model (§12.5),
- stands, their compatibility and assignment (§12.6),
- the movement and door milestones listed in §12.3 — **not** all of
  `FlightMilestone`; the stand-servicing milestones stay `sim.turnaround`'s,
  §12.3 draws the line explicitly, resolving the ambiguity `10-events.md` §10.4
  left implicit,
- calling `IFlowSystem.Inject`/`Absorb` at the aircraft door (§12.7).

`sim.airside` explicitly does **not** own:

- ground handling jobs, vehicles or their scheduling — `sim.turnaround`,
- the landside walking graph or flow fields passengers use — `sim.world`
  (via `sim.flow`, `09-interfaces-flow.md` §9.6),
- weather, wind or runway-direction changes (`RunwayDirectionChanged`,
  `10-events.md`, Phase 2) — Phase 0/1 has one fixed active direction per
  runway, set at load and immutable,
- any delay arithmetic: it emits milestones and blocking-interval events;
  `sim.delay` does the attribution.

### On `sim.world`

`03-module-map.md` lists `sim.world` as a dependency of `sim.airside`, but
`sim.world` has no published interface yet (`03-module-map.md`, "everything else
— not yet specified"). Rather than block T-021 on a second unrelated question,
this file scopes Phase 0/1 narrowly: **the taxiway/runway/stand graph is data
`sim.airside` owns and loads itself** (§12.4), independent of `sim.world`'s
landside representation. This is additive scope, not a permanent boundary —
when `sim.world` is specified, folding airside geometry into it is a candidate
amendment, not a redesign, because §12.4's shape is already a plain graph a
future `IWorldSystem` could equally own. Until then, `sim.airside` makes no
calls into `sim.world` at all.

---

## 12.2 Constants

| Constant | Value | Meaning |
|---|---|---|
| `CRUISE_LEAD_TICKS` | 1200 (2 sim-hours) | §12.6, `InboundAirborne` offset |
| `TAXI_EDGE_CAPACITY` | 1 aircraft | §12.5, single-lane simplification |
| `RUNWAY_SLOT_ROUNDING` | ceiling | §12.5, capacity → separation |

`AircraftHeldForRunway`/`StandUnavailable` etc. carry no constant of their own;
their thresholds are runway/stand content, never hardcoded.

---

## 12.3 Milestone ownership

Resolves the split `10-events.md` §10.4 left as "movement milestones" without
enumerating them.

| Milestone | Owner | Trigger |
|---|---|---|
| `InboundAirborne` | `sim.airside` | §12.6, fixed lead before scheduled arrival |
| `Landed` | `sim.airside` | Runway slot granted and occupancy begins, §12.5 |
| `OffRunway` | `sim.airside` | Runway occupancy ends, aircraft enters taxi graph |
| `OnStand` | `sim.airside` | Taxi-in complete, stand occupied |
| `DoorsOpen` | `sim.airside` | Fixed content delay after `OnStand` |
| — `DeboardComplete`, `ReadyToBoard`, `BoardingComplete` — | `sim.turnaround` | out of scope here, Q-006 |
| `DoorsClosed` | `sim.airside` | Ground service complete, §12.8 |
| `Pushback` | `sim.airside` | Immediately after `DoorsClosed`, stand vacates |
| `TakeoffRoll` | `sim.airside` | Runway slot granted for departure |
| `Airborne` | `sim.airside` | Fixed content delay after `TakeoffRoll` |

Doors are aircraft envelope, not ground service, so they stay with the module
that owns the aircraft's physical state — consistent with
`11-interfaces-schedule.md` §11.1, which already commits `sim.airside` to
injecting arriving passengers "at the aircraft door on the `DoorsOpen`
milestone." Assigning `DoorsOpen` to `sim.turnaround` would have contradicted
that merged text; this table is the correction, made once, here.

---

## 12.4 Layout: the airside graph

Construction-time, immutable for the session (like content, `08` §8.11), and
**not** the Phase 0/1 schedule CSV — a different fixture, owned by this module,
living beside `sim.airside`'s tests.

```
struct RunwayId   { uint16 Value }
struct StandId    { uint16 Value }
struct TaxiNodeId { uint16 Value }
struct TaxiEdgeId { uint16 Value }

enum TaxiNodeKind { RunwayThreshold, Junction, StandPosition }

readonly struct RunwayDef {
  RunwayId   Id
  TaxiNodeId ThresholdNode
  int32      ActiveDirectionDeg      // fixed at Phase 0/1, §12.1
  int32      DeclaredCapacityPerHour // content-declared, arrivals+departures pooled
  uint32     OccupancyTicks          // runway-surface time per movement
}

readonly struct TaxiNodeDef { TaxiNodeId Id; TaxiNodeKind Kind }

readonly struct TaxiEdgeDef {
  TaxiEdgeId Id
  TaxiNodeId From
  TaxiNodeId To
  uint32     TraversalTicks
  bool       Bidirectional           // false: From -> To only
}

readonly struct StandDef {
  StandId    Id
  TaxiNodeId Node                    // must be TaxiNodeKind.StandPosition
  ContentId  MaxAircraftSizeCategory // compatibility, §12.7
  NodeId     DepartureSinkNode       // sim.flow Sink, §12.7
}

readonly struct AirsideLayout {
  IReadOnlyList<RunwayDef>   Runways
  IReadOnlyList<TaxiNodeDef> Nodes
  IReadOnlyList<TaxiEdgeDef> Edges
  IReadOnlyList<StandDef>    Stands
}

interface IAirsideLayoutLoader { AirsideLayout Load(AirsideLayout raw) }
```

Load-time validation, hard failures naming the offending id
(`07-conventions.md`):

- every `TaxiEdgeDef.From`/`To` and `StandDef.Node`/`RunwayDef.ThresholdNode`
  must reference a declared `TaxiNodeDef`;
- the graph must be connected: every `StandPosition` and every
  `RunwayThreshold` reachable from every other;
- `StandDef.Id`, `RunwayDef.Id`, `TaxiNodeDef.Id`, `TaxiEdgeDef.Id` are each
  unique within the layout.

### Routing

Computed once at load, not per tick (`01-architecture.md`, pathfinding rule —
this is the same "never per-agent A\*" discipline applied to a graph small
enough to solve exhaustively up front): for every `(RunwayThreshold,
StandPosition)` pair, the least-`TraversalTicks` path, ties broken by ascending
`TaxiEdgeId` at the first diverging edge. Stored as an ordered edge list per
pair. A layout with more than one runway repeats this per runway; Phase 0/1
ships exactly one.

---

## 12.5 The runway model

Two independent constraints, both binding, checked in this order whenever a
movement (`Landed` or `TakeoffRoll`) is requested:

1. **Pacing.** `DeclaredCapacityPerHour` converts to
   `minSeparationTicks = ceil(TICKS_PER_SIM_HOUR / DeclaredCapacityPerHour)`
   (`RUNWAY_SLOT_ROUNDING`). Each runway tracks `NextSlotTick`, starting at 0.
   A movement may claim a slot only at `tick >= NextSlotTick`; claiming one sets
   `NextSlotTick = tick + minSeparationTicks`. Arrivals and departures draw from
   the **same** slot sequence — mixed-mode single-runway, the Phase 0/1
   simplification `10-events.md`'s worked example in
   `06-delay-attribution.md` ("declared capacity 32/hr exceeded") assumes.
2. **Occupancy.** The runway surface holds at most one aircraft. A movement may
   begin only if `Occupant` is unset. `Landed` sets `Occupant`; `OffRunway`
   clears it. `TakeoffRoll` sets it; `Airborne` clears it.

A movement failing either check **holds**: it joins the runway's queue, ordered
by ascending `EventId` of the hold request (first blocked, first served, the
same tie-break rule as `06-delay-attribution.md` §10.5 rule 3), and emits
`AircraftHeldForRunway { Flight, RunwayId, queuePosition }`. The queue is
re-evaluated once per tick, in queue order, and the head is released the first
tick both constraints pass, emitting `AircraftHeldForRunwayReleased`
immediately before the milestone (`Cause` set to the hold event —
`10-events.md` §10.2).

---

## 12.6 The taxiway model

Single-lane: each `TaxiEdgeDef` holds `TAXI_EDGE_CAPACITY` (1) aircraft. An
aircraft ready to enter an edge that is occupied **holds**, ordered by ascending
`EventId` of the hold request, and emits
`AircraftHeldOnTaxiway { Flight, EdgeId, blocking: <occupant FlightId> }`. On
release, `AircraftHeldOnTaxiwayReleased`, `Cause` set to the hold event.

- Entering an edge sets `PhaseEnteredAt = tick`, `DueAt = tick +
  TraversalTicks`. On or after `DueAt` the aircraft advances to the edge's `To`
  node, subject to the next edge's occupancy per this same rule — an aircraft
  never "vanishes" between edges; if the next edge is occupied it holds at the
  node, not mid-edge.
- `InboundAirborne` fires `CRUISE_LEAD_TICKS` before `ScheduledTick` (from
  `IScheduleSystem.TryGetFlight`, §12.9), always on time — Phase 0/1 does not
  simulate an origin airport, so nothing can make it late. Both `PlannedTick`
  and `ActualTick` equal `ScheduledTick - CRUISE_LEAD_TICKS`.

  > **LOW CONFIDENCE — `InboundAirborne` is a formality event at Phase 0/1.**
  > It exists so the milestone sequence and the delay tree's `late_inbound`
  > category have a well-formed anchor once a later phase models delay
  > upstream of this airport. Until then it never varies and adds no
  > information. Flagged for the human owner in case that reads as dead
  > weight rather than a placeholder worth keeping.

- `Landed` fires when a runway slot is granted per §12.5, at or after
  `ScheduledTick` — never earlier; an early aircraft holds off-graph
  (`AircraftHeldForRunway` with `queuePosition = 0`, occupying no node) until
  its own `ScheduledTick`, so a slack schedule cannot make traffic denser than
  planned.
- `OffRunway` fires `OccupancyTicks` after `Landed`; the aircraft then enters
  the taxi graph at the runway's `ThresholdNode` and follows the precomputed
  route (§12.4) toward a stand chosen per §12.7.
- Symmetric on departure: `Pushback` places the aircraft at the stand's `Node`
  heading for the assigned runway's `ThresholdNode`; `TakeoffRoll` fires when a
  runway slot is granted; `Airborne` fires `OccupancyTicks` after
  `TakeoffRoll`, and the aircraft leaves the module's tracked state.

---

## 12.7 Stands

- Compatibility: an aircraft may occupy a stand only if the aircraft's
  `size_category` (content, `aircraft.schema.json`) is `<=`
  `StandDef.MaxAircraftSizeCategory` under the size-category content's declared
  ordinal ordering. Phase 0/1 ships four contact stands; remote stands and
  buses are out of scope (`00-overview.md` non-goals do not forbid them later,
  but nothing here defines one).
- Assignment happens once, at `OffRunway`: the earliest-declared, compatible,
  currently-free stand, ties broken by ascending `StandId`. If none is free,
  the aircraft holds at the runway threshold node — `StandUnavailable {
  Flight, Stand: null, occupying: null }` — and re-evaluates every tick a stand
  frees, emitting `StandAssigned { Flight, Stand, occupying: null }` the tick
  it succeeds, `Cause` set to whichever `Pushback` freed the stand.
- A stand occupies from `OnStand` to `Pushback` inclusive. Reassigning an
  occupied stand is the `ReassignStand` command, §12.10; nothing else forces a
  reassignment mid-turnaround.
- **Arriving passengers.** `11-interfaces-schedule.md` §11.4 defines
  `FlightRecord.PaxCount` for `Departure` rows only; a Phase 0/1 `Arrival` row
  always carries a pax count of zero (no arrival demand is specified yet).
  `sim.airside` therefore makes **no** `Inject` call at `DoorsOpen` while that
  holds — calling `Inject` with `count == 0` would violate `09
  -interfaces-flow.md` §9.7's precondition, so the call is skipped, not made
  with a zero count. When arrival demand is specified (a schedule amendment),
  this section is where the door-side `Inject` call and its target `Source`
  node are added.
- **Departing passengers.** At `DoorsClosed` (§12.8), `sim.airside` calls
  `IFlowSystem.Absorb(stand.DepartureSinkNode, flight)`, once, unconditionally.
  This both removes any passengers `sim.flow` still has at the flight's `Gate`
  node (there should be none if boarding closed cleanly; if there are, they
  become the population `PassengersMissedFlight` already reported, and
  `Absorb`'s return value is not re-published — `sim.flow` owns that event)
  and reconciles the flow-side head count for the flight.

---

## 12.8 The turnaround handshake, and the no-`sim.turnaround` fallback

Between `DoorsOpen` and `DoorsClosed`, `sim.airside` waits for
`sim.turnaround`'s `BoardingComplete` (`FlightMilestoneReached`, §12.3). It
learns of this **only** through the event bus: `sim.turnaround` depends on
`sim.airside` (`03-module-map.md`), so the call can never run the other way,
and this is the "upward information flows as events" rule in practice.
`sim.airside` subscribes to `FlightMilestoneReached` and reacts to
`BoardingComplete` for a flight it has on stand by transitioning to
`DoorsClosed` on its next `Tick`.

**T-021 ships before `sim.turnaround` exists** (`00-overview.md` build order).
A build with `sim.airside` but no `sim.turnaround` registered would otherwise
wait forever and violate the "every `*Blocked` has an `*Unblocked`" invariant
trivially by never producing a second milestone at all. So, precisely as
`11-interfaces-schedule.md` §11.6 does for `sim.flow`:

> If `sim.turnaround` is not registered in this build, `sim.airside` treats
> `FlightRecord.MinTurnaround` (from `IScheduleSystem.TryGetFlight`) as the
> full ground time: `DoorsClosed` fires unconditionally at `DoorsOpen tick +
> MinTurnaround` (converted via `TICKS_PER_SIM_MINUTE`), and `Absorb` is called
> at that same tick. Once `sim.turnaround` **is** registered, this fallback
> never fires — the event handshake above takes over entirely and
> `MinTurnaround` is read only as the *planned* ground time for `sim.delay`'s
> gap arithmetic, never as a timer.

This is the same decoupling pattern as `11-interfaces-schedule.md` §11.6
("running without `sim.flow`"): a module's own hash and behaviour must not
depend on whether a downstream consumer exists, only on whether an upstream
producer does.

---

## 12.9 Module interface

```
interface IAirsideSystem : ISimSystem {

  // ---- queries, read-only ----
  bool   TryGetTrack(FlightId flight, out AircraftTrack track)
  bool   TryGetStand(StandId id, out StandState stand)
  IReadOnlyList<StandId> FreeStands()                      // ascending StandId
  int32  RunwayQueueLength(RunwayId runway)                 // pacing + occupancy holds combined
  IReadOnlyList<FlightId> TrackedFlights()                  // ascending FlightId
}

enum AircraftLegPhase {
  AwaitingApproach, HeldForRunway, OnRunway, HeldOnTaxiway, Taxiing,
  OnStand, AwaitingPushbackClearance, Departed
}

readonly struct AircraftTrack {
  FlightId         Flight
  MovementKind     Kind                // from FlightRecord, 11 §11.3
  AircraftLegPhase Phase
  TaxiNodeId?      AtNode
  TaxiEdgeId?      OnEdge
  Fx               EdgeProgress        // 0..1, meaningful only if OnEdge set
  StandId?         Stand
  RunwayId?        Runway
  Tick             PhaseEnteredAt
  Tick             DueAt               // TICK_UNSCHEDULED (11 §11.2) while holding indefinitely
}

readonly struct StandState { StandId Id; FlightId? Occupant }
```

`AircraftLegPhase` is reused across both legs of a rotation: the sequence for
an `Arrival` runs left to right through `OnStand`; a `Departure` resumes from
`OnStand` (set directly, no approach) through `Departed`. There is no mutating
entry point besides the one command in §12.10 — everything else is
tick-driven.

Registry position is **3** (`08-interfaces-core.md` §8.5): after `sim.schedule`
(2), before `sim.flow` (4), so a flight's arrival movement is tracked before
`sim.flow` runs in the same tick, and before `sim.turnaround` (5).

---

## 12.10 Commands consumed

| Command | Payload | Effect |
|---|---|---|
| `ReassignStand` | `FlightId`, `StandId newStand` | Only while the flight's `Phase == OnStand`. Rejected (`CommandRejection.NotPermitted`) if `newStand` is occupied or incompatible. Takes effect at the next tick boundary: old stand's occupant clears, new stand's occupant is set, no milestone re-fires. |

Named as the example in `07-conventions.md` ("Commands: imperative, ...,
`ReassignStand`"); this is that command's binding definition.

---

## 12.11 Events emitted and consumed

Full field lists in `10-events.md` §10.6 except where this file adds a
`*Released` pairing name.

**Emitted:**

| Event | When |
|---|---|
| `FlightMilestoneReached` | Each milestone in §12.3's `sim.airside` rows, once per flight |
| `AircraftHeldForRunway` / `AircraftHeldForRunwayReleased` | §12.5 |
| `AircraftHeldOnTaxiway` / `AircraftHeldOnTaxiwayReleased` | §12.6 |
| `StandUnavailable` / `StandAssigned` | §12.7 |

**Consumed:**

| Event | From | Reaction |
|---|---|---|
| `FlightMilestoneReached { Milestone = BoardingComplete }` | `sim.turnaround` | §12.8, transition to `DoorsClosed` next tick |

`sim.airside` calls `IScheduleSystem.TryGetFlight` (query, downward,
`11-interfaces-schedule.md` §11.7) to read `ScheduledTick`, `MinTurnaround` and
`AircraftType`; it does not subscribe to `FlightPlanPublished` — a flight
already exists in `IScheduleSystem.PublishedFlights()` before its
`ScheduledTick` is imminent, and `sim.airside` only needs to look it up once,
at `InboundAirborne` time, when it starts tracking the flight.

---

## 12.12 State, hashing, RNG and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9):

1. Runway state, ascending `RunwayId`: `NextSlotTick`, `Occupant`, hold queue
   in queue order (each entry's `FlightId`).
2. Taxi edge state, ascending `TaxiEdgeId`: `Occupant`, hold queue in queue
   order.
3. Stand state, ascending `StandId`: `Occupant`.
4. Tracked aircraft, ascending `FlightId`: every field of `AircraftTrack`.

Not hashed, because derived: `FreeStands()`, `RunwayQueueLength()`, the
precomputed routing table (§12.4, fixed at load and part of the loaded layout,
not runtime state).

**RNG: none at Phase 0/1.** Every choice in this file (stand assignment,
routing, hold-queue order) is a declared deterministic rule; the module
declares no stream and contributes no stream state to its hash, the same
posture as `sim.schedule` (`11-interfaces-schedule.md` §11.9).

Budget: **0.80 ms/tick at max tier** (`03-module-map.md`). Per-tick work is
O(tracked aircraft + runways + held edges), never a scan of the taxi graph or
the stand list beyond `FreeStands()`'s O(stands) (stands are bounded by
`03-module-map.md`'s max tier at 60, cheap regardless). No allocation in the
update path (`07-conventions.md`); the routing table is computed once at load,
off the tick path.

---

## 12.13 The Phase 0/1 fixture (T-021)

`tests/fixtures/airside/phase1-single-runway.*` (format is the worker's
choice — this is graph data, not the schedule CSV, and carries no byte-level
strictness requirement the way §12.4 doesn't impose one), binding on the Test
Author:

- exactly one `RunwayDef`;
- a taxiway graph connecting the runway threshold to four `StandDef`s, with at
  least one `Junction` node and one edge shared by more than one
  threshold-to-stand route, so `TAXI_EDGE_CAPACITY` contention is exercised;
- at least one stand with a `MaxAircraftSizeCategory` that excludes at least
  one aircraft type used in the companion schedule fixture, so stand
  incompatibility is covered;
- `DeclaredCapacityPerHour` set low enough against the companion schedule
  fixture's movement density that `AircraftHeldForRunway` fires at least once
  in a single sim-day run.

Runs against `tests/fixtures/schedule/phase0-200.csv`
(`11-interfaces-schedule.md` §11.10) with `sim.turnaround` **absent**, so
§12.8's fallback path is what T-021 actually exercises; the event-handshake
path is `sim.turnaround`'s task (T-022, Q-006) to test once it exists.

Done-condition tests this spec expects to exist, phrased per
`07-conventions.md`:

- `test_layout_rejects_disconnected_graph`
- `test_runway_pacing_holds_when_declared_capacity_exceeded`
- `test_taxi_edge_single_occupant_holds_second_aircraft`
- `test_stand_assignment_prefers_lowest_id_among_compatible_free_stands`
- `test_doors_close_after_min_turnaround_when_turnaround_absent`
- `test_arrival_pax_count_zero_skips_inject`
- `test_airside_tick_consumes_no_rng`
