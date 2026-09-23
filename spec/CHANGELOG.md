# Spec changelog

Every spec change is recorded here. The human owner reviews this file weekly — it
is the cheapest way to detect the architect agent drifting.

## Format

```
## <date> — <spec file> — <summary>
Reason:      <why>
Raised by:   <question id, playtest, human directive>
Impact:      <modules needing rework, or "none, additive">
Signed off:  <human | not required>
```

---

## 0000-00-00 — initial — scaffold created
Reason:      Project start
Impact:      none
Signed off:  pending

## 2026-09-21 — spec/08-interfaces-core.md — new file: `sim.core` public interfaces
Reason:      Phase 0 tasks T-001 to T-006 cannot be released without the interface
             they implement (`03-module-map.md`: "Any interface not listed in this
             spec does not exist"). Defines sim time, `Fx`, ids, `ISimSystem` and
             the fixed tick phase order, the event bus transport, the command
             queue, the RNG service, state hashing, logging and content access.
Raised by:   human directive — complete the Phase 0 specification
Impact:      none, additive. No code exists yet; no merged work is invalidated.
             Unblocks T-001..T-006 and T-009.
Signed off:  not required, but see LOW CONFIDENCE below
Notes:       Pins algorithms that are effectively part of the save format:
             xoshiro256** + SplitMix64 seeding, FNV-1a-64 hashing, Q31.32 fixed
             point. Changing any of them later is a migration, not a refactor.

## 2026-09-21 — spec/08-interfaces-core.md §8.2 — LOW CONFIDENCE: `SIM_SECONDS_PER_TICK = 6`
Reason:      `01-architecture.md` locks the tick *rate* (10 Hz at 1x) but not how
             much sim time a tick carries. Every schedule fixture, delay figure
             and soak estimate needs it, so Phase 0 cannot start without a value.
Raised by:   architect, during Phase 0 completion
Impact:      Sets a sim-day at 14 400 ticks and 24 real minutes at 1x. Changing it
             invalidates every golden hash and every tick-valued fixture — but no
             interface. Cheap to change now, expensive after T-009 lands goldens.
Signed off:  **PENDING HUMAN** — this is a pacing decision, which `CLAUDE.md`
             reserves to the owner. Filed as Q-002. Workers may proceed; the
             Verifier must not bless golden hashes until it is confirmed.

## 2026-09-21 — spec/09-interfaces-flow.md — new file: `sim.flow` public interfaces
Reason:      Phase 0 tasks T-007, T-010 and T-011 — the kill gate — need the
             cohort model, the queue throughput model and the promotion contract
             specified before implementation.
Raised by:   human directive — complete the Phase 0 specification
Impact:      none, additive. Unblocks T-007, T-010, T-011, T-023.
Signed off:  not required
Notes:       The load-bearing decision is §9.1: the cohort is the only
             authoritative state and an agent is a derived view that can never
             write back. This makes `01-architecture.md` promotion rule 4 and the
             `determinism_promotion` gate true by construction rather than by
             care. It also means agent visual variety draws from
             `flow.presentation`, the one RNG stream excluded from the state hash.
             Narrower than `01-architecture.md` strictly requires: individual
             passenger identity is *derived* from `(CohortId, index)`, never drawn,
             so naming a passenger in the delay tree consumes no randomness.

## 2026-09-21 — spec/09-interfaces-flow.md §9.6 — LOW CONFIDENCE: deterministic least-cost routing
Reason:      A cohort choosing between two security halls or three gates needs a
             rule. Chose lowest predicted traversal-plus-wait, ties by ascending
             `NodeId`, with no RNG.
Raised by:   architect, during Phase 0 completion
Impact:      Additive. Risk is visual and behavioural, not structural: every
             cohort takes the same door, and two queues can oscillate. The
             mitigation, if playtest shows it, is a content-declared proportional
             split — never an RNG draw, which would cost promotion neutrality.
Signed off:  not required; flagged for the owner's playtest attention

## 2026-09-21 — spec/10-events.md — new file: event catalogue consumed by `sim.delay`
Reason:      `06-delay-attribution.md` requires live attribution through the event
             bus and forbids reconstruction, but never listed the inbound events.
             Without the list, eight modules would each invent their own shape and
             `sim.delay` would be built against none of them.
Raised by:   human directive — complete the Phase 0 specification
Impact:      none, additive. Constrains every future emitting module: the envelope
             (`Cause` reference), the emission discipline (one event per state
             transition, paired blocked/unblocked), and the catalogue itself.
             Unblocks T-024 and the Test Author's delay tests.
Signed off:  not required
Notes:       The model is milestones plus blocking intervals: modules report what
             they were waiting on, at the moment they waited. `sim.delay` performs
             no inference. §10.5 fixes the allocation arithmetic so that
             `sum_of_leaves_equals_total` (`06-delay-attribution.md` rule 1) holds
             by construction — largest-remainder settlement, ties by ascending
             `EventId`.

## 2026-09-21 — spec/10-events.md §10.5 — LOW CONFIDENCE: first-blocker-wins for concurrent causes
Reason:      When two causes block the same flight over the same minute, that
             minute must go to exactly one of them or leaf sums stop matching the
             total. Chose the interval with the lowest `EventId` — the one that was
             blocking first.
Raised by:   architect, during Phase 0 completion
Impact:      Affects tree *contents*, not interfaces. Under-reports a genuine
             second cause that started later and would have delayed the flight
             anyway. The alternative — proportional splitting — is fairer and much
             harder to read, which cuts against "legible failure"
             (`00-overview.md` pillar 3).
Signed off:  not required; the owner should review once the delay tree is visible
             in a build

## 2026-09-21 — spec/03-module-map.md — per-module budgets filled in, plus measurement protocol
Reason:      The table was TBD and `01-architecture.md` delegates the apportioning
             here. Module tests must assert a number, and "TBD" is not one.
Raised by:   human directive — complete the Phase 0 specification
Impact:      none, additive. The five named splits in `01-architecture.md`
             (flow 2.5, baggage 1.0, airside 0.8, turnaround 0.5, delay 0.4) are
             reproduced unchanged; only that document's "everything else combined
             0.8" line is apportioned, into ten modules totalling 0.72 plus a
             0.08 reserve held by the Architect. Total remains 6.00 ms.
Signed off:  not required — the locked total and its named splits are untouched
Notes:       Also adds the measurement protocol, because a budget measured
             differently by two agents is not a budget: reference machine,
             max-tier one-day fixture, mean ≤ budget and p99 ≤ 2x budget, zero
             allocation in the update path. Off-tick ceilings for checkpoint
             hashing (20 ms) and snapshot writes (250 ms) are marked LOW
             CONFIDENCE — they are estimates with no measurement behind them and
             T-011 is the first chance to correct them.

## 2026-09-21 — spec/open-questions.md — Q-002, Q-003 raised by the Architect
Reason:      Two Phase 0 questions the Architect must not answer alone. Q-002: how
             much sim time a tick carries is a pacing decision. Q-003: the locked
             rationale "500 sim-days in minutes" is not reachable at max-tier cost,
             so the soak fixture size needs declaring before the gate becomes slow
             enough that someone disables it.
Raised by:   architect, during Phase 0 completion
Impact:      Q-002 blocks nothing today but should be settled before T-009 commits
             golden hashes. Q-003 affects the nightly soak only.
Signed off:  **PENDING HUMAN** on both

## 2026-09-21 — spec/11-interfaces-schedule.md — new file: `sim.schedule` public interfaces
Reason:      T-008 was BLOCKED on Q-004: `03-module-map.md` forbids starting a
             module with no published interface, and `sim.schedule` had none.
             Defines `IScheduleSystem` (query-only), `FlightRecord`, derived
             `FlightId`s, the CSV fixture schema and its validation, plan
             publication, the show-up curve that drives `IFlowSystem.Inject`,
             hashing, budget shape and the Phase 0 fixture's requirements.
Raised by:   Q-004 (planner / queue expansion for T-008)
Impact:      none, additive — no `src/` code exists yet, so nothing merged is
             invalidated. Unblocks T-008 and transitively T-009, T-021, T-022,
             T-023 (schedule-fed fixtures), T-024.
Signed off:  not required
Notes:       Deliberately narrow. `sim.schedule` injects **departing** passengers
             only; arriving passengers are `sim.airside`'s at `DoorsOpen`. It
             consumes **no RNG** at Phase 0, so the schedule is a pure function of
             the fixture. It emits only `PlanPublished`; revision and cancellation
             stay Phase 1. `FlightId` is derived arithmetically from day and row
             rather than allocated, so file order cannot change behaviour and a
             flight id in a log names its fixture row. The fixture's raw-byte hash
             is fed into the module's state hash, so an edited fixture fails the
             determinism gate loudly instead of silently rewriting a golden.

## 2026-09-21 — spec/03-module-map.md — `sim.schedule` depends on `core, flow`
Reason:      The dependency column said `core`, but `09-interfaces-flow.md` §9.7
             already named `sim.schedule` as a caller of `IFlowSystem.Inject`.
             The map was the side that was wrong. `sim.flow` depends on core and
             world only, so schedule -> flow remains a downward call and the
             "downward calls only" rule still holds.
Raised by:   Q-004
Impact:      none, additive — corrects an existing internal contradiction.
Signed off:  not required

## 2026-09-21 — spec/09-interfaces-flow.md §9.7 — `Inject` preconditions made explicit
Reason:      `11-interfaces-schedule.md` §11.6 relies on a bad injection target
             failing rather than being swallowed; that rule has to be binding on
             `sim.flow`, not asserted from the caller's spec.
Raised by:   Q-004
Impact:      none, additive. `sim.flow` is unimplemented (T-007 not started).
Signed off:  not required

## 2026-09-21 — spec/04-data-schemas.md — `pax_profile` schema row, fixtures-are-not-content note
Reason:      `09-interfaces-flow.md` referenced a passenger profile in content that
             the schema table never listed, and `11-interfaces-schedule.md` adds
             the show-up curve to it. Also records that Phase 0 CSV schedules are
             test fixtures and commit the shipping build to nothing.
Raised by:   Q-004
Impact:      none, additive.
Signed off:  not required

## 2026-09-21 — spec/00-overview.md — systems index points Schedule at its interface file
Reason:      Bookkeeping, so the index names the interface rather than the map.
Raised by:   Q-004
Impact:      none.
Signed off:  not required

## 2026-09-21 — spec/12-interfaces-airside.md — new file: `sim.airside` public interfaces
Reason:      T-021 was BLOCKED on Q-005: no published `sim.airside` interface
             existed, so runway pacing, taxiway conflicts, stand assignment and
             milestone emission would each have been invented by the worker.
             Defines `IAirsideSystem` (query-only), a self-owned taxiway/
             runway/stand graph (§12.4), the runway pacing-plus-occupancy hold
             model (§12.5), single-lane taxiway conflicts (§12.6), stand
             compatibility and assignment plus the door-side `Inject`/`Absorb`
             calls (§12.7), and a no-`sim.turnaround` fallback so T-021 can ship
             and be tested before T-022 exists (§12.8).
Raised by:   Q-005 (planner / queue expansion for T-021)
Impact:      none, additive — no `src/` code exists yet. Unblocks T-021 and
             transitively T-022, T-024. T-022's brief should read §12.8's
             handshake contract (the `BoardingComplete` event `sim.turnaround`
             must emit) as binding on its own interface, once Q-006 is answered.
Signed off:  not required
Notes:       `sim.world` has no published interface either (a separate,
             unopened question); rather than block T-021 on that too, this file
             scopes Phase 0/1 narrowly — `sim.airside` owns and loads its own
             taxiway/runway/stand graph, independent of `sim.world`. Folding it
             into `sim.world` later is additive, not a redesign, since the
             shape is already a plain graph. Consumes no RNG at Phase 0/1, same
             posture as `sim.schedule`. Arrival passenger injection is
             explicitly deferred: `11-interfaces-schedule.md` only defines pax
             counts for departures, so the `Inject`-at-`DoorsOpen` call this
             file's §12.1 references from the schedule spec is a no-op until a
             future amendment adds arrival demand data.

## 2026-09-21 — spec/10-events.md §10.4 — resolves milestone ownership: airside vs. turnaround
Reason:      "sim.airside for the movement milestones, sim.turnaround for the
             stand milestones" never enumerated which milestone belonged to
             which, and `11-interfaces-schedule.md` §11.1 (merged) already
             committed `sim.airside` to acting on `DoorsOpen`, which only works
             if `sim.airside`, not `sim.turnaround`, emits it. Enumerates all
             nine explicitly: doors stay with `sim.airside` (aircraft envelope),
             `sim.turnaround` keeps only `DeboardComplete`, `ReadyToBoard`,
             `BoardingComplete` (ground-service progress).
Raised by:   Q-005, while writing `12-interfaces-airside.md` §12.3
Impact:      Corrects an ambiguity, not a stated contradiction — no merged code
             depended on the old wording since none exists yet. Binding on
             `sim.turnaround`'s eventual interface (Q-006): it does not own
             door milestones.
Signed off:  not required

## 2026-09-21 — spec/10-events.md §10.6 — `AircraftHeldOnTaxiway` field corrected from `EdgeId` to `TaxiEdgeId`
Reason:      The field referenced `EdgeId`, which is already `sim.flow`'s
             landside corridor-edge type (`09-interfaces-flow.md` §9.2) — a
             real type collision, since a taxiway edge and a landside corridor
             edge are different graphs owned by different modules. Renamed to
             `TaxiEdgeId`, defined in `12-interfaces-airside.md` §12.4.
Raised by:   Q-005, while writing `12-interfaces-airside.md`
Impact:      none, additive correction. No code exists yet.
Signed off:  not required

## 2026-09-21 — spec/03-module-map.md, spec/00-overview.md — point `sim.airside` at its interface file
Reason:      Bookkeeping to match the new file, same as Q-004's equivalent
             entries.
Raised by:   Q-005
Impact:      none.
Signed off:  not required

## 2026-09-21 — spec/13-interfaces-turnaround.md — new file: `sim.turnaround` public interfaces
Reason:      T-022 was BLOCKED on Q-006: no published `sim.turnaround`
             interface existed, so the job list, vehicle dispatch and the
             `DeboardComplete`/`ReadyToBoard`/`BoardingComplete` emission rules
             `sim.airside` (`12-interfaces-airside.md` §12.8) already depends
             on would each have been invented by the worker. Defines
             `ITurnaroundSystem` (query-only), a self-owned job catalogue and
             vehicle fleet (§13.4), FIFO vehicle dispatch by ascending
             blocking `EventId` (§13.5), and job creation keyed off
             `sim.airside`'s `OnStand` milestone with the fixed
             `Boarding`-waits-on-five-prerequisite-jobs rule (§13.6).
Raised by:   Q-006 (planner / queue expansion for T-022)
Impact:      none, additive — no `src/` code exists yet. Unblocks T-022 and
             transitively T-024 (via Q-007, still open).
Signed off:  not required
Notes:       `sim.staff` has no published interface either (a separate,
             unopened question); narrowed the same way `12-interfaces-
             airside.md` narrowed around `sim.world` — at Phase 0/1 a vehicle
             is its own crew, "driver assignment" means assigning a vehicle,
             and no calls are made into `sim.staff`. Consumes no RNG, same
             posture as `sim.schedule` and `sim.airside`. One LOW CONFIDENCE
             flag: dispatch is deliberately distance-blind (FIFO by blocking
             order only), even though `06-delay-attribution.md`'s own worked
             example reads as if distance matters — modelling that needs a
             distance metric this module has no dependency to compute.

## 2026-09-21 — spec/12-interfaces-airside.md — amends §12.3, §12.7, §12.8, §12.11: two-`FlightId` handoff and rotation-less flights
Reason:      Writing `sim.turnaround`'s interface (Q-006) exposed a real gap in
             the unmerged `sim.airside` spec: `11-interfaces-schedule.md` gives
             an arrival and its linked departure separate `FlightId`s, so the
             nine airside/turnaround milestones split across two tracks, not
             one — and nothing said which `FlightId` got which milestone, how
             the stand handed off between the two tracks, or what happens to a
             flight with no rotation counterpart (which the Phase 0 schedule
             fixture, `11-interfaces-schedule.md` §11.10, requires at least
             one of). §12.3 gets a "which `FlightId` gets which milestone"
             subsection; §12.7 gets a "Rotation-less flights" subsection;
             §12.8 is rewritten as a five-step handshake plus a corrected
             fallback (previously it fired `DoorsClosed` off `DoorsOpen`'s own
             tick as if both were the same `FlightId`, which no longer holds);
             §12.11's consumed-events table adds `DeboardComplete`.
Raised by:   architect, self-identified while writing Q-006
Impact:      none, additive/corrective — `spec/12-interfaces-airside.md` was
             committed on an unmerged branch (`architect/Q-005-airside-
             interface`) and has not gone through Integrator review, so this
             is a same-cycle correction, not a break of merged work. T-021's
             task file (rewritten by the Planner, also unmerged) should be
             re-checked against the corrected §12.8 before that branch merges
             — its consumed-events list is missing `DeboardComplete` and its
             fallback description predates the two-`FlightId` handoff.
Signed off:  not required
Notes:       One new LOW CONFIDENCE flag: a rotation-less departure gets no
             real ground time under the no-`sim.turnaround` fallback, because
             `MinTurnaround` is spent once to place the aircraft and has
             nothing left to cover boarding. Left as a known gap rather than
             guessed shut — the Phase 0/1 fixture only needs such a row to
             exist for `TICK_UNSCHEDULED` coverage, not to complete a
             plausible turnaround.

## 2026-09-21 — spec/10-events.md §10.6 — `TurnaroundJobBlocked`'s category list drops `pushback`
Reason:      `13-interfaces-turnaround.md` §13.4 fixes `PushbackPrep`'s
             category as `ground_handling` always — `pushback` as a delay
             category is reserved for `sim.airside`'s own `Pushback`
             milestone, not this module's prep job, so a catalogue mapping the
             two together fails to load. The events table's category list
             said otherwise.
Raised by:   Q-006, while writing `13-interfaces-turnaround.md` §13.4
Impact:      none, additive correction. No code exists yet.
Signed off:  not required

## 2026-09-21 — spec/03-module-map.md, spec/00-overview.md — point `sim.turnaround` at its interface file
Reason:      Bookkeeping to match the new file, same as Q-004/Q-005's
             equivalent entries.
Raised by:   Q-006
Impact:      none.
Signed off:  not required

## 2026-09-23 — spec/14-interfaces-delay.md — new file: `sim.delay` public interfaces
Reason:      T-024 was BLOCKED on Q-007: `06-delay-attribution.md` and
             `10-events.md` gave the principles and the allocation rules but no
             module interface, no hashed-state layout, and no rule for
             `DelayEventId` against `EventId`. Defines `IDelaySystem`
             (query-only, §14.10), the per-flight record and node types
             (§14.3), the checkpoint milestones the delay clock measures
             (§14.4), four blocking-interval families keyed without relying on
             `Cause` (§14.5), the allocation algorithm in integer ticks
             (§14.6), tree shape, id allocation and depth (§14.7),
             finalisation-time `DelayEvent` publication and two-day retention
             (§14.8), missed passengers as a non-minute record (§14.9),
             hashing, no RNG and budget shape (§14.13), and fixtures and test
             names (§14.14).
Raised by:   Q-007 (planner / queue expansion for T-024)
Impact:      additive for `sim.delay`, which has no code. The consistency
             amendments below touch five other specs; none of their modules
             has code either (no `src/` exists), so **no merged code is
             invalidated**, but task files the Planner already wrote are now
             stale and must be refreshed before release: T-008 (three new
             `FlightPlanPublished` fields), T-021 (the `PlannedTick` table in
             `12` §12.3), T-022 (the `category` field on
             `TurnaroundJobBlocked`/`Unblocked`, `PlannedTick` for
             `ReadyToBoard`/`BoardingComplete`), and T-024 itself. Unblocks
             T-024 once T-022 and T-023 merge.
Signed off:  not required, but see the LOW CONFIDENCE and HUMAN DECISION
             entries below
Notes:       Deliberately narrow. Trees are two levels deep (flight total and
             allocation leaves) plus a cross-tree `late_inbound` link; the
             envelope `Cause` chain is **not** followed at Phase 1 (§14.7,
             "Cause chains"), because resolving an `EventRef` from an earlier
             tick needs a retained index of past events: new state, new
             budget, new retention problem. Every Phase 1 explanation is
             already in the opening event's own fields. `sim.delay` reads no
             content and follows no references to other modules, so
             `delay_module_never_writes` can be a plain assembly-reference
             check. `DelayEventId` is a module counter, not an `EventId` and
             not from `IIdAllocator`; it is monotone in dispatch order and
             every `Parent`/link points to a lower id, so `no_cycles` holds by
             construction. Explanations are `(DelaySource, A, B)` integers;
             `app.ui` derives the `LocalisedKey`, so no text enters the hash.
             Two things noticed and **not** changed, for the next consistency
             pass: (1) `12-interfaces-airside.md` §12.8 pushes back as soon as
             `BoardingComplete` arrives, with no wait for STD, so a departure
             can leave early; `sim.delay` clamps early to lateness 0, but a
             player may find early pushbacks odd. (2) Every event type, and
             every type an event field carries (`RunwayId`, `JobKind`,
             `DelayCategory`, ...), must be a `sim.core` type, because events
             are defined in `sim.core` (`03-module-map.md`) and
             `delay_module_never_writes` forbids `sim.delay` from referencing
             another module's assembly. T-021/T-022 may write only their own
             directories, so who authors those `sim.core` payload types is a
             Planner scheduling question, not a spec one.

## 2026-09-23 — spec/14-interfaces-delay.md §14.4, §14.6 — LOW CONFIDENCE: checkpoints, cap, recovery order, inbound cap
Reason:      `10-events.md` §10.5 did not say (a) which milestones to measure,
             (b) what happens when blocking intervals cover more than the gap,
             (c) what happens when a flight makes up time, so that the tree
             total falls, or (d) how much of a late handover to charge to the
             inbound. Each needed exactly one deterministic rule. Chose:
             (a) arrival `Landed`/`OnStand`, departure
             `OnStand`/`Pushback`/`Airborne`; (b) intervals take their owned
             ticks in ascending opener `EventId` until the gap is used up;
             (c) the most recently created leaf gives up ticks first, so the
             earliest cause keeps the blame; (d) `late_inbound` never exceeds
             the inbound's own delay, and the rest is `propagated`.
Raised by:   Q-007
Impact:      Affects tree *contents*, not interfaces. (b) and (c) extend the
             already-flagged first-blocker-wins rule. Alternatives
             (proportional trimming, oldest-first forgiveness) are equally
             deterministic and could be swapped in by amendment without a
             schema change, though any swap moves golden hashes.
Signed off:  not required; the owner should review it alongside the existing
             first-blocker-wins flag once a tree is visible in a build

## 2026-09-23 — spec/14-interfaces-delay.md §14.2, §14.8 — LOW CONFIDENCE: `DELAY_RETENTION_DAYS = 2`
Reason:      `06-delay-attribution.md` rule 4 requires yesterday's tree to be
             openable; keeping every tree forever grows the hashed and saved
             state without bound (on the order of a million nodes per season
             at max tier) and would eventually break the 20 ms checkpoint
             ceiling. Two days is the narrowest window satisfying rule 4.
             Rotation pairs are pruned together so no `late_inbound` link
             dangles.
Raised by:   Q-007
Impact:      none today. How far back a player can look is player-facing;
             season-long history is expected to come from compact per-day
             aggregates, which are unscoped.
Signed off:  not required; flagged for the owner

## 2026-09-23 — spec/14-interfaces-delay.md §14.9 — HUMAN DECISION: flights never wait for late passengers at Phase 1
Reason:      Writing the delay interface showed that, as specified, a Phase 1
             flight never waits for a passenger: `Boarding` has a fixed
             duration (`13` §13.6) and `DoorsClosed` absorbs whoever is at the
             gate (`12` §12.7). So `security_queue` and `passenger_late` can
             never appear as delay *minutes*. The one live player lever in
             Phase 1 (T-023, opening security lanes) then shows up only as a
             missed-passenger count, which `sim.delay` records on the flight
             (§14.9) as the narrowest honest thing it can do without changing
             another module's behaviour.
Raised by:   Q-007
Impact:      Not decided here. If flights should hold for late passengers,
             that is a `sim.turnaround`/`sim.airside` behaviour amendment, and
             `sim.delay` then attributes the wait as an ordinary blocking
             interval with no interface change. It bears directly on the
             Phase 1 gate question (T-025: "is unblocking flow fun?").
Signed off:  **PENDING HUMAN** — gameplay decision. Does not block T-024.

## 2026-09-23 — spec/06-delay-attribution.md — `DelayEvent` contract made consistent with `10-events.md` and a multi-branch tree
Reason:      Three internal contradictions. (1) "Every module that can cause
             delay emits `DelayEvent`" contradicted `10-events.md` §10.1/§10.7,
             where modules emit milestones and intervals and only `sim.delay`
             emits `DelayEvent`. (2) `parent: DelayEventId?` with "null means
             root cause" cannot describe a flight with several causes: a
             flight-total node would need several parents. `parent` now points
             to the node a leaf is part of, and `root_cause` is a separate
             flag. (3) Minutes in `Fx` cannot sum exactly (one tick is 0.1
             minute, not representable in Q31.32); `ticks: uint64` is added
             as the authoritative duration and `minutes` becomes derived.
             Also adds `id`, `root_cause`, `linked`, a typed explanation, and
             states that depth counts `late_inbound` links.
Raised by:   Q-007
Impact:      none, no code. Rule 1's intent (one parent per minute, leaves sum
             to total) is unchanged and now checkable exactly.
Signed off:  not required

## 2026-09-23 — spec/10-events.md §10.3, §10.4, §10.5, §10.6, §10.7 — amendments required by `sim.delay`
Reason:      §10.3 rule 2 said an interval still open at end of day must throw,
             while `13-interfaces-turnaround.md` §13.4 says a job with no
             available vehicle legitimately stays blocked forever. Now: throw
             only when an interval is still open as its subject leaves the sim;
             crossing a day boundary is allowed. §10.4 now states that
             `PlannedTick` is schedule-anchored and cumulative, the only
             reading under which "minus any gap already attributed at n−1"
             in §10.5 is well-defined. §10.5 rule 5 now allocates in integer
             ticks (no remainder to settle) and points to `14` §14.4–§14.6 for
             the operational form. §10.6: `FlightPlanPublished` gains
             `kind`, `rotation`, `hasRotation` (`11` §11.7 already claimed
             `sim.delay` "gets the link from `FlightPlanPublished`", but the
             event had no such field); `TurnaroundJobBlocked`/`Unblocked` gain
             `DelayCategory category`, because the category lives in
             `sim.turnaround`'s own catalogue, which `sim.delay` may not read.
             §10.7 states that `DelayEvent` is published at finalisation only.
Raised by:   Q-007
Impact:      none on code (none exists). T-008 and T-022 task files are stale
             on the new fields; the Planner must refresh them before release.
Signed off:  not required

## 2026-09-23 — spec/11-interfaces-schedule.md §11.5, §11.7 — populate the new `FlightPlanPublished` fields
Reason:      Emitter side of the `10-events.md` §10.6 field addition.
Raised by:   Q-007
Impact:      T-008 (QUEUED, no code) publishes three more fields, copied from
             `FlightRecord`. No behaviour change.
Signed off:  not required

## 2026-09-23 — spec/12-interfaces-airside.md §12.3 — `PlannedTick` defined for every airside milestone
Reason:      Only `InboundAirborne` and the departure's `OnStand` had a defined
             `PlannedTick`; every other airside milestone would have been
             invented by the T-021 worker, and `sim.delay`'s lateness depends
             on it. Adds a table, schedule-anchored per `10-events.md` §10.4:
             `Landed` at STA, arrival `OnStand` at STA plus runway occupancy
             plus unimpeded taxi time, `DoorsClosed`/`Pushback` at STD,
             `TakeoffRoll`/`Airborne` at STD plus unimpeded taxi-out and
             occupancy.
Raised by:   Q-007
Impact:      T-021 (QUEUED, no code) must emit these values; its task file
             should cite §12.3's new table. The table uses `OccupancyTicks`
             for `Airborne`, following §12.6; §12.3's older "fixed content
             delay after `TakeoffRoll`" trigger wording is left as is and is
             read as the same thing.
Signed off:  not required

## 2026-09-23 — spec/13-interfaces-turnaround.md §13.6, §13.9 — `PlannedTick` for `ReadyToBoard`/`BoardingComplete`, `category` on job events
Reason:      Same gap as `12` §12.3 for the two departure-side turnaround
             milestones, and the emitter side of the new `category` field.
Raised by:   Q-007
Impact:      T-022 (QUEUED, no code) task file is stale on both.
             `DeboardComplete`'s existing `PlannedTick` (STA plus deboard time)
             is left unchanged: `sim.delay` does not measure it.
Signed off:  not required

## 2026-09-23 — spec/03-module-map.md, spec/00-overview.md — point `sim.delay` at its interface file
Reason:      Bookkeeping to match the new file, same as the Q-004 to Q-006
             entries.
Raised by:   Q-007
Impact:      none.
Signed off:  not required
