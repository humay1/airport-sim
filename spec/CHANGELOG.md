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

## 2026-09-23 — spec/15-interfaces-render.md §15.1, §15.8, §15.12, §15.13(d) — D4: game speeds pause, 1x, 2x, 4x
Reason:      The pacer was 1x-and-pause only, pending a pacing decision. The
             owner chose pause, 1x, 2x and 4x (a 4x day is 6 real minutes),
             with higher speeds deferred until T-011's budget results exist.
             `ITickPacer.Advance` gains a `GameSpeed` parameter. The
             accumulator counts speed-scaled microseconds, so a speed change
             loses and duplicates nothing. The catch-up cap stays at 3 ticks
             per frame at every speed. That bounds one frame's sim work to
             18 ms at max tier, and at 4x the cap binds only below about
             13 fps. Two pacer tests are added.
Raised by:   Q-008 §15.13(d), D4
Impact:      **interface change** to `ITickPacer` (T-020, QUEUED, no code);
             T-020's task file quotes "1x only, no speed parameter" and is
             stale. No sim change: game speed is presentation-side
             (`08` §8.2).
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D4); reversible

## 2026-09-23 — spec/17-interfaces-ui.md — D5: new file, minimal Phase 1 `app.ui`
Reason:      No Phase 1 task gave the player `SetServersOpen`, and T-025 asks
             whether unblocking flow is fun (§15.13(e)). The owner chose a
             minimal `app.ui` built as a headless input/command layer plus a
             thin backend, split the same way as `app.render`. The new file
             specifies:
             - semantic input in screen coordinates and screen-to-world
               mapping (§17.3);
             - pacing state, starting unpaused at 1x (§17.4);
             - the lane click: an inclusive-edge hit test on the render
               layout's flow-node boxes, topmost (highest `NodeId`) winning,
               with primary = one more server and secondary = one fewer,
               handed to an `ILaneCommandSink` (§17.5);
             - the complete list of sim members used (§17.6), the types
               (§17.7), an icon-only backend contract with no text (§17.8),
               and tests, including a pause/speed outcome-neutrality
               integration test (§17.10).
             `16` §16.5 to §16.7 gain the UI step: it runs first in the frame
             loop, so a pause pressed this frame stops this frame's `Step`.
Raised by:   Q-008 §15.13(e), D5
Impact:      additive; no `src/app` code. New work for the Planner: a UI
             scene-layer task (`src/app/ui/Scene/**`, `tests/app/ui/**`),
             after T-020, and later a UI backend task. **Stopped short of
             improvising:** turning a lane request into a `Command` needs a
             payload layout, a `PlayerId`, a `CommandKind` value, a dispatch
             contract and a lane-state read, none of which is published. The
             production sink is therefore marked do-not-write, and Q-010 is
             raised. The +1/−1 click grammar is the Architect's reading of
             "clicking a lane enqueues `SetServersOpen`". It is a UI detail,
             not a balance value, and is flagged here in case the owner wants
             another grammar.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D5) for the
             scope; reversible. The interface detail is the Architect's.

## 2026-09-23 — spec/03-module-map.md, spec/00-overview.md, spec/15-interfaces-render.md intro, spec/open-questions.md Q-008 — D5 bookkeeping; Q-008 answered
Reason:      Points `app.ui` at its interface file. With D1, D4, D5 and D7 all
             written, every §15.13 HUMAN DECISION is resolved, so Q-008 moves
             from PARTIALLY ANSWERED to ANSWERED and names where each
             decision lives. What still blocks a playable build is Q-009 and
             Q-010, which are Architect questions rather than human ones, and
             tasks not yet queued.
Raised by:   D1, D4, D5, D7
Impact:      `tasks/queue.md`'s T-025 row and its "Unity backend is not
             taskable" note are stale. The Planner can now queue the backend,
             host and UI tasks, with their Q-009/Q-010 dependencies.
Signed off:  not required (bookkeeping for signed-off decisions)

## 2026-09-23 — spec/open-questions.md — Q-010 raised: `SetServersOpen` command plumbing and lane state
Reason:      The D5 lane click cannot become a command without five
             unpublished pieces: the payload layout, `PlayerId`,
             `CommandKind.SetServersOpen` and who authors it, sim.core to
             sim.flow command dispatch with admission validation, and a read
             of `ServersOpen`/`ServerCount`/node kind. A proposed answer is
             attached. Item (5) widens `IFlowSystem`, which D5 did not
             authorise, so it waits for the owner.
Raised by:   architect, while writing D5
Impact:      blocks `app.ui`'s production lane sink. Items (1) to (4) are
             **pre-existing** gaps that T-023 and T-005 would otherwise fill
             by guessing. The Planner should hold T-023's command-handler
             work until they are answered.
Signed off:  not required for (1)–(4); **PENDING HUMAN** for (5)

## 2026-09-23 — spec/12-interfaces-airside.md §12.1, §12.4, §12.8, §12.9, §12.11–§12.13; spec/10-events.md §10.6; spec/14-interfaces-delay.md §14.3, §14.5, §14.9, §14.12, §14.14 — D6: bounded boarding hold for late passengers
Reason:      As specified, no Phase 1 flight ever waited for a passenger, so a
             security queue could never reach the delay tree (§14.9). That
             would have cut the core feedback loop out of the T-025 playtest.
             The owner chose a bounded hold:
             - **`sim.airside`** gains a boarding hold at the departure's
               *doors-close point*, in both the handshake and the fallback
               path. If passengers are outstanding it emits
               `DepartureHeldForPassengers`. It waits until everyone has
               reached a gate or until `BoardingHoldMaxMinutes` has passed,
               then emits `...Released` and closes as before. The remainder
               becomes missed passengers through the existing
               `PassengersMissedFlight`. `AircraftTrack` gains
               `PassengerHoldSince`. `AirsideRules.BoardingHoldMaxMinutes` is
               construction data, never a constant.
             - **`10-events.md`** gains the event pair (category
               `passenger_late`).
             - **`sim.delay`** gains a fifth interval family and
               `DelaySource.PassengerHold`, appended so no ordinal moves. The
               leaf's explanation carries the node holding the most late
               passengers and the count at opening. §14.6 is unchanged: the
               hold sits between the `OnStand` and `Pushback` checkpoints and
               is allocated at `Pushback`.
             Blame node (D6 left it to the Architect): the node holding the
             most of the flight's outstanding passengers at the hold's
             opening, with ties broken by ascending `NodeId`. It is in line
             with `06`'s example, where a "passengers cleared security late"
             leaf has the security queue as its cause. The queue's own
             category is reached later through `Cause` chains, which are not
             followed at Phase 1.
Raised by:   Q-007 §14.9, D6
Impact:      no merged code. **Affected interfaces:** `IFlowSystem` (§9.7,
             §9.7a, new query), `IAirsideSystem` (behaviour, `AircraftTrack`
             field, `AirsideRules` construction input, dependency on
             `sim.flow`), the event catalogue (`10` §10.6, new pair),
             `IDelaySystem` (§14.3 `DelaySource`, §14.5 family, §14.12). The
             hashed state of `sim.airside` (a new track field) and of
             `sim.delay` (a new family ordinal) grows, but no golden exists
             yet. `ITurnaroundSystem` is **unchanged**: `Boarding` stays a
             fixed-duration job, and the hold is `sim.airside`'s.
             **Affected tasks:** T-021 (the hold's behaviour, field, rules
             input and tests, or a follow-on airside task, since the hold
             needs T-023's flow query); T-023 or a new `sim.flow` task
             (`TryGetOutstanding`); T-024 (the new family and the test);
             T-026 (the new event types and the `DelaySource` value in
             `sim.core`). A content task writes `data/schemas/balance.schema.json`.
             The human owner writes `data/balance/airside_rules.json` with the
             value 10. T-020 and T-022 are unaffected.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D6); reversible.
             `BoardingHoldMaxMinutes = 10` is a balance value, marked for
             tuning after T-025.

## 2026-09-23 — spec/09-interfaces-flow.md §9.7, §9.7a, §9.10 — D6: `IFlowSystem.TryGetOutstanding` — LOW CONFIDENCE
Reason:      D6 said: if `sim.flow` cannot attribute passengers to a flight,
             spec the smallest addition. **Finding:** it can attribute them.
             Every cohort carries `CohortKey.Flight`, and `PopulationForFlight`
             counts a flight's passengers. What it cannot say through the
             published interface is how many of them are still upstream of a
             gate, or where. `IFlowSystem` has no node enumeration, so a
             caller cannot even scan `CohortsAt` for them. The smallest
             addition is one read-only query returning the count of the
             flight's `Departing` passengers on non-`Gate` nodes, plus the
             node holding most of them. It adds no state, no event and no
             identity.
Raised by:   D6
Impact:      additive. `sim.flow` has no code. The query is served from the
             per-flight index that §9.10 already anticipates, in O(the
             flight's cohorts), with no allocation.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D6) covers
             "spec the smallest addition". LOW CONFIDENCE on the query's shape
             and on the "most passengers" blame rule.

## 2026-09-23 — spec/03-module-map.md — `sim.airside` depends on `core, world, schedule, flow`
Reason:      A consistency fix exposed by D6. `09-interfaces-flow.md` §9.7
             already named `sim.airside` as a caller of `Inject`/`Absorb`, and
             `12` §12.7 calls `Absorb`, but the map's dependency column omitted
             `flow`. This is the same kind of fix as the Q-004 entry for
             `sim.schedule`. `sim.flow` depends only on core and world, so
             airside to flow is downward and no cycle forms.
Raised by:   D6
Impact:      none. It corrects the map to match the interfaces already
             written.
Signed off:  not required

## 2026-09-23 — spec/04-data-schemas.md, spec/16-interfaces-host.md §16.3 — D6: the first balance file
Reason:      D6 put `BOARDING_HOLD_MAX` in data rather than in a constant. It
             is `data/balance/airside_rules.json` (human-only path), validated
             by `balance.schema.json`. The existing validator maps schemas to
             directories by name, so `data/balance/` gets a single schema,
             extended by amendment as balance files are added. The playtest
             bundle includes the file.
Raised by:   D6
Impact:      additive. No agent may write the balance file. A content task
             writes its schema.
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23 (D6); reversible

## 2026-09-23 — running scope total (as of the D1–D9 block)
Reason:      `agents/architect.md` requires added scope to be tracked here
             and the running total surfaced to the owner. No total had been
             kept yet, so this entry starts one. The baseline is the build
             order and task list as first queued (T-001 to T-025). The count
             covers what amendments have added on top of that baseline, and
             excludes pure narrowing.
Raised by:   architect
Impact:      Scope added by amendment, running total **7**, of which 4 are
             owner-approved this cycle:
             1. `sim.airside` owns and loads its own taxiway/stand graph
                (`12` §12.4). Interim: it folds into `sim.world` later.
             2. A presentation-owned render layout file (`15` §15.4).
                Interim: it shrinks once `sim.world` has geometry.
             3. Missed passengers recorded on the delay flight record
                (`14` §14.9).
             4. **D4**: 2x and 4x game speeds (`15` §15.8).
             5. **D5**: a Phase 1 `app.ui`, with pause/speed controls and the
                lane click (`17`). Implies a UI scene-layer task and a UI
                backend task.
             6. **D6**: the boarding hold (`12` §12.8). It brings one
                behaviour, one event pair, one delay family, one `sim.flow`
                query and the first `data/balance/` file. Implies an
                airside task, a flow task and a balance-schema task.
             7. **D7**: the `app.host` module (`16`), with the composition
                root, frame loop, Unity project shell, checkpoint dump and a
                harness subcommand. Implies a headless-host task and a
                Unity-shell task.
             Proposed, not added: the `determinism_cross_runtime` nightly gate
             (`16` §16.9, D1). If adopted it adds a CI job that needs a Unity
             licence. Pending: Q-010 item (5), a second `sim.flow` query.
             Also not counted: the soak-fixture authoring task (D3), which is
             a new task but not new scope.
             New since the baseline: 1 module (`app.host`), 2 interface files
             (`16`, `17`), 1 event pair, 1 query, and roughly 8 tasks for the
             Planner to queue.
Signed off:  not required (reporting)

## 2026-09-23 — spec/INDEX.md — new file: spec index for agent onboarding
Reason:      A freshly spawned agent had to read about 5 000 lines of spec to
             find its sections. The index gives a per-module quick map, and for
             each file: what it owns, its key decisions (D1–D9 included), its
             LOW CONFIDENCE markers, and who should read which sections. The
             index is navigation only; where it disagrees with a file, the
             file wins. **Maintenance rule:** every spec amendment updates
             `INDEX.md` in the same commit.
Raised by:   human directive (owner-approved, relayed by the coordinator)
Impact:      none on interfaces. It adds one file the Architect must keep in
             step. The rule is written in the index's header. It also belongs
             in `agents/architect.md`, which is outside `spec/` and so was not
             edited here.
Signed off:  human (owner-approved)

## 2026-09-23 — spec/08 §8.11a; 09 §9.11; 11 §11.9a; 12 §12.4, §12.12a; 13 §13.10a; 14 §14.13a; 15 §15.9; 16 §16.3–§16.5, §16.11, §16.12; 17 §17.7 — Q-009: the construction surface
Reason:      No module published how its system is built. The composition
             root (D7), the harness and every multi-system test would have
             reached into module internals or invented factories. Answer:
             - **`sim.core` (§8.11a):** `SimHostConfig`, `SystemServices`
               (event bus, id allocator, content index), `ISimHostBuilder`
               and `ContentIndexFactory`.
             - **The factory rule:** one `<Module>Factory` of stateless
               static methods per module. It takes `SystemServices`,
               validated construction data and downward interfaces, and
               nothing else. Construct in dependency order; register in
               registry order.
             - **Per module:**
               - loaders that parse bytes: `IFlowGraphLoader`,
                 `IAirsideLayoutLoader.Parse`, `ITurnaroundSetupLoader`;
                 `IScheduleLoader` and `IRenderLayoutLoader` already existed;
               - factories for each system and for the render and UI scene
                 objects.
             - **`FlowGraph` is opaque** outside `sim.flow`, so the answer
               does not design the graph `sim.world` should own.
             - **`sim.airside` gains an explicit `turnaroundRegistered`
               construction input.** §12.8's fallback depended on "whether
               `sim.turnaround` is registered", and nothing told `sim.airside`
               that.
             - **`16` bundle names:** the bundle's file names are now exact
               (`*.fixture`). The harness must build with the same factories.
Raised by:   Q-009
Impact:      interface additions for every module. No module has code. Stale
             task files: T-001 (the builder), T-005 (the builder sits beside
             the queue), T-007/T-010/T-023 (flow factory and graph loader),
             T-008, T-009 (the harness composes through these factories), T-020,
             T-021 (factory, `Parse`, `turnaroundRegistered`), T-022, T-024,
             and the host and UI tasks not yet queued. Two gaps next to this
             one are raised rather than answered: **Q-011** (content
             definition types and the `data/` loader, which blocks the player
             build) and **Q-012** (`sim.flow` routing without `sim.world`,
             which blocks T-007 and was already latent in its task file).
Signed off:  not required (Architect's interface domain)

## 2026-09-23 — spec/08 §8.7, §8.11a; 09 §9.8; 12 §12.10 — Q-010 (1)–(4): command payloads, `PlayerId`, `CommandKind`, dispatch
Reason:      No command could actually be issued. The payload bytes,
             `PlayerId`, the `CommandKind` values and the route from
             `ApplyDue` to an owning system were all unspecified, so T-023,
             T-005 and `app.ui` would each have guessed. Answer:
             - payloads use a fixed little-endian layout with no padding,
               with a per-kind table (`SetServersOpen` 8 bytes,
               `ReassignStand` 10 bytes);
             - `PlayerId { uint16 }` with `PLAYER_LOCAL = 0`;
             - `CommandKind : uint16 { NoOp = 0, SetServersOpen = 1,
               ReassignStand = 2 }`, never renumbered because the values are
               saved in logs;
             - `ICommandHandler` / `ICommandHandlerRegistry`, registered at
               construction through `SystemServices.Commands`;
             - admission runs `TooLate`, then `UnknownKind`, then a pure
               payload `Validate`;
             - an impossibility found at `Apply` is a logged no-op.
             `ReassignStand` had the same gap and is closed in the same way.
             Its "Rejected (`NotPermitted`) if occupied or incompatible" is
             replaced by an `Apply`-time no-op, because admission cannot read
             runtime state deterministically.
Raised by:   Q-010
Impact:      no code exists. Stale: T-005 (admission order, handler
             registry), T-023 (its handler: `Validate` and `Apply`), T-021
             (the `ReassignStand` handler and its changed rejection
             semantics), T-026, or a new `sim.core` task (`PlayerId`,
             `CommandKind` values, the handler contract). `02-determinism.md`
             and `01-architecture.md` are untouched: `01`'s `Command` shape is
             reproduced unchanged, and only its `PlayerId` and payload types
             are now defined.
Signed off:  not required (Architect's interface domain)

## 2026-09-23 — spec/09 §9.7, §9.7b, §9.10; 15 §15.2, §15.5, §15.6, §15.9, §15.12, §15.13; 17 §17.1, §17.5–§17.7, §17.10, §17.11 — Q-010 (5): `IFlowSystem.TryGetLaneState` and lane pips — LOW CONFIDENCE
Reason:      A lane toggle whose state the player cannot see is not a usable
             control. The coordinator approved this as a direct consequence
             of D5. Answer:
             - a read-only `TryGetLaneState(NodeId, out LaneState
               { ServerCount, ServersOpen })`, returning false for non-queue
               nodes, O(1), and not hashed;
             - `app.render` draws it as lane pips: a new `Lane` layer, and
               `LaneOpen`/`LaneClosed` colour roles appended so no existing
               ordinal moves;
             - `app.ui`'s production lane sink computes
               `clamp(base ± 1, 0, ServerCount)` from a pending target or the
               read, and submits only when the value changes.
Raised by:   Q-010 (5)
Impact:      interface additions. Stale: T-023 or a flow task (the query),
             T-020 (a new polled member, enum values, a constant and a
             test). The UI scene-layer task can now include the production
             sink. The query's shape and the use of render pips rather than a
             UI overlay are LOW CONFIDENCE. **The running scope total becomes
             8** (item 8: the lane-state query and lane pips).

## 2026-09-23 — spec/18-interfaces-world.md (new); 09 intro, §9.2, §9.6, §9.7, §9.11; 03; 00; 16 §16.3, §16.4 — Q-012: minimal `sim.world` so `sim.flow` can route
Reason:      `09` §9.6 routed cohorts along `sim.world` flow fields, and
             `sim.world` had no interface. T-007 could not move a served
             passenger anywhere, which blocked the Phase 0 kill-gate chain.
             Answer: the fixed-graph subset of `sim.world`.
             - **The graph:** a fixture-loaded walk graph (nodes with integer
               `LengthMetres`, directed edges). Routes are computed once at
               load: shortest by length, ties by the smallest `EdgeId`
               sequence. `IWorldSystem` exposes `Nodes`, `LengthMetres`,
               `OutEdges`, `EdgeTo`, `CanReach`, `CanReachVia` and `PathVia`.
               It has no runtime state; its hash is the fixture hash.
             - **`sim.flow`:** traversal ticks come from length and the
               profile's walking speed. The destinations are all reachable
               `Gate` nodes. The route is the `(edge, gate)` pair with the
               lowest traversal plus predicted wait of the queues on its
               path, read at start of tick, ties by `NodeId` then `EdgeId`.
               A non-destination `Source` or `Hall` releases the next tick.
             - **`FlowGraph`:** reduced to node behaviour over the world's
               nodes. `Absorb` boards from any `Gate`.
             - **Types:** `NodeId` and `EdgeId` are stated to be `sim.core`
               types, since events carry them.
             - **Deferred:** gate assignment (flight to gate) is a gameplay
               system, **deferred to the owner**. Phase 0/1 pools gates, and
               its fixtures declare one shared lounge. Construction, grid and
               flow-field recomputation are deferred as well.
Raised by:   Q-012
Impact:      unblocks T-007 (and so T-009, T-010, T-011 and T-023) once a
             `sim.world` task lands. **New task:** `sim.world` fixed graph
             (`src/sim/world/**`), depending on T-001 and T-003. It should be
             prioritised because it gates the kill gate. Stale: T-007, T-010,
             T-011, T-023 (world-backed routing, the new factory and loader
             signatures, `Absorb` from pooled gates), T-009 and the harness
             (register `sim.world`, add the `world` fixture), T-026 (`NodeId`
             and `EdgeId` in `sim.core`), T-020 (render layout fixture shares
             the world fixture's node ids), and the host task (`world.fixture`,
             composition order). No merged code.
Signed off:  not required for the interface. **The gate-pooling stopgap is
             flagged for the owner**, because real gate assignment is
             player-facing scope.

## 2026-09-23 — spec/08 §8.11, §8.11a; 04 Files, "Phase 0/1 content fields"; 11 §11.6; 15 §15.13; 16 §16.1, §16.3, §16.11, §16.12; 03 — Q-011: content definition types and the `data/` loader
Reason:      `IContentIndex` had no concrete definition types, `ContentId` and
             `ContentKind` were never defined, and nothing turned `data/`
             into definitions. Every consumer would have invented its own
             type, and lane rates would have been hardcoded into fixtures.
             Answer:
             - **Types, in `sim.core`** (the T-026 pattern): `ContentId`
               (ordinal string), `ContentKind { SizeCategory, Aircraft,
               PaxProfile, QueueProfile }`, and one definition struct per
               kind.
             - **The loader:** `IContentLoader` over an `IContentSource`,
               also in `sim.core`. It maps four fixed directories to kinds and
               reads files in ordinal path order. It hand-parses a strict
               JSON subset: no package, no floating point, decimals as
               strings, and unknown, missing or duplicate keys fail. Hard
               validation names the path and field.
             - **`04`** lists each kind's fields and pairs each schema name
               with its directory name, because the validator in `ci/`
               pairs them by name. `pax_profile.schema.json` becomes
               `pax_profiles.schema.json`, and no schema file existed yet.
             - **`16`** loads content in the player from a build-time copy of
               `data/`.
Raised by:   Q-011
Impact:      additive. It unblocks the player build's content path, and
             lets T-007, T-008, T-021 and T-023 read typed definitions. Stale:
             T-026 or a `sim.core` task (the types plus the loader); T-008
             (`PaxProfileDefinition`); T-007/T-023 (`QueueProfileDefinition`
             replaces "content-declared" rates and thresholds); T-021
             (`AircraftDefinition` and `SizeCategoryDefinition` for stand
             compatibility); the host task (`LoadContent`). New content task:
             the four schemas, plus size-category and aircraft files.
             **For the owner:** Phase 1 pax-profile and queue-profile values
             (walking speed, show-up curve, lane service rates, thresholds,
             hysteresis) are balance values to be written by the owner. The
             `ci/` path guard protects only `data/balance/`, so the owner may
             want to extend it to those two directories.
Signed off:  not required for the interface; the values are **PENDING
             HUMAN**
Signed off:  HUMAN DECISION — owner (delegated), 2026-09-23, consequence of
             D5; reversible. Approved by the coordinator under the owner's
             delegation.

## 2026-09-24 — spec/07-conventions.md "Solution layout and build" (new, L1–L11), "Naming", "Testing", "Error handling"; 08 Notation — Q-013: solution layout, test framework, project ownership
Reason:      Nothing fixed the project paths or names, the test framework, the
             visibility of internals, who creates each project file, or how a
             test name maps to C#. T-001 and T-003 write `src/sim/core/**`
             concurrently, and the Test Author may write only `tests/**`.
             Answer: one project per module at a fixed path (L1). Byte-for-byte
             sim and test `.csproj` files, so that identical additions merge
             cleanly (L2, L3). xUnit v2 with pinned packages and no
             property-testing library (L4). Public surface only, and `public`
             if and only if the spec names it (L5). Namespaces and file names
             (L6). Test names are C# method names verbatim (L7). An ownership
             table, including `AirportSim.sln` (L8). Tests merge with their
             implementation (L9). The IDL-to-C# mapping (L10). Budget tests
             time with integer `Stopwatch` ticks (L11). There are no
             repo-root build files.
Raised by:   Q-013 (coordinator)
Impact:      additive; no `src/` or `tests/` code exists. Stale task text:
             T-001 (it no longer creates the sln; it adds `tools/SimHarness`),
             and T-003 (it needs `AirportSim.sln (create)` in its writable
             paths). The Planner must add `AirportSim.sln` to the writable
             paths of the first task in each new module, and must serialise
             those tasks. The 08 Notation sentence that gave workers the
             choice of namespaces and access modifiers is withdrawn.
             No scope added (running total unchanged at 8).
Signed off:  not required, but see LOW CONFIDENCE
LOW CONFIDENCE: (1) **No property-testing library.** `07` prefers property
             tests for flow, baggage and delay. Seeded loops lose shrinking,
             but FsCheck's default random seeding conflicts with
             "tests never use unseeded RNG". Adding one later is additive.
             (2) **`NuGetAudit=false` in test projects.** Under
             `-warnaserror`, a newly published advisory for a test package
             would otherwise break every build overnight, with no commit
             to blame. The trade-off is that nobody sees the advisory. The
             owner may prefer an audit job that does not block. (3) The
             package versions were pinned by the Architect. They were
             verified to restore, build with `-warnaserror` and run on SDK
             8.0.425 (Windows), but not on the CI image. (4) There is no
             `global.json`, so CI's `8.0.x` floats. `net8.0` leaves support
             in Nov 2026.

## 2026-09-24 — spec/08-interfaces-core.md §8.1, §8.2, §8.4, §8.5, §8.5a (new), §8.6, §8.8, §8.9, §8.10, §8.11a; 10 §10.2 — Q-014: tick numbering, registry ids, bus signature, invariants, logging shapes
Reason:      T-001's recon found coin flips at every edge. Answer:
             `Step(n)` executes ticks `CurrentTick …`, and the first tick is
             0. Chunking is invisible. There are 24 checkpoints a day and
             none at `Build`. The world hash feeds the ticks-executed count.
             `MinutesBetween` is signed and `TickOfDayTime` floors.
             `SystemId` 1–14 except 8, with `SYSTEM_CORE = 0`, and legal
             probe registration. `SimEventHandler<T>(in EventEnvelope, in T,
             in TickContext)`: the envelope travels beside the payload, the
             bus fills it, and `Publish` takes `in EventRef cause`. Cascade
             passes and limits are defined. `SimInvariantException` exists,
             and the host wraps any exception that escapes a tick. The
             shapes of `LogLevel`, `LogKey` and `LogArgs` are defined. The
             `RngStreamName` shape is defined. Config members are never
             null. The constants live in `SimConstants`.
Raised by:   Q-014 (Test Author, worker-1, worker-2)
Impact:      additive to the interface, except for two **signature
             changes**. `IEventPublisher.Publish` gains a `cause` argument.
             `EventHandler<T>` is renamed `SimEventHandler<T>` and gains
             envelope and context parameters. No code exists, so nothing
             merged breaks. Stale tasks: T-001 (the shape-only and full
             split in Q-014; dispatch recommended into T-001), T-004 (its
             phase-4 recording moves to T-001, and it keeps `IStateHasher`
             and pinning the exact hash), T-005 (`CommandKind` beyond `NoOp`).
             **No task owns `IIdAllocator` behaviour or
             `ContentIndexFactory`.** The Planner must assign both. No scope
             added.
Signed off:  not required
LOW CONFIDENCE: the world hash feeding "ticks executed" (`t + 1` at the
             checkpoint of tick `t`) is chosen so that a checkpoint equals an
             on-demand hash. It is cheap to change now, and costly after
             T-013 lands goldens.

## 2026-09-24 — spec/08-interfaces-core.md §8.3 "C# shape and edge cases" (new) — Q-015: `Fx` edge semantics
Reason:      T-003's tests and implementation could disagree on overflow,
             rounding direction, the parse grammar, display rounding, the
             negative square root, and the C# surface. Answer: add
             `FromRaw`. Static operations with required operators and no
             conversions. Throw, never wrap or saturate, with exact exception
             types. `RoundHalfUp` goes toward +∞. `Sqrt` is the exact floor
             integer root. A strict `Parse` grammar with at most 10 fraction
             digits, exact then floored. `ToDisplayString` floors, with 0 to
             10 decimals. Bit-exactness is proven by golden vectors, not by
             a child process.
Raised by:   Q-015 (Test Author, worker-2)
Impact:      additive (`FromRaw` is new). T-003's task text ("two
             independent process runs") is stale. `04-data-schemas.md`
             decimal strings must match the `Parse` grammar, and
             `ci/validate-content.py` may need the same pattern (that is
             human-owned `ci/`). No scope added.
Signed off:  not required

## 2026-09-24 — spec/open-questions.md — Q-016 filed, PENDING HUMAN: interim "green" before T-006
Reason:      The full `ci/run-checks.sh` needs harness subcommands that
             T-006 delivers, so no earlier task can meet "Determinism gate
             passes". The Architect proposes a rule but does not adopt it:
             it changes a gate, and `ci/` belongs to the owner.
Raised by:   coordinator
Impact:      none until decided
Signed off:  **PENDING HUMAN**

## 2026-09-24 — spec/open-questions.md Q-016 — HUMAN DECISION: interim "green" before T-006
Reason:      The owner adopted the proposal. Until T-006 merges, green =
             `path-guard` + `build-and-test` (`--fast`, with build and tests
             through `AirportSim.sln`). After T-006 merges, the full
             `ci/run-checks.sh` is mandatory.
Raised by:   Q-016
Impact:      T-001 to T-005, T-026 and T-027 can reach "done". There is no
             spec file change beyond the answer.
Signed off:  HUMAN DECISION — owner, 2026-09-24

## 2026-09-24 — spec/08 §8.4, §8.9 — Q-017: core section in the world hash, `StateHasher`, encoding, id allocation
Reason:      The world hash could not see core state, so a `NoOp` was
             invisible to it. Span encoding was ambiguous, and nobody could
             construct a hasher. Answer: a `CoreHash` section (next
             sequence, pending commands, id counters), fed between the tick
             and the systems and carried on `Checkpoint.CoreHash`. `public
             struct StateHasher`, where `default` is fresh. It ships in
             T-001. The encoding is pinned, with length-prefixed spans, and
             there are golden vectors. `EntityId = (owner << 48) | counter`,
             with counters starting at 1.
Raised by:   Q-017
Impact:      `Checkpoint` gains `CoreHash`, a shape change that
             T-001 declares, with no code yet. `ContentId` and every string
             is now hashed with a length prefix: the content hash (§8.11)
             follows the §8.9 encoding. Stale tasks: T-001 (it ships
             `StateHasher` and `IIdAllocator` if the Planner assigns it
             there), T-004 (it proves the vectors, and does not author the
             hasher). No scope added.
Signed off:  not required
LOW CONFIDENCE: `EntityId` puts the owner in the top 16 bits. It is
             collision-free and cheap, but it gives the bit pattern a
             meaning that consumers are told to ignore.

## 2026-09-24 — spec/10 §10.9 (new); 09, 11 §11.3, 14 §14.3 annotations; 07 L10 — Q-018: event structs declared, `sim.core` owns them
Reason:      No task declared the event structs, and T-008 could not emit
             core-owned events. This is option (a): `sim.core` owns every
             event struct, matching `03`. The Phase 0 and Phase 1 structs
             are pinned field for field. `AirlineId`, `MovementKind`,
             `CohortId`, `FlightMilestone`, `DelayNode` and `DelayNodeKind`
             are relocated to `sim.core`. `DelayEvent { DelayNode Node }`.
             `X?` maps to `Nullable<X>`, and `event` maps to a
             `readonly struct : ISimEvent`.
Raised by:   Q-018
Impact:      T-026 grows: every §10.9 struct plus the relocated types. It
             must merge before T-007, T-008, T-021, T-022 and T-024. T-007's
             "`CohortId` stays `sim.flow`'s own type" is stale.
             `FlightPlanRevised`, `FlightCancelled` and the Phase 2 events
             stay undeclared. No scope added.
Signed off:  not required
LOW CONFIDENCE: `DelayEvent` wraps `DelayNode` whole rather than copying
             06's lower-case field list. It is one source of truth, but the
             payload's field names are `14`'s, not `06`'s.

## 2026-09-24 — spec/08 §8.8 "Exact reference" — Q-019: RNG pinned bit for bit, with golden vectors
Reason:      T-002's oracle would otherwise be one worker's reading. The
             answer pins everything and adds `RandomServiceFactory.Create`:
             - the name hash is FNV over UTF-8 with no prefix;
             - standard SplitMix64 fills `s[0..3]` in order, and an all-zero
               state is replaced at `s[0]`;
             - xoshiro256** 1.0;
             - `NextInt` is Lemire on the top 32 bits;
             - `Chance` is always one draw;
             - `Shuffle` runs descending;
             - the stream hash is `s[0..3]`;
             - `Stream` returns the same live stream;
             - the name grammar `sim.<module>.<purpose>` replaces "CI
               asserts uniqueness".
             The Architect computed three golden vectors from these
             algorithms. The SplitMix64 and xoshiro cores match their
             published reference outputs.
Raised by:   Q-019
Impact:      additive. T-002 cites §8.8 "Exact reference". No scope added.
Signed off:  not required

## 2026-09-24 — spec/08 §8.7 "Queue semantics" — Q-020: command queue semantics
Reason:      T-005 had no answer on log access, sequence numbering,
             payload type, admission order, owner enforcement, logging an
             impossible `Apply`, or what `LogSince` includes. Answer:
             - `ICommandQueue` is internal, and `ISimHost.CommandLogSince`
               is added.
             - The payload is a `byte[]`, copied on admission. A `null`
               payload throws.
             - Admission runs TooLate → NotPermitted (issuer) → UnknownKind
               → Validate. `Validate` runs exactly once, and `NoOp` requires
               an empty payload.
             - `Sequence` is global from 1.
             - `LogSince` is inclusive and includes pending commands.
             - The owner comes from the payload table.
             - The handler logs an impossible `Apply`.
             - `TrySubmit` during `Step` throws.
Raised by:   Q-020
Impact:      `ISimHost` gains `CommandLogSince` (additive; only core
             implements `ISimHost`). Q-010's admission order gains the
             issuer check. T-005 is stale in these details. No scope added.
Signed off:  not required

## 2026-09-24 — spec/07 "Solution layout and build"; 11 §11.10 — Q-021: the Test Author writes all of `tests/**`
Reason:      A fixture had two possible authors.
Raised by:   Q-021
Impact:      Worker grants under `tests/**` (for example T-008's
             `tests/fixtures/schedule/**`) are moot. The Planner may drop
             them. No scope added.
Signed off:  not required

## 2026-09-24 — spec/16 §16.4; 07 L2; 03 (`sim.turnaround`, `app.host` rows) — Q-022: `ComposedSim.World`, itemised project references
Reason:      `ComposedSim` omitted `World`. `03`'s umbrella dependency for
             `app.host` cannot drive L2. L2's "references = the `03` cell"
             was also wrong in two cases: `03` lists dependencies a Phase 1
             project does not use (for example `staff`), and it missed one
             it does use (`sim.turnaround` → `schedule`, per `13`'s factory).
             Answer: add `IWorldSystem? World`. L2 now derives references
             from the published interface file and gives a binding Phase 0/1
             table. `03` is amended in the two rows.
Raised by:   Q-022
Impact:      T-031's `World` field becomes spec-backed. L2 replaces its
             Q-013 wording, with no code yet. The `03` change widens
             `sim.turnaround`'s allowed dependencies by one module, and
             that module was already a factory parameter. No scope added.
Signed off:  not required

## 2026-09-26 — spec/08 §8.8 "Exact reference", §8.7 "Issuer, kinds and payloads" — Q-023: RNG name validation, test scope, cost; home of `PLAYER_LOCAL`
Reason:      Five small gaps after Q-019/Q-020. Answer:
             - The name pattern matches the whole string (`\A…\z`). `null`
               throws `ArgumentNullException`, malformed throws
               `ArgumentException`, and so does `Stream(default)`.
             - The golden vectors are the only required proof. There is no
               raw-state test seam. The `{1, 2, 3, 4}` check is an aid for the
               implementer. The zero-state replacement cannot be reached,
               since SplitMix64's mixer is a bijection of a counter that does
               not repeat.
             - There is no per-call time budget. Draws allocate nothing after
               the first `Stream` call and are charged to the calling system.
             - `PLAYER_LOCAL` and `SYSTEM_CORE` are `static readonly` members
               of `SimConstants` under their IDL names (L10), not
               `PlayerId.Local`.
             - T-002's cross-process test and "CI uniqueness check" are
               superseded. The stale "(CI asserts uniqueness)" sentence in
               §8.8 is corrected.
Raised by:   Q-023
Impact:      Constraining only, and no code has merged. T-002's task text
             is stale where it names a child process or a CI uniqueness
             check. No new public surface. No scope added.
Signed off:  not required
LOW CONFIDENCE: charging draw time to the calling system rather than to
             `sim.core` relies on `03`'s per-module budgets being measured
             per system `Tick`. If the harness attributes the RNG
             differently, the answer needs revisiting.

## 2026-09-26 — spec/08 §8.5a, §8.4 — Q-024: tick fed into a wrapped exception's `WorldHash`; id overflow untested
Reason:      §8.5a did not say whether the hash taken when tick `t` fails
             feeds `t` or `t + 1`. The answer is `t`, the ticks completed,
             which matches "ticks executed" in §8.9 and leaves `CurrentTick`
             at `t`. The partial state is hashed as it stands. The
             `IIdAllocator` 2^48 overflow has no public reach and is noted
             as untested by design.
Raised by:   Q-024
Impact:      Constraining only. No merged code. No scope added.
Signed off:  not required

## 2026-09-26 — spec/19 (new); 07 L1, L3, L8; INDEX — Q-025–Q-027: harness test project, CLI contract, interim save/load gate
Reason:      T-006 could not be tested or implemented without inventing
             things. H1: the harness had no test project, and L3 forbade the
             test location the task named. H2: nothing pinned the exit codes
             or stdout that `ci/run-checks.sh` depends on, and there was no
             way to prove that a gate can fail. H3: the task asked for an RNG
             snapshot through a seam that `08` §8.8 says does not exist.
             Answer:
             - `07` L1/L3 add `tests/tools/simharness`, which references the
               harness and runs in process. T-006 adds it to the sln (L8).
             - `19` §19.1 makes `HarnessCli`, `HarnessGates`, `SimComposer`
               and `GateResult` the harness's only public types, and the
               injected composer is the divergence seam.
             - §19.2 fixes a NoOp command script and the order in which two
               runs are compared.
             - §19.3 fixes the five CLI forms, the seed 12345 where the
               script passes none, the exit codes 0/1/2/3 and a one-line
               stdout.
             - §19.4 applies the `03` statistic to the whole-sim 6 ms.
             - §19.2/§19.5: `saveload` is replay from seed and log, and
               `promotion` compares for real but passes vacuously until
               T-010.
Raised by:   Q-025, Q-026, Q-027
Impact:      T-006 is stale in four places:
             - tests go in `tests/tools/simharness/**`, not
               `tests/sim/core/**`;
             - `AirportSim.sln` must be added to its writable paths;
             - "snapshots ... RNG stream state" is replaced by §19.2;
             - "no-op pass-through" for `promotion` is replaced by a real
               comparison that passes vacuously.
             T-001's merged harness returns 2 for any subcommand, and T-006
             replaces that. The no-argument behaviour is untouched, so no
             merged test breaks. New public surface is in the harness only.
             Scope: none added. `sim.save` is still unspecified.
Signed off:  HUMAN DECISION PENDING for §19.5. The Architect recommends
             adopting the replay form as the interim meaning of `02`'s
             `determinism_save_load`. `02` and `ci/` are not edited.
LOW CONFIDENCE: §19.4. Until a max-tier fixture is composed into the harness,
             `budget --tier max` times core alone, so it proves almost
             nothing. The seed 12345 for `saveload`, `promotion` and
             `budget` mirrors the script's `determinism` seed and is not
             an owner value.

## 2026-09-26 — spec/19 §19.5; INDEX — Q-027: owner approves replay as the interim `determinism_save_load`
Reason:      HUMAN DECISION — owner, 2026-09-26: §19.5 is approved. Until
             `sim.save` exists, `determinism_save_load` is satisfied by the
             replay check of §19.2. The real snapshot round-trip replaces it
             when `sim.save` is specified. The Architect recorded the
             decision and did not make it.
Raised by:   Q-027
Impact:      Q-027 is fully answered, and T-006 implements §19.2 as
             specified. Whichever task specifies `sim.save` must amend
             `SaveLoad` and §19.5 to reload from a real snapshot. `02` and
             `ci/` are unchanged. No scope added.
Signed off:  owner, 2026-09-26

## 2026-09-26 — spec/08 §8.11, §8.11a; 07 L6, L10; INDEX — Q-028: content index edge cases, enum casing, test file naming
Reason:      The T-026 Test Author found five unpinned points. Answer:
             - `ContentIndexFactory.Create`: a `null` list throws
               `ArgumentNullException`. A `null` element, a `null` id value
               or a duplicate id throws `ArgumentException`. The input is
               copied.
             - `TryGet<T>` is a pure type-and-id match: false with `default`
               on a type mismatch, never a throw. `IContentDefinition`
               matches any definition.
             - Enum members are PascalCase in C#, converted mechanically
               from the IDL's snake_case (`DelayCategory`). Data keeps
               snake_case.
             - L6: multi-word subjects are allowed, with a prefix rule
               (longest subject wins). This matches every merged test file.
Raised by:   Q-028
Impact:      Constraining. Nothing merged implements `ContentIndexFactory`,
             `TryGet` or `DelayCategory`. All 21 merged
             `tests/sim/core/*Tests.cs` files already satisfy the L6 rule
             (checked). The enum casing binds every module, and snake_case
             appears only in `06`'s `DelayCategory` list. No scope added.
Signed off:  not required

## 2026-09-26 — spec/19 §19.2 — Q-029: `SaveLoad` order, script length, unreachable reports
Reason:      T-006's tests could not pin `at=reload` or the script's extent.
             Answer:
             - U, A and B run in that order, and `reload` is checked first
               and fails at once.
             - The NoOp script always spans the gate's `ticks`, in A too.
               Without that, B would lack U's later commands and no correct
               sim could pass.
             - `at=world` and `at=count` are unreachable by construction.
               CLI exit codes 1 and 3 are unreachable until the CLI
               composition is non-empty. All are untested by design.
             - T-006 depends on T-005.
Raised by:   Q-029
Impact:      Constraining only. The Test Author's assumption (full ticks)
             holds. The Planner adds the T-005 → T-006 edge, already
             satisfied by #26. No scope added.
Signed off:  not required

## 2026-09-26 — spec/18 §18.2, §18.3, §18.6; 07 "Error handling"; INDEX — Q-030: walk-graph file format, load failures, unknown ids
Reason:      The Test Author owns the walk-graph fixture and four raw-byte
             tests, but `18` left the format to the worker. Answer:
             - (a) The `08` §8.11 strict JSON subset, with exactly the keys
               `WalkGraph` needs (`schema_version`, `nodes[id,
               length_metres]`, `edges[id, from, to]`). Ids are ≥ 1, the
               arrays may be in any order, the output is sorted, and
               `FixtureHash` is taken over the exact bytes.
             - (b) Every load failure throws `FormatException`, whose
               message starts with the source name and carries the line or
               the field and id. It is extended to every loader through `07`
               "Error handling", since no loader spec named a type.
             - (c) An unknown id in any `IWorldSystem` query throws
               `ArgumentException`.
Raised by:   Q-030
Impact:      T-012 is unblocked on these tests, and the fixture is
             `tests/fixtures/world/phase0-landside.json`. No loader is
             merged on `main`. **Any in-flight loader work that chose
             another failure type must switch to `FormatException`.** This
             covers T-008's schedule loader and `08` §8.11's
             `IContentLoader`, if either has started, together with their
             tests. `12` §12.13's "format is the worker's choice" posture is
             unchanged for airside, but its failure type is now pinned. No
             scope added.
Signed off:  not required
LOW CONFIDENCE: extending `FormatException` to every loader goes beyond
             T-012's ask. It was chosen so that five loaders do not pick five
             types. If a loader needs a structured failure (for example a
             list of errors), that is a new type by amendment.

## 2026-09-27 — spec/18 §18.2–§18.3; 11 §11.7, §11.9, §11.9a, §11.10; 08 §8.11; 07 L4, "Error handling"; 04 Conventions — Q-031: world, schedule and loader details
Reason:      Test Authors of T-012, T-008 and T-027 hit twelve unpinned
             points (see Q-031). Each is resolved by the narrowest rule:
             - routes use simple paths only, and `CanReach(n, n)` is true;
             - "offending id" is fixed per failure kind;
             - fixtures are found through `AirportSim.sln`;
             - the 200-row fixture is 99 rotations plus two lone flights;
             - the day-materialisation schedule is fixed, and hashed lists
               are count-prefixed;
             - `sim.schedule: ` prefixes construction failures;
             - the `03` statistic governs `sim.schedule`'s budget;
             - `PendingInjectionCount` is 0 before publication;
             - loader messages name the later file and the unresolved
               category;
             - nested kind directories fail.
             `04` gains a note on `Fx.Parse` flooring for content authors.
Raised by:   Q-031
Impact:      No module other than `sim.core` is merged, so nothing breaks
             on `main`. In-flight work must align:
             - T-008 tests that assert the budget by mean only must add p99
               ≤ 0.20 ms over one sim-day;
             - T-008's hash must count-prefix lists 3 and 4;
             - T-012's route search must restrict to simple paths.
             No scope added.
Signed off:  not required

## 2026-09-27 — spec/09 §9.4, §9.5, §9.10, §9.11 "File format", §9.12 (new) — Q-032: flow graph format and exact tick semantics
Reason:      T-007 (critical path) could not be tested. There was no
             pinned `FlowGraph` format, and §9.4 to §9.6 left the credit
             cap, same-tick movement, capacities, the wait formula,
             traversal rounding, the threshold comparisons and the
             blocking-event fields open. Answer:
             - §9.11 pins the JSON format and its failures, as Q-030 did for
               the walk graph.
             - §9.12 pins the order within `Tick`: snapshot, movement with
               one node per tick, merge, thresholds.
             - It pins the queue arithmetic, ordered so that 2.5 pax/min is
               exact, with the cap only on a shortfall.
             - It pins the predicted wait with `EPSILON = 1/1000`,
               `traversalTicks` and the route cost.
             - It pins the thresholds, with strict inequalities that never
               flap.
             - It pins the blocking episodes, where `BlockedBy` is the
               immediate target, and the event order.
             - Node state gains a hashed threshold flag.
             - Cohorts in an open episode do not merge.
Raised by:   Q-032
Impact:      Nothing of `sim.flow` is merged. §9.10's hash gains the
             threshold flag, which is additive before any golden.
             `PassengerCohort.ServiceCredit` is pinned to zero, since the
             credit is per node. The Planner can release T-007 on §9.11 and
             §9.12. The fixture is
             `tests/fixtures/flow/phase0-landside.flow.json`. No scope added.
Signed off:  not required
LOW CONFIDENCE: the choices below are player-visible and are for the
             owner's review. None of them is a content balance value.
             - `EPSILON = 1/1000`: a closed lane with 10 passengers shows a
               10 000-minute wait.
             - The post-idle burst cap of one `serverTick`.
             - Unlimited `Hall` capacity at Phase 0/1.
             - Head-of-line blocking in a `Queue`.
             - Blocked cohorts not merging. This may raise the live-cohort
               count during long spillback, and the §9.10 budget test's
               cohort ceiling will show whether it does.

## 2026-09-27 — spec/09 §9.2, §9.3, §9.7, §9.9, §9.10; 08 §8.11 "Strings"; INDEX — Q-033: flow entry points, JSON strings
Reason:      The T-007 Test Author and the reviews found seven gaps:
             - exceptions of `Inject` and `Absorb`;
             - the granularity of `PassengersMissedFlight` and its node;
             - the merged corridor `DueAt`;
             - the injected `EnteredNodeAt`;
             - the value of the cohort ceiling;
             - `Absorb` outside a tick;
             - string escapes in the shared JSON subset.
             Each gets the narrowest rule. `LastBlockedAt` is redefined as
             the node holding the most missed passengers. The old wording,
             "last blocked", needed per-cohort history that
             `PassengerCohort` does not hold, and the new rule matches
             §9.7a and the D6 hold's `heldAt`.
Raised by:   Q-033
Impact:      Nothing of `sim.flow` is merged. `sim.delay` (`14` §14.9) only
             records the node, so its rules are unchanged. The string rule
             was chosen to match the merged T-027 loader
             (`src/sim/core/ContentJson.cs`). It already accepts exactly the
             eight escapes plus `\u` and rejects raw control characters.
             **One divergence:** that loader accepts a `\u` surrogate, which
             is now a load failure. It needs a small follow-up fix with a
             test, and the Planner should task it. Whether it rejects invalid
             UTF-8 was not checked. No scope added.
Signed off:  not required
Addendum:    The T-010 promotion rules are added (`09` §9.7 "Promotion
             rules", `19` §19.2):
             - one view per passenger;
             - `ArgumentException` for an unknown node, and every node is
               promotable;
             - `SetPromoted` may be called at any time;
             - no allocation after warm-up;
             - a separate harness task, which the Planner creates, makes
               the `Promotion` gate promote.
             T-010 keeps its `src/sim/flow/**`-only grant.
LOW CONFIDENCE: `LastBlockedAt` as "most missed passengers here" rather than
             true blocking history, for the owner with D6. Also, an
             `Absorb` outside a tick is detected only when it has something
             to publish.

## 2026-09-27 — spec/15 §15.5–§15.7, §15.9–§15.12, §15.14 (new); 17 §17.1, §17.3, §17.4a (new), §17.7, §17.8, §17.10, §17.11; 16 §16.5–§16.7, §16.11, §16.12; INDEX — D10 / Q-034: player-adjustable graphics quality
Reason:      HUMAN DECISION — owner, 2026-09-27 (D10): "the final user
             should be able to increase or decrease graphics so the game can
             also be run on a low resource laptop." The Architect specified
             the mechanism:
             - presets and six knobs (§15.14), presentation only;
             - `Build` and `Update` take the settings, and `DrawAgents`
               gates render-driven promotion, which is outcome-neutral by
               `09` §9.1;
             - the backend applies frame cap, resolution scale and
               anti-aliasing;
             - a modal settings panel in `app.ui`;
             - a pinned preference text, stored through the host's
               `IPreferenceStore`, never in a bundle, save, command or hash.
             `01` and `02` are untouched.
Raised by:   D10 (owner), Q-034
Impact:      No `app.*` code is merged, so nothing breaks. For the Planner,
             these tasks are stale:
             - **T-020** (render scene): new `Build`/`Update` signatures,
               `RenderFrame.Graphics`, the `RenderFactory` graphics
               functions, and six new tests;
             - **T-029** (UI scene): the three new inputs, `UiFrame` fields,
               `CreateController`'s `initialGraphics`, the preference codec,
               and six new tests;
             - **T-031** (headless host): frame loop steps 2, 4 and 5,
               `IPresentationComposer.Compose`'s store, and three new
               tests;
             - **T-032** (render backend): apply the backend knobs;
             - **T-033** (UI backend): the settings icon and panel, with
               `LocalisedKey` text, the first player-visible text;
             - **T-034** (Unity shell): the bootstrap's `IPreferenceStore`
               over the engine's player preferences.
             **Scope added: 1** (a settings panel and the graphics setting),
             owner-decided. The running total becomes 9.
Signed off:  owner, 2026-09-27 (D10, the requirement). The mechanism is the
             Architect's.
HUMAN DECISION PENDING (owner):
             - the Low and Medium preset values (until set, both equal High,
               as a placeholder);
             - the first-launch default (High until decided);
             - the low-end target machine, which may sit below `01`'s locked
               minimum spec. Graphics cannot reduce the 6 ms sim cost.
             - whether the settings panel pauses (it does not, until
               decided).
LOW CONFIDENCE: the knob set, in particular `AntiAliasing` as a bool and
             the `FrameRateCap` floor of 15, derived from the pacer's
             catch-up cap.
Addendum:    HUMAN DECISION — owner, 2026-09-27: graphics must not affect
             gameplay or difficulty. `15` §15.14 and `17` §17.4a state a
             binding invariant:
             - every primitive outside the `Agent` layer is identical at
               every setting, and so is its order;
             - future gameplay-relevant elements are never gated by a knob;
             - no knob changes the tick, speed, pause, pacing, command
               timing or click targets, and clicks stay in full-screen
               pixels under resolution scale;
             - performance scaling is presentation only.
             New tests: `test_scene_gameplay_primitives_identical_at_every_graphics_setting`
             (T-020) and
             `test_ui_controls_and_hits_identical_at_every_graphics_setting`
             (T-029). T-032 is bound by the backend clause.

## 2026-09-27 — spec/01 (minimum-spec GPU line only); 15 §15.10, §15.11, §15.12, §15.14; 17 §17.4, §17.4a, §17.10, §17.11; 16 §16.6, §16.10, §16.11, §16.12; INDEX; open-questions — Q-034: owner decisions on graphics quality
Reason:      HUMAN DECISIONS — owner, 2026-09-27, closing the items D10 left
             pending:
             1. The minimum GPU is integrated graphics with no dedicated
                VRAM. The CPU (4 cores) and RAM (8 GB) minimums are
                unchanged. It is recorded in `01-architecture.md` as
                "HUMAN DECISION 2026-09-27 (Q-034)".
             2. `Low` must hold the frame budget on that hardware. Shared
                GPU memory counts against the 8 GB.
             3. The settings panel pauses the sim while it is open.
             4. The first-launch default is `Medium`.
             5. The Architect proposes the `Low` and `Medium` values.
             Changes:
             - `01`: the minimum-spec row now reads "integrated graphics
               with no dedicated VRAM" instead of "GPU with 2 GB VRAM".
               Nothing else in `01` changed, and nothing in `02`.
             - `15` §15.11: on minimum spec, `Low` holds 60 fps at max tier.
               `Medium` and `High` are not bound there.
             - `15` §15.14: the `Low`/`Medium` table, `Medium` as the
               default, the low-end target, and a manual measurement on a
               minimum-spec machine at 1920 × 1080. Invariant 3 notes the
               panel's pause.
             - `15` §15.10: the backend's draw calls are bounded by layers
               and colour roles, never by primitive count.
             - `17` §17.4, §17.4a: `Pacing.Paused` = player's pause OR
               `SettingsOpen`. While the panel is open, only the three
               settings inputs apply.
             - `16` §16.6: the frame loop needs no change, since
               `Ui.Pacing.Paused` includes the panel. §16.10 adds a 2 GB
               process memory budget, shared GPU memory included.
             Graphics stays presentation only. No change touches sim state,
             the tick rate, a hash or difficulty. The panel's pause depends
             on whether it is open, never on a knob, and pausing is
             outcome-neutral (`15` §15.8).
Raised by:   owner, Q-034
Impact:      No `app.*` code is merged, so nothing breaks. For the Planner:
             - **T-020**: `ForPreset` returns the new `Low`/`Medium` values.
               New test `test_graphics_low_and_medium_match_the_preset_table`.
             - **T-029**: the panel pauses, and pause, speed and clicks are
               ignored while it is open. `test_ui_settings_toggle_does_not_pause`
               is **replaced** by
               `test_ui_settings_open_pauses_and_close_restores_player_pause`
               and `test_ui_pause_and_speed_ignored_while_settings_open`.
             - **T-031**: the default preference is `Medium`. New test
               `test_frame_loop_settings_opened_this_frame_steps_nothing`.
             - **T-032**: the draw-call bound.
             - **T-033**: pause and speed controls are inert while the panel
               is open.
             - **T-025** (playtest), or a later one: measure `Low` (60 fps,
               ≤ 2 GB) on a minimum-spec machine.
             - `03-module-map.md`'s budget protocol uses "the minimum spec of
               `01`" as its reference machine. That now means an
               integrated-graphics machine. Sim budgets are CPU-only, so
               their numbers are unaffected.
             Scope added: 0. The panel already existed (D10). Running total
             stays 9.
Signed off:  owner, 2026-09-27 (Q-034). This includes the `01` GPU line,
             which is the only `01` change the owner authorised.
LOW CONFIDENCE — owner may revise:
             - the `Low` and `Medium` values (`15` §15.14);
             - the 2 GB process memory budget (`16` §16.10);
             - the 1920 × 1080 measurement condition (`15` §15.14);
             - the backend draw-call bound (`15` §15.10);
             - the Architect's reading of `01`'s "60 fps at max tier on
               minimum spec" as binding at `Low` (the owner's decision 2),
               not at every preset.
Budget note: `Medium` (the default) is **not** bound to 60 fps on minimum
             spec, and nothing has been measured. The Architect expects it
             to hold at 1920 × 1080 on current integrated graphics, since it
             draws flat colours, about 1 000 agent dots and no
             anti-aliasing, provided the backend batches (§15.10). It is at
             risk on high-DPI laptop panels at 100 % scale, and where GPU
             heat throttles the CPU. On such machines the first launch may
             miss 60 fps until the player picks `Low`. The Architect did not
             work around this, per the owner's instruction.

## 2026-09-27 — spec/08 §8.6 "Allocation" (new); INDEX — Q-035: the event bus allocates nothing after `Build`
Reason:      `EventBus.Publish<T>` creates a channel on the first publish of a
             type with no subscriber. In T-007's budget test that happens at
             tick 1695, inside the measured window, and allocates 368 bytes.
             `08` §8.5 and `07` forbid allocation on the tick path, but §8.6
             never said what that means for the bus. A module cannot
             pre-warm the bus, because `Publish` outside a tick throws. So
             the rule is on the bus:
             - it allocates nothing after `Build`, with no warm-up;
             - the channel set is fixed at `Build`;
             - a type with no subscriber stores nothing, but it still
               consumes its `Sequence`, returns its `EventId` and runs
               every check;
             - capacity for `MAX_EVENTS_PER_TICK` events is reserved at
               `Build`, so a new per-tick peak does not grow storage.
             The alternative, allowing a first publish to allocate, was
             rejected. It would make every module's budget test depend on
             which events happened to fire during warm-up.
Raised by:   Q-035 (worker / T-007; filed as "Q-034" and renumbered)
Impact:      **Merged `sim.core` diverges.** `src/sim/core/EventBus.cs` and
             `Channel.cs` create channels lazily and let their lists grow.
             The Planner needs a small `sim.core` fix task:
             - writable paths `src/sim/core/**` only;
             - the five new tests of §8.6 are the Test Author's, in
               `tests/sim/core/`;
             - no dependencies, since everything it touches is merged;
             - reviewed by `reviewer-core`.
             T-007 stays blocked on
             `test_flow_budget_update_path_allocates_nothing` until that
             task merges. T-007's own code and tests are unchanged.
             Nothing observable changes: `EventId`s, handler order and
             calls, hashes and goldens stay the same, because events are
             not hashed or saved (§8.9) and an unsubscribed event has no
             handler. The existing `test_budget_step_with_events_and_ids_allocates_nothing_in_steady_state`
             stays valid, since the new rule is stricter. Reserving
             `MAX_EVENTS_PER_TICK` per subscribed type costs, once at
             `Build`, `MAX_EVENTS_PER_TICK` × (envelope + payload size) per
             subscribed type. The storage layout is left open so the
             implementer can share it if that figure matters. No scope
             added.
Signed off:  not required. `01` and `02` are untouched.

## 2026-09-28 — spec/09 §9.6 "Routing cache" (new), §9.6 destinations, §9.7 `Inject`, §9.10; 18 §18.5, §18.6; INDEX; open-questions — Q-036, Q-037, Q-040: routing-cache rules, gate count in `sim.flow` fixtures, `Departing`-only cohorts
Reason:      T-011's black-box scaling runs show routing cost linear in the
             number of pooled gates (p99 of 9.9 ms at 96 gates), with the
             worst ticks after show-up injections. `09` allowed a cache but
             never said what it must preserve. It also left open whether
             `18` §18.5's single `Gate` bound `sim.flow` stress fixtures.
             - **Q-036:** six binding rules for an optional cache (§9.6):
               - observably identical: the same per-pair cost `Raw` and
                 the same tie-break, with per-node terms that are each
                 node's own §9.12 value;
               - a static part from three named sources that may live for
                 the run;
               - a wait-dependent part memoised within one tick only;
               - not hashed, not saved, rebuilt on restore;
               - no allocation;
               - no other inputs.
               It also names the required oracle test,
               `test_flow_routing_cache_matches_uncached_reference`, with
               mandatory cases, owned by the Test Author of whichever
               `sim.flow` task adds the cache.
             - **Q-037:** the single `Gate` binds the shared file
               `phase0-landside.json` and fixtures that load it.
               `sim.flow`-local stress and budget fixtures build their own
               graph and may pool several gates, stating the count and its
               derivation (§9.10, §18.5). §18.6's list of users is
               corrected.
Revision:    after the PR #55 review (rejected at 9741b6d), four findings
             were fixed:
             - (1) §18.6 no longer lists T-011's stress fixture as a user
               of the shared file. The single-gate scope is now "the file
               and fixtures that load it", in §18.5, §18.6, §9.10 and
               Q-037.
             - (2) Rule 1 no longer forbids reordering or factoring on a
               false premise. `Fx.Add` and integer-multiple `Mul` are exact
               and every term is non-negative (`08` §8.3), so the grouping
               cannot change `Raw` or the overflow condition. The real
               hazards are named instead: per-node `Ceil` in
               `traversalTicks` and per-node `Div` in the predicted wait.
             - (3) Rule 2 names its three sources: `IWorldSystem`'s
               load-time answers, the `FlowGraph` node behaviour, and the
               pax profiles' walk speeds. Rule 4 rebuilds from exactly
               those.
             - (4) The oracle test now requires these cases:
               - distinct walk speeds released from one node in one tick,
                 choosing different edges;
               - exact-cost gate and edge ties;
               - lane changes;
               - a blocked re-route;
               - a show-up spike;
               - a restart.
               Rule 3 explains why differing destination sets cannot occur
               at Phase 0/1: the effective set is filtered by
               `CanReachVia` from the current node. It also binds the
               amendment that makes them possible to extend the key and the
               test. The restart case says what it proves: until
               `sim.save`, replay shows reproducibility only (finding 5).
Revision 2:  after the second PR #55 review (rejected at 2dab56a):
             - (1) Rule 3's claim that cohorts on one node cannot differ in
               destination set was false. `Inject` accepts any
               `FlowDirection` (§9.7). Rule 3 now fixes the key itself: an
               entry serves a cohort only for the same node, walk speed and
               every cohort field that determines the destination set,
               which is `Key.Direction` at Phase 0/1. The allowed forms are
               `(node, Direction, walk speed)`, or `(node, walk speed)`
               used for `Departing` cohorts only. A `(node, walk speed)`
               memo serving every direction is a review rejection. The
               oracle test gains a Direction case: a `Departing` and a
               non-`Departing` cohort on one node in one tick, in both
               `CohortId` orders. How non-`Departing` cohorts are routed is
               filed as Q-040 (OPEN), and the cache rules do not depend on
               its answer.
             - (2) The assertion now covers every cohort that attempts
               release, not only those that leave. A refused cohort's
               target is checked through `BlockedBy`, and a `Queue`'s
               served count through the oracle's FIFO service. The restart
               paragraph no longer overclaims: the per-tick assertion
               covers scripted states, and review covers the rest until
               the restore arm exists.
             - (3) The test's owner is "the Test Author of whichever
               `sim.flow` task adds the cache" everywhere.
             - Notes tidied:
               - the edge-tie "file order" clause is dropped, because
                 `18` §18.3 returns edges in ascending id;
               - the gate tie now puts the lower-id gate behind the
                 higher `EdgeId`;
               - rule 2 lists all of `IWorldSystem`'s load-time answers.
Revision 3:  after the third PR #55 review (rejected at ba17fda):
             - (1)/(3) **Q-040 is answered (a).** At Phase 0/1, `Inject`
               rejects `key.Direction ≠ Departing` (§9.6, §9.7), because
               the spec defines no destinations for other directions. Rule
               3's key is therefore `(node, walk speed)`. The Direction case
               and its comparison run are removed, which leaves no
               undefined oracle step.
             - (2)/(4) The oracle is now a lockstep, uncached **reference
               model of §9.12's whole tick**, carrying its own credit,
               cohorts, episodes and flags from tick 0. Every input is
               named with its source: the flow-graph JSON the test writes,
               the content it builds, `IWorldSystem`, and the script.
               Nothing comes from `FlowGraph` internals or `ServiceCredit`.
               It compares:
               - per-node, per-`CohortKey` head counts;
               - `Population` and `PredictedWaitMinutes`;
               - `(kind, Held, BlockedBy, Key)` event multisets.
               It does not compare `CohortId`s, which settles the "served
               part" note.
             - Notes fixed:
               - the "differing-set case" wording;
               - the restore-arm sentence is moved out of the Review
                 bullet.
Revision 4:  after the fourth PR #55 review (rejected at b9b0371):
             - (1) §9.3 and §9.12 now say that **every move takes a new
               `CohortId`**: a whole cohort as well as a served part,
               allocated in movement order. That fixes the relative id
               order of same-tick arrivals, and so the `Queue` FIFO
               tie-break. It is the merged behaviour (`MoveCohortPortion`
               always allocates), so no code change follows. The reference
               keeps its own id counter, advanced at the same points, and
               uses it only for ordering.
             - (2) §9.7 `Inject` gives a total check order: unknown `at`,
               not a `Source`, `count <= 0`, then direction. The first
               three are the merged order.
             - (3) The reference's scope names §9.7's `Inject` and
               `Absorb`, including boarding, missed removal and
               `FlowUnblocked`.
             - (4) A `FlowUnblocked`'s `Key` is the one the test remembered
               at the episode's `FlowBlocked`.
Revision 5:  after the fifth PR #55 review (rejected at 9f37ffa):
             - (1) The reference test pins where the script runs:
               `sim.world` at 1, one scripted caller at 2, `sim.flow` at 4
               (as `FlowRig`), and an optional recorder after it that
               calls nothing. Every `Inject` and `Absorb` is made from the
               caller's `Tick`, and `SetServersOpen` is a phase-1 command.
               The reference applies each tick as commands, then calls,
               then its own `Tick`. So `Absorb` never follows `sim.flow` in
               a tick, and the `Key` rule always applies.
             - (2) `test_inject_rejects_non_departing_direction` is pinned
               in §9.7. It covers `Arriving` and `Transferring`, and
               asserts "changes nothing" against a control run, including
               the next id, so the `IIdAllocator` counter is not advanced.
             - (3) A long line in "The reference" is rewrapped.
Raised by:   Q-036 (Test Author / T-011, via coordinator), Q-037 (Architect),
             Q-040 (Architect, from the review)
Impact:      `main` has no cache in `src/sim/flow`, so it violates no cache
             rule. **But merged T-007 code does not conform to Q-040:**
             - `Inject` accepts every `FlowDirection`;
             - `AttemptRelease` routes every cohort to the pooled gates.
             The Planner needs a small `sim.flow` fix task:
             - writable paths `src/sim/flow/**`;
             - `Inject` throws `ArgumentException` for `key.Direction ≠
               Departing`;
             - Test Author test `test_inject_rejects_non_departing_direction`;
             - no other merged test injects a non-`Departing` cohort, and
               `FlowSystemTests.cs:52` only queries.
             PR #56 (T-010) adds a per-tick cache keyed by (node, walk
             speed). That is the Phase 0/1 key under rule 3. #56 must also
             meet the other rules and carry the reference-model test.
             T-011's 24-gate `StressDay` conforms to Q-037. For the Planner: a future `sim.flow`
             performance task:
             - writable paths `src/sim/flow/**`;
             - depends on T-010 merged;
             - released only on a failing budget measurement (the 90k
               max-tier fixture decides, owner, 2026-09-28);
             - done condition: the existing tests plus the oracle test;
             - reviewed by `reviewer-core`, since routing is
               determinism-critical.
             No scope added.
Signed off:  owner, 2026-09-28: go-ahead for both drafts. Gate assignment
             is not brought forward.
LOW CONFIDENCE — owner may revise: the recommended 60 pooled `Gate` nodes
             for the 90k max-tier fixture, one per max-tier stand
             (open-questions Q-037). It is a recommendation, not a spec
             rule. The count is the Test Author's, with the derivation
             stated.

## 2026-09-28 — spec/11 §11.2, §11.3, §11.4, §11.6, §11.7, §11.9, §11.9a, §11.10; INDEX; open-questions — Q-038, Q-039: show-up bound, order within `Tick`, `FlightId` bound
Reason:      The PR #54 (T-008) review found two gaps.
             - **Q-038:** an unbounded `minutes_before_std` can put an
               injection before its flight's publication, or before its
               day is materialised. Those passengers are then lost, or
               injected before `FlightPlanPublished`. The fix:
               - `MAX_SHOW_UP_MINUTES_BEFORE_STD` (1440, derived from the
                 publish lead) is checked at `CreateSystem`, which makes
                 injection tick ≥ `PublishTick` hold on every day;
               - a fixed order for `sim.schedule`'s calls within `Tick`:
                 materialise, then publish, then inject;
               - a statement of what is observable: injection tick ≥
                 publication tick. On a shared tick, `sim.flow` holds the
                 cohort (phase 2) before handlers see
                 `FlightPlanPublished` (phase 3). That order is explicitly
                 accepted.
             - **Q-039:** `RowOrdinal` indexes the whole file, but only rows
               per day were bounded, so `FlightId`s could collide across
               days. The fix: `MAX_FIXTURE_ROWS` (99999) on total rows, a
               line-numbered failure, and a statement of exactly what the
               derivation guarantees.
Raised by:   Q-038, Q-039 (Reviewer, PR #54, via coordinator)
Impact:      `sim.schedule` is not merged, so nothing breaks. For T-008
             (PR #54):
             - rename `MAX_FIXTURE_ROWS_PER_DAY` to `MAX_FIXTURE_ROWS` and
               make it a total-row check with the line number (this also
               clears review findings 3 and 4);
             - add the show-up bound check at `CreateSystem`;
             - compute injections at publication, per the Tick order,
               rather than at materialisation (finding 1).
             For the T-008 Test Author, four new tests (§11.10). Every
             fixture that loads today keeps its ids and hashes.
             `minutes_before_std` values, checked on `main` and on
             `test-author/T-008-schedule-loader-tests`:
             - `data/pax_profiles` peaks at 180;
             - the T-008 `ScheduleTestKit` curves peak at 180;
             - `tests/fixtures/content` peaks at 120;
             - `sim.core`'s content-loader tests go well past the bound:
               `LoaderTests.cs` line 191 loads 4294967295, and lines 259
               and 401 are parameterised. They test the `sim.core` loader
               alone and never reach `sim.schedule`'s `CreateSystem`, where
               the bound is checked, so they are unaffected.
             No scope added.
Revision:    after the PR #57 review (rejected at 18f5960):
             - §11.6's "publication precedes injection" claim is replaced
               by the observable guarantee and the explicitly accepted
               shared-tick order;
             - the Q-038 test is made observable and covers days 0, 1 and
               2 at 00:00;
             - this Impact statement is corrected;
             - §11.9a names the reported bucket (the first over the bound)
               and the order of failures (row order, then `aircraft_type`,
               then `pax_profile`, then the bound);
             - the Q-039 test has a concrete shape (99 999 `A` rows on day
               1, at most 70 per minute, days 1 to 3), with the day-0
               event-limit trap stated.
Revision 2:  after the second PR #57 review (rejected at 90cc883):
             - the Q-038 test fixture is pinned: `pax=10`, both permille
               values 0, curve `[60/400, 1440/600]`. That gives 4 + 6 with
               no remainder, so the 1440-minute bucket always injects 6;
             - the run's four published occurrences (days 0 to 3) are all
               asserted, with each one's publish tick and both injection
               ticks listed. The latest is 42600, inside the run;
             - §11.7's registry sentence is reworded to match §11.6's
               phase-3 statement;
             - the INDEX line is reworded to stay true whichever of #55 and
               #57 merges first;
             - §11.9a is rewrapped.
             The red CI was the `WorldBudgetTests` timing flake, unrelated
             to this diff, and was rerun by the coordinator.
Revision 3:  after the third PR #57 review (rejected at f16c2f7):
             - `test_profile_with_show_up_beyond_publish_lead_fails_load`
               is pinned, with one case per §11.9a rule:
               - (a) 1440 loads, 1441 fails;
               - (b) `pax=0` still fails;
               - (c) the curve `[60/500, 1500/300, 2000/200]` names 1500,
                 not 2000;
               - (d) the failure order, over two rows, both ways, and
                 within one row.
               Multi-row files list rows in descending `flight_ref`;
             - §11.9 Budget now separates the publication queue (built at
               load and materialisation) from the injection queue
               (storage reserved at materialisation, entries written at
               publication), matching §11.6 step 2;
             - §11.10 says day 1 is also the 00:00 edge (tick 0);
             - this header lists §11.7 and §11.9.
Signed off:  not required. `01` and `02` are untouched. The 1440-minute
             show-up bound (Q-038) is owner-confirmed 2026-09-28.

## 2026-09-29 — spec/07 L11a (new); INDEX — the Slow test category
Reason:      HUMAN DECISION — owner, 2026-09-29: long-running tests get a
             `Slow` category. PR push runs skip them. A green pre-merge Slow
             run is required for every PR except one that changes only
             `spec/`, `tasks/`, `agents/` or other docs that cannot affect
             a build or test outcome. The run is on the PR's head, which
             must contain the current `main`, and a failure blocks the
             merge (owner, 2026-09-29, answering the Architect's
             done-means-green question). They also run on every push to
             `main` and nightly, next to the soak. L11a records the
             convention:
             - the trait `[Trait("Category", "Slow")]`, on a test method,
               which may be combined with `Budget`. A `[Theory]` counts
               summed over its rows;
             - the rule for when a test must be Slow: (a) it steps more than
               144 000 ticks in total, which is exact and machine-free; or
               (b) time, judged only on the latest CI duration: the PR's
               test job for an untagged test, the Slow run for a tagged
               one. An untagged test over 5 s must be tagged before its
               PR merges. A tagged test stays tagged at 2.5 s or more,
               whatever caused the change. Under 2.5 s the tag is removed
               by the next Test Author change to that test, which is not a
               merge blocker. With no CI measurement of a test, rule (b) is
               judged on nothing and only rule (a) applies; an author's own
               run only prompts the tag on a new test. The owner's CI
               wiring must report each test's duration in its run log;
             - that a `Budget` or gate test may also be Slow, and that its
               failure blocks the merge through the pre-merge run. It is
               never fixed by retagging;
             - that Slow changes no `ci/run-checks.sh` harness gate,
               including `tools/SimHarness budget`;
             - that T-009 is Slow by rule (a).
             The thresholds are the Architect's. 10 sim-days matches `02`'s
             `determinism_same_process` size. 5 s sits in a measured gap:
             in a `Release` run of `main` at `4c3d900` on the owner's
             laptop, 1223 tests took 103 s summed. The six slowest took
             33.9, 10.2, 7.6, 7.5, 7.2 and 6.1 s, the next 3.4 s and
             below, so 5 s catches exactly those six, about 72 of the
             103 s.
Raised by:   owner, via coordinator
Impact:      T-009's Test Author tags its 100-day test `Slow`. A search of
             merged `tests/` found no test that steps more than about two
             sim-days, so none meets rule (a). The owner's laptop run is
             not authoritative, but it prompts tagging these six merged
             tests now, and CI's measurements decide afterwards. A Test
             Author task must add the trait, because workers never edit
             tests:
             - `PromotionBudgetTests.test_promotion_budget_one_day_with_every_node_promoted`;
             - `FlowHeadlessDayTests.test_flow_headless_day_keeps_every_invariant`;
             - `FlowStressBudgetTests.test_flow_stress_30k_day_update_path_allocates_nothing`;
             - `ScheduleHashTests.test_schedule_hash_identical_with_and_without_flow_registered`;
             - `RoutingCacheTests.test_flow_routing_cache_matches_uncached_reference`;
             - `FlowStressBudgetTests.test_flow_stress_30k_day_tick_within_budget_and_bounded_cohorts`.
             Four of them are budget, stress or kill-gate tests, including
             T-011's. Their regressions still block merges, through the
             pre-merge Slow run. The CI filter and the manually triggered
             pre-merge workflow are the owner's (`ci/`, `.github/`). For
             the Integrator: before merging any PR that is not docs-only,
             confirm a green Slow run on its head commit, and that the head
             contains the current `main`. No scope added.
Revision:    after the PR #61 review (rejected at ae76983):
             - rule (b) now has one authoritative measurement, CI;
             - the untag rule reads the same in L11a, this entry and
               INDEX, and is judged on the latest CI measurement whatever
               the cause;
             - "a test" is a method, with a `[Theory]` summed over its
               rows;
             - the pre-merge scope is every PR that is not docs-only, on a
               head that contains the current `main`. The earlier
               "`src/` or `tests/`" scope was a relay error.
Revision 2:  after the second PR #61 review (rejected at 623e5fe):
             - per-test duration reporting in CI run logs is named as a
               required part of the owner's wiring;
             - until a CI run reports a test's duration, rule (b) is judged
               on nothing, and only rule (a) applies;
             - the hysteresis wording is exact: a measurement between
               2.5 s and 5 s never changes the tag, and only crossing a
               line does.
Signed off:  owner, 2026-09-29 (the category, where it runs, and the
             pre-merge requirement). The thresholds are the Architect's.

## 2026-09-29 — spec/19 intro, §19.2, §19.2a (new), §19.4, §19.6 (new); 08 §8.5; 09 §9.10; INDEX; open-questions — Q-041, Q-042, Q-043: T-009's test location, the Phase 0 CLI composition, the boarding stand-in
Reason:      T-009's Test Author raised three questions.
             - **Q-041:** the task file put the tests in
               `tests/sim/core`, which `07` L3 cannot compile against the
               factories or `HarnessGates`. `tests/tools/simharness` is
               confirmed.
             - **Q-042:** §19.2 deferred the CLI composition to "the first
               composing task" but never specified it. §19.2a now pins
               one Phase 0 composition for every subcommand:
               - four Test Author fixtures, one of them a new content
                 fixture read through a manifest, never `data/`;
               - how the harness finds them (the `AirportSim.sln` root,
                 Q-031);
               - when loading happens, and that its failures are exit 3;
               - construction and registration order.
               T-006's CLI tests replace their empty-composition hashes
               with the Test Author kit's `FinalHash`. §19.2's promise
               that exit codes 1 and 3 become reachable through the CLI is
               withdrawn.
             - **Q-043:** confirmed that nothing calls `Absorb` at Phase
               0, so the `Gate` grows by about 400 cohorts a sim-day, and
               the merged pairwise merge makes that quadratic. A
               harness-internal boarding stand-in at registry position 3
               calls `Absorb` at each departure's STD, hashes 0, and is
               removed when `sim.airside` joins. `08` §8.5 names it as the
               only non-test probe. `09` §9.10 states that `Absorb` bounds
               the keys.
Raised by:   Q-041, Q-042, Q-043 (Test Author, T-009, via coordinator)
Impact:      - **No merged `src/` changes.** `sim.flow`, `sim.schedule`
               and `sim.world` are untouched.
             - **Merged tests change**, all of them T-006's CLI tests in
               `tests/tools/simharness/HarnessCliTests.cs`. Seven expected
               hashes move from the empty composition to the Phase 0 kit
               composition, the equivalence test switches to the kit, and
               one budget test is renamed (§19.6). The T-009 Test Author
               makes these edits in the T-009 test branch, and they merge
               with the T-009 worker's harness change (`07` L9). CI's
               `determinism`, `saveload` and `promotion` hashes all change
               when T-009 merges. That is expected, and no golden is
               checked in `ci/`.
             - **T-009 test branch** (`f531ca7`): the content moves from
               C# into `tests/fixtures/harness/`; the kit gains the probe
               at position 3; the "nothing absorbs" load check becomes the
               §19.6 conservation, `Absorb` and cohort-ceiling checks.
             - **T-009 worker** (`tools/SimHarness/**`): adds the three
               `ProjectReference`s, the fixture locator, the manifest
               content source, the composition and the stand-in.
             - **Planner:** correct `tasks/T-009-100-day-run.md` (tests
               path, writable paths `tests/tools/simharness/**` and
               `tests/fixtures/harness/**`, readable specs, and the
               stand-in in its description), per Q-041.
             - **Scope:** none added. The stand-in is a harness test
               driver for an existing `12` §12.7 call, not a game system.
             **HUMAN DECISION — owner, 2026-09-29 (Q-043): accepted.** The
             boarding stand-in is a valid reading of the kill gate. The
             gate's scope is unchanged: the same modules, fixtures, 100
             days and 60 s. The Architect had first marked this LOW
             CONFIDENCE.
             **For the owner, not decided:** 60 s for 1 440 000 ticks is
             about 41.7 µs a tick, about 1/144 of `01`'s 6 ms tick. It is
             unmeasured with the stand-in. A miss escalates to the owner
             under T-009's "Done when".
Revision:    after the PR #66 review (rejected at 93abebf):
             - Q-044 and Q-045 (the budget window and arithmetic) are
               removed from this PR and move to their own spec PR. The
               `03` edits are reverted here;
             - §19.6(a) is a whole-run wall-clock gate: 60 s in total, one
               `long` `Stopwatch` measurement, and not subject to `03`'s
               per-tick statistic, although it carries `Budget`;
             - §19.6(b)'s load check applies to the second run;
             - §19.2a "When" says the three graph and schedule parses
               happen in the first run's `compose`. §19.3's exit-3 row
               covers pre-run fixture failures;
             - the stand-in's day list is flagged for the snapshot-based
               `SaveLoad` amendment;
             - the Test Author may update `HarnessCliTests.cs` comments;
             - the INDEX `08` entry names the §8.5 line, and Q-043's Status
               uses the standard format.
Signed off:  owner, 2026-09-29 (the Q-043 boarding stand-in). The rest is
             architecture and needs no sign-off.

## 2026-09-30 — spec/12 §12.3, §12.4 (+ "File format"), §12.5, §12.6, §12.7, §12.8, §12.8a (new), §12.10, §12.11, §12.12, §12.12a, §12.13; 13, 14, 15 (fixture name); 16 (file table); 04 (airside_rules.json); 08 §8.10 (`LogKey`); INDEX; open-questions — Q-046 to Q-056: T-021 `sim.airside` gaps
Reason:      T-021's Test Author (branch `test-author/T-021-airside-tests`,
             `d10a9be`, 56 tests) reported 11 gaps in `12`:
             - **Q-046:** the layout fixture format is pinned as JSON.
               `Load` also checks node kinds, ranges and non-empty lists,
               and returns lists sorted by id;
             - **Q-047:** `AirsideRules.DoorsOpenDelayMinutes` is added;
             - **Q-048:** `InboundAirborne` is clamped at tick 0;
             - **Q-049:** a least-queue runway choice (stopgap);
             - **Q-050:** a stand is reserved from assignment, and there is
               one stand-wait queue, which is hashed;
             - **Q-051:** assignment takes the lowest compatible free
               `StandId`;
             - **Q-052:** a landing is requested at `STA`, and the
               early-aircraft clause is removed;
             - **Q-053:** a rotation-less departure with no stand waits in
               the queue;
             - **Q-054:** edges and stands are read at start of tick, so a
               release happens at `t + 1`;
             - **Q-055:** "two integers" is now true;
             - **Q-056:** `LogKey.AirsideReassignStandNoOp = 1`, with
               reason codes.
Raised by:   Q-046 to Q-056 (Test Author, T-021, via coordinator)
Impact:      - `sim.airside` is not merged, so no merged `src/` breaks.
             - **Merged, and must change:**
               - `data/schemas/balance.schema.json` (`additionalProperties:
                 false`, one required key) gains the required
                 `doors_open_delay_minutes`. That is an agent content task;
               - `data/balance/airside_rules.json` must gain
                 `"doors_open_delay_minutes": 2`, and only the owner may
                 write it. **The schema change and the owner's edit must
                 land in the same commit, or at least the same merge.**
                 Otherwise `ci/run-checks.sh`'s content-schema check is
                 red on `main` in between;
               - `src/sim/core/LogKey.cs` gains member 1. T-021's writable
                 paths need that file (Planner), serialised with other
                 `src/sim/core/**` work;
               - `tests/fixtures/content/valid/balance/airside_rules.json`
                 (T-027's loader fixture) keeps the one-key shape. The
                 content loader ignores `balance/` (`08` §8.11), so nothing
                 fails. A Test Author may update it for accuracy, and it is
                 not required.
             - **`tasks/T-021-runway-taxiway-stands.md`** (Planner)
               restates text this PR supersedes: the one-field
               `AirsideRules` (line 126), "the fixed door delay" (line
               175), and "earliest-declared" (line 205). It must point to
               `12` instead, together with the writable-path addition.
             - **T-021 test branch:**
               - `AirsideRules` gains a second constructor argument;
               - add the fixture JSON and the seventeen new §12.13 tests;
               - `Load` messages must contain the §12.4 field names;
               - check that no test assumes a same-tick stand or edge
                 release, an unreserved stand in taxi-in, a
                 `queuePosition = 0` hold, or a track for a rotation-less
                 departure that is still waiting.
             - **`app.host` (`16`, T-031):** the host parses
               `airside_rules.json` and must read the new key, passing it
               as `AirsideRules.DoorsOpenDelayMinutes`. `16`'s file table
               now says so. T-031's task file should cite it (Planner).
             - **`tasks/T-028-content-schemas.md` line 53** gives the
               one-key `airside_rules.json` shape. The content task that
               changes `balance.schema.json` updates it (Planner).
             - **Merge note:** Q-041 to Q-045 merged earlier, with PRs #66
               and #68. The resulting `open-questions.md` and
               `CHANGELOG.md` conflicts are resolved in this branch.
             - **Scope:** none added. The runway choice is a stopgap rule,
               not a system.
             **HUMAN DECISION, owner, 2026-09-30:**
             - `doors_open_delay_minutes = 2` (Q-047). The owner writes it
               to `data/balance/airside_rules.json`;
             - Q-049's least-queue runway stopgap is accepted. It was
               marked LOW CONFIDENCE.
Revision:    after the PR #67 review (rejected at 5f29a25):
             - **Q-053 redesigned.** No track until a stand is assigned, and
               no event while waiting. The track starts in `OnStand` at the
               assigned stand, so §12.3 and §12.9 stay true. Lateness goes
               to `Unexplained` under `14` §14.6, unchanged. The fallback's
               creation tick is the actual `OnStand` tick;
             - **§12.8a (new):** the step order S1 to S7 inside `Tick`,
               with one start-of-tick snapshot for edges and stands. The
               same-tick order is pinned for runway requests and releases
               (S7) and for new stand requests (S5, ascending `FlightId`
               after the queue). The stand-wait queue is in joining order;
             - `Load`'s failures are pinned: check order, field names and
               ids for every kind. Range failures carry no `line <n>`;
             - non-blocking fixes:
               - the §12.12 budget text, and the queue's storage and cost;
               - Q-056 check 1 excludes a handed-off arrival;
               - a waiting arrival is `HeldOnTaxiway`;
               - a node-holding aircraft occupies no edge;
               - the rotation-less due tick is clamped to 0;
               - the planned `TakeoffRoll` names the runway chosen at
                 `Pushback`;
               - the `18` §18.2 `true`/`false` exception is explicit;
               - the Q-054 Status anchor points to §12.8a;
               - the INDEX `04` and `08` entries are updated;
               - the impact list is completed;
             - the owner's decisions on Q-047 and Q-049 are recorded.
Revision 2:  after the third PR #67 review (rejected at c0cd79e):
             - **runways are the S1 exception.** S7 reads runway
               `Occupant` and `NextSlotTick` live, so a runway cleared in S3
               is claimable in S7 of the same tick;
             - **chains (§12.8a).** An action that makes another §12.8
               action due at the current tick runs it at once, in the same
               turn. This pins `DoorsOpenDelayMinutes = 0` and
               `MinTurnaround = 0`, and "0 = the same tick" holds. A
               departure created in an arrival's turn runs its chained
               actions inside that turn, and not at its own `FlightId`
               position;
             - **`Load` messages.** Range check 1 covers only an object's
               own `id` and its value fields. A `0` in a node-reference
               field fails at check 4. Within an object, the first failing
               field in file-format key order is named;
             - **hard bounds.** `STAND_WAIT_CAPACITY = 1024` and
               `PENDING_FLIGHTS_CAPACITY = 2048` are preallocated and never
               grow. Overflow is `SimInvariantException`. This replaces the
               growth-allowed text, which contradicted `08` §8.5 and `07`;
             - **finding flights without a scan (§12.11).** A pending list
               is fed by a day-0 read at `CreateSystem` and by a
               `FlightPlanPublished` subscription. The pending list is
               hashed;
             - non-blocking fixes:
               - §12.8's `StandUnavailable` claim covers arrivals only;
               - the §12.3 departure `OnStand` is clamped;
               - the §12.7 LOW CONFIDENCE note matches the chain rule;
               - duplicate, unknown and missing keys are parse failures;
               - §12.8a is in INDEX and in this entry's heading;
               - the stale `phase1-single-runway.*` references are fixed
                 in `13`, `14` and `15`, and `16`'s fixture-format text;
               - `16` and T-031, and T-028 line 53, are in Impact.
Revision 3:  after the fourth PR #67 review (rejected at b2f3e64):
             - **pending removal (§12.11).** An entry leaves when it is taken
               at its start tick: an arrival in S2, a rotation-less
               departure in S5, whether it gets a stand or moves to the
               stand-wait queue. It is never in both. The 2048 bound is
               argued from this rule: a one-day window holds at most
               1 600 flights at max tier;
             - **late due ticks.** A departure due at or before its
               publication starts at `PublishTick + 1`. `PlannedTick` keeps
               the §12.3 formula, which is schedule-anchored per `10` §10.4,
               and `ActualTick` is later. §12.3, §12.7 and §12.11 agree;
             - **construction overflow.** The day-0 read throws
               `ArgumentException` (`schedule`), because `08` §8.5a's
               `SimInvariantException` exists only during a tick;
             - non-blocking fixes:
               - §12.8 step 3's `PlannedTick` is clamped;
               - §12.6's "asking" list covers S5 grants from the queue, S5
                 chain pushbacks and S6.1 chain pushbacks;
               - §12.7's assignment rule covers queued arrivals;
               - §12.2's wording is corrected (up to 1 600 entries, across
                 two calendar days);
               - four pending tests are added to §12.13, making seventeen
                 new tests in all.
Revision 4:  after the fifth PR #67 review (rejected at a5818cb): the stale
             wording is made consistent across `spec/`:
             - §12.7 S5 step 2 and the §12.11 intro now say *start tick*,
               `max(due tick, PublishTick + 1)`, and Q-050's revision
               matches;
             - §12.12 "Hard bounds" and the INDEX `12` entry now say
               overflow during a tick is `SimInvariantException`, and
               overflow in `CreateSystem`'s day-0 read is
               `ArgumentException`;
             - §12.11 "Overflow" says "during a tick" instead of "in
               `Tick`";
             - the test count is corrected to seventeen new §12.13 tests
               (thirteen, plus four pending tests).
Signed off:  owner, 2026-09-30 (the Q-047 value and the Q-049 stopgap). The
             rest is architecture and needs no sign-off.

## 2026-09-30 — spec/03 "How a budget is measured" ("Measured" bullet, new "Budget tests: window and arithmetic"); 07 L11 (cross-reference); 19 §19.4; INDEX; open-questions — Q-044, Q-045: budget window, arithmetic, `sim.core`
Reason:      While filing T-041, the Planner found that `03` fixes neither
             a budget test's sample window nor how p99 is computed. This
             replaces the answer first attempted in PR #66, which was
             rejected there. That attempt conflicted with `07` L11's
             `long`-only rule, claimed conformance falsely, left
             invalidated tests unnamed and `sim.core` undecided, and gave
             §19.4 a different pass condition.
             - **Q-044:** exactly one sim-day of per-tick samples. The
               scope is every timed per-tick budget test and §19.4. It
               excludes allocation-only `Budget` tests and whole-run
               gates. `sim.core` is bound, with a sample of one `Step(1)`.
             - **Q-045:** `long`-only arithmetic, with each sample rounded
               up to µs and an overflow guard, mean `Σu ≤ B·n`,
               nearest-rank p99 `≤ 2B`, and a rounded-up reported mean.
               §19.4 uses the same condition, and its flooring is removed.
Raised by:   Q-044, Q-045 (Planner, T-041, via coordinator); PR #66 review
Impact:      - **Merged tests invalidated, 9, all named in Q-045.** They
               are `sim.core` `BudgetTests` (2 day tests),
               `CommandQueueTests` (1 day test), `FlowBudgetTests`,
               `FlowStressBudgetTests`, `PromotionBudgetTests`,
               `sim.schedule` `BudgetTests` (2) and `WorldBudgetTests`
               (T-041). Each needs a Test Author rewrite. The Planner files
               the task or tasks. `WorldBudgetTests` is already T-041. The
               15 allocation-only `Budget` tests are out of scope, and the
               2 `HarnessCliTests` budget tests stay valid.
             - **Merged harness code:** `tools/SimHarness`'s `budget`
               subcommand rounds up instead of flooring (§19.4). T-009's
               worker is in `tools/SimHarness/**` now, so the Planner folds
               this into T-009, or files it after T-009 merges, to avoid a
               conflict.
             - **Open branches, which must change:**
               - T-021's `AirsideBudgetTests`
                 (`test-author/T-021-airside-tests`, `d10a9be`) uses
                 rescaled raw-tick comparisons with no per-sample µs
                 round-up, which is non-conforming like
                 `PromotionBudgetTests`. The T-021 Test Author rewrites it
                 to `03`'s rule before T-021 merges;
               - T-009's kill-gate (a) is a whole-run gate and is out of
                 scope.
             - **Merge note:** PR #67 appends to `open-questions.md` and
               `CHANGELOG.md` after the same lines, so expect textual
               conflicts only.
             - No `src/sim` change. No scope added.
Revision:    after the PR #68 review (rejected at ae74944):
             - the T-021 `AirsideBudgetTests` claim is corrected (above,
               and in Q-045);
             - `03` states the frequency bound behind the cap's
               no-verdict claim;
             - `03` says any 14 400 consecutive ticks after warm-up form a
               window, with no alignment to a sim-day boundary;
             - the `19` §19.6 cite resolves, now that PR #66 is on `main`.
Signed off:  not required (measurement protocol, not balance).

## 2026-10-01 — spec/12 §12.3, §12.6, §12.8 (+ "The handed-off arrival", new), §12.8a, §12.9, §12.10, §12.11, §12.12, §12.13; 03 "How a budget is measured"; 07 "Performance"; 08 §8.5, §8.6; 09 §9.10; 10 §10.6; 13 §13.11; 14 §14.3, §14.13; INDEX; open-questions — Q-060 to Q-062: T-021 airside gaps, round 2
Reason:      T-021's Test Author found three gaps in the merged `12` (#67):
             - **Q-060:** which flight a taxi hold's `Blocking` names when
               the edge was free in the snapshot but granted earlier in
               the same step. It is the snapshot occupant if there is
               one, even one that left in S6.1, else this step's
               grantee. It is never null on the hold, and always null on
               `AircraftHeldOnTaxiwayReleased`, like `HeldAt` on
               `DepartureHeldForPassengersReleased`;
             - **Q-061:** whether event handlers are in the "update
               path". They are, for every module. `03` now defines the
               update path as phases 1 to 3: command `Apply`, `Tick`,
               event handlers, and queries called then. It also says how
               an allocation test meters it: `Step` windows without a
               checkpoint, or a direct rig that delivers the handlers'
               input in the same window. Every module with a handler
               has one test that runs each handler inside the window.
               Q-061 left timing unchanged. Q-064 (PR #75) later
               extends "Measured" to the same handler work;
             - **Q-062:** the handed-off arrival's track. It leaves
               tracked state at the handoff, on both paths, so it has no
               phase or stand left to pin, and it is no longer hashed or
               drawn. `10` §10.3 rule 2 already treated the handoff as
               the arrival's exit. So that `DoorsOpen` always fires
               first, the handshake's handoff never runs before the
               arrival's `DoorsOpen`. When `DeboardComplete` comes first,
               the handoff chains right after `DoorsOpen`.
Raised by:   Q-060 to Q-062 (Test Author, T-021, via coordinator)
Impact:      - **`sim.airside` is not merged**, so no merged `src/` breaks.
               `src/sim/core/AircraftHeldOnTaxiway*.cs` already carry
               `FlightId? Blocking`, so no payload type changes.
             - **T-021 test branch** (`test-author/T-021-airside-tests`,
               `432c081`):
               - add the five new §12.13 tests (two for Q-060, one for
                 Q-061, two for Q-062);
               - `TaxiEdgeTests` already expects the snapshot occupant
                 (`d1`), which is consistent. No test asserts a
                 non-null release `Blocking`;
               - `ReassignStandTests`' handed-off case still logs
                 reason 1. Its comment "R_A's track stays OnStand" is
                 now wrong, and should say the track is gone.
                 `HandshakeTests`' `TrackedFlights` subset check still
                 holds;
               - the airside allocation test must also meter the
                 `FlightPlanPublished` handler, `ReassignStand`'s
                 `Apply`, and, with a position-5 probe, the
                 `FlightMilestoneReached` handler (§12.12).
             - **Q-061 on merged modules' tests** (`main`, `84c754d`):
               - `sim.schedule` and `sim.world` register no handler, so
                 `ScheduleTickTests`' `Tick`-only meter conforms, and so
                 does `WorldQueriesTests`;
               - `sim.core`'s allocation tests cover the loop and the
                 bus, and are unaffected;
               - `sim.flow` meters `Step` windows in
                 `FlowBudgetTests.test_flow_budget_update_path_allocates_nothing`,
                 `BoardingTests.test_boarding_thousands_of_clean_flights_allocate_nothing_and_leave_no_state`
                 and `PromotionAllocationTests`. None of these carries the
                 `Budget` trait, and none needs it: `03` counts any test
                 that asserts zero allocation (revision below). They
                 include its six no-op event handlers whenever those
                 events fire in the window, which the Test Author
                 confirms. **No metered window applies a
                 `SetServersOpen`**, so `sim.flow` has no conforming
                 test for its command handler. T-023's merged
                 `SecurityLaneBudgetTests.test_security_lane_switching_every_tick_allocates_nothing_in_flow_tick`
                 (#71) switches lanes every tick but meters `sim.flow`'s
                 `Tick` alone, so phase 1's `Apply` is outside its meter.
                 Metering each `Step(1)` instead, with the submits left
                 between `Step`s, makes it conform. Like the others, it
                 needs no `Budget` trait. The Planner files that as a
                 test-only task under `tests/sim/flow/**`. No `src/`
                 change is expected;
               - T-022 (`13`) and T-024 (`14`, whose work is mostly in
                 handlers) must meet the rule. `13` §13.11 names
                 T-022's test, and `14` §14.13 says so.
             - **Q-062 and the T-021 worker:** the handoff removes the
               arrival's track, and the handshake waits for `DoorsOpen`.
               The removal also bounds the tracked set, which would
               otherwise grow by one track per rotation arrival, forever.
             - **`tasks/T-021-runway-taxiway-stands.md`** (Planner): if
               it restates the handoff or the allocation scope, it must
               cite `12` instead.
             - **Not decided here, flagged:** `03` "Measured" times
               `Tick` only. A module that works in its handlers, chiefly
               `sim.delay`, therefore has almost nothing timed against
               its budget. Changing that is a budget-protocol decision
               that Q-061 did not ask. It is filed as its own question,
               Q-064, in a separate PR, before T-024.
             - **Merge note:** PR #70 (Q-057 to Q-059) merged first. This
               branch merged `main` (`84c754d`) and kept both sides of
               the `open-questions.md` and `CHANGELOG.md` conflicts.
               Q-060 to Q-062 are listed before Q-057 there.
             - **Scope:** none added.
Revision:    after the PR #73 review (rejected at `fccf76a`):
             - **Determinism, hash and save (Q-062).** Waiting for
               `DoorsOpen` kept a consumed `DeboardComplete`, and its
               `EventRef`, across ticks and checkpoints with no hashed
               home. It is now explicit state. `AircraftTrack` gains
               `EventRef RecordedCause` (§12.9). It is set only by the
               `DeboardComplete` and `BoardingComplete` handlers, and
               only for a tracked flight in `OnStand` with no record yet.
               A `DeboardComplete` is also recorded only for an arrival
               with a rotation. The action the record waits for clears
               it. It is fed with the track, as `HasValue`, `Id.Tick`
               and `Id.Sequence`, and it is saved with it. The departure
               `OnStand`'s `Cause` is read from it. This also closes the
               older one-tick gap for both consumed events, which crossed
               a phase-4 checkpoint unhashed. §12.12 now says the
               handlers write only to items 4 and 5. Q-060 is unchanged:
               state is added where behaviour reads it, and a copied
               release blocker would be read by nothing;
             - **§12.8a governs (Q-062).** S4 now runs `DoorsOpen` first,
               then the handoff only once `DoorsOpen` has fired. A
               handoff made due by this tick's `DoorsOpen` runs right
               after it. §12.3, §12.8 steps 3 and 5, the §12.11 consumed
               table, the TryGetRotation note and "Chains" all say the
               same;
             - **Q-061 conformance.** `03` and §12.12 no longer say
               "`Budget`-trait allocation test". An allocation test is
               any test that asserts zero allocation, with or without
               the trait, since `07` L11 binds timed assertions. The
               impact note above names the untagged `sim.flow` tests it
               counts;
             - **Impact added.** `AircraftTrack` gains a field, so T-021's
               worker implements and hashes it. Test code that builds or
               prints tracks (`Show.Track`) may add it. The §12.13
               handoff-wait test also checks the record and the hash. No
               merged `src/` reads `AircraftTrack`.
Signed off:  not required (interface precision; no balance, scope, or
             `01`/`02` change).

## 2026-10-01 — spec/19 §19.1, §19.2, §19.2a, new §19.2b, §19.3, §19.4, new §19.7; 03 "The soak fixture", "Budget tests: window and arithmetic"; 16 §16.8; INDEX; open-questions — Q-057, Q-058 (Q-059 filed): `soak` subcommand, budget statistic seam
Reason:      While syncing task files (PR #69), the Planner found two gaps in
             `19`:
             - **Q-057:** no `soak` subcommand, although the nightly
               workflow already runs `soak --days 500 --golden
               tests/golden/soak-500.hashes`. New §19.2b: one run of the
               §19.2a composer over a separate soak fixture set in
               `tests/fixtures/soak/`, with seed 12345. It passes iff the
               run's `16` §16.8 dump is byte-identical to the golden, and
               otherwise prints `FAIL soak line=<L>` with exit 1.
               `soak --out P` writes a dump to a new file, never
               overwriting, so that a golden can be authored. Paths are
               repository-relative from the `AirportSim.sln` root, or
               fully qualified. `soak` times nothing. The 0.1 ms bound is a
               whole-run sizing test (§19.7). The invocation matches CI as
               it stands, so `ci/` and `.github/` need no change.
             - **Q-058:** no seam to test `budget`'s rounding. New pure
               public member `HarnessGates.BudgetFromSamples(samples,
               frequency) -> GateResult`, which the CLI must call exactly
               once. `07` L5 rules out an internal function, and a CLI
               injection flag would be a seam in the gate. §19.7 gives
               five tests with an exact `Report` and one argument test.
               One of the five tells rounding up apart from flooring.
             - **Q-059, filed OPEN for the owner.** The nightly
               "Performance trend" step runs `budget --tier max --report`,
               which is a usage error under §19.3. What the report means is
               the owner's, so this change does not answer it.
Raised by:   Q-057, Q-058 (Planner, PR #69, via coordinator); Q-059
             (Architect)
Impact:      - **Additive.** No merged code or test is invalidated. `soak`
               was an unknown subcommand, so nothing implements it.
               `BudgetFromSamples` is a new member. The existing `budget`
               tests in `HarnessCliTests` are unchanged.
             - **T-009 (in progress):** none. The stand-in's `Name` stays
               free, and the value T-009 merges is kept once the soak
               golden exists. §19.2a's rules are restated to apply to
               either fixture set, with no change for the Phase 0 set.
             - **T-013:** its task file must follow §19.2b and §19.7. It
               names `soak --days 500 --seed <n>`, but there is no `--seed`
               (seed 12345). Its tests go in `tests/tools/simharness/`. The
               Test Author writes `tests/fixtures/soak/**`. The 500-day
               golden can only be generated with `soak --out` once T-013's
               worker code exists, so the Planner must sequence the
               golden commit after the implementation, under
               `tests/golden/README.md` (the owner confirms).
             - **T-045:** its spec gap is closed. The worker adds
               `BudgetFromSamples` and routes `RunBudget` through it.
             - **Nightly CI:** after T-013 merges and until the golden is
               committed, `soak` exits 3 instead of 2. For Q-059, see the
               next entry.
             - Scope: one subcommand with two forms, one public member and
               one fixture set. All of these are tooling. No sim scope is
               added.
Revision:    after the PR #70 review, which approved `f1e1961` with
             non-blocking notes:
             - a path is used as given only if
               `Path.IsPathFullyQualified`. `Path.IsPathRooted` would
               accept `/x` and `C:x` on Windows, which resolve against the
               cwd. A path that is rooted but not fully qualified is a
               usage error;
             - `soak` is dropped from the divergence line's `<gate>` list,
               and kept for the pass line;
             - the §19.7 prefix test keeps line `M`'s LF, and it states
               the result if the LF is dropped;
             - the test count reads five exact `Report`s plus one argument
               test.
Signed off:  not required (harness tooling, not balance). Q-059 was the
             owner's, and the next entry records the decision.

## 2026-10-01 — open-questions only — Q-059: nightly `budget --tier max --report` (HUMAN DECISION)
Reason:      The owner chose option (B). The owner removes `--report` from
             `.github/workflows/nightly.yml`. `19` §19.3 keeps no
             `--report` flag, and no spec text changes.
Raised by:   Q-059 (Architect, while answering Q-057)
Impact:      None to the spec or to code. The nightly "Performance trend"
             step stops failing with exit 2 once the owner's workflow edit
             lands. Until then, `budget --tier max --report` stays a usage
             error.
Signed off:  owner, 2026-10-01

## 2026-10-01 — spec/12 §12.5, §12.12, §12.13; 03 "How a budget is measured" ("Measured", new "Timing a module's handlers", arithmetic step 1, Q-061's update-path sentence); 10 §10.6; 13 §13.10; 14 §14.3, §14.13; INDEX; open-questions (Q-061 wording) — Q-063, Q-064: runway `queuePosition`, timing handler work
Reason:      Two gaps the Architect found while answering Q-060 and Q-061
             (PR #73):
             - **Q-063:** `AircraftHeldForRunway.QueuePosition` had no
               base, and its `Released` value was unspecified. It is now
               1-based and counts the flight itself: the runway's one
               hold queue's length just after the flight joins. It is
               fixed at emission, and it is always 0 on `Released`;
             - **Q-064:** `03` timed `Tick` only, so `sim.delay`, whose
               work is nearly all in event handlers, had almost nothing
               timed against its 0.40 ms. A module's measured time is
               now its `Tick` plus its command and event handler bodies.
               It is timed through non-allocating shims that the test
               installs in the module's `SystemServices`, summed per
               tick. The bus's call into a handler stays `sim.core`'s.
Raised by:   Q-063, Q-064 (Architect, during PR #73, via coordinator)
Impact:      - **Q-063:** `sim.airside` is not merged. The payload types in
               `src/sim/core/AircraftHeldForRunway*.cs` are unchanged.
               T-021's test branch prints the value, and no test there
               asserts it. One new §12.13 test. Q-060 (PR #73) gives the
               taxi family the same "empty on `Released`" rule.
             - **Q-064 on merged timed budget tests** (`main`, `84c754d`):
               - **Unaffected:** `sim.schedule`'s `BudgetTests` and
                 `WorldBudgetTests`, whose modules register no handler,
                 and `sim.core`'s `BudgetTests` and `CommandQueueTests`,
                 which sample `Step(1)`;
               - **Non-conforming:** `sim.flow` registers six event
                 handlers and `SetServersOpen`. Three of its timed tests
                 time `Tick` alone: `FlowStressBudgetTests` and
                 `SecurityLaneBudgetTests` through `StressDay`'s
                 `TimedSystem`, and `PromotionBudgetTests` between
                 probes at positions 3 and 5. `SecurityLaneBudgetTests`
                 says outright that phase 1 is outside its sample,
                 although it applies three `SetServersOpen` every tick.
                 The fourth, `FlowBudgetTests`' max-tier test, times
                 whole `Step`s over one hour. It was already listed as
                 non-conforming under Q-044, and it becomes a per-tick
                 test with shims. A test-only task
                 (Planner, `tests/sim/flow/**`) adds the shims to the
                 shared rigs. The event handlers have empty bodies today,
                 so only `SecurityLaneBudgetTests` is expected to measure
                 materially more. No `src/` change is expected;
               - **Open branch:** T-021's `AirsideBudgetTests` must shim
                 `sim.airside`'s handlers before T-021 merges. Since #73
                 (Q-062) they are real work. The `FlightPlanPublished`
                 handler appends to the pending list. The
                 `FlightMilestoneReached` handler filters every milestone
                 on the bus, the module's own included. With
                 `sim.turnaround` registered it also calls
                 `TryGetRotation` and writes `RecordedCause` for each
                 `DeboardComplete` and `BoardingComplete`. The
                 `ReassignStand` `Apply` is shimmed too. T-021's max-tier
                 test runs the fallback build, so it times the first two
                 handlers' work, filtering included, and `Apply` whenever
                 a command is in the window. The recording path is timed
                 in a build with `turnaroundRegistered` true. That is
                 T-022's integration budget, with both modules shimmed,
                 or a T-021 test with a position-5 probe whose publishing
                 is not in `sim.airside`'s sample, because the shims time
                 only `sim.airside`'s code.
             - **T-022, T-024:** their budget tests use the shims. For
               T-024 this is the whole point. `14` §14.13 says so. `12`
               §12.12 and `13` §13.10 now say that their budgets cover
               handlers too.
             - **PR #73 reconciled.** This branch merged `main`
               (`7d14761`, #73). #73's `03` sentence "This is wider than
               "Measured" above, which bills time to `Tick` alone" now
               says that the update path covers the same work as
               "Measured" plus the module's queries. #73's CHANGELOG and
               Q-061 text "Timing is unchanged" now says that Q-061 left
               timing unchanged and Q-064 extends it.
             - **Scope:** none added. **No budget value changes.**
             - **LOW CONFIDENCE (Q-064):** billing handler time to the
               subscriber means `sim.delay`'s 0.40 ms, from `01`, now
               covers its handlers. If T-024 measures over, the remedy is
               the reserve, by amendment, or the owner reopening `01`'s
               split.
Signed off:  not required (event payload precision and measurement
             protocol; no balance, scope or `01`/`02` change). The owner
             should review the Q-064 LOW CONFIDENCE marker before T-024.

## 2026-10-01 — spec/08 §8.5, §8.7 (new "Allocation"); 03 "How a budget is measured" (`Step` meter bullet); INDEX; open-questions — Q-065: applying commands allocates nothing
Reason:      `12` §12.12 (Q-061) has `sim.airside`'s allocation test meter
             `ISimHost.Step` with a `ReassignStand` applied and one a no-op
             in the window. `08` pinned the bus as allocation-free (§8.6,
             Q-035) but said nothing about command application, or about
             the loop. So an allocation in `ApplyDue` would fail a
             module's test for a `sim.core` reason. Pinning it is a
             narrower spec than letting module tests subtract an unknown
             core cost.
             - **§8.7 "Allocation":** on a tick that completes normally,
               `ApplyDue` and its dispatch allocate nothing, for every
               kind, any number of due commands per tick and any log size,
               from the first tick after `Build`. The log grows only at
               admission, outside `Step`. `Apply` gets the admitted
               payload copy, and nothing else is copied. One new test is
               named.
             - **§8.5:** on such a tick, the loop allocates nothing outside
               a checkpoint tick's phase 4.
             - **The bound (review of #77 at `8111cdc`).** It mirrors
               Q-035's on §8.6. A tick that throws, whether from a
               handler's `Apply` or from a broken `sim.core` limit such as
               `MAX_EVENTS_PER_TICK`, is wrapped by §8.5a in a new
               `SimInvariantException`. Merged `SimHost.WrapAndBreak`
               allocates the message and the exception. That tick is
               outside the rule and never in an allocation test's window.
               The first draft's unbounded wording contradicted §8.5a.
             - **03:** the `ISimHost.Step` meter excludes nothing for
               `sim.core`, on ticks that complete normally.
Raised by:   Q-065 (Test Author / T-021)
Impact:      - **Merged code:** none invalidated. On a tick that
               completes normally, T-005's `CommandQueue.ApplyDue` already
               allocates nothing: it walks
               a pointer over the sorted log, the handler lookup is a
               `switch` over two fields, and `TickContext` is a struct.
               The host loop already passes
               `test_budget_step_with_no_systems_allocates_nothing` and
               `test_budget_step_with_events_and_ids_allocates_nothing_in_steady_state`.
             - **Merged tests:** stay valid.
               `test_command_queue_apply_due_allocates_zero_bytes` shows
               only a difference between two hosts, for `SetServersOpen`
               and `NoOp`, so it does not pin an absolute 0 or the
               `ReassignStand` path.
             - **Test-only follow-up (`tests/sim/core/**`):**
               `test_command_queue_apply_due_of_every_kind_allocates_nothing`.
               Recommended: a separate small test-only task, released
               after T-042 merges, because both write `tests/sim/core/**`.
               It is not folded into T-042. T-042's task file puts a
               `Budget` test that asserts only allocation out of scope,
               and this test asserts only allocation. Folding it in would
               widen T-042's scope, and the Planner would have to edit its
               task file.
             - **T-021:** no change to its tests or code. Its `Step`-metered
               allocation test can now attribute any nonzero result to
               `sim.airside`.
             - **Scope:** none added. No budget value changes.
Signed off:  not required (measurement protocol and a `sim.core`
             invariant that merged code already meets; no balance, scope
             or `01`/`02` change).

## 2026-10-01 — spec/19 (new §19.2c, §19.8; §19.1, §19.2, §19.2a, §19.2b, §19.3); 16 §16.3, §16.4, §16.8, §16.9; INDEX; open-questions — Q-066 to Q-076: the harness `checkpoints` subcommand; Q-077 filed OPEN
Reason:      T-030's Test Author was blocked by eleven gaps. `16` §16.8
             pinned only the dump bytes, and `19` never defined the
             subcommand, while `19` §19.2 and §19.2a contradicted it
             (no flag selects a composition, and the boarding stand-in
             is in every CLI composition). The smallest closure is a new
             §19.2c that applies `16` §16.4's composition rules in the
             harness's own code, and §19.8 tests with no new seam.
             - **Grammar (Q-066):** `checkpoints --bundle B --content C
               --days D --out P`. All four flags required, in any order,
               once each. `D` follows every other `--days`. `--content`
               is new (Q-072).
             - **Output and failures (Q-067):** `WROTE checkpoints
               ticks=<n> checkpoints=<k> final=<hex16>`, exit 0. It never
               exits 1. Every later failure is exit 3, in a fixed order,
               and `P` is created only after the run.
             - **Paths (Q-068):** §19.2b's rules for `B`, `C` and `P`.
               `P` is never overwritten. `B` is read by exact name and
               never listed.
             - **Composition (Q-069, Q-070, Q-073):** `16` §16.4 steps 1
               to 4, with no stand-in, no probe, and `sourceName` = the
               bundle file name on both sides. **Staged:** T-030 composes
               world, schedule and flow, and any other Phase 1 system is
               exit 3. A Phase 1 stage harness task adds airside,
               turnaround and delay, after T-021, T-022 and T-024 and
               before T-031. `16` §16.3 gets a strict `bundle.json`
               form, and each system's `Name` is its module name (merged
               world, schedule and flow already comply).
             - **Commands (Q-071):** neither side submits any.
             - **Content (Q-072):** a listed content directory, with the
               order fixed by `08` §8.11's loader. `16` §16.9 step 1
               passes the build step's copies of the playtest bundle and
               of `data/` (see the review fixes below).
             - **Batch size (Q-074):** both sides pin `Days` calls of
               `Step(TICKS_PER_SIM_DAY)`. The test compares a
               published-surface kit, stepped three other ways, with the
               CLI's file.
             - **Factories only (Q-075):** a reflection check. The harness
               references no `AirportSim.App.*` assembly, and its
               `AirportSim.*` references carry no `InternalsVisibleTo`.
             - **Fixtures (Q-076):**
               `tests/fixtures/harness/checkpoints-phase0/` (T-030's Test
               Author) and `-phase1/` (the Phase 1 stage's Test Author).
             - **§16.8 D7:** the Phase 0 bundle now lists `sim.world`
               (the old "only `sim.schedule` and `sim.flow`" was a load
               failure by §16.4), and the Phase 1 half uses the
               `-phase1/` test bundle, because the playtest bundle is the
               Unity shell's, which follows T-031.
             - **Review of #83 at `8df4681`, five fixes:**
               1. `test_checkpoints_rejects_usage_errors` passed against
                  a harness without the subcommand, because an unknown
                  subcommand is also exit 2 with empty stdout. It now
                  starts with a control: the valid invocation exits 0 and
                  prints the `WROTE checkpoints` line. Then each usage
                  error exits 2.
               2. `test_checkpoints_subcommand_composes_through_published_factories_only`
                  was only a static check that `main` already passes. It
                  now has a behavioural part (exit 0, and the file is
                  byte-identical to the published-surface kit's dump), and
                  the static part is kept as a regression guard.
               3. §19.2 "Untested by design" said code 3 is reachable only
                  through `soak`. It now names `checkpoints` too (§19.8).
               4. The first draft dropped "in-process" from §16.8's D7
                  description with no record. It is restored. Q-077's
                  framing is corrected: Q-025 binds harness tests only,
                  so spawning the harness (C) is not forbidden. It is
                  rejected because it drops §16.8's in-process run and
                  depends on the build layout. #84, which answers Q-077,
                  gets the same correction in its own entry.
               5. §16.9 step 1's `--bundle B` named the playtest bundle,
                  but `unity/AirportSim/Scenario/` holds only
                  `bundle.json`, so §19.2c could not run it. Step 1 now
                  reads the player build step's copies,
                  `unity/AirportSim/Assets/StreamingAssets/Scenario` and
                  `.../Content` (`16` §16.3), after the build step has
                  run. The harness therefore reads the bytes the player
                  reads. The §16.8 LOW CONFIDENCE marker now says that
                  step 1 reads the build copy. The first claim here that
                  the marker "holds" was premature: see the next block.
             - **Review of #83 at `a8e3edb`: the playtest bundle had no
               walk graph.** §16.3's playtest-bundle list named no
               `world.fixture`, but that bundle registers every Phase 1
               system, `sim.world` included, which `sim.flow` needs. So
               §19.2c, and the player, would fail with exit 3 for the
               missing file. §19.2c's Phase 1 test bundle already added
               "plus the walk graph", so §16.3, §16.9 and §19.2c
               disagreed. This was a spec omission, not a content or
               scope decision: the §16.3 table already required the file,
               and `18` §18.6 already names
               `tests/fixtures/world/phase0-landside.json` as the walk
               graph that T-023's flow fixture shares. The fix:
               - §16.3 adds the walk graph to the playtest bundle, with a
                 table that gives each bundle file name and its source;
               - the `world.fixture` row now says it is required whenever
                 `sim.world` is listed, which §16.4 requires whenever
                 `sim.flow` is;
               - §16.9 step 1 points to that table, `world.fixture`
                 included;
               - §19.2c's Phase 1 test bundle names its six files and
                 takes each from the same table.
               Every bundle file list was re-checked against the systems
               its bundle registers. The Phase 0 checkpoints bundle has
               world, schedule and flow, with `world.fixture`,
               `schedule.csv` and `flow.fixture`. The Phase 1 test bundle
               has six files for the five systems that need one
               (`sim.delay` needs none). The playtest bundle has those six
               plus `render_layout.fixture` for presentation. §16.4 and
               §16.8 name no file list of their own. The §16.8 LOW
               CONFIDENCE marker's mitigation now holds, because the
               build copy is a complete bundle. **T-034:** its task file
               copies §16.3's old list, which has no walk graph. The
               Planner syncs it to the new table. Its "Playtest bundle
               names every file `bundle.json` lists" check then covers
               `world.fixture`.
Raised by:   Q-066 to Q-076 (Test Author / T-030, via coordinator). Q-077
             was raised by the Architect while answering them.
Impact:      - **Merged code:** none invalidated. No harness code for
               `checkpoints` exists. Merged `sim.world`, `sim.schedule`
               and `sim.flow` already return their module names as
               `Name`. The §19.2a composition, the soak, the stand-in and
               every existing subcommand are unchanged, and so are their
               tests.
             - **T-030:** its interface line gains `--content <dir>`.
               The Planner syncs the task file: the grammar, §19.2c and
               §19.8 as spec sources, the five §19.8 tests, and the Test
               Author's grant of
               `tests/fixtures/harness/checkpoints-phase0/**`. Its
               writable paths stay `tools/SimHarness/**`. Its
               dependencies are enough for its stage.
             - **New task (Planner):** the Phase 1 stage of
               `checkpoints`, writing `tools/SimHarness/**`, with its
               Test Author writing `tests/fixtures/harness/checkpoints-phase1/**`
               and one §19.8 test. It depends on T-021, T-022, T-024 and
               T-030, and T-031 depends on it. This is a pure ordering
               choice for the Planner. The alternative is to move T-030
               itself after T-021, T-022 and T-024. The Architect
               recommends the split, because T-013, T-014 and T-045
               queue behind T-030 as harness writers, and the nightly
               soak waits on T-013.
             - **T-031:** `IHeadlessRun` submits no command and steps one
               sim-day per `Step`. Its batch-size test composes through
               `ISimComposer` with no seam. Its D7 test uses the two test
               bundles, not the playtest bundle. `bundle.json` gets a
               strict form. T-031 is not started, so nothing is reworked,
               but the Planner syncs its task file. **T-031 also stays
               blocked by Q-077 (OPEN):** `07` L3 leaves no test project
               that can call both sides.
             - **T-035:** step 1 now runs `checkpoints` over
               `unity/AirportSim/Assets/StreamingAssets/Scenario` and
               `.../Content`, after the player build step. The Planner
               syncs its task file. T-035 already depends on T-034.
             - **LOW CONFIDENCE:** the listed content directory (Q-072),
               and D7 on test bundles rather than the playtest bundle
               (§16.8).
             - **Scope:** none added. The subcommand was already planned
               (D7, T-030). `--content` and the stage split refine it.
             - **PENDING HUMAN:** none. Ordering is the Planner's, and
               Q-077 is architecture, recommended (A), left OPEN for its
               own review.
Signed off:  not required (harness interface detail and test protocol;
             no balance, scope or `01`/`02` change). The owner should
             review the two LOW CONFIDENCE markers.

## 2026-10-02 — spec/12-interfaces-airside.md §12.5–§12.13, spec/10-events.md §10.3, spec/14-interfaces-delay.md §14.5 — `Cause` of airside events; cross-tick cause ids in hashed state (Q-078, Q-079)
Reason:      The T-021 worker found that PR #78's `Pairs` helper requires
             every release's `Cause` to be its opening hold, while `12`
             §12.7 (and tests in the same PR) give `StandAssigned` the
             freeing `Pushback`. The spec was already unambiguous here:
             the helper is wrong for the stand family. But only some
             `Cause`s were stated anywhere, so `12` §12.11 now has a
             binding table for every event `sim.airside` publishes, and
             `10` §10.3 rule 2 says that pairs are never matched by
             `Cause`. Separately, the release `Cause`s and
             `StandAssigned`'s `Pushback` are ids from an earlier tick,
             and §12.12 gave them no home, so they would be unhashed and
             lost on save and load. That breaks `08` §8.6, as Q-062 did.
             They now live in `AircraftTrack.OpenHold` and
             `StandState.VacatedBy`, both hashed and saved. Also stated:
             `AwaitingPushbackClearance` and `Departed` are reserved and
             never set at Phase 0/1.
Raised by:   Q-078, Q-079 (Worker / T-021, via coordinator)
Impact:      - **No merged work is invalidated.** `sim.airside` is not
               merged, and PR #78 (T-021 tests) is open.
             - **T-021 tests (Test Author, PR #78):** `Pairs` stops
               asserting `Cause` = opening event, at least for
               `StandUnavailable`/`StandAssigned` (its callers in
               `StandTests`, `BoardingHoldTests` and
               `AirsideHeadlessDayTests` already assert the `Pushback`).
               `AirsideTypesTests` builds `AircraftTrack` and `StandState`
               positionally, so it gains the new last arguments. Two new
               tests: `test_airside_event_causes_follow_cause_table` and
               `test_open_hold_and_vacated_by_track_cross_tick_causes`
               (§12.13). Apart from `Pairs`, no existing assertion changes.
             - **T-021 worker:** moves the hold ids and freeing `Pushback`
               ids out of unhashed side state into the two fields, feeds
               them per §12.12, and sets each `Cause` per the §12.11 table.
               The milestone rows (None for timer-driven milestones,
               the release for a held `Landed`/`TakeoffRoll`, `DoorsClosed`
               for `Pushback`) were unstated before, so the worker checks
               its choices against them.
             - **T-022 (`sim.turnaround`):** none. `13` §13.5 is cited as
               is. `TurnaroundJobUnblocked` is always in the tick of the
               freeing completion, so it needs no cross-tick state.
             - **T-024 (`sim.delay`):** none. It pairs by key and never
               reads a closing `Cause` (§14.5, §14.7).
             - **`app.render`:** none. `15` §15.4 reads `Occupant` and
               the phase only, and maps the two reserved phases already.
             - **LOW CONFIDENCE:** None as the `Cause` of timer-driven
               milestones and of opening hold events (§12.11). It keeps
               cross-tick state to three fields and is honest while
               nothing follows `Cause`. It is revisited when `sim.delay`
               follows `Cause` chains (`14` §14.7).
             - **Scope:** none added. Two state fields and a table of
               existing behaviour.
             - **PENDING HUMAN:** none.
Signed off:  not required (interface detail and state layout; no balance,
             scope or `01`/`02` change). The owner should review the LOW
             CONFIDENCE marker.
