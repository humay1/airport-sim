# T-021 — One runway, taxiway graph, four contact stands

| Field | Value |
|---|---|
| Status | IN PROGRESS (worker implemented locally, 106/110; blocked on Q-078/Q-079; test PR #78 approved) |
| Module | `sim.airside` |
| Assigned role | worker |
| Depends on | T-003 (merged #18), T-005 (merged #26), T-008 (merged #54), T-026 (merged #29); all merged |
| Spec source | `spec/00-overview.md` build order #3; `spec/12-interfaces-airside.md` (answers Q-005; §12.8's boarding hold answers part of Q-007 §14.9, HD D6; §12.8a step order, §12.11 pending list, §12.2 capacities, §12.4 file format and `AirsideRules`, §12.10 no-op log line, §12.13 tests: PR #67, `8cf445c`); `spec/03-module-map.md` "Budget tests: window and arithmetic" (PR #68) and "How a budget is measured" (Q-061, Q-064; PRs #73, #75); `spec/12-interfaces-airside.md` Q-060 to Q-063 (PRs #73, #75) |
| Blocked by | — |

**Dependency correction (systematic type-dependency recheck):**
`AircraftTrack.EdgeProgress` is `Fx` (T-003), and the `ReassignStand`
command handler this task registers is built against the `ICommandHandler`
contract T-005 publishes — this task cannot compile without either.
`Depends on` is amended to `T-003, T-005, T-008, T-026`.

## Writable paths

```
src/sim/airside/**
src/sim/core/LogKey.cs
AirportSim.sln
```

**`src/sim/core/LogKey.cs` (PR #67):** `12` §12.10 says "The key is appended
to `sim.core`'s `LogKey` as `AirsideReassignStandNoOp = 1` (`08` §8.10)." The
file today holds only `None = 0`. This task appends that one value and
nothing else in `src/sim/core/**`. `src/sim/core/**` is a shared write
surface, so do not release this task concurrently with any other task that
writes it (check the queue before release).

**Correction (Q-021):** `tests/**` (including `tests/fixtures/**`) is the
Test Author's territory exclusively; the path guard already blocks a worker
grant there. The earlier grants of `tests/sim/airside/**` and
`tests/fixtures/airside/**` are dropped.

**First task of a new module (`07` L8, Q-013):** this task creates
`src/sim/airside/AirportSim.Sim.Airside.csproj` and
`tests/sim/airside/AirportSim.Sim.Airside.Tests.csproj` (byte for byte per
`07` L2/L3) and adds both to `AirportSim.sln`. Never release this task
concurrently with any other "first task of a new module" (T-007, T-008,
T-012, T-020, T-022, T-024, T-029, T-031) — concurrent `.sln` edits
conflict (`07` L8).

`sim.airside` depends on `core`, `world`, `schedule` and `flow`
(`spec/03-module-map.md`, the last added by D6), but this task must build
and pass **without** calling into `sim.world` at all —
`spec/12-interfaces-airside.md` §12.1 ("On `sim.world`") is binding: the
taxiway/runway/stand graph is self-owned, loaded directly by this module, not
queried from `sim.world`. It must also build and pass **without**
`sim.turnaround` registered — §12.8's fallback is what this task actually
exercises; do not add a hard dependency on T-022 merging first. It must also
build and pass **with `sim.flow` absent or with a fake `IFlowSystem`** — the
boarding hold (below) is the only call into `sim.flow`
(`IFlowSystem.TryGetOutstanding`, T-023), and this task's own tests use a
fake for it per `spec/12-interfaces-airside.md` §12.13; do not add a hard
dependency on T-023 merging first.

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
  Tick             DueAt               // the hold deadline during a boarding hold, D6
  Tick             PassengerHoldSince  // §12.8 boarding hold start; TICK_UNSCHEDULED unless held
  EventRef         RecordedCause       // §12.8 steps 3 and 5, Q-062; EventRef.None unless a consumed event awaits action
}

readonly struct StandState { StandId Id; FlightId? Occupant }

readonly struct AirsideRules {           // construction data, beside the layout; D6
  uint32 BoardingHoldMaxMinutes          // §12.8 "The boarding hold"; 0 disables the hold
  uint32 DoorsOpenDelayMinutes           // §12.3 DoorsOpen after OnStand; 0 = the same tick (Q-047)
}

interface IAirsideSystem : ISimSystem {
  bool   TryGetTrack(FlightId flight, out AircraftTrack track)
  bool   TryGetStand(StandId id, out StandState stand)
  IReadOnlyList<StandId> FreeStands()
  int32  RunwayQueueLength(RunwayId runway)
  IReadOnlyList<FlightId> TrackedFlights()
  AirsideLayout Layout()                                    // the validated layout of §12.4, immutable
}
```

`AirsideRules` is construction data: immutable for the session, not
hashed. Per `12` §12.4, "Neither field is **ever a compiled constant**. Both
are **balance values** ... stored in `data/balance/airside_rules.json` and
authored by the human owner, not by an agent". `DoorsOpenDelayMinutes`
(Q-047): "the playtest value is 2 sim-minutes. **HUMAN DECISION, owner,
2026-09-30.**" This task reads both as constructor arguments; it does not
author `data/balance/airside_rules.json` (human-only). The schema
(`balance.schema.json`, now requiring both `boarding_hold_max_minutes` and
`doors_open_delay_minutes`) is T-028's.

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
  | `InboundAirborne` | arrival | `max(0, STA − CRUISE_LEAD_TICKS)` (§12.6, Q-048) |
  | `Landed` | arrival | `STA` |
  | `OffRunway` | arrival | `STA + OccupancyTicks` |
  | `OnStand` | arrival | `STA + OccupancyTicks + RouteTicks(threshold, stand)`, for the stand the aircraft actually reaches |
  | `DoorsOpen` | arrival | planned `OnStand` + `DoorsOpenDelayMinutes × TICKS_PER_SIM_MINUTE` |
  | `OnStand` | departure | `max(0, STD − MinTurnaround)` |
  | `DoorsClosed` | departure | `STD` |
  | `Pushback` | departure | `STD` |
  | `TakeoffRoll` | departure | `STD + RouteTicks(stand, threshold)`, where `threshold` is that of the runway chosen at `Pushback` (§12.5) |
  | `Airborne` | departure | planned `TakeoffRoll` + `OccupancyTicks` |

  `sim.delay` measures only `Landed` and arrival `OnStand`, and departure
  `OnStand`, `Pushback` and `Airborne` (`14-interfaces-delay.md` §14.4); the
  others are still binding, for the UI and for later checkpoints.
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
  `InboundAirborne` fires at `max(0, ScheduledTick − CRUISE_LEAD_TICKS)`
  (1200 ticks, the clamp is Q-048), always on time — Planned and Actual are
  equal, no upstream delay model exists yet. `Landed` never fires before
  `ScheduledTick`; the request is made at `STA` exactly (§12.5, Q-052).
- **Stands** (§12.7, Q-050, Q-051, Q-053): an arrival "takes the compatible
  free stand with the **lowest `StandId`**. The earlier 'earliest-declared'
  is dropped". "Free" means free in the start-of-tick snapshot (§12.8a S1),
  and not already granted earlier in the same step. "A stand vacated by a
  `Pushback` during tick `t` is assignable from `t + 1`." Assignment sets
  `StandState.Occupant` at once, so the stand is held "from assignment to
  `Pushback` inclusive, taxi-in included"; only `OnStand` starts the
  turnaround. A requester with no compatible free stand joins the tail of
  the single **stand-wait queue** (`STAND_WAIT_CAPACITY` 1024). An arrival
  emits `StandUnavailable { Flight, Stand: null, occupying: null }` as it
  joins and waits at its runway's threshold node with `Phase =
  HeldOnTaxiway`; on success it emits `StandAssigned` with `Cause` the
  freeing `Pushback`, or `EventRef.None` if a `ReassignStand` freed it. A
  rotation-less departure waiting for a stand has no track and emits
  nothing. S5 order: the queue is walked first, an entry with no stand
  keeping its place; then the tick's new requests in ascending `FlightId`.
  **Arrivals inject zero passengers at Phase 0/1** — `FlightRecord.PaxCount`
  is departures-only (`spec/11-interfaces-schedule.md` §11.4); do not call
  `Inject` at `DoorsOpen`, the call is a documented no-op until a future
  amendment adds arrival demand data. At `DoorsClosed`, call
  `IFlowSystem.Absorb(stand.DepartureSinkNode, flight)` once,
  unconditionally.
- **Order within `Tick`** (§12.8a, Q-054), binding: S1 Snapshot, S2 Inbound,
  S3 Runway exits, S4 Ground, S5 Stands, S6 Taxi, S7 Runway. "Within a step,
  flights are taken in ascending `FlightId` unless the step says otherwise."
  Edge and stand grants read the S1 snapshot; "Runways are the exception".
  **Chains** ("zero delays"): "When an action makes another §12.8 action due
  at the current tick `t`, that action runs **at once**, in the same turn".
  `ReassignStand` has already applied at the boundary before `Tick`. §12.8a
  governs where §12.5 to §12.8 read loosely.
- **The pending list** (§12.11, Q-053): `sim.airside` never scans published
  flights in `Tick`. `CreateSystem` reads day 0 through
  `schedule.MovementsBetween(0, TICKS_PER_SIM_DAY, kind)` for both kinds and
  adds every arrival and every rotation-less departure; a
  `FlightPlanPublished` handler (phase 3) appends later ones and "ignores a
  flight with `ScheduledTick < TICKS_PER_SIM_DAY`". A rotation-less
  departure's start tick is `max(due tick, PublishTick + 1)`, or the due
  tick for day 0. Removal is exact: "An entry leaves the pending list at the
  moment it is taken, and never later" (arrival in S2, departure in S5).
  `PENDING_FLIGHTS_CAPACITY` is 2048, `STAND_WAIT_CAPACITY` is 1024: both
  preallocated at `CreateSystem`, neither grows. Overflow during a tick
  throws `SimInvariantException`; in the day-0 read, `CreateSystem` throws
  `ArgumentException` for parameter `schedule`, message starting
  `sim.airside: `. Both structures are hashed per §12.12 items 3 and 5.
- **Runway choice** (§12.5, Q-049; "HUMAN DECISION, owner, 2026-09-30: the
  stopgap is accepted"): a movement chooses its runway once, at its request
  point (arrival: the `Landed` request at `STA`; departure: `Pushback`),
  taking "the runway with the fewest aircraft in its hold queue, as
  `RunwayQueueLength` would report at that moment. Ties go to the lowest
  `RunwayId`." Not a runway-allocation system.
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
- **The boarding hold, both paths** (§12.8, HD D6): the **doors-close
  point** of a departure is the tick step 5 of the handshake, or the
  fallback, would call `Absorb`/fire `DoorsClosed`. At that point, if
  `sim.flow` is registered, `BoardingHoldMaxMinutes > 0`, and
  `IFlowSystem.TryGetOutstanding(flight)` returns true, do **not** close
  the doors: that tick, emit `DepartureHeldForPassengers { Flight,
  outstanding = o.Count, heldAt = o.MostHeldAt }` (`Cause` = the event that
  led to the doors-close point), set `PassengerHoldSince = tick`,
  `DueAt = tick + BoardingHoldMaxMinutes × TICKS_PER_SIM_MINUTE`. On every
  later `Tick`, re-evaluate each held departure in ascending `FlightId`.
  When `TryGetOutstanding` returns false or `tick >= DueAt`: emit
  `DepartureHeldForPassengersReleased { Flight, outstanding }` (`outstanding`
  = count still upstream, 0 if all arrived, `Cause` = the hold event), clear
  `PassengerHoldSince` to `TICK_UNSCHEDULED`, then proceed as at an unheld
  doors-close point (`Absorb`, `DoorsClosed`, `Pushback`). A departure is
  held **at most once**. The hold is measured from the actual doors-close
  point, not the planned one; `DoorsClosed`'s `PlannedTick` stays `STD`, so
  the hold's ticks show up as lateness at `Pushback`. The stand stays
  occupied during the hold — an aircraft waiting for it gets an ordinary
  `StandUnavailable` interval. Anyone still outstanding at the eventual
  `Absorb` becomes a missed passenger, exactly as before (§12.7). `sim.flow`
  not registered, or `BoardingHoldMaxMinutes == 0`: no hold, neither event
  fires. `sim.airside` runs before `sim.flow` in the registry (3 before 4),
  so it sees `sim.flow`'s state as of the end of the previous tick —
  deterministic, same on every run.
- **Registry position 3** (§12.9), after `sim.schedule` (2), before
  `sim.flow` (4) and `sim.turnaround` (5).
- **`ReassignStand` command** (§12.10, Q-010 — amended): the handler's
  `Validate` checks the payload only (a length other than 10 bytes, or an
  unknown `StandId`, is `MalformedPayload`). Whether the flight is
  `OnStand`, whether the target stand is occupied, and whether it is
  compatible are **runtime state**, so they are checked at `Apply`, not at
  admission. A command that fails them there is a **logged no-op**, not a
  rejection — this replaces the earlier "rejected `NotPermitted` if occupied
  or incompatible", which admission cannot decide deterministically. On
  success: takes effect at the next tick boundary; old stand's occupant
  clears, new stand's occupant is set; no milestone re-fires. On a failed
  check `Apply` changes nothing and writes one line, `Write(tick,
  LogLevel.Info, SystemId(3), LogKey.AirsideReassignStandNoOp, new
  LogArgs(flight.Value, newStand.Value, reason))` with `reason` 1 (not
  tracked, not `OnStand`, or not its stand's current `Occupant`), 2
  (`newStand` has an occupant) or 3 (incompatible), checked in that order
  (§12.10, Q-056). Register the
  handler through `SystemServices.Commands` in `AirsideFactory.CreateSystem`
  (below), using the `ICommandHandler` contract T-005 publishes.
- **No RNG at Phase 0/1** (§12.12). Do not add a stream speculatively.

## Construction (`12` §12.12a, Q-009)

```
interface IAirsideLayoutLoader {
  AirsideLayout Load(AirsideLayout raw)                              // validate
  AirsideLayout Parse(ReadOnlySpan<byte> file, string sourceName)    // parse the fixture, then Load
}

AirsideFactory.CreateLayoutLoader() -> IAirsideLayoutLoader
AirsideFactory.CreateSystem(in SystemServices services, in AirsideLayout layout,
                            in AirsideRules rules, IScheduleSystem schedule,
                            IFlowSystem? flow, bool turnaroundRegistered) -> IAirsideSystem
```

- `layout` must come from `IAirsideLayoutLoader` (validated). `Parse` is
  new — this task's own `IAirsideLayoutLoader` interface previously named
  only `Load`; add `Parse` so nothing else has to load the fixture file a
  second time. `Parse` reads the strict JSON file format of `12` §12.4 "File
  format" (Q-046) exactly, hand-parsed with no package. Its failure rules
  are binding: parse failures throw `FormatException` with `sourceName` and
  `line <n>`; `Load` failures carry `sourceName` and no line.
- `rules` is parsed by the **caller** (the harness, or `app.host` later,
  §16.3) — `AirsideRules` is two integers, `BoardingHoldMaxMinutes` and
  `DoorsOpenDelayMinutes` (Q-047, Q-055), and no sim module parses JSON.
- `flow` null: no `Inject`/`Absorb` calls and no boarding hold.
- `turnaroundRegistered` selects §12.8's handshake (true) or its fallback
  (false). It was previously implicit ("is `sim.turnaround` registered in
  this build"); it is now an **explicit construction input**, fixed for the
  session — do not infer it from anything at runtime.
- `schedule` is required; `sim.airside` is not constructed without
  `sim.schedule`.

## Events

Emitted: `FlightMilestoneReached` (for the nine milestones listed above),
`AircraftHeldForRunway`/`AircraftHeldForRunwayReleased`,
`AircraftHeldOnTaxiway`/`AircraftHeldOnTaxiwayReleased`,
`StandUnavailable`/`StandAssigned`,
`DepartureHeldForPassengers`/`DepartureHeldForPassengersReleased` (§12.8, D6)

Also writes the log line of §12.10 (a log line, not an event).

Consumed: `FlightMilestoneReached{Milestone=DeboardComplete}` and
`FlightMilestoneReached{Milestone=BoardingComplete}` (both from
`sim.turnaround`, when registered)

Also consumed: `FlightPlanPublished` (`sim.schedule`, §12.11 pending list).

Calls (downward, query-only): `IFlowSystem.TryGetOutstanding` (T-023, `09`
§9.7a) — the module's only `sim.flow` read, made once at a departure's
doors-close point and once per tick per held departure.

## Tests to pass

```
tests/sim/airside/**
```

Written by the Test Author, against a companion fixture at
`tests/fixtures/airside/phase1-single-runway.json`, in `12` §12.4's "File
format" (Q-046), whose binding requirements are
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

The 17 tests added by PR #67 (`12` §12.13):

- `test_layout_parse_fixture_file_equals_built_layout`
- `test_layout_parse_rejects_unknown_key_with_line_number`
- `test_layout_parse_range_failure_has_no_line_number`
- `test_inbound_airborne_clamps_to_tick_zero_before_cruise_lead`
- `test_stand_reserved_from_assignment_until_pushback`
- `test_stand_freed_by_pushback_is_assigned_next_tick`
- `test_taxi_hold_released_the_tick_after_blocker_leaves_edge`
- `test_rotationless_departure_waits_for_stand_with_no_track_and_fires_late_on_stand`
- `test_reassign_stand_no_op_logs_key_and_reason` (the handed-off arrival among its cases)
- `test_same_sta_arrivals_request_runway_in_flight_id_order`
- `test_same_tick_stand_requests_take_stands_in_flight_id_order`
- `test_zero_door_delay_and_turnaround_chain_in_one_tick`
- `test_stand_wait_queue_overflow_throws_sim_invariant`
- `test_pending_list_overflow_in_publication_handler_throws_sim_invariant`
- `test_pending_list_overflow_in_day_zero_read_throws_argument_exception`
- `test_pending_list_does_not_overflow_over_three_max_tier_days`
- `test_departure_due_before_publication_keeps_planned_on_stand_formula`

Boarding-hold tests (§12.13, D6), run with `sim.flow` registered or with a
fake `IFlowSystem` answering `TryGetOutstanding` — this task's own to carry,
per that section's explicit assignment to "whichever task the Planner
assigns the hold to":

- `test_departure_holds_while_passengers_outstanding_and_releases_when_all_at_gate`
- `test_departure_hold_times_out_at_boarding_hold_max_and_remainder_is_missed`
- `test_departure_hold_emits_one_event_pair_with_most_held_at_node`
- `test_no_hold_when_flow_absent_or_hold_max_zero`
- `test_boarding_hold_applies_in_turnaround_fallback_path`

**`AirsideBudgetTests` (`Budget` trait, to the PR #68 rule).** A max-tier
budget test for `sim.airside` (0.80 ms/tick, `B = 800` whole microseconds)
that follows `03` "Budget tests: window and arithmetic" exactly, not the
older "across the day's ticks" reading:

- the sample is the module's `Tick` plus its handlers and `Apply`, timed by
  shims (`03` "Measured", Q-064; see the post-#73/#75 note above), one sample per
  tick, over exactly `n = 14 400` consecutive ticks after warm-up; no shorter
  window satisfies a budget; several windows are judged one by one, never as
  a union, and do not overlap;
- `long` arithmetic only: no `Int128`, no floating point, no `TimeSpan`.
  `u = min((d × 1 000 000 + f − 1) / f, C)` with `C = B × n + 1`, and the
  `d > (long.MaxValue − f + 1) / 1 000 000` guard giving `u = C`;
- passes iff `Σu ≤ B × n` and `u[(99 × n + 99) / 100 − 1] ≤ 2 × B` (sorted
  ascending); it reports `(Σu + n − 1) / n` and the p99 on every run;
- the load is `03`'s max-tier fixture (800 daily movements, 60 stands, 3
  runways), with a layout and schedule the Test Author sizes (fixture
  sizing, not balance). If `03` and `12` do not define it well enough, the
  Test Author stops and reports a spec gap;
- Slow (`07` L11a): 14 400 sampled ticks plus a warm-up of at most 14 400 is
  under rule (a)'s 144 000 ticks. Rule (b) applies if the CI-reported
  duration exceeds 5 s: then add `Slow` beside `Budget`. The window is never
  shortened.

**Post-#73/#75 sync (2026-10-01): Q-060 to Q-064.** `12` is the source; this
file cites it and does not restate it. The worker implements to §12.8 steps 3
and 5, §12.8a S4, §12.9 `RecordedCause`, §12.10 and §12.12; the tests are the
Test Author's. Additional §12.13 tests:

- `test_taxi_hold_blocking_names_same_step_grantee_and_release_blocking_is_null` (Q-060)
- `test_taxi_hold_blocking_names_snapshot_occupant_that_left_this_tick` (Q-060)
- `test_airside_update_path_allocates_nothing_including_handlers` (Q-061, §12.12). It meters `ISimHost.Step` and subtracts nothing for `sim.core`: it relies on Q-065 (merged #77, `08` §8.7 "Allocation"), which pins command application as allocation-free on ticks that complete normally. Test PR #78 is open.
- `test_handed_off_arrival_leaves_tracked_state_at_handoff` (Q-062)
- `test_handoff_waits_for_arrival_doors_open_when_deboard_completes_first` (Q-062; probe at position 5, `turnaroundRegistered` true)
- `test_runway_hold_queue_position_is_one_based_and_release_carries_zero` (Q-063)

Consequences for the worker, each by `12` and not new here:

- **`AircraftTrack.RecordedCause`** (`EventRef`, §12.9) is implemented, set only
  by the §12.8 step 3 and 5 handlers, cleared only by the action it waits for,
  **hashed in §12.12 item 4 and saved with the track**. The handed-off arrival
  leaves tracked state (Q-062): `TryGetTrack(arrival)` is false afterwards.
- `Blocking` and `QueuePosition` payloads follow Q-060 and Q-063 exactly; both
  `Released` events carry their empty value (null, 0).
- **The update path includes handlers and `ReassignStand` `Apply`** (Q-061): no
  allocation in the `FlightPlanPublished` or `FlightMilestoneReached` handlers
  or in `Apply`, which write only into the preallocated pending list and
  existing tracks' `RecordedCause`.
- **Test-side note for the Test Author:** a `Show.Track`-style helper that
  renders or compares an `AircraftTrack` must include `RecordedCause`, or two
  tracks that differ only in it compare equal.
- **`AirsideBudgetTests` times handlers (Q-064).** The sample is `Tick` plus
  the bodies of the module's handlers and `Apply`, each wrapped by a
  non-allocating shim in the test's `SystemServices` (`03` "Timing a module's
  handlers"), summed per tick; the arithmetic above then applies unchanged. Its
  max-tier run uses `turnaroundRegistered` true with a position-5 probe, so
  the `FlightMilestoneReached` handler is in the window (§12.12).

**Do not edit them.** If a test contradicts `spec/12-interfaces-airside.md`,
file an open question and stop.

## Performance budget

`0.80` ms/tick at max tier (`spec/03-module-map.md`,
`spec/12-interfaces-airside.md` §12.12), asserted as `Σu ≤ B × n` over a
14 400-sample window with p99 ≤ 1.6 ms (`AirsideBudgetTests` above). Per-tick
work is O(tracked aircraft + runways + held edges + pending + waiters ×
stands), never a scan of the taxi graph; `FreeStands()` is O(stands), bounded and cheap at max
tier (60 stands). No allocation in the update path, which includes the handlers and the `ReassignStand` `Apply` (Q-061); the routing table is
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

The boarding hold's LOW CONFIDENCE flags (§12.8) — measuring from the actual
doors-close point rather than the plan, and releasing at a zero outstanding
count rather than accounting for not-yet-injected passengers — are not this
task's to resolve; build to the rule as written and leave the flags for the
owner to revisit after T-025.

Q-006 is now answered (`spec/13-interfaces-turnaround.md`); the handshake this
task implements — `DeboardComplete` triggers the stand handoff to the
departure's `FlightId`, `BoardingComplete` triggers `DoorsClosed` — is the
one `sim.turnaround`'s own spec commits to producing, so no further amendment
is expected here. If a future change to either spec needs a different
handshake, that is a coordinated amendment to both `§12.8` and
`13-interfaces-turnaround.md` §13.6, not a local workaround in either
module's code.
