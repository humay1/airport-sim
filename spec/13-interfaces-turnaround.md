# 13 — Public interfaces: `sim.turnaround`

Implements the `sim.turnaround` row of `03-module-map.md`: ground handling jobs,
vehicles, job scheduling. Answers `open-questions.md` Q-006. Notation and
binding rules are as in `08-interfaces-core.md`; where this file appears to
contradict `01-architecture.md` or `02-determinism.md`, those win and it is a
spec bug.

Reading order for a `sim.turnaround` worker: `01`, `02`, `07`, `08`,
`12-interfaces-airside.md`, this file, then `09` §9.7 and `10` §10.4/§10.6.
`12-interfaces-airside.md` §12.8 is the contract this module must honour; it
is not repeated here in full.

---

## 13.1 What the module is, and what it is not

`sim.turnaround` runs the ground-service work that happens while an aircraft
is on stand: deboarding, cabin cleaning, catering, fuel, baggage, and the
boarding process itself, dispatched against a small fixed vehicle fleet.

`sim.turnaround` owns:

- the job list per flight (§13.4),
- the vehicle fleet and dispatch (§13.5),
- `DeboardComplete`, `ReadyToBoard`, `BoardingComplete`
  (`12-interfaces-airside.md` §12.3 — the only three of the nine
  arrival/departure/stand milestones it owns).

`sim.turnaround` explicitly does **not** own:

- the aircraft's physical state, doors, or the stand itself — `sim.airside`;
  it learns everything about the flight's position from
  `FlightMilestoneReached{OnStand}`, never by querying `sim.airside`'s track
  state directly, even though `03-module-map.md`'s dependency column would
  allow the call — the milestone is sufficient and event-only keeps this
  module honest about when it actually learned something,
- passengers of any kind — `sim.flow`; `sim.turnaround` has no dependency on
  `sim.flow` and makes no `Inject`/`Absorb` calls,
- roster, shifts, fatigue or driver identity as people — `sim.staff` (§13.2),
- delay arithmetic — it emits job lifecycle and blocking events;
  `sim.delay` does the attribution.

### On `sim.staff`

`03-module-map.md` lists `sim.staff` as a dependency of `sim.turnaround`, but
`sim.staff` has no published interface (a separate, unopened question).
Following the same narrowing `12-interfaces-airside.md` §12.1 applied to
`sim.world`: **at Phase 0/1, a vehicle is its own crew.** "Driver assignment"
(T-022's own title) means assigning an available *vehicle* to a job; there is
no separate driver entity, roster or shift to model yet. `sim.turnaround`
makes no calls into `sim.staff`. Splitting vehicle from driver — a fuel truck
sitting idle for lack of a qualified operator even though the truck itself is
free — is exactly the kind of thing `sim.staff`'s eventual interface should
add, as an amendment, not a local invention here.

---

## 13.2 Constants

| Constant | Value | Meaning |
|---|---|---|
| `JOB_KIND_BITS` | 8 | §13.3, `JobId` derivation |

No other module-specific constants; every duration and fleet size is
construction data (§13.4), never hardcoded.

---

## 13.3 Types

```
struct VehicleId { uint16 Value }
struct JobId     { uint64 Value }        // derived, never allocated

enum JobKind {
  Deboard, BaggageUnload,                          // arrival-triggered, §13.6
  CabinClean, Catering, Fuel, BaggageLoad,
  PushbackPrep, Boarding                            // departure-triggered, §13.6
}

enum VehicleKind { CleaningCrew, CateringTruck, FuelTruck, BaggageTractor, PushbackTug }

enum JobStatus { Blocked, Active, Completed }

enum ResourceKind { Vehicle, JobDependency, Crew }   // Crew is Phase 2, unused here
```

### `JobId` derivation

```
JobId.Value = (Flight.Value << JOB_KIND_BITS) | (uint64)(int)Kind
```

At most one job of a given `JobKind` ever exists for a given `FlightId`
(§13.6), so this is injective and needs no allocator — the same reasoning
`11-interfaces-schedule.md` §11.3 gives for deriving `FlightId`.

```
readonly struct TurnaroundJob {
  JobId      Id
  FlightId   Flight
  JobKind    Kind
  JobStatus  Status
  VehicleId? Vehicle
  Tick       CreatedAt
  Tick       StartedAt        // TICK_UNSCHEDULED while Blocked
  Tick       DueAt            // TICK_UNSCHEDULED unless Active; StartedAt + NominalDurationTicks
}

readonly struct VehicleState { VehicleId Id; VehicleKind Kind; JobId? Assignment }
```

---

## 13.4 The job catalogue and the vehicle fleet

Construction-time, immutable for the session, self-owned — the same posture
`12-interfaces-airside.md` §12.4 takes toward its layout: this is not `data/`
content at Phase 0/1, it lives beside this module's tests.

```
readonly struct JobDef {
  JobKind       Kind
  VehicleKind?  RequiresVehicle       // null: Deboard, Boarding — no contended resource at Phase 0/1
  uint32        NominalDurationTicks
  DelayCategory Category              // ground_handling, fuel, catering, cleaning, loading — 10-events.md §10.6
}

readonly struct TurnaroundCatalogue { IReadOnlyList<JobDef> Jobs }   // exactly one entry per JobKind

readonly struct VehicleDef { VehicleId Id; VehicleKind Kind }
readonly struct TurnaroundFleet { IReadOnlyList<VehicleDef> Vehicles }
```

Load-time validation, hard failures naming the offending `JobKind`/`VehicleId`
(`07-conventions.md`):

- `TurnaroundCatalogue.Jobs` must contain exactly one entry per `JobKind`
  value, no duplicates, no omissions;
- `Pushback` never appears as a `Category` here despite being a delay
  category name in `10-events.md` §10.6 — `PushbackPrep`'s category is always
  `ground_handling`; `pushback` as a category is reserved for a delay whose
  root cause is the aircraft-side act itself (`sim.airside`'s `Pushback`
  milestone), not this module's prep job. A catalogue that maps
  `PushbackPrep` to `pushback` fails to load.
- `VehicleDef.Id` unique within the fleet.

`TurnaroundFleet` is not required to include every `VehicleKind` — a scenario
with no catering demand can ship zero `CateringTruck`s, and any `Catering` job
it creates simply stays `Blocked` forever, which is a legitimate (if grim)
outcome, not a load failure.

---

## 13.5 Vehicle dispatch

One deterministic rule, applied uniformly to every `VehicleKind`, every tick,
in this order:

1. **Completions first.** Any `Active` job with `DueAt == CurrentTick`
   transitions to `Completed`, emits `TurnaroundJobCompleted`, and frees its
   `Vehicle` (if any) — the freed vehicle is eligible for assignment later in
   this same tick's pass, not next tick. Processed in ascending `JobId` order.
   If this completion is the fifth and last of `Boarding`'s five prerequisite
   jobs (§13.6) for its flight, `Boarding` unblocks in this same step:
   `TurnaroundJobUnblocked`, then `TurnaroundJobStarted`, then
   `FlightMilestoneReached{ReadyToBoard}`, all with `Cause` set to this
   completion event.
2. **New jobs.** Jobs created this tick (§13.6): those with `RequiresVehicle
   = null` other than `Boarding` (i.e. `Deboard`) go straight to `Active`.
   Those needing a vehicle attempt assignment immediately, same as a
   re-evaluation (step 3) restricted to this tick's new arrivals. `Boarding`
   is always created `Blocked` on `JobDependency` (§13.6) regardless of this
   step.
3. **Vehicle assignment.** For each `VehicleKind` with at least one free
   vehicle, ascending `VehicleKind` ordinal: among all `Blocked` jobs requiring that
   kind, across every flight currently in turnaround, assign the free
   vehicles to the jobs with the **lowest `EventId`** of their `Blocked`
   request (first blocked, first served — the same rule
   `12-interfaces-airside.md` §12.5 and `06-delay-attribution.md` §10.5 rule 3
   both use), ties impossible (`EventId` is already a total order). Ties in
   *which* free vehicle to hand out are broken by ascending `VehicleId`. Each
   assignment: `Status -> Active`, `StartedAt = CurrentTick`, `DueAt =
   StartedAt + NominalDurationTicks`, emit `TurnaroundJobUnblocked` then
   `TurnaroundJobStarted`, `Cause` of both set to the vehicle-freeing
   `TurnaroundJobCompleted` (or, if the vehicle was free at creation, no
   `Blocked`/`Unblocked` pair is emitted at all — see §13.6).

`sim.turnaround` ignores physical distance between stands entirely — dispatch
is FIFO by blocking order, never by which stand is closest.

> **LOW CONFIDENCE — distance-blind dispatch.** `06-delay-attribution.md`'s
> own worked example ("3 trucks serving 5 concurrent turnarounds, stand H7
> furthest") reads as if distance matters. This spec deliberately does not
> model it: doing so needs a distance metric, which needs `sim.world` or
> `sim.airside`'s taxi graph, and `sim.turnaround` depends on neither for
> movement. If playtest shows FIFO dispatch looks wrong (a truck crossing the
> field past a closer aircraft), the fix is a distance-weighted dispatch rule
> declared here by amendment — not a local heuristic. Flagged for the human
> owner.

---

## 13.6 Job creation and milestone emission

`sim.turnaround` subscribes to `FlightMilestoneReached{Milestone=OnStand}`
(from `sim.airside`, `12-interfaces-airside.md` §12.3). On the tick it
receives one, for that `FlightId`:

- If the milestone's `FlightId` is an **Arrival** (`FlightRecord.Kind`, via
  `IScheduleSystem.TryGetFlight`): create jobs `Deboard` and `BaggageUnload`,
  in that ascending `JobKind` order, each attempting immediate vehicle
  assignment per §13.5. The tick `Deboard` completes,
  `FlightMilestoneReached{Milestone=DeboardComplete, Flight=this arrival}`
  fires, with `PlannedTick` set per the footnote below and `Cause` set to the
  `Deboard` job's `TurnaroundJobCompleted`. `BaggageUnload` completing
  triggers no milestone; it is tracked purely for its own blocking/delay
  accounting.
- If the milestone's `FlightId` is a **Departure**: create jobs `CabinClean`,
  `Catering`, `Fuel`, `BaggageLoad`, `PushbackPrep`, in that ascending
  `JobKind` order, each attempting immediate vehicle assignment per §13.5.
  `Boarding` is created in the same batch, has `RequiresVehicle = null`, and
  is never blocked *on a vehicle* — but it has the one fixed job-dependency
  relationship in this catalogue: it is created `Blocked` with
  `ResourceKind = JobDependency` (`EntityId` unset) regardless of vehicle
  availability elsewhere, and stays `Blocked` until `CabinClean`, `Catering`,
  `Fuel`, `BaggageLoad` and `PushbackPrep` are all `Completed`. This is fixed
  by this spec, not content-declared, because `JobKind` itself is a fixed
  enum, not a content extension point (consistent with `NodeKind` in
  `09-interfaces-flow.md`). The tick the fifth of those five completes:
  `TurnaroundJobUnblocked` fires for `Boarding`, then `TurnaroundJobStarted`
  (`StartedAt = CurrentTick`, `DueAt` per §13.5), and
  `FlightMilestoneReached{Milestone=ReadyToBoard, Flight=this departure}`
  fires in the same tick, `Cause` set to that fifth job's
  `TurnaroundJobCompleted`. `BoardingComplete` fires the tick `Boarding`
  completes, `Cause` set to `Boarding`'s own `TurnaroundJobCompleted`.

`PlannedTick` for `DeboardComplete` is
`ArrivalRecord.ScheduledTick + CatalogueEntry(Deboard).NominalDurationTicks` —
the nominal deboard time, anchored to the arrival's own scheduled touchdown,
not a guess; `sim.delay`'s gap arithmetic (`06-delay-attribution.md` §10.5)
needs a plan, and this is the only one available without inventing a separate
"planned duration" content field beyond what §13.4 already declares.

`PlannedTick` for the departure's milestones follows the same
schedule-anchored rule (`10-events.md` §10.4): `ReadyToBoard` is the
departure's planned `OnStand` (`12-interfaces-airside.md` §12.3, `STD −
MinTurnaround`) plus the largest `NominalDurationTicks` among `CabinClean`,
`Catering`, `Fuel`, `BaggageLoad` and `PushbackPrep` (they run in parallel when
unimpeded); `BoardingComplete` is planned `ReadyToBoard` plus `Boarding`'s
`NominalDurationTicks`. `sim.delay` measures none of this module's three
milestones (`DeboardComplete`, `ReadyToBoard`, `BoardingComplete`;
`14-interfaces-delay.md` §14.4); their `PlannedTick`s are binding for the UI
and for later checkpoints.

No job for a flight is created more than once — `OnStand` fires exactly once
per `FlightId` (`10-events.md` §10.3 rule 1), so job creation is naturally
idempotent per flight.

---

## 13.7 Module interface

```
interface ITurnaroundSystem : ISimSystem {
  bool  TryGetJob(JobId id, out TurnaroundJob job)
  IReadOnlyList<JobId> JobsForFlight(FlightId flight)      // ascending JobKind ordinal
  bool  TryGetVehicle(VehicleId id, out VehicleState vehicle)
  IReadOnlyList<VehicleId> FreeVehicles(VehicleKind kind)  // ascending VehicleId
}
```

No mutating entry point. Everything is tick-driven, per §13.5–§13.6.

Registry position is **5** (`08-interfaces-core.md` §8.5), after `sim.flow`
(4), before `sim.baggage` (6) — consistent with the existing table, unchanged
by this file.

---

## 13.8 Commands consumed

None at Phase 0/1. A future "prioritise this vehicle" or "call in overtime"
command is additive and needs a spec amendment, not a local invention.

---

## 13.9 Events emitted and consumed

Full field lists in `10-events.md` §10.6.

**Emitted:**

| Event | When |
|---|---|
| `TurnaroundJobStarted` / `Completed` | §13.5 step 3 (start), §13.5 step 1 (complete) |
| `TurnaroundJobBlocked` / `Unblocked` | Created needing an unavailable vehicle / assigned one thereafter, §13.5. `category` is the job's `JobDef.Category` (§13.4), copied onto the event because `sim.delay` cannot read this catalogue (`10-events.md` §10.6) |
| `FlightMilestoneReached` | `DeboardComplete`, `ReadyToBoard`, `BoardingComplete` only, §13.6 |

`CrewUnavailable`/`CrewReady` are **not emitted** — they are Phase 2, gated on
`sim.staff` existing (`10-events.md` §10.6), and this module makes no crew
distinction yet (§13.1).

**Consumed:**

| Event | From | Reaction |
|---|---|---|
| `FlightMilestoneReached { Milestone = OnStand }` | `sim.airside` | §13.6, create this flight's jobs |

`sim.turnaround` calls `IScheduleSystem.TryGetFlight` (query, downward,
`11-interfaces-schedule.md` §11.7) once per flight, at job creation, to read
`Kind` (`Arrival`/`Departure`) and `ScheduledTick`.

---

## 13.10 State, hashing, RNG and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9):

1. Vehicles, ascending `VehicleId`: `Kind`, `Assignment`.
2. Jobs, ascending `JobId`: every field of `TurnaroundJob`.

Not hashed, because derived or load-time immutable: `TurnaroundCatalogue`,
`TurnaroundFleet`, `FreeVehicles()`, `JobsForFlight()`.

**RNG: none at Phase 0/1.** Dispatch is FIFO by `EventId`; job creation and
duration are catalogue lookups. Same posture as `sim.schedule`
(`11-interfaces-schedule.md` §11.9) and `sim.airside`
(`12-interfaces-airside.md` §12.12).

Budget: **0.50 ms/tick at max tier** (`03-module-map.md`). Per-tick work is
O(active jobs + blocked jobs + vehicles), never a scan proportional to
flights not currently in turnaround. No allocation in the update path
(`07-conventions.md`).

---

## 13.10a Construction (Q-009)

```
readonly struct TurnaroundSetup { TurnaroundCatalogue Catalogue; TurnaroundFleet Fleet }

interface ITurnaroundSetupLoader {
  TurnaroundSetup Load(ReadOnlySpan<byte> file, string sourceName)   // parse and apply §13.4's validation
}

TurnaroundFactory.CreateSetupLoader() -> ITurnaroundSetupLoader
TurnaroundFactory.CreateSystem(in SystemServices services, in TurnaroundSetup setup,
                               IScheduleSystem schedule) -> ITurnaroundSystem
```

The file format stays the worker's choice (§13.11). `schedule` is required
(§13.9).

## 13.11 The Phase 0/1 fixture (T-022)

`tests/fixtures/turnaround/phase1-four-vehicles.*` (format is the worker's
choice, same posture as `12-interfaces-airside.md` §12.13), binding on the
Test Author:

- exactly four `VehicleDef`s in total, composition the Test Author's choice,
  **except** at least one `VehicleKind` used by the companion fixtures must
  have fewer vehicles than the peak concurrent demand for it, so
  `TurnaroundJobBlocked` fires at least once in a single sim-day run — the
  same requirement in spirit as `12-interfaces-airside.md` §12.13's runway
  capacity;
- `TurnaroundCatalogue` covering all eight `JobKind`s, durations short enough
  relative to `MinTurnaround` in the schedule fixture that a full rotation
  can plausibly complete within its scheduled ground time when no vehicle
  contention occurs — so the "everything works" path is covered as well as
  the blocked path;
- runs against `tests/fixtures/schedule/phase0-200.csv`
  (`11-interfaces-schedule.md` §11.10) and
  `tests/fixtures/airside/phase1-single-runway.*`
  (`12-interfaces-airside.md` §12.13), with `sim.airside` **registered** this
  time — T-022 is exactly the task that exercises the real handshake in
  `12-interfaces-airside.md` §12.8 instead of its no-`sim.turnaround`
  fallback.

Done-condition tests this spec expects to exist, phrased per
`07-conventions.md`:

- `test_catalogue_rejects_missing_job_kind`
- `test_vehicle_dispatch_assigns_lowest_event_id_first`
- `test_boarding_waits_for_all_other_departure_jobs`
- `test_deboard_complete_fires_once_per_arrival`
- `test_vehicle_freed_on_completion_is_reassigned_same_tick`
- `test_turnaround_tick_consumes_no_rng`
- `test_airside_doors_close_after_boarding_complete_with_turnaround_registered`
  (integration-shaped: this is the counterpart to
  `12-interfaces-airside.md` §12.13's fallback-only test, and is what
  actually proves the §12.8 handshake works end to end)
