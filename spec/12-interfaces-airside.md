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
- calling `IFlowSystem.Inject`/`Absorb` at the aircraft door (§12.7),
- the **boarding hold**: a departure waits, for a bounded time, for its
  passengers still in the terminal (§12.8, D6).

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
| `STAND_WAIT_CAPACITY` | 1024 entries | §12.7 stand-wait queue, a hard bound (Q-050) |
| `PENDING_FLIGHTS_CAPACITY` | 2048 entries | §12.11 pending list, a hard bound |

The two capacities are engineering bounds, not balance. They are sized
from `01`'s max tier, 800 daily movements. The pending list only holds
flights scheduled within the next 24 hours (§12.11 "Why 2048 holds"). That
window can span two calendar days, so it holds up to 1 600 entries, day 0
at construction included. The stand-wait queue is larger than any one
day's arrivals.
Both are preallocated at `CreateSystem`, and neither ever grows.

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
| `DoorsOpen` | `sim.airside` | `AirsideRules.DoorsOpenDelayMinutes` after `OnStand` (§12.4, Q-047) |
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

### `PlannedTick` per milestone

Binding, per `10-events.md` §10.4 (schedule-anchored and cumulative: never
shifted by an earlier milestone's actual lateness). `STA` is the arrival's
`ScheduledTick`, `STD` the departure's. `RouteTicks(a, b)` is the sum of
`TraversalTicks` along the precomputed route of §12.4 — the unimpeded taxi
time, holds excluded.

| Milestone | `FlightId` | `PlannedTick` |
|---|---|---|
| `InboundAirborne` | arrival | `max(0, STA − CRUISE_LEAD_TICKS)` (§12.6, Q-048) |
| `Landed` | arrival | `STA` |
| `OffRunway` | arrival | `STA + OccupancyTicks` |
| `OnStand` | arrival | `STA + OccupancyTicks + RouteTicks(threshold, stand)`, for the stand the aircraft actually reaches |
| `DoorsOpen` | arrival | planned `OnStand` + `DoorsOpenDelayMinutes × TICKS_PER_SIM_MINUTE` |
| `OnStand` | departure | `max(0, STD − MinTurnaround)` (§12.8 step 3; §12.7 for a rotation-less departure, whose due tick this is) |
| `DoorsClosed` | departure | `STD` |
| `Pushback` | departure | `STD` |
| `TakeoffRoll` | departure | `STD + RouteTicks(stand, threshold)`, where `threshold` is that of the runway chosen at `Pushback` (§12.5) |
| `Airborne` | departure | planned `TakeoffRoll` + `OccupancyTicks` |

`sim.delay` measures only `Landed` and arrival `OnStand`, and departure
`OnStand`, `Pushback` and `Airborne` (`14-interfaces-delay.md` §14.4); the
others are still binding, for the UI and for later checkpoints.

### Which `FlightId` gets which milestone

`11-interfaces-schedule.md` §11.3 gives an arrival and its linked departure
**separate `FlightId`s**. Each `AircraftTrack` (§12.9) belongs to exactly one
`FlightId`, so the milestone sequence splits across the two:

- The **arrival**'s `FlightId` carries `InboundAirborne`, `Landed`,
  `OffRunway`, `OnStand`, `DoorsOpen`, and (`sim.turnaround`'s)
  `DeboardComplete`. Its track's useful `AircraftLegPhase` sequence ends at
  `OnStand` — it never reaches `AwaitingPushbackClearance` or `Departed`.
  With a rotation, the track is removed at the handoff (§12.8, Q-062).
- The **departure**'s `FlightId` carries (`sim.turnaround`'s) `ReadyToBoard`
  and `BoardingComplete`, then `DoorsClosed`, `Pushback`, `TakeoffRoll`,
  `Airborne`. Its track is created directly in `AircraftLegPhase.OnStand`
  (§12.9's "resumes from `OnStand`, no approach") — it never receives its own
  `Landed`/`OffRunway`/`DoorsOpen`, because the door only opens once, for
  deplaning, under the arrival's `FlightId`.
- The stand itself does not change occupant between the two: the departure's
  track is created at the arrival's `Stand` the tick `sim.turnaround` (or the
  §12.8 fallback) reaches the equivalent of "ready to hand off" — precisely,
  the `Tick` after the arrival's `DeboardComplete`, and never before its
  `DoorsOpen` (§12.8 step 3, Q-062), or the fallback's handoff tick —
  carrying `StandState` forward without a `StandAssigned` event, since the
  stand was never freed.

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

interface IAirsideLayoutLoader {
  AirsideLayout Load(AirsideLayout raw)                              // validate
  AirsideLayout Parse(ReadOnlySpan<byte> file, string sourceName)    // parse the fixture file, then Load (Q-009)
}

readonly struct AirsideRules {           // construction data, beside the layout; D6
  uint32 BoardingHoldMaxMinutes          // §12.8 "The boarding hold"; 0 disables the hold
  uint32 DoorsOpenDelayMinutes           // §12.3 DoorsOpen after OnStand; 0 = the same tick (Q-047)
}
```

`AirsideRules` is construction data: immutable for the session and not
hashed, like the layout. Neither field is **ever a compiled constant**.
Both are **balance values**
(`04-data-schemas.md`, "Balance values are human-owned"), stored in
`data/balance/airside_rules.json` and authored by the human owner, not by
an agent:

- `BoardingHoldMaxMinutes`: the playtest value is 10 sim-minutes (D6),
  marked for tuning after the T-025 playtest;
- `DoorsOpenDelayMinutes` (Q-047): the playtest value is 2 sim-minutes.
  **HUMAN DECISION, owner, 2026-09-30.** The owner writes it to
  `data/balance/airside_rules.json`. Until then, that file fails the
  schema, and no build that parses it can register `sim.airside` (`16`).

Test fixtures carry their own values beside their tests. Those are fixture
sizing, not balance.

Load-time validation (Q-046). Each check is a hard failure (`07-conventions.md`).
`Load` runs the checks in the order below and throws at the first failure.
Within a check, the lists are taken in the order runways, nodes, edges,
stands, and the first list with a failure is used. Within that list, the
offending object with the lowest id is reported (for a duplicate, the
duplicated id). Within that object, the first failing field in the
file-format key order of the shape below is reported. The failure is a
`FormatException` whose message starts with `sim.airside: ` and contains
the **field name** and the **id or ids** given here, in decimal. Tests
assert the type, the prefix, the field name and the ids, and nothing else.

1. **Ranges.** Each object's **own** `id` is `≥ 1`;
   `DeclaredCapacityPerHour ≥ 1`; `OccupancyTicks ≥ 1`;
   `TraversalTicks ≥ 1`; `0 ≤ ActiveDirectionDeg ≤ 359`;
   `DepartureSinkNode.Value ≥ 1`. The message names the object's id and
   the field, using the file-format key (`id`, `occupancy_ticks`, and so
   on). Node-reference fields (`from`, `to`, `node`, `threshold_node`) are
   **not** range-checked. A `0` there fails at check 4 as an undeclared
   node, because no node has id `0` once check 1 has passed.
2. **Non-empty.** At least one runway and at least one stand. The message
   names the list, `runways` or `stands`, and no id.
3. **Unique ids.** `RunwayDef.Id`, `TaxiNodeDef.Id`, `TaxiEdgeDef.Id` and
   `StandDef.Id` are each unique within their list. The message names the
   list and the duplicated id.
4. **References.** Every `TaxiEdgeDef.From`/`To`, `StandDef.Node` and
   `RunwayDef.ThresholdNode` is a declared node. The message names the
   referring object's id, the field (`from`, `to`, `node` or
   `threshold_node`) and the undeclared node id.
5. **Kinds.** A `StandDef.Node` is a `StandPosition` node, and a
   `RunwayDef.ThresholdNode` is a `RunwayThreshold` node. The message names
   the stand or runway id, the field and the node id.
6. **Connected.** Let `R0` be the lowest-id `RunwayThreshold` node. Every
   `StandPosition` and every `RunwayThreshold` node is reachable from `R0`,
   and `R0` is reachable from it, over edges in their allowed directions.
   That is the same as "every one reachable from every other". The message
   names the field `nodes` and the lowest-id such node that fails either
   way.

`Load` returns each of the four lists sorted by ascending id. So nothing
downstream sees declaration order, as in `18` §18.2.
`MaxAircraftSizeCategory` is resolved in `CreateSystem` through
`services.Content`, not in `Load`. An unresolved id throws
`FormatException` whose message starts with `sim.airside: ` and contains
the stand id and the category id.

### File format (Q-046)

Binding. It replaces §12.13's earlier "the worker's choice". `Parse` reads
the strict JSON subset of `08` §8.11 "The loader", with `18` §18.2's
rules and one exception: `18` §18.2 forbids `true` and `false`, but here
they are allowed, and only as the value of `bidirectional`. Duplicate,
unknown and missing keys are **parse** (shape) failures, and keys may come
in any order. `sim.airside` hand-parses the file, with no package.
The exact shape is:

```
{
  "schema_version": 1,
  "runways": [ { "id": <uint16>, "threshold_node": <uint16>, "active_direction_deg": <int32>,
                 "declared_capacity_per_hour": <int32>, "occupancy_ticks": <uint32> }, ... ],
  "nodes":   [ { "id": <uint16>, "kind": "runway_threshold" | "junction" | "stand_position" }, ... ],
  "edges":   [ { "id": <uint16>, "from": <uint16>, "to": <uint16>,
                 "traversal_ticks": <uint32>, "bidirectional": true | false }, ... ],
  "stands":  [ { "id": <uint16>, "node": <uint16>,
                 "max_aircraft_size_category": "<ContentId>", "departure_sink_node": <uint32> }, ... ]
}
```

- Each object has exactly the keys shown. `schema_version` must be `1`.
- An integer is `0` or `-?[1-9][0-9]*`. One outside its C# type's range,
  for example `70000` for a `uint16`, is a **parse** failure.
- After parsing, `Parse` calls `Load` on the result. So the validation
  list above, its ranges included (for example `"id": 0`), is `Load`'s.
  Its message is `Load`'s, prefixed by `sourceName` and `": "`, and it
  carries **no** `line <n>`.
- **Parse failures** are syntax, shape (keys, types, `schema_version`)
  and C#-type range. Each throws `FormatException` whose message starts
  with `sourceName` followed by `": "` and contains the 1-based `line
  <n>`. A `null` `sourceName` throws `ArgumentNullException`.
- The §12.13 fixture is `tests/fixtures/airside/phase1-single-runway.json`.

### Routing

Computed once at load, not per tick (`01-architecture.md`, pathfinding rule —
this is the same "never per-agent A\*" discipline applied to a graph small
enough to solve exhaustively up front): for every `(RunwayThreshold,
StandPosition)` pair, the least-`TraversalTicks` path, ties broken by ascending
`TaxiEdgeId` at the first diverging edge. Stored as an ordered edge list per
pair. A layout with more than one runway repeats this per runway. The
Phase 0/1 fixture ships exactly one, and §12.5 "Runway choice" says which
runway a movement uses.

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

**When a movement is requested (Q-052).** An arrival requests `Landed` in
its `Tick` at `STA`, exactly. A departure requests `TakeoffRoll` in the
tick it reaches its runway's `ThresholdNode`. A movement that can claim a
slot in its request tick fires its milestone that tick and never enters
the queue.

**Order of runway work in one tick (Q-049, Q-052).** This is step S7 of
§12.8a:

1. For each runway in ascending `RunwayId`, its hold queue is walked in
   queue order. Each entry that passes both checks is released, and the
   walk stops at the first that does not.
2. Then the tick's new requests are taken in ascending `FlightId`. Arrivals
   at `STA` and departures just arrived at a threshold are one list. Each
   arrival chooses its runway at that moment (below), so the queue lengths
   it sees include the requests handled before it in this step. Each
   request then either claims a slot, or holds and emits
   `AircraftHeldForRunway`. So `EventId` order, and queue order, is
   ascending `FlightId` within a tick.

A queue head and a new request never compete for the same slot, because
the queue is walked first.

**Runway choice (Q-049) — HUMAN DECISION, owner, 2026-09-30: the stopgap
is accepted.** A movement chooses its runway
once, at its request point, and never changes it. For an arrival, that
point is the `Landed` request at `STA`. For a departure, it is `Pushback`,
because the taxi route leads to one threshold. It takes the runway with the
fewest aircraft in its hold queue, as `RunwayQueueLength` would report at
that moment. Ties go to the lowest `RunwayId`. With one runway, this is
always that runway. It is a Phase 0/1 stopgap, so that multi-runway
fixtures such as the max-tier budget layout spread their load. It is not a
runway-allocation system. Runway modes, segregated arrival and departure
runways, and player control are gameplay, and they are the owner's, to
come by amendment, like gate assignment (`18` §18.5).

---

## 12.6 The taxiway model

Single-lane: each `TaxiEdgeDef` holds `TAXI_EDGE_CAPACITY` (1) aircraft. An
aircraft ready to enter an edge that is occupied **holds**, ordered by ascending
`EventId` of the hold request, and emits
`AircraftHeldOnTaxiway { Flight, EdgeId, blocking: <occupant FlightId> }`. On
release, `AircraftHeldOnTaxiwayReleased`, `Cause` set to the hold event.

- **The blocker named (Q-060).** A hold is only ever emitted in S6.2
  (§12.8a), while that edge is being decided. Its `Blocking` is the flight
  that denied the grant, and it is never null:
  - if the edge is occupied in the S1 snapshot, `Blocking` is that
    snapshot occupant. This holds even when the occupant left the edge in
    S6.1 of the same tick, since the edge is still not enterable before
    `t + 1`;
  - if the edge is free in the snapshot, `Blocking` is the flight granted
    the edge in this step: the head of its hold queue, or else the
    lowest-`FlightId` asker. That holds whatever its `FlightId` is
    relative to the held flight.

  `Blocking` is fixed when the hold is emitted, and it is not updated
  while the flight waits. `AircraftHeldOnTaxiwayReleased.Blocking` is
  always **null**, as `DepartureHeldForPassengersReleased.HeldAt` is
  (`10` §10.6). `sim.delay` reads the explanation from the opening event
  (`14` §14.3), and copying the blocker onto the release would need new
  per-hold state that `AircraftTrack` does not carry.

- Entering an edge sets `PhaseEnteredAt = tick`, `DueAt = tick +
  TraversalTicks`. On or after `DueAt` the aircraft advances to the edge's `To`
  node, subject to the next edge's occupancy per this same rule — an aircraft
  never "vanishes" between edges; if the next edge is occupied it holds at the
  node, not mid-edge.
- **Same-tick edge release (Q-054).** Edge entry reads occupancy from the
  start-of-tick snapshot (§12.8a S1). An edge that its occupant leaves
  during tick `t` can be entered no earlier than `t + 1`. A hold on it is
  released, with its `AircraftHeldOnTaxiwayReleased`, at `t + 1` at the
  earliest. In step S6, the edges are taken in ascending `TaxiEdgeId`. An
  edge that is free in the snapshot, and not yet granted in this step,
  goes first to the head of its hold queue. If the queue is empty, it
  goes to the aircraft with the lowest `FlightId` among those asking for it
  in that tick. Every other aircraft asking for it holds, joining the queue
  in ascending `FlightId`, so queue order is also the `EventId` order of
  their hold events. "Asking" includes every aircraft placed at a node
  earlier in the same tick, whichever step placed it:
  - an arrival that reached `OffRunway` in S3 and was granted a stand in
    S5, whether as a new request or from the stand-wait queue;
  - a departure pushed back in S4, in an S5 chain (§12.8a), or in an S6.1
    chain.
- `InboundAirborne` fires at `max(0, ScheduledTick − CRUISE_LEAD_TICKS)`
  (from `IScheduleSystem.TryGetFlight`, §12.9), always on time. Phase 0/1
  does not simulate an origin airport, so nothing can make it late. Both
  `PlannedTick` and `ActualTick` equal that tick. The clamp (Q-048) only
  matters for a day-0 arrival with `STA < CRUISE_LEAD_TICKS`, such as
  `phase0-200.csv`'s 00:20 arrival, and it is the same clamp as `11`
  §11.6's show-up clamp. Day 0 is materialised at construction (`11`
  §11.9), so the flight is visible at tick 0.

  > **LOW CONFIDENCE — `InboundAirborne` is a formality event at Phase 0/1.**
  > It exists so the milestone sequence and the delay tree's `late_inbound`
  > category have a well-formed anchor once a later phase models delay
  > upstream of this airport. Until then it never varies and adds no
  > information. Flagged for the human owner in case that reads as dead
  > weight rather than a placeholder worth keeping.

- `Landed` fires when a runway slot is granted per §12.5, at or after
  `ScheduledTick`, never earlier. The request is made at `STA` exactly
  (§12.5, Q-052). At Phase 0/1 no aircraft is ever early, since
  `InboundAirborne` is fixed. So the earlier "early aircraft holds
  off-graph with `queuePosition = 0`" case cannot occur, and no
  `AircraftHeldForRunway` with `queuePosition = 0` is ever emitted. The case
  returns by amendment when upstream delay is modelled.
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
- **The assignment rule (Q-051).** An arrival is assigned a stand once:
  in S5 of its `OffRunway` tick if one is free, or otherwise in the S5
  where the stand-wait queue gives it one. It takes the compatible free
  stand with the **lowest
  `StandId`**. The earlier "earliest-declared" is dropped: `Load` sorts
  stands by id (§12.4), so declaration order means nothing. "Free" means
  free in the start-of-tick snapshot (§12.8a S1, Q-054), and not already
  granted earlier in the same step. A stand vacated by a `Pushback` during
  tick `t` is assignable from `t + 1`. A stand vacated by a `ReassignStand`
  applied at the boundary of `t` is free at `t`.
- **Reservation (Q-050).** Assignment sets `StandState.Occupant` at once,
  so the stand is held from assignment to `Pushback` inclusive, taxi-in
  included. `FreeStands()` excludes it from that tick on, and no other
  aircraft is given it. Only `OnStand` starts the turnaround (§12.8).
- **Waiting for a stand (Q-050, Q-053).** A requester with no compatible
  free stand joins the tail of the airport's single **stand-wait queue**.
  The queue is in joining order. Every entry is a `FlightId`, of one of two
  kinds:
  - an **arrival**. It emits `StandUnavailable { Flight, Stand: null,
    occupying: null }` as it joins, and it waits at its runway's threshold
    node with `Phase = HeldOnTaxiway`, `AtNode` = that node, `Stand` unset
    and `DueAt = TICK_UNSCHEDULED`. On success it emits `StandAssigned {
    Flight, Stand, occupying: null }`. Its `Cause` is the `Pushback` that
    freed that stand, or `EventRef.None` if a `ReassignStand` freed it.
    Then it asks for its first edge in S6 of the same tick;
  - a **rotation-less departure** (below). It has no track and emits no
    event while it waits.
- **Order of stand work in one tick (Q-050, Q-053).** This is step S5 of
  §12.8a:
  1. The stand-wait queue is walked in order. Each entry takes the
     lowest-id compatible stand that is free. An entry with no such stand
     keeps its place and does not block later entries.
  2. The tick's new requests are taken in ascending `FlightId`. They are
     arrivals that reached `OffRunway` in S3 of this tick, and
     rotation-less departures whose **start tick** is this tick (§12.11:
     `max(due tick, PublishTick + 1)`, or the due tick for day 0). Each takes
     the lowest-id compatible free stand, or joins the queue's tail in
     that order.
- A stand is held from assignment to `Pushback` inclusive (above).
  Reassigning an occupied stand is the `ReassignStand` command (§12.10).
  Nothing else forces a reassignment mid-turnaround.
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

### Rotation-less flights (no `Rotation` counterpart)

- A rotation-less **Arrival** (`HasRotation = false`) completes its
  arrival-side lifecycle exactly as normal, through `DoorsOpen` and (with
  `sim.turnaround` present) `DeboardComplete`, and then simply **stays on
  stand**: no departure track is ever created, `StandState.Occupant` stays
  set, and the stand is unavailable for the remainder of the run. This is
  intentional at Phase 0/1, not a bug — a single-leg arrival has nowhere to
  go without a linked departure. A scenario needing it to vacate needs a
  schedule amendment (e.g. a synthetic empty ferry departure), not a
  workaround here.
- A rotation-less **Departure** (`HasRotation = false`) is assumed already on
  its assigned stand at its **due tick**, `max(0, ScheduledTick −
  MinTurnaround)`. That is clamped as in Q-048. At its **start tick**
  (§12.11), in S5, `sim.airside` claims the lowest-id compatible free stand
  under the normal assignment rule above. The start tick is the due tick,
  except for a departure due at or before its own publication. In the same
  step it creates the `AircraftTrack` directly in `OnStand` phase at that
  stand, and fires its own `FlightMilestoneReached{OnStand}`, with
  `PlannedTick` = the due tick (§12.3, never moved) and `ActualTick` = that
  tick. The two are equal whenever the start tick is the due tick. That is
  the same trigger `sim.turnaround` uses to start jobs for any departure,
  rotation-linked or not.
- **No stand free (Q-053).** No track is created, and no event is emitted.
  The flight joins the stand-wait queue as a departure entry. In the S5
  where it gets a stand, the track is created exactly as above, at that
  stand, and `OnStand` fires with `PlannedTick` = the due tick and
  `ActualTick` = that tick. So a departure track still always starts in
  `OnStand` at a stand (§12.3, §12.9), and nothing about the flight exists
  in hashed track state before then. Its lateness at `OnStand` is not a
  stand interval. With no `StandUnavailable`, `14` §14.6 step 3 applies
  unchanged, and for `!HasRotation` all of it goes to the `Unexplained`
  leaf. Attributing it to stand shortage would need `14` amended, and it
  is not done here.
- **The OnStand tick.** For a rotation-less departure, the "creation tick"
  below is the tick its track is created, which is its actual `OnStand`
  tick, whether or not it waited. It is the fallback's doors-close anchor
  (§12.8), and the stand it names is the one `Absorb` uses.

  > **LOW CONFIDENCE — rotation-less departures get no real ground time under
  > the §12.8 fallback.** Without `sim.turnaround`, the fallback's
  > doors-close point for a rotation-less departure is its creation tick,
  > the actual `OnStand` tick. So in S5 the chain `Absorb`, `DoorsClosed`,
  > `Pushback` (or a boarding hold) runs at once after `OnStand` (§12.8a
  > "Chains"). `MinTurnaround` is spent once, to place the aircraft, and
  > has nothing left to cover boarding. The Phase 0/1
  > fixture requirement (`11-interfaces-schedule.md` §11.10) only needs one
  > such row to exist to exercise `TICK_UNSCHEDULED`, not to complete a
  > plausible turnaround, so this is left as a known gap rather than guessed
  > shut. A fixture that needs a *working* standalone departure needs a real
  > formula (e.g. `ScheduledTick - 2 * MinTurnaround` as the creation tick) —
  > a deliberate amendment, flagged for the human owner, not decided here.

---

## 12.8 The turnaround handshake, stand handoff, and the no-`sim.turnaround` fallback

### With `sim.turnaround` registered

1. `sim.turnaround` subscribes to the arrival's `OnStand`
   (`FlightMilestoneReached`) and starts that flight's jobs. It learns of
   this **only** through the event bus: `sim.turnaround` depends on
   `sim.airside` (`03-module-map.md`), so the call can never run the other
   way, and this is the "upward information flows as events" rule in
   practice.
2. `sim.turnaround` emits `DeboardComplete` for the **arrival**'s `FlightId`
   when deboarding finishes.
3. `sim.airside` subscribes to `DeboardComplete`. On its next `Tick`, for an
   arrival it still has on stand: if `HasRotation`
   (`IScheduleSystem.TryGetRotation`), it creates the departure's
   `AircraftTrack` directly in `OnStand` phase at the same `Stand`,
   reassigns `StandState.Occupant` to the departure's `FlightId`, and fires
   `FlightMilestoneReached{OnStand}` for the **departure** (`PlannedTick =
   max(0, ScheduledTick − MinTurnaround)`, as §12.3, `ActualTick` = this tick, `Cause` set to
   the `DeboardComplete` event). No `StandAssigned` event fires — the stand
   was never freed, only handed off. The arrival's track is removed in the
   same action (below, "The handed-off arrival"). If the arrival has no
   rotation, nothing further happens automatically; see "Rotation-less
   flights" above.
   **The handoff never runs before the arrival's `DoorsOpen` (Q-062).**
   `sim.turnaround` starts `Deboard` at the arrival's `OnStand` (`13`
   §13.6), so `DeboardComplete` can come before `DoorsOpen` is due. In
   that case the handoff is not due on the next `Tick`. It runs at once
   after the arrival's `DoorsOpen`, in the same turn, as a chain (§12.8a
   "Chains"). The `Cause` of the departure's `OnStand` is still the
   `DeboardComplete` event.
4. `sim.turnaround`, seeing the departure's `OnStand`, starts
   departure-prep jobs and eventually emits `ReadyToBoard` then
   `BoardingComplete` for the **departure**'s `FlightId`.
5. `sim.airside` subscribes to `BoardingComplete`. On its next `Tick`, for a
   departure it has on stand: calls `Absorb` (§12.7), fires
   `FlightMilestoneReached{DoorsClosed}` for the departure, then `Pushback`.
   The boarding hold below may defer this step.

### Without `sim.turnaround` — the fallback T-021 ships and tests

**T-021 ships before `sim.turnaround` exists** (`00-overview.md` build order).
A build with `sim.airside` but no `sim.turnaround` registered would otherwise
wait forever for events nobody will ever publish, and `10-events.md` §10.3's
emission discipline forbids `sim.airside` emitting `DeboardComplete`,
`ReadyToBoard` or `BoardingComplete` on `sim.turnaround`'s behalf — each event
has exactly one owning module. So, precisely as `11-interfaces-schedule.md`
§11.6 does for `sim.flow`, the whole ground stay collapses into one fixed
block:

> If `sim.turnaround` is not registered in this build, `sim.airside` treats
> `FlightRecord.MinTurnaround` (from `IScheduleSystem.TryGetFlight`) as the
> full ground time. At `DoorsOpen tick + MinTurnaround` (converted via
> `TICKS_PER_SIM_MINUTE`), for an arrival with `HasRotation`, in the same
> tick: create the departure's `AircraftTrack` in `OnStand` phase at the same
> `Stand`, reassign `StandState.Occupant`, remove the arrival's track (below,
> "The handed-off arrival"), fire `FlightMilestoneReached
> {OnStand}` for the departure, call `Absorb`, then fire
> `FlightMilestoneReached{DoorsClosed}` for the departure. The boarding hold
> below may defer the `Absorb` and `DoorsClosed`. For an arrival with
> no rotation, only "Rotation-less flights" above applies; none of this fires.
> Once `sim.turnaround` **is** registered, this fallback never fires — the
> handshake above takes over entirely and `MinTurnaround` is read only as the
> *planned* ground time for `sim.delay`'s gap arithmetic, never as a timer.

This is the same decoupling pattern as `11-interfaces-schedule.md` §11.6
("running without `sim.flow`"): a module's own hash and behaviour must not
depend on whether a downstream consumer exists, only on whether an upstream
producer does.

### The handed-off arrival (Q-062)

At the handoff, on either path, the arrival **leaves tracked state**, as a
departure does at `Airborne` (§12.6). `10` §10.3 rule 2 already names the
handoff as the point where an arrival leaves the simulation. The handoff
action runs in this order: create the departure's track, reassign `StandState.Occupant`,
remove the arrival's track, then fire the departure's `OnStand`, and in
the fallback, go on to its doors-close point.

- From that action on, `TryGetTrack(arrival)` returns false, and the
  arrival is not in `TrackedFlights()`. It is never tracked again, and
  no later action in this file names it. Every arrival milestone that
  `sim.airside` owns (§12.3) has fired by then.
- The removal is in the hashed state from that tick: the arrival is no
  longer in §12.12 item 4. Nothing else records it, since the departure
  now holds the stand.
- So after the handoff, the arrival has no phase and no `Stand` to pin,
  whether its departure is still on stand, has pushed back, or the stand
  has been given to another flight. `15` §15.4 draws only tracked
  flights, so one aircraft is drawn at the stand, not two.
- A `ReassignStand` naming the arrival after the handoff fails §12.10's
  check 1, because the flight is not tracked.
- Only an arrival **with** a rotation is handed off. A rotation-less
  arrival stays tracked, `OnStand` at its stand, for the rest of the run
  (§12.7 "Rotation-less flights").

### The boarding hold (both paths) — D6

HUMAN DECISION — owner (delegated), 2026-09-23 (D6), reversible. A departure
waits for passengers still in the terminal, for a bounded time. Without the
hold, a security queue could never reach the delay tree.

The **doors-close point** of a departure is the tick at which step 5 above
(with `sim.turnaround`), or the fallback (without it), would call `Absorb` and
fire `DoorsClosed`. At that point, if `sim.flow` is registered,
`BoardingHoldMaxMinutes > 0`, and `IFlowSystem.TryGetOutstanding(flight)`
returns true, `sim.airside` does **not** close the doors. Instead, that tick
it:

- emits `DepartureHeldForPassengers { Flight, outstanding = o.Count,
  heldAt = o.MostHeldAt }`, with `Cause` set to the event that led to the
  doors-close point (`BoardingComplete`, or the departure's `OnStand` in the
  fallback);
- sets the track's `PassengerHoldSince = tick` and
  `DueAt = tick + BoardingHoldMaxMinutes × TICKS_PER_SIM_MINUTE`.

On every later `Tick`, each held departure is re-evaluated, in ascending
`FlightId`. When `TryGetOutstanding` returns false (everyone has reached the
gate), or when `tick >= DueAt` (the hold has run out), that tick
`sim.airside`:

1. emits `DepartureHeldForPassengersReleased { Flight, outstanding }`, where
   `outstanding` is the count still upstream (0 if everyone arrived), with
   `Cause` set to the hold event;
2. clears `PassengerHoldSince` to `TICK_UNSCHEDULED`;
3. proceeds exactly as at an unheld doors-close point: `Absorb`,
   `DoorsClosed`, then `Pushback`.

Anyone still outstanding at that `Absorb` becomes a missed passenger, which
`sim.flow` reports as `PassengersMissedFlight` exactly as before (§12.7).

- A departure is held **at most once**. The release goes straight on to close
  the doors.
- The hold is measured from the actual doors-close point, not from the planned
  one: a flight whose boarding finished late still gets the whole hold.
  `DoorsClosed`'s `PlannedTick` stays `STD` (§12.3), so the hold's ticks show
  up as lateness at the `Pushback` checkpoint, where `sim.delay` attributes
  them to this interval (`14-interfaces-delay.md` §14.5).
- The stand stays occupied during the hold. An **arrival** waiting for that
  stand gets an ordinary `StandUnavailable` interval, so the knock-on delay
  is attributed as well. A rotation-less departure waiting for it emits
  nothing (§12.7, Q-053), and its delay goes to `Unexplained`.
- `sim.flow` not registered, or `BoardingHoldMaxMinutes == 0`: no hold, and
  neither event is emitted. As above, behaviour depends only on the upstream
  producer, here the passengers' owner.
- `sim.airside` runs before `sim.flow` in the registry (3 before 4), so it
  sees `sim.flow`'s state as of the end of the previous tick. That is
  deterministic and is the same on every run.

> **LOW CONFIDENCE — measuring from the actual doors-close point, and releasing
> as soon as the count reaches zero.** Measuring from the plan would give a
> late-boarding flight less hold, or none. Releasing only at the gate count
> ignores passengers that are not injected yet, because their show-up bucket
> is still due. That can only happen to a flight closing before STD, which the
> early-pushback note in `CHANGELOG.md` (Q-007) already flags. Both are
> revisited after T-025, together with the balance value.

---

## 12.8a Order within `Tick` (Q-054)

Binding on `sim.airside`'s `Tick` at tick `t`. Where §12.5 to §12.8 read
loosely, this section governs. `ReassignStand` has already applied at the
tick boundary, before `Tick` (`08` §8.7). Consumed events
(`DeboardComplete`, `BoardingComplete`) were recorded by their handlers in
phase 3 of an earlier tick, and they act here. Within a step, flights are
taken in ascending `FlightId` unless the step says otherwise.

- **S1 Snapshot.** Record which taxi edges are occupied and which stands
  are free, as at the start of `Tick`. Every edge grant (S6) and stand
  grant (S5) in this tick reads this snapshot. A grant made earlier in the
  same step also makes that edge or stand unavailable for the rest of the
  step. No edge or stand vacated during this tick is granted before
  `t + 1`. **Runways are the exception.** S7 reads each runway's
  `Occupant` and `NextSlotTick` live, so a runway cleared by `OffRunway`
  or `Airborne` in S3 can be claimed in S7 of the same tick. `Occupant`
  already models the surface, and `NextSlotTick` the separation.
- **S2 Inbound.** Start tracking each pending arrival whose
  `InboundAirborne` tick is `t` (§12.6, §12.11 "How flights are found"),
  and fire `InboundAirborne`.
- **S3 Runway exits.** `OffRunway` for each arrival due, which puts it at
  its threshold node needing a stand (S5). `Airborne` for each departure
  due, which leaves tracked state.
- **S4 Ground.** For each tracked flight on stand, the §12.8 actions that
  fell due at `t` in an earlier tick's reckoning, each flight's in §12.8's
  own order:
  - the handshake or fallback departure-track creation, which also
    removes the arrival's track (§12.8 "The handed-off arrival", Q-062);
  - `DoorsOpen` at `OnStand + DoorsOpenDelayMinutes`;
  - the boarding-hold re-evaluation;
  - `Absorb`, `DoorsClosed` and `Pushback`. `Pushback` chooses the runway
    (§12.5), vacates the stand, and puts the aircraft at the stand node,
    asking for its first edge in S6.
- **S5 Stands.** §12.7 "Order of stand work in one tick": the queue
  first, then new requests. A rotation-less departure's track is created
  here, when it gets a stand, and its `OnStand` starts a chain (below).
- **S6 Taxi.**
  1. Every aircraft on an edge with `DueAt ≤ t` reaches the edge's end
     node and leaves the edge, which frees it for `t + 1`. An aircraft
     holding at a node occupies no edge. If the node ends its route:
     - an arrival fires `OnStand`, which starts a chain (below);
     - a departure is at its threshold and requests `TakeoffRoll` in S7.
     Otherwise it asks for the route's next edge.
  2. Edge grants, per §12.6 "Same-tick edge release", in ascending
     `TaxiEdgeId`. A departure pushed back by a chain in S6.1 asks for
     its first edge here.
- **S7 Runway.** §12.5 "Order of runway work in one tick": the queues
  first, then new requests (`Landed` at `STA`, and `TakeoffRoll` from S6).

**Chains (Q-054, zero delays).** When an action makes another §12.8 action
due at the current tick `t`, that action runs **at once**, in the same
turn, and not in S4. So does every action it makes due at `t` in turn.
This covers `DoorsOpenDelayMinutes = 0`, `MinTurnaround = 0`, or both:

- an arrival's `OnStand` in S6 fires `DoorsOpen` at once when the delay
  is 0;
- a `DoorsOpen` whose fallback handoff (`DoorsOpen + MinTurnaround`) is
  `t` creates the departure at once;
- a `DoorsOpen` for an arrival whose `DeboardComplete` is already
  recorded hands off at once (§12.8 step 3, Q-062);
- a departure's `OnStand`, whether created by a handoff or in S5, runs
  its fallback doors-close point at once when it is due at `t`. That is
  `Absorb` then `DoorsClosed` then `Pushback`, or the start of a boarding
  hold.

The whole chain belongs to the turn of the flight whose action started
it. A departure `D` created in arrival `A`'s turn runs its chained
actions right after `D`'s `OnStand`, inside `A`'s turn, and not at `D`'s
own `FlightId` position. Later in the same step, `D` has nothing left due
at `t`, so nothing runs twice. From the next tick on, `D`'s due actions
(for example boarding-hold re-evaluations) are taken at `D`'s own
`FlightId` position.

A step's events are published in the order its actions run, so this order
fixes every `EventId` order that §12.5 to §12.7 and `sim.delay`'s
`DelayEventId`s use.

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
  AirsideLayout Layout()                                    // the validated layout of §12.4, immutable
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
  Tick             DueAt               // TICK_UNSCHEDULED (11 §11.2) while holding indefinitely; the hold deadline during a boarding hold
  Tick             PassengerHoldSince  // §12.8 boarding hold start; TICK_UNSCHEDULED unless held
}

readonly struct StandState { StandId Id; FlightId? Occupant }
```

**`AtNode` and `OnEdge` together.** While `OnEdge` is set, `AtNode` holds the
node the aircraft **entered the edge from**, and `EdgeProgress` runs from 0 at
`AtNode` to 1 at the edge's other endpoint. For a `Bidirectional` edge this is
the only way to know the direction of travel. While `OnEdge` is unset,
`AtNode` is the node the aircraft is at (including holding at a node, §12.6),
or unset if the aircraft is off-graph (approaching, or held before `Landed`).

`Layout()` returns the layout `IAirsideLayoutLoader` validated at load. It is
load-time data, not runtime state, so it is not hashed (§12.12). It exists for
presentation (`15-interfaces-render.md` §15.4), which must not load the airside
fixture a second time on its own.

`AircraftLegPhase` is reused across both legs of a rotation: the sequence for
an `Arrival` runs left to right through `OnStand`, and a handed-off arrival
then leaves tracked state (§12.8, Q-062); a `Departure` resumes from
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
| `ReassignStand` | `FlightId`, `StandId newStand` — byte layout `08` §8.7 | Only while the flight's `Phase == OnStand`. Takes effect at the next tick boundary: old stand's occupant clears, new stand's occupant is set, no milestone re-fires. |

Handler (`08` §8.7, Q-010), registered in `AirsideFactory.CreateSystem`.
`Validate` checks the payload only: a length other than 10, or an unknown
`StandId`, is `MalformedPayload`. Whether the stand is occupied, whether it
is compatible, and whether the flight is `OnStand` are **runtime state**, so
they are checked at `Apply`. A command that fails them is a logged no-op,
not a rejection. This replaces the earlier "Rejected (`NotPermitted`) if
occupied or incompatible", which admission cannot decide deterministically.

**The no-op log line (Q-056).** `Apply` checks in this order and stops at
the first failure:

1. the flight is not tracked, or its `Phase ≠ OnStand`, or it is not its
   `Stand`'s current `Occupant`: reason 1. An arrival whose stand was
   handed off to its departure (§12.8 step 3) fails the first clause,
   because its track was removed at the handoff (§12.8 "The handed-off
   arrival", Q-062). Since then, no tracked `OnStand` flight can fail the
   last clause. It is kept so that the check stays total;
2. `newStand` has an occupant, which includes the flight's own stand:
   reason 2;
3. the aircraft is incompatible with `newStand` (§12.7): reason 3.

On a failure it changes nothing and writes one line, `Write(tick,
LogLevel.Info, SystemId(3), LogKey.AirsideReassignStandNoOp, new
LogArgs(flight.Value, newStand.Value, reason))`, where `tick` is the tick
whose boundary applies the command (`08` §8.7). The key is appended to
`sim.core`'s `LogKey` as `AirsideReassignStandNoOp = 1` (`08` §8.10).

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
| `DepartureHeldForPassengers` / `DepartureHeldForPassengersReleased` | §12.8, the boarding hold |

**Consumed:**

| Event | From | Reaction |
|---|---|---|
| `FlightMilestoneReached { Milestone = DeboardComplete }` | `sim.turnaround` | §12.8, create the departure's track, hand off the stand and remove the arrival's track, next tick, and never before the arrival's `DoorsOpen` (Q-062) |
| `FlightMilestoneReached { Milestone = BoardingComplete }` | `sim.turnaround` | §12.8, transition to `DoorsClosed` next tick |
| `FlightPlanPublished` | `sim.schedule` | §12.11 "How flights are found": add the flight to the pending list |

`sim.airside` also calls `IScheduleSystem.TryGetRotation` (query, downward,
`11-interfaces-schedule.md` §11.7) at the handoff in §12.8 step 3, to find the
departure `FlightId` to create a track for.

`sim.airside` calls `IFlowSystem.TryGetOutstanding` (query, downward,
`09-interfaces-flow.md` §9.7a) at a departure's doors-close point, and once per
tick for each departure on a boarding hold (§12.8). It is the module's only
`sim.flow` read.

`sim.airside` calls `IScheduleSystem.TryGetFlight` (query, downward,
`11-interfaces-schedule.md` §11.7) to read `ScheduledTick`, `MinTurnaround` and
`AircraftType`.

**How flights are found (Q-053, revised).** `sim.airside` never scans the
published flights in `Tick`. It keeps a **pending list** of flights it
must start: arrivals, which start at `InboundAirborne`, and rotation-less
departures, which start at their start tick, `max(due tick, PublishTick +
1)` (below). A departure with a rotation is
never pending, because it is created at the handoff (§12.8).

- **Day 0.** `CreateSystem` reads day 0 through
  `schedule.MovementsBetween(0, TICKS_PER_SIM_DAY, kind)` for both kinds.
  Day 0 is materialised at construction (`11` §11.9), and construction may
  allocate. It adds every arrival, and every departure without a rotation
  (`TryGetRotation` false), to the pending list.
- **Later days.** `sim.airside` subscribes to `FlightPlanPublished` (`11`
  §11.5). The handler, in phase 3, appends the flight to the pending list
  on the same rule. It ignores a flight with `ScheduledTick <
  TICKS_PER_SIM_DAY`, which day 0 already covered. A later flight is
  published at `ScheduledTick − 14 400`, and its `InboundAirborne` is at
  `STA − 1 200`. So every later arrival is pending well before it starts.
- **The start tick.** Each pending entry has a **start tick**:
  - for an arrival, its `InboundAirborne` tick;
  - for a rotation-less departure, `max(due tick, PublishTick + 1)`. The
    day-0 read has no publication lag, so for day-0 departures it is the
    due tick itself.

  A departure's start tick is later than its due tick only when its due
  tick is at or before its publication. That needs a `MinTurnaround` of
  1 440 minutes or more. Its `OnStand` then keeps `PlannedTick` = the due
  tick, the §12.3 formula, and has `ActualTick` = the tick it actually
  gets a stand, which is at least the start tick. The difference is
  lateness at `OnStand` (`14` §14.6: `Unexplained` for `!HasRotation`).
  `PlannedTick` is never moved, because §12.3 is schedule-anchored (`10`
  §10.4).
- **Each tick.** S2 starts the pending arrivals whose start tick is `t`,
  and S5 takes the pending departures whose start tick is `t`, each in
  ascending `FlightId`. The per-tick scan is O(pending), which is bounded
  by `PENDING_FLIGHTS_CAPACITY` (§12.2, §12.12).
- **Removal (exact).** An entry leaves the pending list at the moment it
  is taken, and never later:
  - an arrival leaves in S2 of its start tick, when its track is created
    and `InboundAirborne` fires;
  - a rotation-less departure leaves in S5 of its start tick, when it is
    taken as a new request. If it gets a stand, its track is created. If
    it does not, it moves to the stand-wait queue as a departure entry.
    Either way it is no longer pending.

  So no flight is ever both pending and waiting for a stand, and no flight
  is pending after its start tick.
- **Why 2048 holds.** Under that removal rule, an entry is pending from
  its publication, `ScheduledTick − 14 400` (or construction for day 0),
  to its start tick, which is at most `ScheduledTick`. So at any tick `t`,
  every pending flight has `ScheduledTick` in `[t, t + 14 400]`, a window
  of one sim-day. That window can span two calendar days. At `01`'s max
  tier of 800 daily movements, two days hold 1 600 movements, so the list
  holds at most 1 600 flights, under 2 048.
- **Overflow.** During a tick, an append past the capacity throws
  `SimInvariantException` (`08` §8.5a) at that tick. Such an append can
  only come from the `FlightPlanPublished` handler, in phase 3. During the day-0 read in `CreateSystem` there is no tick, so
  `CreateSystem` throws `ArgumentException` for parameter `schedule`,
  whose message starts with `sim.airside: ` and names the pending list and
  the capacity. That is consistent with `08` §8.5a, which defines
  `SimInvariantException` only during a tick, and leaves exceptions from
  construction unwrapped.

---

## 12.12 State, hashing, RNG and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9):

1. Runway state, ascending `RunwayId`: `NextSlotTick`, `Occupant`, hold queue
   in queue order (each entry's `FlightId`).
2. Taxi edge state, ascending `TaxiEdgeId`: `Occupant`, hold queue in queue
   order.
3. Stand state, ascending `StandId`: `Occupant`. Then the stand-wait queue
   (§12.7, Q-050), in queue order: each entry's `FlightId`, preceded by the
   queue length.
4. Tracked aircraft, ascending `FlightId`: every field of `AircraftTrack`.
   A flight is fed only while tracked: from `InboundAirborne` to its
   handoff for an arrival with a rotation (§12.8, Q-062), and from
   `OnStand` to `Airborne` for a departure.
5. The pending list (§12.11): its length, then each entry's `FlightId`, in
   ascending `FlightId`.

§12.8a's S1 snapshot leaves no state across ticks, so nothing more is
hashed for it.

Not hashed, because derived: `FreeStands()`, `RunwayQueueLength()`, the
precomputed routing table (§12.4, fixed at load and part of the loaded layout,
not runtime state). `AirsideRules` is load-time data and not hashed either.
The hold state is hashed through `AircraftTrack.PassengerHoldSince` and
`DueAt`.

**RNG: none at Phase 0/1.** Every choice in this file (stand assignment,
routing, hold-queue order) is a declared deterministic rule; the module
declares no stream and contributes no stream state to its hash, the same
posture as `sim.schedule` (`11-interfaces-schedule.md` §11.9).

Budget: **0.80 ms/tick at max tier** (`03-module-map.md`). Per-tick work is
O(tracked aircraft + runways + held edges + pending + waiters × stands). It is never a
scan of the taxi graph. The only scans of the stand list are the S5 grants
(§12.8a), at O(stands) per waiter or new request, and `FreeStands()`'s
O(stands). Stands are bounded by `03-module-map.md`'s max tier at 60.

- **Hard bounds (Q-050).** The stand-wait queue holds at most
  `STAND_WAIT_CAPACITY` entries, and the pending list at most
  `PENDING_FLIGHTS_CAPACITY` (§12.2). Both are preallocated at
  `CreateSystem` and never grow. An append during a tick that would exceed
  either one throws `SimInvariantException` (`08` §8.5a). The message names
  the flight and the structure, and nothing is appended. The one append
  outside a tick is the pending list's day-0 read in `CreateSystem`. There
  an overflow throws `ArgumentException` instead (§12.11 "Overflow"). The
  stand-wait queue is empty at construction. For the stand-wait
  queue that means the layout has too few stands for its schedule, for
  example because rotation-less arrivals hold stands for good (§12.7). At
  Phase 0/1 the layout is a fixed fixture, so this is a fixture error. When
  construction exists, the bound is revisited by amendment.
- No allocation in the update path (`07-conventions.md`, `08` §8.5). The
  routing table and the day-0 read are done once at construction, off the
  tick path. The update path includes the module's event handlers and its
  `ReassignStand` `Apply` (`03` "How a budget is measured", Q-061). So the
  `FlightPlanPublished` and `FlightMilestoneReached` handlers allocate
  nothing. They write into storage preallocated at `CreateSystem`, the
  pending list included. A `Budget`-trait allocation test meters them as
  `03` says, with at least one later-day `FlightPlanPublished` appended to
  the pending list, and one `ReassignStand` applied and one a no-op,
  inside the metered window. The
  `FlightMilestoneReached` handler, which also receives the module's own
  milestones, is metered in a build with `turnaroundRegistered` true. In
  that build a probe registered at position 5 publishes `DeboardComplete`
  and `BoardingComplete` inside the window.

---

## 12.12a Construction (Q-009)

```
AirsideFactory.CreateLayoutLoader() -> IAirsideLayoutLoader
AirsideFactory.CreateSystem(in SystemServices services, in AirsideLayout layout,
                            in AirsideRules rules, IScheduleSystem schedule,
                            IFlowSystem? flow, bool turnaroundRegistered) -> IAirsideSystem
```

- `layout` must come from `IAirsideLayoutLoader` (validated).
- `rules` is parsed by the caller: `AirsideRules` is two integers,
  `BoardingHoldMaxMinutes` and `DoorsOpenDelayMinutes` (Q-047, Q-055), and
  no sim module parses JSON. The format is `04-data-schemas.md`.
- `flow` null: no `Inject`/`Absorb` calls and no boarding hold (§12.7, §12.8).
- `turnaroundRegistered` is what selects §12.8's handshake (true) or its
  fallback (false). It was previously implicit, and it is now an explicit
  construction input, fixed for the session.
- `schedule` is required. `sim.airside` is not constructed without
  `sim.schedule`.

## 12.13 The Phase 0/1 fixture (T-021)

`tests/fixtures/airside/phase1-single-runway.json`, in §12.4's "File
format" (Q-046), binding on the Test Author:

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
path is `sim.turnaround`'s task (T-022, Q-006) to test end to end once it
exists. T-021 reaches the handshake only through a probe registered at
position 5, as two tests below do (Q-061, Q-062).

Done-condition tests this spec expects to exist, phrased per
`07-conventions.md`:

- `test_layout_rejects_disconnected_graph`
- `test_runway_pacing_holds_when_declared_capacity_exceeded`
- `test_taxi_edge_single_occupant_holds_second_aircraft`
- `test_stand_assignment_prefers_lowest_id_among_compatible_free_stands`
- `test_doors_close_after_min_turnaround_when_turnaround_absent`
- `test_arrival_pax_count_zero_skips_inject`
- `test_airside_tick_consumes_no_rng`
- `test_layout_parse_fixture_file_equals_built_layout` (Q-046): `Parse` of
  the fixture file equals `Load` of the same layout built in code.
- `test_layout_parse_rejects_unknown_key_with_line_number` (Q-046)
- `test_inbound_airborne_clamps_to_tick_zero_before_cruise_lead` (Q-048)
- `test_stand_reserved_from_assignment_until_pushback` (Q-050)
- `test_stand_freed_by_pushback_is_assigned_next_tick` (Q-054)
- `test_taxi_hold_released_the_tick_after_blocker_leaves_edge` (Q-054)
- `test_rotationless_departure_waits_for_stand_with_no_track_and_fires_late_on_stand` (Q-053)
- `test_reassign_stand_no_op_logs_key_and_reason` (Q-056), with the
  handed-off arrival among its cases.
- `test_layout_parse_range_failure_has_no_line_number` (Q-046)
- `test_same_sta_arrivals_request_runway_in_flight_id_order` (Q-049, Q-052)
- `test_same_tick_stand_requests_take_stands_in_flight_id_order` (Q-050)
- `test_zero_door_delay_and_turnaround_chain_in_one_tick` (Q-054, §12.8a
  "Chains"): with both values 0, arrival `OnStand`, `DoorsOpen`, and the
  departure's `OnStand`, `DoorsClosed` and `Pushback` all fire in one tick,
  in that order, inside the arrival's turn.
- `test_stand_wait_queue_overflow_throws_sim_invariant` (§12.12)
- `test_pending_list_overflow_in_publication_handler_throws_sim_invariant`
  (§12.11 "Overflow")
- `test_pending_list_overflow_in_day_zero_read_throws_argument_exception`
  (§12.11 "Overflow")
- `test_pending_list_does_not_overflow_over_three_max_tier_days` (§12.11
  "Removal", "Why 2048 holds"). Three sim-days of an 800-movement schedule
  run without `SimInvariantException`, which fails without removal by
  about day 3.
- `test_departure_due_before_publication_keeps_planned_on_stand_formula`
  (§12.11 "The start tick")
- `test_taxi_hold_blocking_names_same_step_grantee_and_release_blocking_is_null`
  (Q-060): two aircraft ask in one tick for an edge that is free in the
  snapshot. The hold names the grantee, and its release carries null.
- `test_taxi_hold_blocking_names_snapshot_occupant_that_left_this_tick`
  (Q-060)
- `test_airside_update_path_allocates_nothing_including_handlers` (Q-061,
  §12.12)
- `test_handed_off_arrival_leaves_tracked_state_at_handoff` (Q-062): from
  the handoff tick, `TryGetTrack(arrival)` is false and the arrival is not
  in `TrackedFlights()`, before and after the departure's `Pushback`.
- `test_handoff_waits_for_arrival_doors_open_when_deboard_completes_first`
  (Q-062, §12.8 step 3): `turnaroundRegistered` true, with a probe at
  position 5 publishing the arrival's `DeboardComplete` before its
  `DoorsOpen` is due. The departure's `OnStand` fires right after the
  arrival's `DoorsOpen`, in the same tick and turn.

Boarding-hold tests (D6). They run with `sim.flow` registered, or with a fake
`IFlowSystem` answering `TryGetOutstanding`, and they belong to whichever task
the Planner assigns the hold to:

- `test_departure_holds_while_passengers_outstanding_and_releases_when_all_at_gate`
- `test_departure_hold_times_out_at_boarding_hold_max_and_remainder_is_missed`
- `test_departure_hold_emits_one_event_pair_with_most_held_at_node`
- `test_no_hold_when_flow_absent_or_hold_max_zero`
- `test_boarding_hold_applies_in_turnaround_fallback_path`
