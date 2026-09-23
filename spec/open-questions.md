# Open questions

Workers append here when the spec does not cover something. **Do not guess and do
not improvise an interface.** File the question and stop — a stopped agent is
cheap, a confidently wrong one is expensive.

The Architect answers by amending the spec, then records it in `CHANGELOG.md` and
marks the question resolved with a link to the spec section.

## Format

```
### Q-<nnn> — <short title>
Raised by:   <agent role> / <task id>
Blocking:    <task id, or "no">
Question:    <what the spec does not say>
Why it matters: <what breaks if guessed wrong>
Status:      OPEN | ANSWERED (spec/<file>#<section>)
```

---

### Q-002 — How much sim time does one tick represent?
Raised by:   architect / Phase 0 spec completion
Blocking:    T-001, T-008, T-009 (everything expressed in ticks)
Question:    `01-architecture.md` locks `TICK_MS = 100` at 1x, which fixes the
             tick *rate* in real time but not how much *sim* time a tick carries.
             The Architect has provisionally set `SIM_SECONDS_PER_TICK = 6`
             (`spec/08-interfaces-core.md` §8.2): 14 400 ticks per sim-day, and a
             sim-day lasting 24 real minutes at 1x.
Why it matters: It is a pacing decision — how long a day feels — and pacing is
             human-owned, not an agent call. It also sets the resolution of every
             delay minute and the tick cost of the 500-day soak. Changing it later
             invalidates every golden hash and every tick-valued fixture, though
             no interface.
Answer:      Confirmed at 6 (D2). A smaller value would make T-009's
             100-days-in-60-s gate infeasible, and a larger one coarsens delay
             resolution. Golden hashes may now be authored.
             `spec/08-interfaces-core.md` §8.1 and §8.2 record the decision.
Status:      ANSWERED (spec/08-interfaces-core.md#82-sim-time) — HUMAN
             DECISION — owner (delegated), 2026-09-23; reversible

### Q-003 — "500 sim-days in minutes" versus the per-tick budget
Raised by:   architect / Phase 0 spec completion
Blocking:    no (nightly soak only)
Question:    `01-architecture.md` justifies the plain-.NET sim by requiring 500
             sim-days to run "in minutes on a build agent". At 6 s/tick that is
             7.2 M ticks; even at 0.1 ms/tick it is 12 minutes, and at the max-tier
             budget of 6 ms/tick it is 12 hours. The soak is therefore only
             "minutes" if it runs against a small fixture, not the max-tier one.
Why it matters: If the soak silently ends up running at max tier it will be
             disabled for being slow, and `02-determinism.md` forbids disabling a
             gate. Better to declare the soak fixture size now.
Proposed:    Soak runs a mid-tier fixture sized so per-tick cost stays under
             0.1 ms; max-tier performance is covered separately by the budget
             tests in `03-module-map.md`. Needs sign-off because it narrows what
             the nightly gate actually proves.
Answer:      Proposal accepted (D3). `spec/03-module-map.md` "The soak
             fixture" makes it binding: a mid-tier fixture under 0.1 ms per
             tick, every built system registered, and the fixture shrunk rather
             than the gate weakened if the cost grows.
Status:      ANSWERED (spec/03-module-map.md#the-soak-fixture) — HUMAN
             DECISION — owner (delegated), 2026-09-23; reversible

### Q-004 — `sim.schedule` has no published interface
Raised by:   planner / queue expansion for T-008
Blocking:    T-008, and transitively T-009, T-021, T-022, T-023(schedule-fed
             fixtures), T-024
Question:    `03-module-map.md` states every module besides `sim.core` and
             `sim.flow` is "not yet specified — a worker may not start without
             one." T-008 ("Schedule loader from CSV fixture, 200 movements")
             needs at minimum: the `IScheduleSystem` shape (an `ISimSystem`),
             what it injects into `sim.flow` via `Inject` and how, the CSV
             fixture's column schema, and how `FlightPlanPublished` /
             `FlightMilestoneReached` (for `PlanPublished`) are populated from
             it. None of this is in `04-data-schemas.md` or `10-events.md` at
             signature level.
Why it matters: Without a binding interface a worker would have to invent
             `IScheduleSystem`, which `CLAUDE.md` rule 2 forbids outright.
Answer:      New file `spec/11-interfaces-schedule.md`. `IScheduleSystem` is
             query-only (§11.7); the CSV fixture schema, its validation rules and
             its byte-level strictness are §11.4; publication and the
             `FlightPlanPublished` + `FlightMilestoneReached(PlanPublished)` pair
             are §11.5; the show-up curve and `IFlowSystem.Inject` contract are
             §11.6; hashing, the no-RNG rule and the budget shape are §11.9; the
             200-movement fixture's binding requirements and the expected test
             names are §11.10.
Status:      ANSWERED (spec/11-interfaces-schedule.md)

### Q-005 — `sim.airside` has no published interface
Raised by:   planner / queue expansion for T-021
Blocking:    T-021, and transitively T-022, T-024
Question:    T-021 ("One runway, taxiway graph, four contact stands") needs an
             `IAirsideSystem` (or equivalent) interface: how stands are
             represented, how `sim.airside` calls `sim.flow.Inject`/`Absorb` at
             door/gate transitions, how it emits the airside milestones and
             `AircraftHeldForRunway`/`StandUnavailable` events (`10-events.md`),
             and its relationship to `sim.world`'s navigation graph. None of
             this has a signature-level spec yet.
Why it matters: Movement, stand assignment and milestone emission are exactly
             the kind of interface `03-module-map.md` requires be spec'd before
             a worker starts.
Answer:      New file `spec/12-interfaces-airside.md`. `IAirsideSystem` is
             query-only (§12.9); the self-owned taxiway/runway/stand graph
             format and its routing are §12.4; the runway pacing/occupancy
             model and its hold queue are §12.5; the single-lane taxiway
             conflict model is §12.6; stand compatibility, assignment and the
             `Inject`/`Absorb` calls at the door are §12.7; the milestone
             ownership split against `sim.turnaround` (resolving
             `10-events.md` §10.4's ambiguity) is §12.3; the no-`sim.turnaround`
             fallback that lets T-021 ship before T-022 is §12.8; hashing, the
             no-RNG rule and the budget shape are §12.12; the fixture
             requirements and expected test names are §12.13.
Status:      ANSWERED (spec/12-interfaces-airside.md)

### Q-006 — `sim.turnaround` has no published interface
Raised by:   planner / queue expansion for T-022
Blocking:    T-022, and transitively T-024
Question:    T-022 ("Turnaround as job list, 4 vehicles, driver assignment")
             needs `ITurnaroundSystem`, the `JobKind`/vehicle data shapes, and
             how job start/completion drives `TurnaroundJobStarted/Completed`
             and `TurnaroundJobBlocked/Unblocked` (`10-events.md`). Not
             specified anywhere yet.
Why it matters: Same as Q-005 — job scheduling and vehicle assignment logic is
             exactly the kind of design decision the module-map reserves for a
             published interface.
Answer:      New file `spec/13-interfaces-turnaround.md`. `ITurnaroundSystem`
             is query-only (§13.7); the job catalogue and vehicle fleet
             (self-owned, not `data/` content at Phase 0/1) are §13.4; FIFO
             vehicle dispatch by ascending blocking `EventId` is §13.5; job
             creation off `sim.airside`'s `OnStand` milestone, the
             arrival/departure job split and the `DeboardComplete`/
             `ReadyToBoard`/`BoardingComplete` emission rules (fulfilling the
             handshake `12-interfaces-airside.md` §12.8 already committed to)
             are §13.6; hashing, the no-RNG rule and the budget shape are
             §13.10; the fixture requirements and expected test names are
             §13.11.
Status:      ANSWERED (spec/13-interfaces-turnaround.md)

### Q-007 — `sim.delay` has no published module interface
Raised by:   planner / queue expansion for T-024
Blocking:    T-024, and transitively T-025
Question:    `06-delay-attribution.md` specifies the `DelayEvent` payload and
             the allocation rules, and `10-events.md` §10.7 says `sim.delay`
             emits only `DelayEvent`. Neither specifies the `ISimSystem`-facing
             interface (`IDelaySystem`?) — what queries `app.ui`'s delay-tree
             view calls, the exact hashed-state layout for the attribution
             tree, and how `DelayEventId` is allocated/ordered relative to
             `EventId`.
Why it matters: T-024 is the Phase 1 headline feature and the module the
             overview explicitly calls out as gating every later balance
             decision (`00-overview.md`). Guessing its query surface risks a
             rebuild once `app.ui` needs it.
Answer:      New file `spec/14-interfaces-delay.md`. `IDelaySystem` is
             query-only (§14.10); the per-flight record and node types are
             §14.3; the checkpoint milestones the delay clock measures, and
             the schedule-anchored `PlannedTick` rule they rely on, are
             §14.4; the four blocking-interval families and their pairing
             keys are §14.5; the allocation algorithm (first-blocker-wins,
             cap, `late_inbound` capped at the inbound's own delay, residue,
             recovery) is §14.6; tree shape, `DelayEventId` allocation — a
             module-owned counter, never compared with `EventId`, monotone
             in dispatch order, every reference pointing backwards — and the
             depth rule are §14.7; finalisation, `DelayEvent` publication
             and two-day retention are §14.8; hashed state, the no-RNG rule
             and the budget shape are §14.13; fixtures and expected test
             names are §14.14. Consistency amendments to `06`, `10` §10.3/
             §10.4/§10.5/§10.6/§10.7, `11` §11.5, `12` §12.3 and `13` §13.6/
             §13.9 are listed in `CHANGELOG.md`. One part is left open as a
             HUMAN DECISION (§14.9): at Phase 1 no flight waits for a late
             passenger, so security queues can never show up as delay
             minutes, only as missed passengers.
Status:      ANSWERED (spec/14-interfaces-delay.md) — one HUMAN DECISION
             noted in §14.9, not blocking T-024

### Q-008 — `app.render` has no published interface or scope note
Raised by:   planner / queue expansion for T-020
Blocking:    T-020, and transitively T-025
Question:    `03-module-map.md` lists `app.render` as owning "Rendering,
             cameras, overlays" and reading sim state read-only, but there is
             no spec section describing what "minimal top-down renderer, flat
             colours" (T-020) must actually draw (which sim queries it polls,
             at what cadence, how it drives `IFlowSystem.SetPromoted` for the
             promotion-neutrality gate it must not break) or what a done
             condition/test for a renderer even looks like given the sim is
             headless by design (`01-architecture.md`).
Why it matters: Without this, T-020's done-condition cannot be stated as a
             passing test, per the Planner's own sizing rule, and every
             agent's interpretation of "top-down renderer" would differ.
Answer:      New file `spec/15-interfaces-render.md`. `app.render` is split
             into a headless scene layer with no engine reference, tested by
             `dotnet test`, and a thin engine backend that holds no logic
             (§15.1, §15.3). What is drawn is §15.5. The presentation-owned
             layout that positions nodes is §15.4. The complete list of sim
             members polled, and the rebuild-only-on-tick-or-camera cadence,
             is §15.6. The promotion controller, the only caller of
             `SetPromoted`, is §15.7: first update sets every node, later
             updates only changes, in ascending `NodeId`, only between
             `Step`s. It is proven outcome-neutral by an integration test.
             The 1x tick pacer and the binding frame order are §15.8, the
             types §15.9, the backend contract §15.10, the budget §15.11,
             and T-020's scope, fixture and test names §15.12. Consistency
             amendments: `12` §12.9 gains `Layout()` and defines `AtNode`
             while `OnEdge` is set. T-020 now depends on T-009, T-010 and
             T-021.
             **Left open as HUMAN DECISIONS (§15.13):**
             (a) Unity 6 appears unable to load a `net8.0` assembly, which
             puts two locked `01-architecture.md` decisions in tension;
             (b) who owns the Unity project shell; (c) the composition root
             that hands a running sim to presentation; (d) game speeds other
             than 1x; (e) no Phase 1 task gives the player a way to issue
             `SetServersOpen`. None blocks T-020's headless scope; together
             they block the backend and a playable build.
Status:      PARTIALLY ANSWERED (spec/15-interfaces-render.md) — the headless
             scope is answered and T-020 is releasable for it; the engine
             backend stays open pending the HUMAN DECISIONS in §15.13
