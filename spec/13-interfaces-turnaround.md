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
| `TURNAROUND_RETENTION_DAYS` | 2 | §13.10 "Retention": a finished flight's jobs are kept on the current and the previous sim-day (Q-092) |
| `TURNAROUND_FLIGHTS_CAPACITY` | 4096 flights | §13.10 "Retention": flights with jobs in state, a hard bound (Q-092) |

No other module-specific constants; every duration and fleet size is
construction data (§13.4), never hardcoded. The two bounds above are
engineering sizing, not balance.

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
  VehicleKind?  RequiresVehicle       // fixed by Kind, table below (Q-088)
  uint32        NominalDurationTicks
  DelayCategory Category              // ground_handling, fuel, catering, cleaning, loading — 10-events.md §10.6
}

readonly struct TurnaroundCatalogue { IReadOnlyList<JobDef> Jobs }   // exactly one entry per JobKind

readonly struct VehicleDef { VehicleId Id; VehicleKind Kind }
readonly struct TurnaroundFleet { IReadOnlyList<VehicleDef> Vehicles }
```

### Which vehicle serves which job (Q-088)

Binding and fixed by this spec, not content-declared, for the same reason
as `Boarding`'s dependency (§13.6): `JobKind` and `VehicleKind` are fixed
enums, and the vehicle a job needs is part of what the job *is*.

| `JobKind` | `RequiresVehicle` |
|---|---|
| `Deboard` | null |
| `BaggageUnload` | `BaggageTractor` |
| `CabinClean` | `CleaningCrew` |
| `Catering` | `CateringTruck` |
| `Fuel` | `FuelTruck` |
| `BaggageLoad` | `BaggageTractor` |
| `PushbackPrep` | `PushbackTug` |
| `Boarding` | null |

`BaggageTractor` is the only `VehicleKind` that serves more than one
`JobKind`. The file does not carry `RequiresVehicle` (§13.10a "File
format"): `Load` fills it in from this table.

### Validation

These are hard failures, each naming the offending `JobKind` or
`VehicleId` (`07-conventions.md`). They are checked in this order, and the
first failure is thrown (Q-086):

1. `TurnaroundCatalogue.Jobs` has exactly one entry per `JobKind` value,
   with no duplicates and no omissions. Duplicates are checked first and
   name the duplicated `JobKind` with the lowest ordinal. Otherwise, an
   omission names the missing `JobKind` with the lowest ordinal.
2. `RequiresVehicle` matches the table above for every entry. This can
   only fail for a setup built in code (§13.10a), since the file has no
   such field.
3. `NominalDurationTicks ≥ 1` for every entry (Q-080). A zero duration would
   give a job started at tick `t` a `DueAt` of `t`, which §13.5 step 1 has
   already passed, so the job would never complete. It would also put a
   completion in the tick of its start, which §13.9's `Cause` table does
   not cover.
4. Every `Category` is one of `ground_handling`, `fuel`, `catering`,
   `cleaning` and `loading` (`10-events.md` §10.6). `PushbackPrep`'s is
   always `ground_handling`. `pushback` is a delay category in `10`
   §10.6, but it is reserved for a delay whose root cause is the
   aircraft-side act itself (`sim.airside`'s `Pushback` milestone), not
   this module's prep job, so a catalogue that maps `PushbackPrep` to
   `pushback` fails to load.
5. Every `VehicleDef.Id` is `≥ 1` and unique within the fleet. An id of
   0 is reported before any duplicate. A duplicate names the lowest
   duplicated id.

Within checks 2 to 4, the failing `JobKind` with the lowest ordinal is
named.

`TurnaroundFleet` is not required to include every `VehicleKind` — a scenario
with no catering demand can ship zero `CateringTruck`s, and any `Catering` job
it creates simply stays `Blocked` forever, which is a legitimate (if grim)
outcome, not a load failure.

---

## 13.5 Vehicle dispatch

One deterministic rule, applied uniformly to every `VehicleKind`, every tick,
in this order:

**Where each step runs (Q-093).** The numbers name the steps; they are not
one sequence inside `Tick`. Steps 1 and 3 run in `Tick` (phase 2, `08`
§8.5), step 1 then step 3, after §13.10's pruning on a day's first tick.
Step 2 runs in the `OnStand` handler (phase 3), which comes after `Tick` in
the same tick, because `sim.airside` publishes `OnStand` in its own phase-2
`Tick` (§13.9). So in one tick: completions, then assignment of freed
vehicles to waiting jobs, then creation, with immediate assignment, for
each `OnStand` dispatched that tick. A job created in a tick's step 2 is
first considered by step 3 on the next tick. By then it is `Blocked`,
because creation takes any free vehicle of its kind.

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
   Those needing a vehicle attempt assignment immediately. `Boarding`
   is always created `Blocked` on `JobDependency` (§13.6) regardless of this
   step. **Order (Q-090).** Within one `OnStand` handler call, the flight's
   jobs are created one at a time in ascending `JobKind` ordinal, and each
   is fully resolved before the next is created: its events are published
   (`TurnaroundJobStarted` if it goes `Active`, `TurnaroundJobBlocked` if
   it does not), then the next job is created. A job needing kind `K`
   takes the free vehicle of kind `K` with the lowest `VehicleId`, if there
   is one, and is otherwise `Blocked`. So when one free `BaggageTractor`
   meets a departure, `BaggageLoad` takes it and nothing else competes for
   it, since `BaggageUnload` belongs to arrivals. Step 3's ascending
   `VehicleKind` order does **not** apply at creation. Two flights'
   `OnStand`s in one tick are handled in their dispatch order, so the
   earlier `OnStand` creates, and takes vehicles, first.
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
   `Blocked`/`Unblocked` pair is emitted at all — see §13.6). The
   vehicle-freeing completion is the step 1 `TurnaroundJobCompleted`, in
   this tick, of the job that held **the very vehicle assigned** (Q-091).
   It is not the first or last completion of the tick, nor any completion
   of the same `VehicleKind`. A free vehicle here was always freed in this
   tick's step 1: a vehicle free at the end of a tick has no `Blocked` job
   of its kind waiting, because step 3 would have assigned it, and
   creation (step 2) takes a free vehicle before it blocks.

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
and for later checkpoints. The departure's planned `OnStand` is read from
the `PlannedTick` of the `OnStand` event the handler received, which is
`max(0, STD − MinTurnaround)` in ticks (`12` §12.3). `sim.turnaround` does
not recompute it.

### Job event payloads (Q-089)

`TurnaroundJobStarted.PlannedStart` and `TurnaroundJobCompleted.PlannedStart`
are the job's planned start, by the same schedule-anchored rule. They do not
depend on when the job actually started:

| `JobKind` | `PlannedStart` |
|---|---|
| `Deboard`, `BaggageUnload` | the arrival's `ScheduledTick` (STA), as for `DeboardComplete` above |
| `CabinClean`, `Catering`, `Fuel`, `BaggageLoad`, `PushbackPrep` | the departure's planned `OnStand` |
| `Boarding` | the departure's planned `ReadyToBoard` |

A job's `Started` and `Completed` carry the same value.

`TurnaroundJobBlocked.Resource` and `TurnaroundJobUnblocked.Resource`
(`EntityId?`) are **null on every event** at Phase 0/1, for a vehicle wait
as well as for `Boarding`'s `JobDependency` wait. A vehicle wait is for any
vehicle of the kind, not a particular one, and a `VehicleId` is not an
`EntityId` (`08` §8.4's allocator does not issue it). `WaitingOn` is
`Vehicle` for a vehicle wait and `JobDependency` for `Boarding`. An
`Unblocked` carries the same `WaitingOn`, `Resource` and `Category` as its
`Blocked`. The assigned vehicle is read from `TryGetJob(...).Vehicle`.

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

**The `Cause` of each emitted event (Q-080).** Binding, and it applies the
rules of `10-events.md` §10.2. Every event below has the `Cause` given and
no other. "Creation" is §13.6's job creation, which runs in the `OnStand`
handler (§13.6, "on the tick it receives one"). Handlers run in phase 3,
event dispatch, of the tick whose queue holds the event (`08` §8.5 "Fixed
phase order", §8.6). `sim.airside` publishes the `OnStand` in phase 2 of
that tick, so the handler's `OnStand` event is published earlier in the
same tick.

| Event | `Cause` |
|---|---|
| `TurnaroundJobBlocked` | the flight's `OnStand` that the creating handler received. Every `Blocked` is emitted at creation, both on a vehicle and, for `Boarding`, on `JobDependency` |
| `TurnaroundJobStarted` at creation | the same `OnStand`: `Deboard`, which goes straight to `Active`, and a job assigned a free vehicle at creation (§13.5 step 2) |
| `TurnaroundJobUnblocked`, and the `TurnaroundJobStarted` that follows it | for a vehicle wait, the `TurnaroundJobCompleted` of the job that held the very vehicle now assigned, published in step 1 of this tick (§13.5 step 3, Q-091); for `Boarding`, the fifth prerequisite's `TurnaroundJobCompleted` (§13.5 step 1, §13.6) |
| `TurnaroundJobCompleted` | None: it follows a duration of `NominalDurationTicks ≥ 1` (§13.4) |
| `DeboardComplete` | the `Deboard` job's `TurnaroundJobCompleted` (§13.6) |
| `ReadyToBoard` | the fifth prerequisite's `TurnaroundJobCompleted` (§13.6) |
| `BoardingComplete` | `Boarding`'s own `TurnaroundJobCompleted` (§13.6) |

`sim.turnaround` keeps no event id across ticks: every `Cause` above is
published earlier in the same tick.

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
`TurnaroundFleet`, `FreeVehicles()`, `JobsForFlight()`, the finish-order
list and the per-`VehicleKind` waiting lists (both below, Q-092). Both
are fully determined by the hashed jobs.

### Retention and the capacity bound (Q-092)

A flight is **finished** when all of its jobs are `Completed`: both jobs
for an arrival, all six for a departure. Its finish tick is the largest
`DueAt` among its jobs, so no new field is needed. A finished flight's
jobs stay in state, and in the queries and the hash, until they are
pruned:

- On a tick with `CurrentTick % TICKS_PER_SIM_DAY == 0`, before step 1
  (§13.5), `Tick` removes every finished flight whose finish tick is
  below `(CurrentTick / TICKS_PER_SIM_DAY − (TURNAROUND_RETENTION_DAYS − 1))
  × TICKS_PER_SIM_DAY`. That is, a flight is kept for the sim-day it
  finished on and the next one. This mirrors `sim.delay`'s
  `DELAY_RETENTION_DAYS` (`14` §14.8).
- Pruning removes all of the flight's jobs at once. Afterwards `TryGetJob`
  returns false for them and `JobsForFlight` returns an empty list, the
  same as for a flight that never had jobs.
- Finished flights are kept in finish order (ties broken by ascending
  `FlightId`), so pruning removes a prefix. It never scans unfinished
  flights. It is not an allocation.
- A flight that never finishes is never pruned. An example is a
  `Catering` job with no `CateringTruck` in the fleet (§13.4).

At most `TURNAROUND_FLIGHTS_CAPACITY` flights have jobs in state. Storage
for that many flights' jobs (eight job slots each) is preallocated at
construction and never grows. When the `OnStand` handler would create jobs
for flight 4 097, it throws `SimInvariantException` (`08`) naming that
`FlightId`, before creating any of its jobs, as `12` §12.2's bounds do.

Every other variable-size structure in the update path is bounded by the
same constant. Each is preallocated at construction to the capacity below
and never grows:

| Structure | Capacity | Why it is enough |
|---|---|---|
| job slots | `TURNAROUND_FLIGHTS_CAPACITY × 8` | at most one job per `JobKind` per flight (§13.3) |
| finish-order list | `TURNAROUND_FLIGHTS_CAPACITY` | one entry per finished flight in state |
| waiting list, one per `VehicleKind`: its `Blocked` jobs in ascending `EventId` of their `TurnaroundJobBlocked` (§13.5 step 3) | `TURNAROUND_FLIGHTS_CAPACITY` each | by §13.4's table, a flight has at most one job needing a given `VehicleKind`. An arrival's only tractor job is `BaggageUnload`, and a departure's is `BaggageLoad`. So each list holds at most one entry per flight in state |

`Boarding`'s `JobDependency` wait is in no waiting list. It is resolved by
§13.5 step 1 from its own flight's jobs.

None of these can overflow while the flight bound holds. A full structure
at an insertion is a broken invariant. It throws `SimInvariantException`
naming the `FlightId` being inserted, and it never grows. A waiting list
appends at its tail, because a new `Blocked` always has a higher `EventId`
than every entry already there. It removes from anywhere in it without
allocating. Its own layout is the worker's choice, provided it meets
§13.10's O(active + blocked + vehicles) bound.

Sizing works like `12` §12.2's (Q-085). At `01`'s max tier there are at
most 800 movements, and so 800 `OnStand`s, in a calendar day. Assume no
flight stays unfinished more than one sim-day after its `OnStand`. Then
every flight in state reached `OnStand` on the current calendar day or one
of the two before it: at most 2 400, under 4 096. Only two kinds of run
reach the bound: a flight left unfinished for days, which is a fixture
error at Phase 0/1 (a missing `VehicleKind`, or a fleet far too small for
the schedule), or a schedule above the max tier. `sim.airside` reaches its
own `TRACKED_FLIGHTS_CAPACITY` in the same runs (`12` §12.2). §13.4's
"`Blocked` forever is legitimate" therefore holds for runs of a few
sim-days, not for a soak.

**RNG: none at Phase 0/1.** Dispatch is FIFO by `EventId`; job creation and
duration are catalogue lookups. Same posture as `sim.schedule`
(`11-interfaces-schedule.md` §11.9) and `sim.airside`
(`12-interfaces-airside.md` §12.12).

Budget: **0.50 ms/tick at max tier** (`03-module-map.md`). It covers the
module's event handlers, for example the `OnStand` handler that creates jobs
(§13.6), as well as its `Tick`. They are timed with `03`'s handler shims
(Q-064). Per-tick work is
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

`schedule` is required (§13.9).

`CreateSystem` accepts a setup returned by `Load` or one built in code, as
tests do. It runs §13.4's five checks, in the same order, on the setup it
is given. A failure throws `ArgumentException` whose message names the
same `JobKind` (file spelling, below) or `VehicleId` (decimal) that `Load`
would name (Q-088). It adds no new public type.

### File format (Q-086)

Binding. It replaces the earlier "the worker's choice". The file is the
strict JSON subset of `08` §8.11 "The loader", with `18` §18.2's rules:
UTF-8 without a BOM, objects, arrays, strings and integers only, and no
fraction, exponent, `null`, `true` or `false`. Duplicate, unknown and
missing keys are parse (shape) failures, and keys may come in any order.
`sim.turnaround` hand-parses the file, with no package. The exact shape
is:

```
{
  "schema_version": 1,
  "jobs":     [ { "kind": "<job kind>", "nominal_duration_ticks": <uint32>,
                  "category": "<delay category>" }, ... ],
  "vehicles": [ { "id": <uint16>, "kind": "<vehicle kind>" }, ... ]
}
```

- Each object has exactly the keys shown. `schema_version` must be `1`.
- `<job kind>` is one of `deboard`, `baggage_unload`, `cabin_clean`,
  `catering`, `fuel`, `baggage_load`, `pushback_prep` and `boarding`.
  `<vehicle kind>` is one of `cleaning_crew`, `catering_truck`,
  `fuel_truck`, `baggage_tractor` and `pushback_tug`. `<delay category>`
  is any of `06-delay-attribution.md`'s 21 spellings. Any other string
  is a parse (shape) failure. A category that is spelled correctly but
  not allowed for a job, for example `pushback`, fails §13.4 check 4.
- There is no `requires_vehicle` key. `Load` sets `RequiresVehicle` from
  §13.4's table.
- An integer is `0` or `-?[1-9][0-9]*`. One outside its C# type's range
  is a parse failure. Values inside the range (a duration of 0, an id of
  0) parse, and fail §13.4's checks.
- `jobs` and `vehicles` may be in any order, and either may be empty
  (`jobs` then fails check 1). `Load` returns `Jobs` in ascending `JobKind`
  ordinal and `Vehicles` in ascending `VehicleId`.
- **Failures.** Every failure throws `FormatException` whose message
  starts with `sourceName` followed by `": "`. A parse failure (syntax,
  shape, C#-type range) contains the 1-based `line <n>`. A §13.4 failure
  contains the named `JobKind` in its file spelling above (for example
  `baggage_unload`) or the named `VehicleId` in decimal. A `null`
  `sourceName` throws `ArgumentNullException`. Tests assert the exception
  type, the `sourceName` prefix and the named token, and nothing else in
  the message.

## 13.11 The Phase 0/1 fixture (T-022)

`tests/fixtures/turnaround/phase1-five-vehicles.json`, in §13.10a's file
format, binding on the Test Author (Q-086, Q-088; it was
`phase1-four-vehicles.*`):

- exactly five `VehicleDef`s, one of each `VehicleKind`, with ids 1 to 5.
  At least one `VehicleKind` must have fewer vehicles than the peak
  concurrent demand for it over the companion fixtures, so
  `TurnaroundJobBlocked` fires at least once in a single sim-day run — the
  same requirement in spirit as `12-interfaces-airside.md` §12.13's runway
  capacity. Four vehicles cannot meet both this and §13.4's table: a
  missing `VehicleKind` blocks every departure forever;
- `TurnaroundCatalogue` covering all eight `JobKind`s, durations short enough
  relative to `MinTurnaround` in the schedule fixture that a full rotation
  can plausibly complete within its scheduled ground time when no vehicle
  contention occurs — so the "everything works" path is covered as well as
  the blocked path;
- runs against `tests/fixtures/schedule/phase0-200.csv`
  (`11-interfaces-schedule.md` §11.10) and
  `tests/fixtures/airside/phase1-single-runway.json`
  (`12-interfaces-airside.md` §12.13), with `sim.airside` **registered** this
  time — T-022 is exactly the task that exercises the real handshake in
  `12-interfaces-airside.md` §12.8 instead of its no-`sim.turnaround`
  fallback.

**Reaching `sim.airside` (Q-087).** The test project
`tests/sim/turnaround/AirportSim.Sim.Turnaround.Tests.csproj` has exactly
two `ProjectReference`s, in this order:
`../../../src/sim/turnaround/AirportSim.Sim.Turnaround.csproj`, then
`../../../src/sim/airside/AirportSim.Sim.Airside.csproj` (`07` L3). Tests
in it may construct `sim.airside` only for the tests below that say
`sim.airside` is registered: the doors-close handshake test, the
allocation test, and the headless day. Every other test drives `OnStand`
without `sim.airside`, as before. A registered-airside test supplies `AirsideRules`
directly (`12` §12.12a: no sim module parses JSON; the values are the
Test Author's, since with `flow` null there is no boarding hold), and
passes `flow` null and `turnaroundRegistered` true.

Done-condition tests this spec expects to exist, phrased per
`07-conventions.md`:

- `test_catalogue_rejects_missing_job_kind` (through `Load`, on bytes in
  §13.10a's format)
- `test_fixture_file_loads_with_one_vehicle_per_kind` (Q-086): `Load` on
  `phase1-five-vehicles.json` returns eight jobs in ascending `JobKind`,
  each with §13.4's `RequiresVehicle`, and five vehicles in ascending id
- `test_create_system_rejects_wrong_required_vehicle` (Q-088): a setup
  built in code with `PushbackPrep` on `BaggageTractor` throws
  `ArgumentException` naming `pushback_prep`
- `test_creation_events_follow_job_kind_order` (Q-090): a departure's
  creation events are published in ascending `JobKind`, `Boarding`'s
  `Blocked` last
- `test_finished_flight_jobs_pruned_after_retention_days` (Q-092): a flight
  that finishes on day `d` still answers `TryGetJob` at the last tick of
  day `d + 1`, and does not at the first tick of day `d + 2`. An
  unfinished flight is never pruned
- `test_turnaround_flights_overflow_throws_sim_invariant` (Q-092): with
  4 096 unfinished flights in state, the next `OnStand` throws
  `SimInvariantException` naming its `FlightId`. Before that, the
  creations allocate nothing
- `test_vehicle_dispatch_assigns_lowest_event_id_first`
- `test_boarding_waits_for_all_other_departure_jobs`
- `test_deboard_complete_fires_once_per_arrival`
- `test_vehicle_freed_on_completion_is_reassigned_same_tick`
- `test_turnaround_tick_consumes_no_rng`
- `test_airside_doors_close_after_boarding_complete_with_turnaround_registered`
  (integration-shaped: this is the counterpart to
  `12-interfaces-airside.md` §12.13's fallback-only test, and is what
  actually proves the §12.8 handshake works end to end)
- `test_turnaround_update_path_allocates_nothing_including_handlers`
  (Q-061, `03` "How a budget is measured"): with `sim.airside`
  registered, every `sim.turnaround` handler runs inside the metered
  window
- `test_turnaround_event_causes_follow_cause_table` (Q-080): every event
  this module publishes has the `Cause` of §13.9, including the `OnStand`
  for each `TurnaroundJobBlocked` and each `TurnaroundJobStarted` at
  creation
- `test_catalogue_rejects_zero_nominal_duration` (Q-080, §13.4)
