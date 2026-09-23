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

## 2026-09-23 — spec/15-interfaces-render.md — new file: `app.render` Phase 1 interfaces (partial answer)
Reason:      T-020 was BLOCKED on Q-008: `app.render` had a module-map row but
             no scope and no interface, and no test could be written for a
             renderer that lives entirely in an engine CI cannot run. Splits
             the module in two. A headless scene layer with no engine
             reference turns read-only sim queries into a stable, sorted draw
             list of flat-colour primitives (§15.5), and also owns the
             promotion controller (§15.7) and a 1x tick pacer with a binding
             frame order (§15.8). A thin backend draws the list and holds no
             logic (§15.10, contract only). The complete list of sim members
             `app.render` may call is §15.6. The presentation-owned layout
             that positions nodes is §15.4. Budget §15.11, T-020 scope and
             test names §15.12.
Raised by:   Q-008 (planner / queue expansion for T-020)
Impact:      additive; no `src/app` code exists. T-020's scope narrows to the
             scene layer (`src/app/render/Scene/**`, `tests/app/render/**`,
             `tests/fixtures/render/**`). Its dependencies grow from T-009 to
             T-009, T-010 and T-021, because it compiles against
             `SetPromoted`/`AgentsAt` and `IAirsideSystem.Layout()`. The
             Planner must rewrite T-020 accordingly. The backend needs its
             own task, which cannot be queued until the HUMAN DECISIONS below
             are made.
Signed off:  not required for the headless scope; see the HUMAN DECISION
             entry below
Notes:       Deliberately narrow. The renderer reads nothing from
             `sim.turnaround`, `sim.delay` or `sim.schedule`. It draws no
             text, vehicles, corridors or delay state, issues no commands,
             and does not interpolate between ticks. Promotion neutrality is
             kept by construction on the sim side (`09` §9.1) and by three
             constraints here: `SetPromoted` is the only call that changes
             anything, it is made only between `Step`s, and nothing read is
             fed back. It is proven by
             `test_render_loop_is_outcome_neutral_with_scripted_camera`,
             which runs the module's real call pattern against a headless
             run and compares checkpoints.

## 2026-09-23 — spec/15-interfaces-render.md §15.2, §15.4, §15.11 — LOW CONFIDENCE: zoom threshold, split layout, frame budget
Reason:      Three values with no measurement or precedent behind them.
             `AGENT_ZOOM_THRESHOLD = 120` world units of view height.
             Positions kept in a presentation-owned layout, separate from the
             airside graph they position, so that presentation-only data stays
             out of sim state and its hash; the cost is that two files must
             agree on ids. Scene build plus promotion update limited to a
             2.0 ms mean and 4.0 ms p99 per frame.
Raised by:   Q-008
Impact:      None of these can move a sim outcome. Each is cheap to retune
             after the first measurement or the first playable build.
Signed off:  not required; flagged for the owner

## 2026-09-23 — spec/15-interfaces-render.md §15.13 — HUMAN DECISIONS left open by Q-008
Reason:      Five things `app.render` needs, beyond T-020's headless scope,
             that the Architect must not decide:
             (a) **Unity 6 against a .NET 8 sim library.** The Architect
             believes, and this should be verified, that Unity 6's scripting
             runtime loads assemblies built for .NET Standard 2.1 / .NET
             Framework APIs, not `net8.0`. If so, `01-architecture.md`
             (locked) cannot hold as written: ".NET 8" and "Unity 6 importing
             the sim library as a compiled assembly" conflict. One possible
             reading is to multi-target the sim, but that limits the
             language and BCL features every sim worker may use and changes a
             locked file.
             (b) **The engine project shell.** No module owns the Unity
             project (location, scenes, settings, build). `app.ui` will need
             it too.
             (c) **The composition root.** No spec says how a running sim is
             built and handed to presentation. It becomes specifiable once
             (b) is settled.
             (d) **Game speeds** beyond 1x and pause are pacing.
             (e) **No player-facing `SetServersOpen`.** T-023 implements the
             command, but no Phase 1 task lets the player issue it, and
             T-025's question ("is unblocking flow fun?") needs that lever in
             the player's hands.
Raised by:   Q-008
Impact:      None blocks T-020's headless scope. Together they block the
             engine backend and so any playable build: T-025 cannot happen
             without them. (a) also bears on T-001, which creates the solution
             and chooses its target framework; if the answer is
             multi-targeting, it is cheapest before T-001 merges. (e) is
             scope and planning, raised here because writing `app.render`'s
             "issues no commands" rule exposed it.
Signed off:  **PENDING HUMAN** on all five; (a) requires a recorded sign-off
             because it touches `01-architecture.md`

## 2026-09-23 — spec/12-interfaces-airside.md §12.9 — `Layout()` query; `AtNode` defined while `OnEdge` is set
Reason:      The renderer needs the validated layout, and must not load the
             airside fixture a second time on its own. It also needs the
             direction of travel on a `Bidirectional` edge, which
             `AircraftTrack` did not give: `EdgeProgress` had no stated origin.
             `AtNode` now holds the edge-entry node while `OnEdge` is set, and
             `EdgeProgress` runs from it.
Raised by:   Q-008
Impact:      T-021 (QUEUED, no code) exposes one more query and keeps `AtNode`
             set during edge traversal instead of clearing it. `AtNode` is a
             hashed field, but no golden exists yet. The task file is stale on
             both points.
Signed off:  not required

## 2026-09-23 — spec/03-module-map.md, spec/00-overview.md, spec/08-interfaces-core.md §8.1 — point `app.render` at its interface file
Reason:      Bookkeeping to match the new file. §8.1 now names where
             `AGENT_ZOOM_THRESHOLD` is defined.
Raised by:   Q-008
Impact:      none.
Signed off:  not required

---

## Owner decisions of 2026-09-23 (D1–D9)

On 2026-09-23 the human owner delegated the open HUMAN DECISIONs to the main
session, which decided them as D1–D9. The entries below write those
decisions into the spec. Each one is signed off as
`HUMAN DECISION — owner (delegated), 2026-09-23`, and each is **reversible**
by the owner. The sign-off covers the changes each decision names and
nothing more. In particular it covers exactly one change to
`01-architecture.md` (D1, the runtime row) and **no** change to
`02-determinism.md`.

## 2026-09-23 — spec/open-questions.md — D9: Q-001 placeholder deleted
Reason:      Q-001 was the scaffold's example question, and its own title said
             to delete it once a real question landed. Q-002 to Q-008 are
             real questions.
Raised by:   D9
Impact:      none. Nothing cited Q-001.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D9); reversible

## 2026-09-23 — spec/08-interfaces-core.md §8.1, §8.2 — D2: `SIM_SECONDS_PER_TICK = 6` confirmed (Q-002)
Reason:      The value was provisional, pending the owner, because it is a
             pacing decision. Confirmed at 6: 14 400 ticks per sim-day, and
             24 real minutes per day at 1x. At 1 s per tick, T-009's
             100-days-in-60-s gate would be 8.6 M ticks and infeasible; a
             larger value coarsens delay resolution. The LOW CONFIDENCE marker
             becomes a HUMAN DECISION marker.
Raised by:   Q-002, D2
Impact:      none on interfaces or code (none exists). Golden hashes may now be
             authored. T-001 and T-009 carry worker notes saying the value is
             provisional and forbidding goldens, so those notes are stale and
             the Planner must refresh them.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D2); reversible,
             at the cost of every golden and every tick-valued fixture

## 2026-09-23 — spec/03-module-map.md — D3: the soak fixture is mid-tier, under 0.1 ms per tick (Q-003)
Reason:      At max-tier cost, 500 sim-days take about 12 hours, not "minutes"
             (`01-architecture.md`). A gate that slow would get disabled, which
             `02-determinism.md` forbids. The Architect's proposal is accepted
             and written as a new subsection, "The soak fixture": whole-sim
             mean under 0.1 ms per tick (about 12 minutes for 7.2 M ticks),
             every built system registered, a `repeat_daily` schedule, and a
             fixture that is shrunk rather than a gate that is weakened when a
             module's cost grows. Max-tier performance stays with the budget
             tests.
Raised by:   Q-003, D3
Impact:      additive. It narrows what the nightly gate proves, which is why
             it needed sign-off. `02-determinism.md` is **not** edited: its
             `soak_500_days` row names no fixture size, so the new subsection
             adds detail without contradicting it. No task yet authors the soak
             fixture or its golden. The Planner should queue one once T-009
             lands its first goldens.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D3); reversible

## 2026-09-23 — spec/14-interfaces-delay.md §14.2, §14.4, §14.6; spec/15-interfaces-render.md §15.2, §15.4, §15.11 — D8: Q-007/Q-008 LOW CONFIDENCE items accepted as provisional
Reason:      The owner accepted every LOW CONFIDENCE call from Q-007 and Q-008
             as provisional: the checkpoint set (§14.4), the cap and recovery
             allocation order (§14.6), the 2-day retention (§14.2), the zoom
             threshold of 120 (§15.2), the split render layout (§15.4) and the
             2 ms scene budget (§15.11). Each marker gains one acceptance line.
             **The markers stay.** They are to be revisited after the T-025
             playtest.
Raised by:   D8
Impact:      none. No value or rule changes. The older LOW CONFIDENCE items
             from Phase 0 and Q-004 to Q-006 are not covered by D8 and remain
             open for the owner's review as before.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D8); reversible

## 2026-09-23 — spec/01-architecture.md (LOCKED) "Platform decisions", Simulation row — D1: the sim targets netstandard2.1 only
Reason:      The row said ".NET 8". The Presentation row said Unity 6 LTS
             imports the sim "as a compiled assembly". As of September 2026,
             Unity 6 LTS runs Mono, exposes only .NET Standard 2.1 and C# 9,
             and cannot load `net8.0` assemblies, so the two rows could not
             both hold (Q-008 §15.13(a)). Unity's guidance is that precompiled
             plugins target `netstandard2.1`, and that stays supported after
             CoreCLR (.NET 10, still experimental) arrives. New row: sim
             assemblies target **`netstandard2.1` only** with `LangVersion 9`,
             and so does every headless `app.*` layer Unity consumes
             (`15` §15.3, and the new `16` and `17`). Tests and
             `tools.simharness` target `net8.0` and consume the sim
             unchanged. Single-targeting is deliberate: one compiled sim and no
             BCL-divergence risk between two builds of it. "Zero engine
             references" is unchanged. The locked file changes in that one
             table cell only.
Raised by:   Q-008 §15.13(a), D1
Impact:      no merged code (no `src/` exists). T-001 creates `AirportSim.sln`
             and must set these targets, so its task file is stale. Every sim
             worker loses .NET 8-only APIs; T-003 is the one visibly affected
             (see the next entry). Revisit trigger: Unity ships production
             CoreCLR (.NET 10).
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D1); reversible.
             This is the recorded sign-off `01-architecture.md` requires for a
             change to a locked platform decision.

## 2026-09-23 — spec/08-interfaces-core.md §8.3 — D1: `Fx` hand-rolls its 128-bit arithmetic and leading-zero count
Reason:      `netstandard2.1` has no `Int128`, no `Math.BigMul(long, long,
             out long)` and no `BitOperations`. §8.3 already required a 128-bit
             intermediate for `Mul`/`Div`. It now says that the intermediate,
             the 128-by-64 division and any leading-zero count are written in
             plain integer code inside `Fx`, and that `BigInteger` is not used.
             Otherwise a worker would reach for .NET 8 APIs that do not
             compile. It also says `Fx` checks overflow cases explicitly
             instead of relying on which BCL exception a runtime throws.
             Tests (`net8.0`) may use `Int128`/`BigInteger` as an oracle.
Raised by:   D1
Impact:      T-003 (QUEUED, no code). Its task file should cite the new
             bullet.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D1); reversible

## 2026-09-23 — spec/07-conventions.md "Runtime portability" — D1: audit of the determinism rules for Mono vs CoreCLR BCL differences
Reason:      D1 ships the sim on Mono and tests it on CoreCLR, so the
             determinism rules must rule out every BCL behaviour that differs
             between the two. Audit of `02-determinism.md` and
             `07-conventions.md` as they stood:
             - **Dictionary/HashSet enumeration order: covered.** `02` rule 5
               forbids iterating a hash map or dictionary where order affects
               outcomes. HashSet counts as a hash map in substance, though it
               is not named.
             - **`string.GetHashCode`: not covered.** `02` rule 7 forbids
               branching on a *default* object hash code, and `02` rule 2
               forbids hash-based pseudo-randomness on unordered data. Neither
               reaches a string's (overridden, CoreCLR-randomised) hash used
               as a seed, id, bucket or sort key. Nor does either reach
               `System.HashCode`.
             - **Unstable sort order: not covered.** `02` rule 5's "sort by a
               stable key" names a key, not a stable algorithm. It says
               nothing about ties, and `Array.Sort`/`List.Sort` leave tied
               items in runtime-specific order.
             - Found in addition, **not covered:** culture-sensitive string
               comparison and number parsing, reflection member order, and
               struct memory punning.
             **`02-determinism.md` is not edited** (it is locked, and these are
             not strictly the retarget). The gaps are closed in `07`, which
             the Architect owns. A new "Runtime portability" section adds seven
             binding rules: hash-collection order, no hash code reaching
             behaviour, total sort comparers, ordinal and invariant strings, no
             reflection order, no memory punning, and the netstandard2.1/C# 9
             API surface with no NuGet polyfills. The owner may want to lift
             rules 2 and 3 into `02` itself at some point. That needs a
             separate sign-off.
             `02-determinism.md` was also checked for assumptions that only
             hold on .NET 8. **None found.** Nothing in it names a .NET 8 API
             or runtime. Its one retarget-shaped gap is that no gate runs the
             shipped runtime; see the cross-runtime gate entry below (written
             with D7).
Raised by:   D1
Impact:      constraining. No code exists. Every sim task brief should cite
             the new section, and T-001 (solution and target setup), T-003,
             T-007 and T-008 (the first sorts, string parsing and hashing) are
             the most exposed.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D1) for the
             retarget. The `07` rules themselves are Architect-owned and need
             no sign-off.

## 2026-09-23 — spec/15-interfaces-render.md §15.3, §15.13(a) — D1: the scene layer targets netstandard2.1
Reason:      §15.3 deferred the scene layer's target to §15.13(a), which is now
             decided.
Raised by:   D1
Impact:      T-020 (QUEUED) targets `netstandard2.1`, with tests on `net8.0`.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D1); reversible

## 2026-09-23 — spec/16-interfaces-host.md — D7: new module `app.host`, the composition root and Unity project shell
Reason:      No module owned the Unity project (§15.13(b)), and no spec said
             how a running sim reaches presentation (§15.13(c)). The owner
             created `app.host`. The new file specifies:
             - the module's scope (§16.1);
             - the headless/Unity split and the binding Unity settings (§16.2):
               pinned Unity 6 LTS, **Mono** scripting backend (D1's "shipped
               runtime"), .NET Standard 2.1 API level, the CI-tested
               assemblies consumed as plugins and never recompiled by Unity,
               backends included by reference, and one scene;
             - the scenario bundle, read by exact file name only, with its
               `systems` list and the Phase 1 playtest bundle (§16.3);
             - `ComposedSim`/`ISimComposer`, whose composition is a pure
               function of the bundle's bytes (§16.4);
             - presentation assembly (§16.5);
             - the frame loop (§16.6), the thin bootstrap contract (§16.7),
               and a headless checkpoint run with a byte-exact text dump
               format and a matching `tools.simharness checkpoints`
               subcommand (§16.8). That subcommand is what D7's "same
               checkpoint hashes as the harness" test compares.
             The frame loop takes over the binding frame order from
             `15` §15.8. Once D5 adds a UI step, a frame spans two
             presentation modules, and `app.render` may not reference
             `app.ui` (`15` §15.3). Putting the order in headless code also
             makes it testable, where the engine runner was not.
Raised by:   Q-008 §15.13(b), (c); D7
Impact:      additive. No `src/app` code exists. New work for the Planner: a
             headless-host task (`src/app/host/**`, `tests/app/host/**`) and a
             Unity-project task (`unity/AirportSim/**`), both needed for
             T-025, not for T-020. `tools.simharness` (T-006/T-009) gains a
             `checkpoints` subcommand, and its existing `ci/`-invoked
             subcommands are unchanged. **Stopped short of improvising:** the
             construction entry point of every module is unpublished, so
             §16.4/§16.5 are specified by their inputs and outputs only, and
             Q-009 is raised. The composition task cannot finish until Q-009
             is answered.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D7) for the
             module and its ownership; reversible. The interface detail is the
             Architect's.

## 2026-09-23 — spec/15-interfaces-render.md §15.1, §15.3, §15.6, §15.8, §15.9, §15.10, §15.12, §15.13(b)(c) — D7: frame order moves to `app.host`; the backend calls no sim member
Reason:      Follows from the entry above. The render backend no longer runs
             the frame order or calls `Step`. It supplies the camera and draws
             what it is handed. §15.8 keeps `app.render`'s part of the order
             (promotion before `Step`, build after). T-020's
             outcome-neutrality test now drives the §16.6 order itself, with no
             dependency on `app.host`. §15.13(b) and (c) are marked decided.
Raised by:   D7
Impact:      T-020 (QUEUED, no code): its task file quotes the old §15.8 frame
             order and the "backend runner calls `Step`" rule, so it is stale.
             The scene layer's own interfaces are unchanged by D7.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D7); reversible

## 2026-09-23 — spec/16-interfaces-host.md §16.9 — D1: cross-runtime determinism gate, PROPOSED — LOW CONFIDENCE
Reason:      D1 asked for a gate that runs the sim under Mono as well as
             CoreCLR and compares checkpoint hashes. **It does need a spec
             section**, for three reasons. The Mono side can only run as a
             Unity player, through `app.host`'s batch mode (§16.7, §16.8). Two
             implementations must emit one dump format byte for byte (§16.8).
             And the fixture and pass condition must be fixed. Proposed as
             `determinism_cross_runtime`: `tools.simharness checkpoints` on
             CoreCLR versus the Mono Unity player in `-batchmode -nographics`,
             on the Phase 1 playtest bundle, for 10 sim-days, passing on
             byte-identical dumps. It must be the real player, because Unity
             ships its own Mono fork and class libraries.
Raised by:   D1
Impact:      none until adopted. **Escalated:** making it a gate means adding
             a row to `02-determinism.md`'s gate table (locked, and not
             strictly the retarget) and a step to `ci/` (human-only). The
             Architect does neither. Proposed cadence: nightly, plus on any
             change of Unity version, target framework or plugin set, because
             the player build needs the Unity editor and a licence on the
             agent, which `01-architecture.md` keeps off per-merge gates. The
             Planner can task the runnable pieces now (harness subcommand, dump
             writer, headless run, batch mode).
Signed off:  **PENDING HUMAN** for adoption. LOW CONFIDENCE on nightly rather
             than per-merge, and on the real player rather than a standalone
             Mono.

## 2026-09-23 — spec/03-module-map.md, spec/00-overview.md — `app.host` row and interface pointer
Reason:      Bookkeeping for the new module. `app.host` depends on sim, render
             and ui. It is the top of the graph, so every call it makes is
             downward.
Raised by:   D7
Impact:      none.
Signed off:  not required

## 2026-09-23 — spec/open-questions.md — Q-009 raised: module construction entry points
Reason:      The D7 composition root cannot be written without constructing
             every module, and no spec publishes a constructor or factory for
             any of them. That includes `ISimHost` and the content index. The
             Architect stopped rather than invent six factories under a
             decision that did not cover them. A proposed answer is attached.
Raised by:   architect, while writing D7
Impact:      blocks the `app.host` composition task. The same gap is latent in
             T-009 and in every multi-system integration test. The Planner
             should check whether T-009's brief lets the harness reach module
             internals.
Signed off:  not required (Architect to answer)
