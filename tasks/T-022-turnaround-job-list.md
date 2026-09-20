# T-022 — Turnaround as job list, 4 vehicles, driver assignment

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.turnaround` |
| Assigned role | worker |
| Depends on | T-021 |
| Spec source | `spec/00-overview.md` build order #5; `spec/13-interfaces-turnaround.md` (answers Q-006) |
| Blocked by | — |

## Writable paths

```
src/sim/turnaround/**, tests/sim/turnaround/**, tests/fixtures/turnaround/**
```

`sim.turnaround` depends on `core`, `airside` and `staff`
(`spec/03-module-map.md`), but this task must build and pass **without**
calling into `sim.staff` at all — `spec/13-interfaces-turnaround.md` §13.1
("On `sim.staff`") is binding: at Phase 0/1 a vehicle is its own crew, there
is no separate driver entity, and "driver assignment" (this task's own title)
means assigning a vehicle. It must also make **no queries into
`sim.airside`'s state** — it learns a flight is on stand exclusively from
`FlightMilestoneReached{OnStand}`, never by calling `IAirsideSystem` directly,
even though the dependency column would permit it (§13.1).

This task depends on T-021 **merged**, unlike T-021's own relationship to
T-022: this is the task that exercises the real `sim.airside`↔`sim.turnaround`
handshake (`spec/12-interfaces-airside.md` §12.8) instead of T-021's
no-`sim.turnaround` fallback, so `sim.airside` must already be in the build.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/13-interfaces-turnaround.md`,
`spec/12-interfaces-airside.md` §12.3, §12.8 (the handshake this module must
fulfil), `spec/11-interfaces-schedule.md` §11.7 (`IScheduleSystem` queries
this module calls), `spec/10-events.md` §10.4, §10.6 ("From sim.turnaround")

## Interface to implement

```
struct VehicleId { uint16 Value }
struct JobId     { uint64 Value }

enum JobKind {
  Deboard, BaggageUnload,
  CabinClean, Catering, Fuel, BaggageLoad,
  PushbackPrep, Boarding
}

enum VehicleKind { CleaningCrew, CateringTruck, FuelTruck, BaggageTractor, PushbackTug }
enum JobStatus { Blocked, Active, Completed }
enum ResourceKind { Vehicle, JobDependency, Crew }

readonly struct TurnaroundJob {
  JobId      Id
  FlightId   Flight
  JobKind    Kind
  JobStatus  Status
  VehicleId? Vehicle
  Tick       CreatedAt
  Tick       StartedAt
  Tick       DueAt
}

readonly struct VehicleState { VehicleId Id; VehicleKind Kind; JobId? Assignment }

readonly struct JobDef {
  JobKind       Kind
  VehicleKind?  RequiresVehicle
  uint32        NominalDurationTicks
  DelayCategory Category
}

readonly struct TurnaroundCatalogue { IReadOnlyList<JobDef> Jobs }
readonly struct VehicleDef { VehicleId Id; VehicleKind Kind }
readonly struct TurnaroundFleet { IReadOnlyList<VehicleDef> Vehicles }

interface ITurnaroundSystem : ISimSystem {
  bool  TryGetJob(JobId id, out TurnaroundJob job)
  IReadOnlyList<JobId> JobsForFlight(FlightId flight)
  bool  TryGetVehicle(VehicleId id, out VehicleState vehicle)
  IReadOnlyList<VehicleId> FreeVehicles(VehicleKind kind)
}
```

Binding, copied from `spec/13-interfaces-turnaround.md`, not paraphrased:

- **`JobId` derivation** (§13.3): `JobId.Value = (Flight.Value <<
  JOB_KIND_BITS) | (uint64)(int)Kind`, `JOB_KIND_BITS = 8`. Never an
  allocator — at most one job of a given `JobKind` exists per `FlightId`.
- **Catalogue and fleet** (§13.4): self-owned construction data, not `data/`
  content at Phase 0/1. Catalogue must cover exactly the eight `JobKind`
  values, no more, no fewer. `PushbackPrep`'s `Category` must be
  `ground_handling` — mapping it to `pushback` is a load failure (`pushback`
  is reserved for `sim.airside`'s own `Pushback` milestone).
- **Vehicle dispatch** (§13.5): three ordered steps every tick —
  completions first (freeing vehicles, and unblocking `Boarding` if this was
  the fifth of its five prerequisite jobs), then new jobs created this tick,
  then vehicle assignment. Assignment is FIFO by the **lowest `EventId`** of
  a job's `Blocked` request across every flight currently in turnaround —
  never by physical distance between stands. Ties on which free vehicle to
  hand out: ascending `VehicleId`.
- **Job creation** (§13.6): triggered by `FlightMilestoneReached{OnStand}`
  from `sim.airside`. Arrival → create `Deboard`, `BaggageUnload` (ascending
  `JobKind` order); `DeboardComplete` fires when `Deboard` completes,
  `PlannedTick = ArrivalRecord.ScheduledTick +
  CatalogueEntry(Deboard).NominalDurationTicks`. Departure → create
  `CabinClean`, `Catering`, `Fuel`, `BaggageLoad`, `PushbackPrep` (ascending
  order, each attempting immediate vehicle assignment), plus `Boarding`
  (always created `Blocked` on `ResourceKind.JobDependency`, `RequiresVehicle
  = null`). `Boarding` unblocks and starts the tick all five of the other
  departure jobs are `Completed`; `ReadyToBoard` fires the same tick, `Cause`
  set to the fifth completion. `BoardingComplete` fires when `Boarding`
  completes.
- **No mutating entry point.** No commands at Phase 0/1 (§13.8).
- **No RNG at Phase 0/1** (§13.10). Do not add a stream speculatively.

## Events

Emitted: `TurnaroundJobStarted`/`Completed`, `TurnaroundJobBlocked`/`Unblocked`,
`FlightMilestoneReached` (`DeboardComplete`, `ReadyToBoard`,
`BoardingComplete` only — **not** `DoorsOpen`/`DoorsClosed`/`Pushback`, which
belong to `sim.airside`)

Consumed: `FlightMilestoneReached{Milestone=OnStand}` (from `sim.airside`)

## Tests to pass

```
tests/sim/turnaround/**
```

Written by the Test Author, against a companion fixture at
`tests/fixtures/turnaround/**` whose binding requirements are
`spec/13-interfaces-turnaround.md` §13.11, run against
`tests/fixtures/schedule/phase0-200.csv`
(`spec/11-interfaces-schedule.md` §11.10) and
`tests/fixtures/airside/phase1-single-runway.*`
(`spec/12-interfaces-airside.md` §12.13), with `sim.airside` **registered**.
Expect at least:

- `test_catalogue_rejects_missing_job_kind`
- `test_vehicle_dispatch_assigns_lowest_event_id_first`
- `test_boarding_waits_for_all_other_departure_jobs`
- `test_deboard_complete_fires_once_per_arrival`
- `test_vehicle_freed_on_completion_is_reassigned_same_tick`
- `test_turnaround_tick_consumes_no_rng`
- `test_airside_doors_close_after_boarding_complete_with_turnaround_registered`
  (the counterpart to T-021's fallback-only test; proves the §12.8 handshake
  works end to end with both modules registered)

**Do not edit them.** If a test contradicts `spec/13-interfaces-turnaround.md`,
file an open question and stop.

## Performance budget

`0.50` ms/tick at max tier (`spec/03-module-map.md`,
`spec/13-interfaces-turnaround.md` §13.10). Per-tick work is O(active jobs +
blocked jobs + vehicles), never a scan proportional to flights not currently
in turnaround. No allocation in the update path.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

The fixed one-way dependency `Boarding` has on the other five departure jobs
is part of the spec, not something to make content-configurable — `JobKind`
is a closed enum, not a content extension point (§13.6 is explicit about
this, by analogy with `NodeKind` in `sim.flow`). Do not add a generic
prerequisite-graph mechanism; only `Boarding` ever waits on other jobs.

Vehicle dispatch is deliberately blind to physical distance between stands —
`spec/13-interfaces-turnaround.md` §13.5 flags this `LOW CONFIDENCE` against
`06-delay-attribution.md`'s own worked example, which reads as if distance
matters. Build the FIFO-by-`EventId` rule as specified; if it looks wrong in
playtest, that is a human/Architect call, not something to pre-empt here.

`spec/12-interfaces-airside.md` §12.8 names the exact two subscriptions this
module's counterpart (`sim.airside`) depends on this module producing:
`DeboardComplete` (arrival `FlightId`) triggers the stand handoff to the
departure `FlightId`; `BoardingComplete` (departure `FlightId`) triggers
`DoorsClosed`. Get the `FlightId` on each event right — mixing up the
arrival's and departure's ids on either emission will silently desync
`sim.airside`'s stand-handoff logic without an obvious test failure at this
module's own boundary.
