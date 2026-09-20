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
