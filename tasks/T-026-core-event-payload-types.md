# T-026 — `sim.core`: Phase 1 event payload types (airside/turnaround/delay/world/content)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/03-module-map.md` ("Events are immutable value types, defined in `sim.core`"); `spec/10-events.md` §10.6 (the catalogue, including D6's new pair); `spec/12-interfaces-airside.md` §12.9; `spec/13-interfaces-turnaround.md` §13.3–§13.4; `spec/14-interfaces-delay.md` §14.3 (including D6's `PassengerHold`); `spec/06-delay-attribution.md` (`DelayCategory`); `spec/18-interfaces-world.md` §18.2 (`NodeId`/`EdgeId`, answers part of Q-012); `spec/08-interfaces-core.md` §8.11 (content definition types, answers part of Q-011) |
| Blocked by | — |

## Why this task exists

Writing `spec/14-interfaces-delay.md` (Q-007) surfaced a scheduling gap the
Architect flagged but explicitly left to the Planner (`spec/CHANGELOG.md`,
2026-09-23, "Two things noticed and not changed... (2)"): every event type,
and every type an event field carries (`RunwayId`, `JobKind`,
`DelayCategory`, `DelayEventId`, `DelaySource`, `DelayExplanation`, ...),
must live in `sim.core`, because `03-module-map.md` states events are
"defined in `sim.core`" and `sim.delay` may reference no assembly outside
`sim.core` (`delay_module_never_writes`, `14-interfaces-delay.md` §14.13).
But `T-021` may write only `src/sim/airside/**` and `T-022` only
`src/sim/turnaround/**` — neither is scheduled to write `src/sim/core/**`.
Without this task, nobody is scheduled to author these types where they need
to physically live, and T-021/T-022/T-024 cannot compile against them.

This task authors **types only, no logic**: the id structs, enums and
payload structs that `12-interfaces-airside.md`, `13-interfaces-turnaround.md`,
`06`/`14-interfaces-delay.md`, `18-interfaces-world.md` and `08` §8.11
already fully specify (signature-level, not invented here), placed in
`src/sim/core/**` where the event catalogue and the content definition
types live. The interface files that describe them for readability (`12`
§12.9, `13` §13.3–§13.4, `14` §14.3, `18` §18.2, `08` §8.11) are unchanged
and remain the binding source of each type's exact shape; this task changes
only *where the compiled type lives*, never its fields, names or values.

**Extended twice since first written**, both times for the same reason:
Q-012 (`18-interfaces-world.md`) needs `NodeId`/`EdgeId` in `sim.core`
because events carry them, and Q-011 (`08` §8.11) needs the content
definition types there because several modules read each one. Both land
here rather than in a new task, on the Planner's sizing call: the work is
the same shape (types only, no logic) as this task's original scope, and
`src/sim/core/**` is already this task's writable path. The **content
loader itself** (`IContentLoader`, the strict hand-written JSON parser) is
**not** added here — it is real parsing logic, not a type declaration, and
is sized as its own task, T-027.

## Writable paths

```
src/sim/core/**, tests/sim/core/**
```

Same shared write surface as T-001–T-006 (`tasks/queue.md`'s Phase 0 release
order note) — do not release this task concurrently with another open
`sim.core` task touching the same files; default to sequential.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.11, `spec/10-events.md`,
`spec/12-interfaces-airside.md` §12.9, `spec/13-interfaces-turnaround.md`
§13.3–§13.4, `spec/14-interfaces-delay.md` §14.3,
`spec/06-delay-attribution.md`, `spec/18-interfaces-world.md` §18.2,
`spec/04-data-schemas.md` (the content fields these types carry)

## Interface to implement

Copied verbatim from the modules that already specify them; this task
relocates them, it does not redesign them.

```
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

// from spec/18-interfaces-world.md §18.2 (Q-012) — events carry these
struct NodeId { uint32 Value }
struct EdgeId { uint32 Value }

// DepartureHeldForPassengers / Released (spec/10-events.md §10.6, D6) —
// fields: FlightId, int32 outstanding, NodeId? heldAt (set on the opening
// event, always null on Released). Whatever event-payload shape this
// task's existing types already follow for the rest of the catalogue
// (union, per-kind struct, or otherwise) is reused unchanged here; this
// task does not introduce a new representation for one event pair.

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

Binding notes:

- **No behaviour, no allocation logic, no hashing rule.** This task defines
  types only. Each consuming module (`sim.airside`, `sim.turnaround`,
  `sim.delay`) still owns *when* and *how* it constructs, hashes and mutates
  values of these types, exactly as already specified in `12`, `13`, `06`
  and `14`. This task does not touch any hashed-state ordering.
- **`DelayEventId` is not `IIdAllocator`-drawn** (`14-interfaces-delay.md`
  §14.7): the type itself is a plain wrapper struct; the counter that
  allocates values of it lives in `sim.delay`'s own state (T-024), not here.
- **`DelayCategory`'s member list is fixed** (`06-delay-attribution.md`
  "extend only via spec amendment") — do not add, remove or reorder members;
  a reorder changes every serialised/hashed value that stores the enum's
  ordinal.
- **`FlightId`, `EventId`, `CohortId`, `EventRef` are out of scope** — they
  are already established `sim.core`/`sim.flow` conventions from earlier
  tasks and are not touched here. `NodeId` and `EdgeId` **are** in scope
  now (added by the Q-012 extension above) — they were previously declared
  inside `sim.flow`'s own task (T-007); T-007 has been updated to reference
  them from here instead. Do not rename or move anything not listed in the
  interface block above.
- **The content definition types are read by, but not owned by, every
  module that reads content** — `sim.flow` (`PaxProfileDefinition`,
  `QueueProfileDefinition`), `sim.airside`/`sim.schedule`
  (`AircraftDefinition`, `SizeCategoryDefinition`). This task does not add
  any read of them; it only makes the types exist so `IContentIndex.TryGet<T>`
  compiles against something real. `IContentSource`/`IContentLoader`
  themselves are **not** built here — that is T-027.
- If a consuming module's spec (`12`, `13`, `06`, `14`, `18`, `08` §8.11) is
  amended later to change one of these types' fields, that amendment lands
  here, in `src/sim/core/**`, not as a local redefinition inside the
  consuming module's directory.

## Events

Emitted: none. Consumed: none. This task defines payload types only; it does
not touch the event bus, dispatch, or any `ISimSystem`.

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
- `test_airside_turnaround_delay_payload_types_compile_in_sim_core_only` —
  static: every type listed above resolves from the `sim.core` assembly,
  with no dependency edge from `sim.core` to `sim.airside`, `sim.turnaround`,
  `sim.delay`, `sim.flow` or `sim.world`
- `test_delay_event_id_struct_shape` — plain `uint64` wrapper, `0` is a
  valid, unallocated value
- `test_content_definition_ids_unique_across_all_kinds` — construction-time
  invariant asserted directly on `ContentIndexFactory.Create`'s input shape
  (this task's types feed it; the factory itself is T-001's)
- `test_content_ids_compare_ordinally_not_by_default_hash` — guards the
  `07-conventions.md` "no hash code reaches behaviour" rule for `ContentId`

**Do not edit them.** If a test asserts a shape that contradicts the
consuming module's own spec (`12`, `13`, `06`, `14`), file an open question
and stop — do not silently reconcile a mismatch by guessing which side is
right.

## Performance budget

None — no per-tick code. This task must add nothing measurable to
`sim.core`'s existing budget; it is types only.

## Done when

- [ ] Interface matches spec exactly (relocated, not redesigned)
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task must merge before T-007, T-012, T-021, T-022, T-024 and T-027 are
released — each compiles against one or more of these types (T-007/T-012:
`NodeId`/`EdgeId`; T-021: `RunwayId`/`StandId`/`TaxiNodeId`/`TaxiEdgeId`/
`DepartureHeldForPassengers` fields; T-022: `JobKind`/`VehicleKind`/
`JobStatus`/`ResourceKind`/`DelayCategory` on `TurnaroundJobBlocked`/
`Unblocked`; T-024: `DelayEventId`/`DelaySource`/`DelayExplanation`/
`DelayCategory`; T-027: the content definition types). See
`tasks/queue.md`'s release-order notes.

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
