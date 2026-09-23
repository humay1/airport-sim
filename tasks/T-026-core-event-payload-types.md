# T-026 — `sim.core`: Phase 1 event payload types (airside/turnaround/delay)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/03-module-map.md` ("Events are immutable value types, defined in `sim.core`"); `spec/10-events.md` §10.6 (the catalogue); `spec/12-interfaces-airside.md` §12.9; `spec/13-interfaces-turnaround.md` §13.3–§13.4; `spec/14-interfaces-delay.md` §14.3; `spec/06-delay-attribution.md` (`DelayCategory`) |
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
payload structs that `12-interfaces-airside.md`, `13-interfaces-turnaround.md`
and `06`/`14-interfaces-delay.md` already fully specify (signature-level, not
invented here), placed in `src/sim/core/**` where the event catalogue itself
lives. The interface files that describe them for readability
(`12` §12.9, `13` §13.3–§13.4, `14` §14.3) are unchanged and remain the
binding source of each type's exact shape; this task changes only *where the
compiled type lives*, never its fields, names or values.

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
`spec/08-interfaces-core.md`, `spec/10-events.md`,
`spec/12-interfaces-airside.md` §12.9, `spec/13-interfaces-turnaround.md`
§13.3–§13.4, `spec/14-interfaces-delay.md` §14.3,
`spec/06-delay-attribution.md`

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
  StandUnavailable, TurnaroundJobWait, Unexplained
}

readonly struct DelayExplanation {
  DelaySource Source
  uint64      A
  uint64      B
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
- **`FlightId`, `EventId`, `NodeId`, `CohortId`, `ContentId`, `EventRef` are
  out of scope** — they are already established `sim.core`/`sim.flow`
  conventions from earlier tasks and are not touched here. Do not
  rename or move anything not listed in the interface block above.
- If a consuming module's spec (`12`, `13`, `06`, `14`) is amended later to
  change one of these types' fields, that amendment lands here, in
  `src/sim/core/**`, not as a local redefinition inside the consuming
  module's directory.

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
- `test_airside_turnaround_delay_payload_types_compile_in_sim_core_only` —
  static: every type listed above resolves from the `sim.core` assembly,
  with no dependency edge from `sim.core` to `sim.airside`, `sim.turnaround`
  or `sim.delay`
- `test_delay_event_id_struct_shape` — plain `uint64` wrapper, `0` is a
  valid, unallocated value

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

This task must merge before T-021, T-022 and T-024 are released — each
compiles against one or more of these types (T-021: `RunwayId`/`StandId`/
`TaxiNodeId`/`TaxiEdgeId`; T-022: `JobKind`/`VehicleKind`/`JobStatus`/
`ResourceKind`/`DelayCategory` on `TurnaroundJobBlocked`/`Unblocked`; T-024:
`DelayEventId`/`DelaySource`/`DelayExplanation`/`DelayCategory`). See
`tasks/queue.md`'s Phase 1 release-order notes.

If, once this task is underway, the exact same type name is found already
declared somewhere unexpected (for instance if a worker on an earlier task
guessed at one of these types locally before this task existed), that is a
merge conflict for the Integrator to resolve by deleting the local guess in
favour of this task's `sim.core` definition — not a reason to redefine the
type twice.
