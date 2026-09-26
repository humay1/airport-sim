# T-008 — Schedule loader from CSV fixture, 200 movements

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.schedule` |
| Assigned role | worker |
| Depends on | T-001, T-003, T-026 |
| Spec source | `spec/00-overview.md` build order #2; `spec/11-interfaces-schedule.md` (answers Q-004) |
| Blocked by | — |

## Writable paths

```
src/sim/schedule/**
AirportSim.sln
```

**Correction (Q-021), especially for the fixture grant:** `tests/**`
(including `tests/fixtures/**`) is the Test Author's territory exclusively
(`07-conventions.md` "Solution layout and build") — `tests/fixtures/schedule/phase0-200.csv`
is the Test Author's to write, per `11-interfaces-schedule.md` §11.10's own
framing ("binding on the Test Author"), not this task's, and the path guard
already blocks a worker grant there regardless. The earlier grants of
`tests/sim/schedule/**` and `tests/fixtures/schedule/**` are dropped.

**First task of a new module (`07` L8, Q-013):** this task creates
`src/sim/schedule/AirportSim.Sim.Schedule.csproj` and
`tests/sim/schedule/AirportSim.Sim.Schedule.Tests.csproj` (byte for byte
per `07` L2/L3) and adds both to `AirportSim.sln`. Never release this task
concurrently with any other "first task of a new module" (T-007, T-012,
T-020, T-021, T-022, T-024, T-029, T-031) — concurrent `.sln` edits
conflict (`07` L8).

`sim.schedule` depends on `core` and `flow` (`spec/03-module-map.md`, corrected
by the Q-004 answer) but this task must build and pass **without** `sim.flow`
registered — §11.6 "Running without `sim.flow`" is binding: the module's state
hash must be identical with and without the injector wired in. Do not add a
hard dependency on T-007 merging first.

**Dependency correction (systematic type-dependency recheck):**
`FlightRecord.MinTurnaround` is `SimMinutes`, which is `Fx` (`08` §8.2), and
`FlightRecord.EntryNode` is `NodeId` while `FlightRecord.AircraftType`/
`PaxProfile` are `ContentId` — both `sim.core` types T-026 authors. This
task cannot compile without either `T-003` or `T-026`. `Depends on` is
amended to `T-001, T-003, T-026`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/11-interfaces-schedule.md`,
`spec/09-interfaces-flow.md` §9.7 (the `Inject` contract this module calls),
`spec/10-events.md` §10.4, §10.6 ("From sim.schedule")

## Interface to implement

**Correction (Q-018):** `AirlineId`, `MovementKind` and the
`FlightPlanPublished` event struct are now **`sim.core` types**, authored
by T-026 (`10-events.md` §10.9), not declared or sketched locally here —
the earlier inline comment-block sketch of `FlightPlanPublished`'s fields
is stale and withdrawn; reference the real struct from `sim.core` instead.
`kind`, `rotation` and `hasRotation` are still copied verbatim from
`FlightRecord.Kind`/`Rotation`/`HasRotation` when this task publishes the
event — `sim.delay` reads the rotation link from the event, never by
querying `IScheduleSystem` (`spec/14-interfaces-delay.md` §14.12).
`schedArr`/`schedDep` use `TICK_UNSCHEDULED` for the side with no linked
counterpart.

```
readonly struct FlightRecord {
  FlightId     Id
  AirlineId    Airline
  ContentId    AircraftType
  MovementKind Kind
  uint32       DayIndex
  Tick         ScheduledTick
  Tick         PublishTick
  FlightId     Rotation
  bool         HasRotation
  SimMinutes   MinTurnaround
  ContentId    PaxProfile
  int32        PaxCount
  int32        HoldBagPermille
  int32        AssistPermille
  NodeId       EntryNode
}

readonly struct ScheduleTable {
  IReadOnlyList<FlightTemplate> Rows          // ascending flight_ref, ordinal
  uint64                        FixtureHash   // FNV-1a-64 over the raw file bytes
}

interface IScheduleLoader {
  ScheduleTable Load(ReadOnlySpan<byte> csv, string sourceName)
}

interface IScheduleSystem : ISimSystem {
  bool TryGetFlight(FlightId id, out FlightRecord flight)
  IReadOnlyList<FlightId> PublishedFlights()
  IReadOnlyList<FlightId> MovementsBetween(Tick fromInclusive,
                                           Tick toExclusive,
                                           MovementKind kind)
  bool TryGetRotation(FlightId flight, out FlightId counterpart)
  int32 PendingInjectionCount(FlightId flight)
}
```

Binding, copied from `spec/11-interfaces-schedule.md`, not paraphrased:

- **Id derivation** (§11.3): `FlightId.Value = DayIndex * FLIGHT_ID_DAY_STRIDE
  + RowOrdinal + 1`, `RowOrdinal` from ordinal sort of `flight_ref`. Never
  `IIdAllocator`.
- **Fixture format** (§11.4): exact header byte-match, UTF-8/no-BOM/LF, no
  quoting/comments/blank lines, every violation a hard load failure naming
  file and 1-based line number. Header is reproduced verbatim in §11.4.
- **Publication** (§11.5): `PublishTick = ScheduledTick - PLAN_PUBLISH_LEAD_TICKS`
  clamped to 0; emits `FlightPlanPublished` then
  `FlightMilestoneReached{PlanPublished}` (with `Cause` set to the former's
  `EventId`) once per flight, in ascending `FlightId` order among flights
  publishing the same tick. `FlightPlanPublished.kind`/`rotation`/`hasRotation`
  are copied from `FlightRecord.Kind`/`Rotation`/`HasRotation` (added to
  `10-events.md` §10.6 by the Q-007 amendment) — this is how `sim.delay` learns
  the rotation link without a query.
- **Passenger demand** (§11.6): show-up curve is content (`pax_profile`), no
  RNG; largest-remainder splitting across buckets then across the four
  `(HasHoldBaggage, RequiresAssistance)` classes, ties by ascending index;
  injection tick clamped to 0; `Inject` called once per due
  `(FlightId, bucketIndex, classIndex)` in that ascending order, only for
  `Departing` passengers.
- **Registry position 2** (§11.7), before `sim.airside` and `sim.flow`.
- **No mutating entry point.** Nothing may write to `sim.schedule`.
- **No commands consumed at Phase 0** (§11.8).

## Events

Emitted: `FlightPlanPublished`, `FlightMilestoneReached` (Milestone =
`PlanPublished` only)
Consumed: none

## Tests to pass

```
tests/sim/schedule/**
```

Written by the Test Author, against the fixture `tests/fixtures/schedule/phase0-200.csv`
whose binding requirements are §11.10. Expect at least:

- `test_loader_rejects_reordered_header_with_line_number`
- `test_flight_ids_are_independent_of_row_order`
- `test_show_up_split_conserves_head_count`
- `test_publication_emits_plan_then_milestone_once_per_flight`
- `test_schedule_hash_identical_with_and_without_flow_registered`
- `test_schedule_tick_consumes_no_rng`

**Do not edit them.** If a test contradicts `11-interfaces-schedule.md`, file
an open question and stop.

## Performance budget

`0.10` ms/tick at max tier (`spec/03-module-map.md`, `spec/11-interfaces-schedule.md`
§11.9). Per-tick work is O(flights published this tick + injections due this
tick) only — due-ordered queues built at load/day-materialisation, never a
scan of the whole flight table inside `Tick`. No allocation in the update
path; day materialisation for `repeat_daily` rows may allocate off the
injection path, at the day boundary.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] **Green per Q-016 (HUMAN DECISION, owner, 2026-09-24): until T-006
      merges, green = `ci/run-checks.sh`'s `path-guard` and `build-and-test`
      (`--fast`) jobs. The full script becomes mandatory once T-006 merges.**
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`sim.schedule` consumes **no RNG** at Phase 0 (§11.9) — do not add a stream
speculatively for "future" demand variation; that is a later spec amendment.
Cross-midnight rotations (`STA < STD` required same-day) are explicitly
unsupported; a fixture needing one is a fixture bug, not a feature to add
here.
