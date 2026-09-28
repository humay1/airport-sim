# 11 — Public interfaces: `sim.schedule`

Implements the `sim.schedule` row of `03-module-map.md`: the published timetable,
flight records, rotations, and the passenger demand those flights create. Answers
`open-questions.md` Q-004. Notation and binding rules are as in
`08-interfaces-core.md`; where this file appears to contradict `01-architecture.md`
or `02-determinism.md`, those win and it is a spec bug.

Reading order for a `sim.schedule` worker: `01`, `02`, `07`, `08`, this file,
then `09` §9.7 and `10` §10.4/§10.6.

---

## 11.1 What the module is, and what it is not

The published schedule is the clock (`00-overview.md`, pillar 2). `sim.schedule`
is therefore a **source of intent, not of behaviour**: it declares what was
supposed to happen and when, and it creates the passengers that intent implies.
It never decides what actually happens.

`sim.schedule` owns:

- the flight table for the session (loaded, validated, immutable thereafter),
- the tick at which each flight's plan is published,
- the rotation link between an arrival and the departure it feeds,
- **departing** passenger demand, injected into `sim.flow` over a show-up curve.

`sim.schedule` explicitly does **not** own:

- aircraft position, stands, runways, taxi time — `sim.airside`,
- turnaround work — `sim.turnaround`,
- **arriving** passengers: those are injected by `sim.airside` at the aircraft
  door on the `DoorsOpen` milestone, not by this module,
- any milestone other than `PlanPublished` (`10-events.md` §10.4),
- delay: it emits no `DelayEvent` and holds no delay state.

Schedule disruption (`FlightPlanRevised`, `FlightCancelled`, `10-events.md`
§10.6, Phase 1) is out of scope for this file. It is additive to §11.7 and
needs an amendment, not a local invention.

---

## 11.2 Constants

Compile-time constants in `sim.schedule`.

| Constant | Value | Meaning |
|---|---|---|
| `PLAN_PUBLISH_LEAD_TICKS` | `TICKS_PER_SIM_DAY` (14400) | §11.5 |
| `FLIGHT_ID_DAY_STRIDE` | 100000 | §11.3, id derivation |
| `MAX_FIXTURE_ROWS` | 99999 | `FLIGHT_ID_DAY_STRIDE − 1`. Total data rows in the file, asserted at load (§11.3, Q-039). It replaces the earlier per-day limit |
| `MAX_SHOW_UP_MINUTES_BEFORE_STD` | `PLAN_PUBLISH_LEAD_TICKS / TICKS_PER_SIM_MINUTE` (1440) | The largest `minutes_before_std` a referenced pax profile may use, asserted at load (§11.6, Q-038) |
| `TICK_UNSCHEDULED` | `uint64.MaxValue` | "this rotation has no such movement" |

`TICK_UNSCHEDULED` is the only sentinel in the module. It appears in
`FlightPlanPublished.schedArr` / `schedDep` for a movement with no linked
counterpart, and consumers must test for it rather than doing arithmetic on it.

---

## 11.3 Types

```
struct AirlineId { uint32 Value }               // from the fixture's airline code, §11.4; compiled in sim.core (10 §10.9, Q-018)

enum MovementKind { Arrival, Departure }        // compiled in sim.core (10 §10.9, Q-018)

readonly struct FlightRecord {
  FlightId     Id
  AirlineId    Airline
  ContentId    AircraftType
  MovementKind Kind
  uint32       DayIndex
  Tick         ScheduledTick          // STA for an Arrival, STD for a Departure
  Tick         PublishTick            // §11.5
  FlightId     Rotation               // linked counterpart; Id itself if none
  bool         HasRotation
  SimMinutes   MinTurnaround
  ContentId    PaxProfile
  int32        PaxCount               // 0 for an Arrival at Phase 0, §11.1
  int32        HoldBagPermille
  int32        AssistPermille
  NodeId       EntryNode              // landside Source node; Departures only
}
```

`FlightRecord` is immutable for the life of the session. Everything that changes
about a flight during the day lives in the module that changes it.

### Flight id derivation

Ids are derived, never allocated:

```
FlightId.Value = DayIndex * FLIGHT_ID_DAY_STRIDE + RowOrdinal + 1
```

`RowOrdinal` is the row's 0-based index after sorting the fixture rows by
`flight_ref` under ordinal string comparison. Deriving instead of allocating
gives three properties worth the arithmetic: file order cannot change behaviour
(`04-data-schemas.md` convention), the same flight has the same id on every
machine and in every replay, and a flight id read from a log names its day and
its fixture row by inspection. `IIdAllocator` is **not** used for `FlightId`.

**What the derivation guarantees (Q-039).** `RowOrdinal` indexes the whole
file, not one day, so the bound is on the file. A fixture loads only if it
has at most `MAX_FIXTURE_ROWS` data rows (§11.4). Each row occurs at most
once per day, so for every loaded table:

- `0 ≤ RowOrdinal ≤ MAX_FIXTURE_ROWS − 1`, so
  `1 ≤ Value % FLIGHT_ID_DAY_STRIDE ≤ MAX_FIXTURE_ROWS`;
- `FlightId` is unique across all days of the session;
- `Value / FLIGHT_ID_DAY_STRIDE = DayIndex` and
  `Value % FLIGHT_ID_DAY_STRIDE − 1 = RowOrdinal`, so both can be decoded
  from an id;
- ascending `FlightId` equals ascending `(DayIndex, RowOrdinal)`.

The module may rely on these four facts and on nothing stronger. The
`uint64` `Value` cannot overflow for any `uint32` day. Nothing else bounds
`DayIndex`.

Ordering rule: every iteration over flights, every publication and every
injection is in ascending `FlightId` (`02-determinism.md` rule 5).

---

## 11.4 The fixture format

Phase 0 schedules are CSV **test fixtures**, not shipping content. They live
beside the tests that use them (`07-conventions.md`), never in `data/`. The
shipping schedule format is part of scenario content and is a later amendment;
nothing in this section may be taken as a commitment to ship CSV.

### File rules

- UTF-8, no BOM, LF line endings, final newline required.
- Line 1 is the header and must equal the header below **byte for byte**,
  including column order. A mismatch is a load failure, not a remapping.
- Comma-separated, no quoting, no embedded commas, no comment lines, no blank
  lines. Leading/trailing whitespace in a field is a load failure, not trimmed.
- Any violation fails the load hard, naming file and 1-based line number
  (`07-conventions.md`). Content never silently defaults.

### Header

```
flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node
```

| Column | Type | Rule |
|---|---|---|
| `flight_ref` | string | Unique within the file. Ordinal sort order defines `RowOrdinal`. Display identity only; the sim keys on `FlightId`. |
| `day` | uint32 | Day index of the first occurrence. |
| `repeat_daily` | `0`\|`1` | `1`: the row recurs on every day from `day` onward. `0`: occurs on `day` only. |
| `movement` | `A`\|`D` | Arrival or Departure. |
| `airline` | string | Maps to `AirlineId` by `FNV-1a-32` of the ordinal bytes; a collision within one fixture is a load failure. |
| `aircraft_type` | string | `ContentId`. Must resolve in `IContentIndex` at load. |
| `sched_hhmm` | `HH:MM` | 24-hour, zero-padded, within the row's day. Converted via `ISimClock.TickOfDayTime(day, (hh*60+mm)*60)`. |
| `rotation_ref` | string | `flight_ref` of the linked counterpart, or empty. Must be same `day`, opposite `movement`, and mutually referential. |
| `min_turnaround_minutes` | uint32 | `SimMinutes` via `Fx.FromInt`. Mandatory at Phase 0; a later amendment may default it from aircraft content. |
| `pax` | int32 ≥ 0 | Departing passengers. Must be `0` when `movement=A` at Phase 0 (§11.1). |
| `pax_profile` | string | `ContentId` of a pax profile (§11.6). Must resolve. |
| `hold_bag_permille` | 0..1000 | Share of `pax` with hold baggage. |
| `assist_permille` | 0..1000 | Share of `pax` requiring assistance. |
| `entry_node` | uint32 | `NodeId` of the landside `Source` node the passengers enter at. Empty when `movement=A`. |

Additional load-time validations, all hard failures:

- total data rows `<= MAX_FIXTURE_ROWS` (Q-039). Rows are counted in file
  order, and the failure names the line of the first row over the limit
  (line `MAX_FIXTURE_ROWS + 2`, since line 1 is the header). The check
  enumerates no dictionary and does not depend on `day` or `repeat_daily`;
- a `rotation_ref` pair must satisfy `STA < STD` on the same day — cross-midnight
  rotations are **not supported at Phase 0** and are a question, not a workaround;
- a `D` row with `pax > 0` must name an `entry_node`;
- `repeat_daily` must agree between the two rows of a rotation.

### The parsed table

```
readonly struct ScheduleTable {
  IReadOnlyList<FlightTemplate> Rows          // ascending flight_ref, ordinal
  uint64                        FixtureHash   // FNV-1a-64 over the raw file bytes
}

interface IScheduleLoader {
  ScheduleTable Load(ReadOnlySpan<byte> csv, string sourceName)
}
```

`FixtureHash` is over the **raw bytes**, so an edited fixture cannot quietly
change a golden hash: it is fed into the module's state hash (§11.9) and a
changed fixture fails the determinism gate loudly rather than drifting.
`FlightTemplate` is the per-row, day-independent form of `FlightRecord`; it is
internal in shape but its field values are fixed by the table above.

---

## 11.5 Publication

A flight becomes visible to the rest of the sim when its plan is published.

- `PublishTick = ScheduledTick - PLAN_PUBLISH_LEAD_TICKS`, clamped to 0.
- For a `repeat_daily` row, each day's occurrence is materialised independently
  and its rotation link resolves **within its own day**.
- At each tick, flights whose `PublishTick` equals the current tick are published
  in ascending `FlightId`. For each, in this order and on the same tick:
  1. `FlightPlanPublished`, fields per `10-events.md` §10.6. `schedArr` is the
     arrival side of the rotation (this flight's `ScheduledTick` if it is an
     `Arrival`, the linked arrival's otherwise, `TICK_UNSCHEDULED` if none);
     `schedDep` is the mirror. `kind`, `rotation` and `hasRotation` are copied
     from `FlightRecord.Kind`, `Rotation` and `HasRotation` — they are how
     `sim.delay` learns the rotation link without a query
     (`14-interfaces-delay.md` §14.12).
  2. `FlightMilestoneReached { Milestone = PlanPublished, PlannedTick = ActualTick
     = PublishTick }`, with `Cause` set to the `EventId` of the
     `FlightPlanPublished` above. Once per flight, never re-emitted.
- A published flight is never unpublished. Cancellation is a Phase 1 event.

Because of the clamp, every flight in day 0 whose lead falls before tick 0
publishes **at tick 0**. Two events per flight puts a 200-movement fixture at 400
events on tick 0, well inside `MAX_EVENTS_PER_TICK` (4096); a fixture large
enough to breach it is a fixture bug, and the invariant in
`08-interfaces-core.md` §8.6 will say so.

---

## 11.6 Passenger demand

### Show-up curve

Departing passengers do not appear at their flight's STD; they arrive over a
curve. The curve is **content**, declared on the pax profile
(`04-data-schemas.md`), never hardcoded and never drawn from RNG:

```
pax_profile definition, schedule-relevant fields:
  show_up_curve : [ { minutes_before_std : uint32, share_permille : uint32 } ]
```

- Buckets are strictly ascending in `minutes_before_std`, distinct, and
  `share_permille` sums to exactly 1000. Anything else fails content load.
- The profile's `walk_speed_mps` (`09-interfaces-flow.md` §9.6) belongs to
  `sim.flow` and is not read here. The definition type is
  `PaxProfileDefinition` (`08` §8.11).

### Expansion to injections

At publication, the flight's injections are computed once and queued:

1. Split `PaxCount` across the curve's buckets by `share_permille`, using
   **largest remainder**, ties broken by ascending bucket index, so the bucket
   counts sum to `PaxCount` exactly. No passenger is created or lost.
2. Split each bucket's count across the four `(HasHoldBaggage, RequiresAssistance)`
   classes, using the products of `hold_bag_permille` / `assist_permille`, again
   by largest remainder with ties by ascending class index, where class index is
   `(hasBag ? 2 : 0) + (assist ? 1 : 0)`.
3. The injection tick is
   `ScheduledTick - minutes_before_std * TICKS_PER_SIM_MINUTE`, **clamped to 0**.
   Clamping rather than dropping preserves head-count conservation on day 0;
   several buckets collapsing onto tick 0 is harmless because `sim.flow` merges
   cohorts with equal keys (`09-interfaces-flow.md` §9.3).
4. Classes with a count of 0 produce no injection.

**No injection is due before its publication (Q-038).** Every bucket has
`minutes_before_std ≤ MAX_SHOW_UP_MINUTES_BEFORE_STD` (§11.9a), so:

`max(0, ScheduledTick − minutes_before_std × TICKS_PER_SIM_MINUTE) ≥ max(0, ScheduledTick − PLAN_PUBLISH_LEAD_TICKS) = PublishTick`

So every injection tick is at or after its flight's `PublishTick`. It is
therefore also at or after the tick that materialised its day (§11.9). No
entry is ever queued for a tick that has already run. Every passenger of
a published flight is injected exactly once, so no passenger is created or
lost. `clamp to 0` stays the only clamp. It is the day-0 case of the same
inequality. A profile past the bound fails load. Neither clamping nor
dropping is used.

**Order of the steps in `sim.schedule`'s `Tick` (Q-038).** For tick `t`:
1. day materialisation, if `t % TICKS_PER_SIM_DAY == 0` (§11.9);
2. publication (§11.5): for each flight with `PublishTick == t` in
   ascending `FlightId`, its two events are **published**, and then its
   injections are computed and queued;
3. injection: every entry due at `t` is drained, including entries queued
   in step 2 of this tick, in ascending `(FlightId, bucketIndex,
   classIndex)`.

This fixes the order of the calls, and so the `EventId`s and the `Inject`
sequence. It is **not** an order a handler can observe between an event
and an injection.

**What is guaranteed, in observable terms.**
- **By tick.** For every flight, every `Inject` of its passengers happens in
  a tick `≥` the tick in which its `FlightPlanPublished` is published. No
  passenger ever reaches `sim.flow` in an earlier tick than its flight's
  plan. That closes Q-038's early-injection case.
- **On a shared tick** (injection tick `= PublishTick`), the order across
  modules follows `08` §8.5 and §8.6 rule 1. `Publish` only queues. The
  `Inject` call is synchronous, in phase 2, inside `sim.schedule`'s
  `Tick`. `sim.flow` then ticks in the same phase 2; the new cohort has
  `EnteredNodeAt = t`, so it does not move before `t + 1` (§9.12).
  Handlers see `FlightPlanPublished` only in phase 3. So on a shared tick,
  **`sim.flow` holds the cohort before any subscriber sees the plan.** This
  is **accepted**, and nothing depends on the opposite. No Phase 0/1
  handler of `FlightPlanPublished` reads `sim.flow`, and nothing in
  `sim.flow` reads the plan. A shared tick happens when a bucket sits
  exactly at `MAX_SHOW_UP_MINUTES_BEFORE_STD`, and for day-0 flights whose
  buckets clamp to tick 0, which `PublishTick` 0 shares (§11.10 requires
  one). A future consumer that needs the plan before the passengers exist
  needs an amendment. It must not assume this order.

No RNG is consumed anywhere in this expansion. See §11.9.

### Injection

During `Tick`, entries due at the current tick are drained in ascending
`(FlightId, bucketIndex, classIndex)` and each calls, once:

```
IFlowSystem.Inject(key, count, record.EntryNode)
  key = { Flight, Direction = Departing, PaxProfile, HasHoldBaggage, RequiresAssistance }
```

`Inject` is the only call `sim.schedule` makes into another module. It targets a
`Source` node; targeting any other kind is a programmer error and `sim.flow`
throws (`07-conventions.md`).

### Running without `sim.flow`

A build may register `sim.schedule` without `sim.flow` — T-008 lands before the
two are wired together. In that case the injector is absent, due entries are
still dequeued at exactly the same ticks, and **the module's state hash is
identical with and without it**. This is binding, and it is what makes
`sim.schedule` testable on its own: the passengers live in `sim.flow`'s hash, the
intent to create them lives here.

---

## 11.7 Module interface

```
interface IScheduleSystem : ISimSystem {

  // ---- queries, read-only ----
  bool TryGetFlight(FlightId id, out FlightRecord flight)
  IReadOnlyList<FlightId> PublishedFlights()                        // ascending FlightId
  IReadOnlyList<FlightId> MovementsBetween(Tick fromInclusive,
                                           Tick toExclusive,
                                           MovementKind kind)       // ascending ScheduledTick, then FlightId
  bool TryGetRotation(FlightId flight, out FlightId counterpart)
  int32 PendingInjectionCount(FlightId flight)                      // passengers not yet injected
}
```

- `MovementsBetween` filters on `ScheduledTick`, not `PublishTick`, and is how
  `sim.airside` learns what it must handle. It returns published and unpublished
  flights alike; publication governs events, not visibility to a downward caller.
- `TryGetRotation` is what makes `late_inbound` attributable: a departure's parent
  cause is the arrival that feeds it (`06-delay-attribution.md`). Consumers read
  it; `sim.delay` does not — it is event-only by rule 2 and gets the link from
  `FlightPlanPublished`'s `rotation`/`hasRotation` fields (§11.5).
- `PendingInjectionCount` exists for tests and the UI. It is derived from hashed
  state, not separate state. It is 0 for a flight not yet published, because
  injections are computed at publication, and 0 for an unknown id. Publication
  happens inside `Tick`, through `ctx.Events` (Q-031).
- There is no mutating entry point. Nothing may write to `sim.schedule`.

Registry position is **2** (`08-interfaces-core.md` §8.5), before `sim.airside`
and `sim.flow`. So in phase 2 of a tick, a passenger injected in that tick is
already in `sim.flow` when `sim.flow` ticks. `FlightPlanPublished` is only
queued in phase 2, and subscribers see it in phase 3 (§11.6 "What is
guaranteed").

---

## 11.8 Commands consumed

None at Phase 0. Player-initiated schedule change (slot negotiation, cancellation)
arrives with Phase 1 and extends this table by amendment only.

---

## 11.9 State, hashing, RNG and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9):

1. `ScheduleTable.FixtureHash`.
2. The highest day index materialised so far.
3. Published flights in ascending `FlightId`: `Id`, `PublishTick`, `ScheduledTick`.
4. Pending injections in ascending `(FlightId, bucketIndex, classIndex)`: due
   tick, count, class index.

Lists 3 and 4 are each preceded by their element count as a `uint64`
(Q-031). **Materialisation (Q-031).** To materialise day `d` is to create
the occurrences of every row with `day == d`, and of every row with
`repeat_daily = 1` and `day < d`. `CreateSystem` materialises day 0, so the
highest day is 0. At the start of `Tick` for a tick `t` with
`t % TICKS_PER_SIM_DAY == 0`, before any publication on that tick, day
`t / TICKS_PER_SIM_DAY + 1` is materialised, and it becomes the highest.
Because `PLAN_PUBLISH_LEAD_TICKS` is one day, every occurrence therefore
exists before its `PublishTick`. The highest day advances even when a day
has no rows.

Not hashed, because derived: the `MovementsBetween` index, any per-airline or
per-day lookup, `PendingInjectionCount`.

**RNG: `sim.schedule` consumes none at Phase 0.** It declares no stream and
contributes no stream state to its hash. Demand variation, no-shows and
disruption all look like natural uses of randomness later; each is a spec
question, because introducing a stream here shifts nothing else (streams are
per-system, `08-interfaces-core.md` §8.8) but does make the schedule stop being a
pure function of the fixture, which several Phase 0 tests rely on.

Budget: **0.10 ms/tick at max tier** (`03-module-map.md`). It is measured
with `03`'s statistic, "How a budget is measured": mean ≤ 0.10 ms **and**
p99 ≤ 0.20 ms over the ticks of one full sim-day. A mean alone, or a mean
over several days, does not satisfy it (Q-031). Per-tick work is
O(flights published this tick + injections due this tick) and nothing else. A
scan over the whole flight table inside `Tick` is a review rejection: both
publication and injection are due-ordered queues, built once at load and at day
materialisation. No allocation in the update path (`07-conventions.md`); day
materialisation for a `repeat_daily` fixture happens off the injection path, at
the day boundary, and may allocate.

---

## 11.9a Construction (Q-009)

```
ScheduleFactory.CreateLoader() -> IScheduleLoader                        // §11.4
ScheduleFactory.CreateSystem(in SystemServices services, in ScheduleTable table,
                             IFlowSystem? flow) -> IScheduleSystem
```

`flow` is null when `sim.flow` is not registered (§11.6). `aircraft_type` and
`pax_profile` resolve through `services.Content` at construction; a miss is a
load failure (§11.4). It throws `FormatException` whose message starts with
`sim.schedule: ` and contains the row's `flight_ref`, the column name and
the unresolved id (`07` "Error handling", Q-031).

**Show-up bound (Q-038).** At the same point, a resolved `pax_profile`
with any bucket whose `minutes_before_std > MAX_SHOW_UP_MINUTES_BEFORE_STD`
is a load failure. This applies whatever the row's `pax`. It throws the
same `FormatException` shape: `sim.schedule: `, the row's `flight_ref`,
the column `pax_profile`, the profile id, and the `minutes_before_std` of
the **first** bucket over the bound in curve order. Buckets are strictly
ascending, so that is the smallest offending value.

**Which failure is reported (Q-038).** Construction checks rows in the
table's order, ascending `flight_ref` (§11.4). Within a row it checks in a
fixed order: `aircraft_type` resolves, then `pax_profile` resolves, then
the show-up bound. It throws the first failure it meets, so a resolution
miss on an earlier row wins over a bound failure on a later one, and on
one row a resolution miss wins over the bound. The check lives here, not
in `sim.core`'s content loader, because the bound is derived from
`sim.schedule`'s `PLAN_PUBLISH_LEAD_TICKS`.

## 11.10 The Phase 0 fixture (T-008)

`tests/fixtures/schedule/phase0-200.csv`, binding on the Test Author, who
writes it (Q-021):

- exactly 200 movement rows (100 `A` + 100 `D`), all with `day=0` and
  `repeat_daily=1`, so that T-009's 100-day run has load on every day. They
  form 99 rotations plus the lone `A` and lone `D` below (Q-031);
- every `D` row carries `pax > 0`, a resolvable `pax_profile` and an `entry_node`;
- at least one rotation scheduled early enough that its show-up curve clamps to
  tick 0, so the clamp in §11.6 is covered;
- at least one `A` row and one `D` row with an empty `rotation_ref`, so the
  `TICK_UNSCHEDULED` path in `FlightPlanPublished` is covered.

Passenger counts and the shape of the day (bank structure, peak) are **fixture
sizing, not balance**: they may be chosen by the Test Author to exercise the
budget, and they carry no implication for shipping content.

Done-condition tests this spec expects to exist, phrased per
`07-conventions.md`:

- `test_loader_rejects_reordered_header_with_line_number`
- `test_flight_ids_are_independent_of_row_order`
- `test_show_up_split_conserves_head_count`
- `test_profile_with_show_up_beyond_publish_lead_fails_load` (Q-038)
- `test_injection_tick_never_before_publication_tick_on_any_day` (Q-038).
  - **Fixture, exact values.** One `D` row with `day=0`, `repeat_daily=1`,
    `sched_hhmm=00:00`, `pax=10`, `hold_bag_permille=0`,
    `assist_permille=0`, no `rotation_ref`, and an `entry_node`. Its pax
    profile's curve is exactly
    `[ { minutes_before_std: 60, share_permille: 400 }, { minutes_before_std: 1440, share_permille: 600 } ]`.
  - **What that gives.** The split is exact, with no remainder: bucket 0
    (60 min) gets 4 and bucket 1 (1440 min) gets 6. All go to class 0,
    since both permille values are 0. So every occurrence makes exactly two
    `Inject` calls, `count` 4 and `count` 6, and the 1440-minute bucket is
    never empty.
  - **Run.** Ticks `0` to `3 × TICKS_PER_SIM_DAY − 1` (0 to 43199).
    **Four** occurrences are published inside the run: days 0, 1, 2 and 3.
    Day 3 is materialised and published at tick 28800. Day 4 is
    materialised at 43200, outside the run.
  - **Observed.** The kit's recorder (`RecorderSystemId` 7, which receives
    in phase 3) records the tick of each `FlightPlanPublished`. The kit's
    fake `IFlowSystem` records the tick of each `Inject` call.
  - **Asserted, for each of the four occurrences.**
    - Its `FlightPlanPublished` tick equals its `PublishTick`.
    - Its 6-passenger `Inject` falls on that same tick, a shared tick.
    - Its 4-passenger `Inject` falls on `ScheduledTick − 600` (60 × 10),
      clamped to 0.
    - Every `Inject` tick is `≥` the publication tick, and the injected
      counts sum to 10.
    - `PendingInjectionCount` is 0 at the end of the run. All eight
      injections fall inside it: the latest is day 3's at tick 42600.
  - **The four days** give these ticks
    `(ScheduledTick; PublishTick = 1440-bucket tick; 60-bucket tick)`:
    - day 0: `(0; 0; 0)`. Both buckets clamp to 0.
    - day 1: `(14400; 0; 13800)`.
    - day 2: `(28800; 14400; 28200)`. Tick 14400 also materialises day 2.
    - day 3: `(43200; 28800; 42600)`. Tick 28800 also materialises day 3.
    Days 2 and 3 are the 00:00 edge, where materialisation, publication and
    injection all fall on `(d − 1) · 14400`.
  - The test does **not** assert an order between the recorder's event and
    the `Inject` within a shared tick. That order is the accepted
    cross-module one of §11.6.
- `test_loader_rejects_rows_over_max_fixture_rows_with_line_number` (Q-039).
  A file of `MAX_FIXTURE_ROWS + 1` rows fails at line `MAX_FIXTURE_ROWS + 2`.
- `test_flight_ids_unique_and_decodable_at_max_fixture_rows` (Q-039).
  - **Fixture.** Exactly `MAX_FIXTURE_ROWS` rows, all `A` with `pax=0`,
    `day=1`, `repeat_daily=1` and no `rotation_ref`. `sched_hhmm` is
    spread so that no minute holds more than 70 rows. That puts at most
    140 events on any publication tick, within `MAX_EVENTS_PER_TICK`
    (4096).
  - **Run.** Ticks `0` to `3 × TICKS_PER_SIM_DAY − 1`, which publishes days
    1, 2 and 3.
  - **Asserted.**
    - `PublishedFlights()` holds `3 × MAX_FIXTURE_ROWS` distinct ids, in
      ascending order;
    - each id decodes to its `(DayIndex, RowOrdinal)`;
    - `RowOrdinal` `MAX_FIXTURE_ROWS − 1` on day `d` and `RowOrdinal` 0 on
      day `d + 1` do not collide.
  - The fixture must **not** put these rows on day 0. Every day-0 flight
    publishes at tick 0 (§11.5), so 99 999 of them would publish about
    200 000 events in one tick. That throws `SimInvariantException` at the
    event limit (`08` §8.6), which is the fixture bug §11.5 describes, not
    a `FlightId` failure.
- `test_publication_emits_plan_then_milestone_once_per_flight`
- `test_schedule_hash_identical_with_and_without_flow_registered`
- `test_schedule_tick_consumes_no_rng`
