# T-026 — `sim.core`: event catalogue, relocated types, content definitions and `ContentIndexFactory`

| Field | Value |
|---|---|
| Status | IN_PROGRESS |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001, T-003 |
| Spec source | `spec/03-module-map.md` ("Events are immutable value types, defined in `sim.core`"); `spec/10-events.md` §10.4, §10.9 (the full event catalogue and relocated types, answers Q-018); `spec/12-interfaces-airside.md` §12.9; `spec/13-interfaces-turnaround.md` §13.3–§13.4; `spec/14-interfaces-delay.md` §14.3 (including D6's `PassengerHold`); `spec/06-delay-attribution.md` (`DelayCategory`); `spec/18-interfaces-world.md` §18.2 (`NodeId`/`EdgeId`, answers part of Q-012); `spec/08-interfaces-core.md` §8.11, §8.11a (content definition types and `ContentIndexFactory`, answers Q-011 and, for the factory's ownership, coordinator decision on the `db78df0` batch) |
| Blocked by | — |

**Dependency correction (systematic type-dependency recheck):**
`PaxProfileDefinition.WalkSpeedMps` and several `QueueProfileDefinition`
fields are `Fx` (`08` §8.11) — this task cannot compile without `Fx`
(T-003). `Depends on` is amended to `T-001, T-003`.

**Scope amendment, third round (Architect batch, Q-018; coordinator
decision on `ContentIndexFactory`):** this task's scope grew again, twice:

1. **Every `10-events.md` §10.9 event struct**, not only the payload types
   an event's fields carry. Q-018 answered the ownership question the
   earlier version of this task file left implicit ("no task declares the
   event structs themselves") with "option (a)": `sim.core` owns **all**
   of them, and this is the established place to add them, for the same
   reason as everything else this task already carries.
2. **`ContentIndexFactory`** (coordinator decision, this batch): T-026 owns
   it, since it owns the content definition types `ContentIndexFactory`
   constructs from, and T-027's loader feeds it. This is a small piece of
   real logic (sorting and duplicate-detection), not "types only" — noted
   as the one exception to this task's usual framing, below.

This task must now merge before **T-007, T-008, T-012, T-021, T-022, T-024
and T-027** — `T-008` is added to that list this round, since it emits
`FlightPlanPublished` (now declared here, not by any module) and uses
`AirlineId`/`MovementKind` (now relocated here).

## Why this task exists

Writing `spec/14-interfaces-delay.md` (Q-007) surfaced a scheduling gap the
Architect flagged but explicitly left to the Planner (`spec/CHANGELOG.md`,
2026-09-23, "Two things noticed and not changed... (2)"): every event type,
and every type an event field carries, must live in `sim.core`, because
`03-module-map.md` states events are "defined in `sim.core`" and `sim.delay`
may reference no assembly outside `sim.core` (`delay_module_never_writes`,
`14-interfaces-delay.md` §14.13). No task was scheduled to write
`src/sim/core/**` for this. Q-018 later confirmed the same is true of the
event *structs themselves* (not only their field types): nobody had been
asked to declare `FlightPlanPublished`, `FlightMilestoneReached`, and the
rest, even though every emitting module (`sim.schedule`, `sim.airside`,
`sim.turnaround`, `sim.flow`) may write only its own module directory.

This task authors **types only, no logic** (except `ContentIndexFactory`,
noted separately below): the id structs, enums, event structs and payload
structs that `10-events.md`, `12-interfaces-airside.md`,
`13-interfaces-turnaround.md`, `06`/`14-interfaces-delay.md`,
`18-interfaces-world.md` and `08` §8.11 already fully specify
(signature-level, not invented here), placed in `src/sim/core/**` where the
event catalogue and the content definition types live. The interface files
that describe them for readability are unchanged and remain the binding
source of each type's exact shape; this task changes only *where the
compiled type lives*, never its fields, names or values.

## Writable paths

```
src/sim/core/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory
exclusively (`07-conventions.md` "Solution layout and build"); the path
guard already blocks a worker grant there. This task's earlier grant of
`tests/sim/core/**` is dropped.

Same shared write surface as T-001–T-006 (`tasks/queue.md`'s Phase 0
release order note) — do not release this task concurrently with another
open `sim.core` task touching the same files; default to sequential.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.11, §8.11a, `spec/10-events.md` (all of it),
`spec/11-interfaces-schedule.md` §11.3 (`AirlineId`/`MovementKind`),
`spec/12-interfaces-airside.md` §12.9, `spec/13-interfaces-turnaround.md`
§13.3–§13.4, `spec/14-interfaces-delay.md` §14.3, `spec/06-delay-attribution.md`,
`spec/18-interfaces-world.md` §18.2, `spec/04-data-schemas.md` (the content
fields these types carry)

## Interface to implement

Copied verbatim from the modules that already specify them; this task
relocates them, it does not redesign them.

```
// from spec/11-interfaces-schedule.md §11.3
struct AirlineId { uint32 Value }
enum MovementKind { Arrival, Departure }

// from spec/12-interfaces-airside.md §12.4/§12.9
struct RunwayId   { uint16 Value }
struct StandId    { uint16 Value }
struct TaxiNodeId { uint16 Value }
struct TaxiEdgeId { uint16 Value }

// from spec/13-interfaces-turnaround.md §13.3
struct VehicleId { uint16 Value }
struct JobId     { uint64 Value }

enum JobKind {
  Deboard, BaggageUnload,
  CabinClean, Catering, Fuel, BaggageLoad,
  PushbackPrep, Boarding
}
enum VehicleKind { CleaningCrew, CateringTruck, FuelTruck, BaggageTractor, PushbackTug }
enum JobStatus    { Blocked, Active, Completed }
enum ResourceKind { Vehicle, JobDependency, Crew }

// from spec/06-delay-attribution.md and spec/14-interfaces-delay.md §14.3
enum DelayCategory {
  late_inbound, runway_congestion, taxi_congestion, stand_unavailable,
  ground_handling, fuel, catering, cleaning, loading, pushback,
  crew, passenger_late, security_queue, immigration_queue, baggage,
  weather, deicing, atc_flow, incident, policy_constraint, propagated
}

struct DelayEventId { uint64 Value }         // sim.delay-allocated at runtime; 0 = none

enum DelaySource {
  FlightTotal, InboundAircraft, RunwayHold, TaxiwayHold,
  StandUnavailable, TurnaroundJobWait, Unexplained,
  PassengerHold                              // D6; appended last, no ordinal moves
}

readonly struct DelayExplanation {
  DelaySource Source
  uint64      A
  uint64      B
}

enum DelayNodeKind { FlightTotal, Allocation }              // relocated, Q-018 — 14 §14.3

readonly struct DelayNode {                                  // relocated, Q-018 — 14 §14.3
  DelayEventId    Id
  DelayNodeKind   Kind
  FlightId        Subject
  DelayEventId    Parent
  DelayCategory?  Category
  uint64          Ticks
  SimMinutes      Minutes                    // derived; never hashed
  bool            RootCause
  FlightId        LinkedFlight
  EventRef        SourceEvent
  DelayExplanation Explanation
  Tick            CreatedAt
}

// from spec/18-interfaces-world.md §18.2 (Q-012) — events carry these
struct NodeId { uint32 Value }
struct EdgeId { uint32 Value }

// relocated, Q-018 — 09-interfaces-flow.md, still allocated by sim.flow
struct CohortId { uint64 Value }

// relocated, Q-018 — 10-events.md §10.4
enum FlightMilestone {
  PlanPublished, InboundAirborne, Landed, OffRunway, OnStand, DoorsOpen,
  DeboardComplete, ReadyToBoard, BoardingComplete, DoorsClosed, Pushback,
  TakeoffRoll, Airborne
}

// from spec/08-interfaces-core.md §8.11 (Q-011) — content definition types
readonly struct ContentId { string Value }     // ordinal equality and order; hashed as its UTF-8 bytes

enum ContentKind { SizeCategory, Aircraft, PaxProfile, QueueProfile }

interface IContentDefinition { ContentId Id { get }; ContentKind Kind { get } }

readonly struct SizeCategoryDefinition : IContentDefinition { ContentId Id; int32 Ordinal }
readonly struct AircraftDefinition     : IContentDefinition { ContentId Id; ContentId SizeCategory }

readonly struct ShowUpBucket { uint32 MinutesBeforeStd; uint32 SharePermille }
readonly struct PaxProfileDefinition   : IContentDefinition {
  ContentId Id
  Fx        WalkSpeedMps
  IReadOnlyList<ShowUpBucket> ShowUpCurve
}

readonly struct QueueProfileDefinition : IContentDefinition {
  ContentId     Id
  Fx            ServiceRatePerServerPerMinute
  int32         CapacityStanding
  Fx            ThresholdWaitMinutes
  Fx            HysteresisMinutes
  DelayCategory Category                       // security_queue | immigration_queue
}
```

**The full event catalogue (`10` §10.9, binding, copied verbatim — every
event struct declared and compiled in `sim.core` by this task):**

```
// Phase 0
event FlightPlanPublished {
  FlightId     Flight
  MovementKind Kind
  FlightId     Rotation             // Flight itself if !HasRotation
  bool         HasRotation
  AirlineId    Airline
  ContentId    AircraftType
  Tick         SchedArr
  Tick         SchedDep
  SimMinutes   MinTurnaround
}
event FlightMilestoneReached { FlightId Flight; FlightMilestone Milestone; Tick PlannedTick; Tick ActualTick }
event QueueThresholdExceeded { NodeId Node; Fx WaitMinutes; int32 ServersOpen; int32 ServerCount }
event QueueThresholdCleared  { NodeId Node; Fx WaitMinutes; int32 ServersOpen; int32 ServerCount }
event FlowBlocked            { CohortId Cohort; NodeId Held; NodeId BlockedBy }
event FlowUnblocked          { CohortId Cohort; NodeId Held; NodeId BlockedBy }

// Phase 1
event AircraftHeldForRunway              { FlightId Flight; RunwayId Runway; int32 QueuePosition }
event AircraftHeldForRunwayReleased      { FlightId Flight; RunwayId Runway; int32 QueuePosition }
event AircraftHeldOnTaxiway              { FlightId Flight; TaxiEdgeId Edge; FlightId? Blocking }
event AircraftHeldOnTaxiwayReleased      { FlightId Flight; TaxiEdgeId Edge; FlightId? Blocking }
event StandUnavailable                   { FlightId Flight; StandId? Stand; FlightId? Occupying }
event StandAssigned                      { FlightId Flight; StandId? Stand; FlightId? Occupying }
event DepartureHeldForPassengers         { FlightId Flight; int32 Outstanding; NodeId? HeldAt }
event DepartureHeldForPassengersReleased { FlightId Flight; int32 Outstanding; NodeId? HeldAt }   // HeldAt null
event PassengersArrivedAtGate            { FlightId Flight; int32 Count }
event PassengersMissedFlight             { FlightId Flight; int32 Count; NodeId LastBlockedAt }
event TurnaroundJobStarted               { FlightId Flight; JobKind Job; Tick PlannedStart }
event TurnaroundJobCompleted             { FlightId Flight; JobKind Job; Tick PlannedStart }
event TurnaroundJobBlocked               { FlightId Flight; JobKind Job; ResourceKind WaitingOn; EntityId? Resource; DelayCategory Category }
event TurnaroundJobUnblocked             { FlightId Flight; JobKind Job; ResourceKind WaitingOn; EntityId? Resource; DelayCategory Category }
event DelayEvent                         { DelayNode Node }
```

- A paired closing event carries the same fields as its opening event,
  with the values at closing time.
- `EventEnvelope`/`EventRef` are **not** declared here — they are T-001's
  (the bus needs them; `10` §10.9: "`EventEnvelope` and `EventRef` are
  T-001's, because the bus needs them"). Every event struct above holds
  payload fields only, per `08` §8.6.
- **Not declared yet:** `FlightPlanRevised` and `FlightCancelled` (`11`
  says schedule disruption needs its own amendment), and every Phase 2
  row. Their "reason key", "subject id" and "affected ids" have no type.
  Each is pinned by amendment before a task emits it — do not add one
  speculatively.

## Construction (`08` §8.11a, coordinator decision — the one exception to
"types only")

```
ContentIndexFactory.Create(IReadOnlyList<IContentDefinition> definitions) -> IContentIndex
```

This is real logic, not a type declaration — the one exception to this
task's usual "types only, no logic" framing, taken on because this task
already owns every content definition type the factory constructs from.
Binding (`08` §8.11a): sorts definitions by ordinal id and throws on a
duplicate id (across **all** kinds, not per-kind). The returned
`IContentIndex.TryGet<T>`/`AllOf` are read-only views over that sorted,
validated set — no mutation after construction, no re-sort, no caching
beyond what the sort already computed. `IContentIndex` itself is declared
by T-001 (shape only there); this task provides the one factory that
builds a real instance of it.

## Binding notes

- **No behaviour, no allocation logic, no hashing rule**, besides
  `ContentIndexFactory` above. Each consuming module (`sim.schedule`,
  `sim.airside`, `sim.turnaround`, `sim.flow`, `sim.delay`) still owns
  *when* and *how* it constructs, hashes and mutates values of these
  types, exactly as already specified in their own interface files. This
  task does not touch any hashed-state ordering.
- **`DelayEventId` is not `IIdAllocator`-drawn** (`14-interfaces-delay.md`
  §14.7): the type itself is a plain wrapper struct; the counter that
  allocates values of it lives in `sim.delay`'s own state (T-024), not here.
- **`DelayCategory`'s member list is fixed** (`06-delay-attribution.md`
  "extend only via spec amendment") — do not add, remove or reorder members;
  a reorder changes every serialised/hashed value that stores the enum's
  ordinal. The same applies to `FlightMilestone`'s and `DelaySource`'s
  member order.
- **`FlightId` and `EventId` are out of scope here** — both are T-001's
  (§8.4's single IDL block, implemented there in full). `NodeId`, `EdgeId`
  and `CohortId` **are** in scope here (Q-012, Q-018) — `CohortId` was
  previously declared inside `sim.flow`'s own task (T-007); T-007 has been
  updated to reference it from here instead. Do not rename or move
  anything not listed in the interface block above.
- **The content definition types are read by, but not owned by, every
  module that reads content** — `sim.flow` (`PaxProfileDefinition`,
  `QueueProfileDefinition`), `sim.airside`/`sim.schedule`
  (`AircraftDefinition`, `SizeCategoryDefinition`). This task does not add
  any read of them beyond `ContentIndexFactory`'s own sort/validate step.
  `IContentSource`/`IContentLoader` themselves are **not** built here —
  that is T-027.
- If a consuming module's spec is amended later to change one of these
  types' fields, that amendment lands here, in `src/sim/core/**`, not as a
  local redefinition inside the consuming module's directory.

## Events

Emitted: none. Consumed: none. This task defines payload types and the one
factory above; it does not touch the event bus, dispatch, or any
`ISimSystem`.

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect at least:

- `test_delay_category_member_list_matches_spec_and_is_stable` — asserts the
  exact `06-delay-attribution.md` member list and ordinal order
- `test_job_kind_enum_matches_spec_eight_values`
- `test_delay_source_passenger_hold_appended_last_ordinal_unchanged` —
  asserts the seven pre-existing `DelaySource` ordinals are untouched
- `test_flight_milestone_enum_matches_spec_thirteen_values_ordinal_order`
- `test_event_catalogue_structs_match_10_9_field_for_field` — every event
  struct listed above, checked against `10-events.md` §10.9's field list
  and order
- `test_airside_turnaround_delay_payload_types_compile_in_sim_core_only` —
  static: every type listed above resolves from the `sim.core` assembly,
  with no dependency edge from `sim.core` to `sim.airside`, `sim.turnaround`,
  `sim.delay`, `sim.flow`, `sim.world` or `sim.schedule`
- `test_delay_event_id_struct_shape` — plain `uint64` wrapper, `0` is a
  valid, unallocated value
- `test_content_definition_ids_unique_across_all_kinds` — asserted on
  `ContentIndexFactory.Create`'s own behaviour (duplicate id throws)
- `test_content_ids_compare_ordinally_not_by_default_hash` — guards the
  `07-conventions.md` "no hash code reaches behaviour" rule for `ContentId`
- `test_content_index_factory_sorts_by_ordinal_id` — `AllOf` returns ids in
  ordinal order regardless of input order

**Do not edit them.** If a test asserts a shape that contradicts the
consuming module's own spec, file an open question and stop — do not
silently reconcile a mismatch by guessing which side is right.

## Performance budget

None for the type declarations — no per-tick code. `ContentIndexFactory.Create`
is load-time only, off the tick path; it must not allocate pathologically
for the content set's size (~250 files at 1.0 scope, `00-overview.md`), but
there is no ms/tick ceiling to meet.

## Done when

- [ ] Interface matches spec exactly (relocated, not redesigned, except
      `ContentIndexFactory`'s own sort/validate logic)
- [ ] All assigned tests pass
- [ ] **Green per Q-016 (HUMAN DECISION, owner, 2026-09-24): until T-006
      merges, green = `ci/run-checks.sh`'s `path-guard` and `build-and-test`
      (`--fast`) jobs. The full script becomes mandatory once T-006 merges.**
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task must merge before **T-007, T-008, T-012, T-021, T-022, T-024 and
T-027** are released — each compiles against one or more of these types or
emits one of these events (T-007/T-012: `NodeId`/`EdgeId`, T-007 also
`CohortId`; T-008: `FlightPlanPublished`, `AirlineId`, `MovementKind`;
T-021: `RunwayId`/`StandId`/`TaxiNodeId`/`TaxiEdgeId`/
`DepartureHeldForPassengers` fields; T-022: `JobKind`/`VehicleKind`/
`JobStatus`/`ResourceKind`/`DelayCategory` on `TurnaroundJobBlocked`/
`Unblocked`; T-024: `DelayEventId`/`DelaySource`/`DelayExplanation`/
`DelayCategory`/`DelayNode`/`DelayNodeKind`; T-027: the content definition
types and `ContentIndexFactory`). See `tasks/queue.md`'s release-order
notes.

`PlayerId`/`CommandKind`/`ICommandHandler` are deliberately **not** added
here, even though they follow the same "scheduling gap" shape — the Planner
folded them into T-005 instead, since they extend the `Command` shape T-005
already owns and no code exists yet to split from. Do not duplicate them
here.

If, once this task is underway, the exact same type name is found already
declared somewhere unexpected (for instance if a worker on an earlier task
guessed at one of these types locally before this task existed), that is a
merge conflict for the Integrator to resolve by deleting the local guess in
favour of this task's `sim.core` definition — not a reason to redefine the
type twice.
