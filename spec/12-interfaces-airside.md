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

### `PlannedTick` per milestone

Binding, per `10-events.md` §10.4 (schedule-anchored and cumulative: never
shifted by an earlier milestone's actual lateness). `STA` is the arrival's
`ScheduledTick`, `STD` the departure's. `RouteTicks(a, b)` is the sum of
`TraversalTicks` along the precomputed route of §12.4 — the unimpeded taxi
time, holds excluded.

| Milestone | `FlightId` | `PlannedTick` |
|---|---|---|
| `InboundAirborne` | arrival | `STA − CRUISE_LEAD_TICKS` (§12.6) |
| `Landed` | arrival | `STA` |
| `OffRunway` | arrival | `STA + OccupancyTicks` |
| `OnStand` | arrival | `STA + OccupancyTicks + RouteTicks(threshold, stand)`, for the stand the aircraft actually reaches |
| `DoorsOpen` | arrival | planned `OnStand` + the fixed door delay |
| `OnStand` | departure | `STD − MinTurnaround` (§12.8 step 3; §12.7 for a rotation-less departure) |
| `DoorsClosed` | departure | `STD` |
| `Pushback` | departure | `STD` |
| `TakeoffRoll` | departure | `STD + RouteTicks(stand, threshold)` |
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
- The **departure**'s `FlightId` carries (`sim.turnaround`'s) `ReadyToBoard`
  and `BoardingComplete`, then `DoorsClosed`, `Pushback`, `TakeoffRoll`,
  `Airborne`. Its track is created directly in `AircraftLegPhase.OnStand`
  (§12.9's "resumes from `OnStand`, no approach") — it never receives its own
  `Landed`/`OffRunway`/`DoorsOpen`, because the door only opens once, for
  deplaning, under the arrival's `FlightId`.
- The stand itself does not change occupant between the two: the departure's
  track is created at the arrival's `Stand` the tick `sim.turnaround` (or the
  §12.8 fallback) reaches the equivalent of "ready to hand off" — precisely,
  the tick the arrival's `DeboardComplete` fires — carrying `StandState`
  forward without a `StandAssigned` event, since the stand was never freed.

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
}
```

`AirsideRules` is construction data: immutable for the session and not
hashed, like the layout. It is **never a compiled constant**.
`BoardingHoldMaxMinutes` is a **balance value**
(`04-data-schemas.md`, "Balance values are human-owned"). The playtest value is
10 sim-minutes (D6), stored in `data/balance/airside_rules.json` and authored
by the human owner, not by an agent. It is marked for tuning after the T-025
playtest. Test fixtures carry their own values beside their tests; those are
fixture sizing, not balance.

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
  its assigned stand at `ScheduledTick - MinTurnaround`: `sim.airside` creates
  its `AircraftTrack` directly in `OnStand` phase at that tick, claims the
  earliest compatible free stand under the normal assignment rule above, and
  fires its own `FlightMilestoneReached{OnStand}` (`PlannedTick = ActualTick
  = ScheduledTick - MinTurnaround`) — the same trigger `sim.turnaround` uses
  to start jobs for any departure, rotation-linked or not.

  > **LOW CONFIDENCE — rotation-less departures get no real ground time under
  > the §12.8 fallback.** Without `sim.turnaround`, the fallback timer also
  > measures `MinTurnaround` from this same creation tick, so `DoorsClosed`
  > fires the instant the track is created — `MinTurnaround` is spent once to
  > place the aircraft and has nothing left to cover boarding. The Phase 0/1
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
   ScheduledTick - MinTurnaround`, `ActualTick` = this tick, `Cause` set to
   the `DeboardComplete` event). No `StandAssigned` event fires — the stand
   was never freed, only handed off. If the arrival has no rotation, nothing
   further happens automatically; see "Rotation-less flights" above.
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
> `Stand`, reassign `StandState.Occupant`, fire `FlightMilestoneReached
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
- The stand stays occupied during the hold. An aircraft waiting for that stand
  gets an ordinary `StandUnavailable` interval, so the knock-on delay is
  attributed as well.
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
| `DepartureHeldForPassengers` / `DepartureHeldForPassengersReleased` | §12.8, the boarding hold |

**Consumed:**

| Event | From | Reaction |
|---|---|---|
| `FlightMilestoneReached { Milestone = DeboardComplete }` | `sim.turnaround` | §12.8, create the departure's track and hand off the stand, next tick |
| `FlightMilestoneReached { Milestone = BoardingComplete }` | `sim.turnaround` | §12.8, transition to `DoorsClosed` next tick |

`sim.airside` also calls `IScheduleSystem.TryGetRotation` (query, downward,
`11-interfaces-schedule.md` §11.7) at the handoff in §12.8 step 3, to find the
departure `FlightId` to create a track for.

`sim.airside` calls `IFlowSystem.TryGetOutstanding` (query, downward,
`09-interfaces-flow.md` §9.7a) at a departure's doors-close point, and once per
tick for each departure on a boarding hold (§12.8). It is the module's only
`sim.flow` read.

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
not runtime state). `AirsideRules` is load-time data and not hashed either.
The hold state is hashed through `AircraftTrack.PassengerHoldSince` and
`DueAt`.

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

## 12.12a Construction (Q-009)

```
AirsideFactory.CreateLayoutLoader() -> IAirsideLayoutLoader
AirsideFactory.CreateSystem(in SystemServices services, in AirsideLayout layout,
                            in AirsideRules rules, IScheduleSystem schedule,
                            IFlowSystem? flow, bool turnaroundRegistered) -> IAirsideSystem
```

- `layout` must come from `IAirsideLayoutLoader` (validated).
- `rules` is parsed by the caller: `AirsideRules` is two integers, and no sim
  module parses JSON. The format is `04-data-schemas.md`.
- `flow` null: no `Inject`/`Absorb` calls and no boarding hold (§12.7, §12.8).
- `turnaroundRegistered` is what selects §12.8's handshake (true) or its
  fallback (false). It was previously implicit, and it is now an explicit
  construction input, fixed for the session.
- `schedule` is required. `sim.airside` is not constructed without
  `sim.schedule`.

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

Boarding-hold tests (D6). They run with `sim.flow` registered, or with a fake
`IFlowSystem` answering `TryGetOutstanding`, and they belong to whichever task
the Planner assigns the hold to:

- `test_departure_holds_while_passengers_outstanding_and_releases_when_all_at_gate`
- `test_departure_hold_times_out_at_boarding_hold_max_and_remainder_is_missed`
- `test_departure_hold_emits_one_event_pair_with_most_held_at_node`
- `test_no_hold_when_flow_absent_or_hold_max_zero`
- `test_boarding_hold_applies_in_turnaround_fallback_path`
