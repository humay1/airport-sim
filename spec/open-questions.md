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
Decision:    §14.9 resolved — HUMAN DECISION — owner (delegated),
             2026-09-23 (D6), reversible. A departure holds for passengers
             still in the terminal for at most `BoardingHoldMaxMinutes`
             (10 sim-minutes, a balance value in `data/balance/`), then
             closes; the remainder are missed. The hold is a `sim.airside`
             blocking interval (`12` §12.8), attributed as `passenger_late`
             blaming the node holding most of the late passengers (`14`
             §14.5, §14.9). It needs one new read-only `sim.flow` query,
             `TryGetOutstanding` (`09` §9.7a, LOW CONFIDENCE).
Status:      ANSWERED (spec/14-interfaces-delay.md), including §14.9

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
Decisions:   All five §15.13 items are HUMAN DECISION — owner (delegated),
             2026-09-23, each reversible:
             (a) D1 — the sim and every Unity-consumed headless layer target
             `netstandard2.1` only; tests and the harness target `net8.0`
             (`01-architecture.md` runtime row, `15` §15.3);
             (b) D7 — new module `app.host` owns `unity/AirportSim/`
             (`16-interfaces-host.md` §16.2);
             (c) D7 — `app.host`'s headless composition root and frame loop
             (`16` §16.3 to §16.7); module construction is open as Q-009;
             (d) D4 — pause, 1x, 2x and 4x (`15` §15.8);
             (e) D5 — minimal `app.ui` with pause/speed controls and the lane
             click (`17-interfaces-ui.md`); its command plumbing is open as
             Q-010.
Status:      ANSWERED (spec/15-interfaces-render.md#1513) — the HUMAN
             DECISIONS are made. What remains of a playable build is Q-009 and
             Q-010, plus tasks the Planner has yet to queue.

### Q-009 — No module publishes how its system is constructed
Raised by:   architect / D7 (writing `spec/16-interfaces-host.md`)
Blocking:    the `app.host` composition task (`16` §16.4, §16.5, §16.8's
             `IHeadlessRun` and harness-equivalence test). Latent in T-009,
             which registers `sim.schedule` and `sim.flow` in the harness, and
             in every integration test that composes several systems (T-020's
             outcome-neutrality test, T-024's integrated day).
Question:    D7 has the composition root build `ISimHost` and the registered
             systems "from fixtures". No spec publishes a construction entry
             point for any of them: `ISimHost` (master seed, registry,
             content index, checkpoint sink, log sink), the `IContentIndex`
             over `data/`, `sim.flow` (node graph and `QueueConfig`s — its
             fixture format is not specified either), `sim.schedule` (from a
             `ScheduleTable` and its `IFlowSystem`), `sim.airside` (from an
             `AirsideLayout`, the `IScheduleSystem` and `IFlowSystem` it calls),
             `sim.turnaround` (from a `TurnaroundCatalogue`, a
             `TurnaroundFleet` and its `IScheduleSystem`), `sim.delay` (no
             data), and the `app.render` scene objects (§15.9 says what they are
             built from, not how). `08-interfaces-core.md` says anything
             unpublished is internal to its module and may not be referenced
             from another one. So the host, and strictly the harness too, would
             have to reach into module internals or invent factories.
Why it matters: A composition root built against guessed constructors is
             rebuilt as each module lands. Worse, the harness and the host
             could wire the same modules differently and disagree on hashes,
             which is exactly what D7's equivalence test exists to catch.
Proposed:    Each interface file gains a short "Construction" section with
             **one** factory per module. It takes the module's validated
             construction data and the downward interfaces the module calls,
             and nothing else: no service locator and no statics. `sim.core`
             publishes an `ISimHost` builder that takes the seed, the content
             index, the checkpoint and log sinks, and the systems in registry
             order. The content loader gets its own small spec. Fixture
             formats stay the worker's choice, but they are named in the
             factory's input type, not in a file path.
Answer:      The proposal is adopted. `08` §8.11a publishes `SimHostConfig`,
             `SystemServices` (event bus, id allocator, content index),
             `ISimHostBuilder` (register in registry order, build once) and
             `ContentIndexFactory`. It also gives the factory rule: one
             `<Module>Factory` of stateless static methods per module,
             taking only `SystemServices`, validated data and downward
             interfaces; construct in dependency order, register in registry
             order. Each module has a "Construction" section:
             `09` §9.11 (`IFlowGraphLoader`, with an opaque `FlowGraph`),
             `11` §11.9a, `12` §12.12a (adds
             `IAirsideLayoutLoader.Parse` and an explicit
             `turnaroundRegistered` input), `13` §13.10a
             (`ITurnaroundSetupLoader`), `14` §14.13a, `15` §15.9
             (`RenderFactory`), `17` §17.7 (`UiFactory`). `16` §16.3–§16.5
             now composes with them, and the harness must use the same
             factories. Two neighbouring gaps are raised separately rather
             than invented: parsing `data/` into content definitions (Q-011),
             and what `sim.flow`'s graph must express to route without
             `sim.world` (Q-012).
Status:      ANSWERED (spec/08-interfaces-core.md#811a-construction-q-009)

### Q-011 — Content definition types and the `data/` loader are unspecified
Raised by:   architect / Q-009
Blocking:    the Unity player build (`16` §16.11), which has no other source
             of content, and production content for any module. It does not
             block tests, which supply definitions directly to
             `ContentIndexFactory.Create` (`08` §8.11a).
Question:    `IContentIndex.TryGet<T>` needs concrete `IContentDefinition`
             types. Phase 1 needs at least an aircraft definition (a
             `size_category` ordinal, `12` §12.7), a pax profile (show-up
             curve `11` §11.6, `walk_speed` `09` §9.6) and a flow-node or queue
             definition (service rate, threshold and hysteresis, delay
             category, `09` §9.4, `10` §10.6). None is typed anywhere. Nor is
             the loader that turns `data/**/*.json` into definitions, or where
             it lives (sim assemblies take no JSON package, `07` "Runtime
             portability" rule 7). No Phase 1 content exists in `data/` either.
Why it matters: Each consuming module would invent the definition type it
             reads, and the Phase 1 lane service rates are balance values
             (`04`) that someone would otherwise hardcode into a fixture.
Proposed:    Definition types in `sim.core` beside the event types (the
             T-026 pattern). A loader in a small non-sim assembly (`content`
             or `app.host`) that parses the JSON and calls
             `ContentIndexFactory.Create`. Phase 1 balance-bearing values
             (lane service rates, queue thresholds) under `data/balance/`, as
             D6 did for the hold.
Status:      OPEN — Architect to answer; the balance values themselves are
             the owner's

### Q-012 — `sim.flow` has no way to route without `sim.world`
Raised by:   architect / Q-009 (making `FlowGraph` opaque)
Blocking:    T-007 (`tasks/T-007` already tells its worker to stop if routing
             needs a `sim.world` query), and so the Phase 0 kill gate chain.
Question:    `09` §9.6 routes cohorts along `sim.world` flow fields toward a
             "current destination", with traversal time from `sim.world`
             distances. `sim.world` has no interface. Nothing says which node
             a departing cohort is heading for (which `Gate` serves a
             flight), nor how edge traversal time is known.
Why it matters: T-007 cannot implement §9.4 step 4 ("served passengers move
             to the outbound edge per §9.6") without it. Q-009's opaque
             `FlowGraph` deliberately does not decide it.
Proposed:    The same narrowing `12` §12.1 applied to airside: at Phase 0/1,
             `sim.flow`'s graph is self-owned construction data with
             per-edge `TraversalTicks`. A departing cohort's destination is
             the `Gate` node(s) the graph declares for the stand, or a
             fixture-level gate per flight. Folding it into `sim.world` later
             is an amendment.
Status:      OPEN — Architect to answer

### Q-010 — `SetServersOpen` cannot be issued: command plumbing and lane state are unpublished
Raised by:   architect / D5 (writing `spec/17-interfaces-ui.md`)
Blocking:    `17` §17.5 step 4, the production `ILaneCommandSink`. Also
             bears on T-023, whose command handler has no published dispatch
             interface to implement, and on T-005 (admission-time
             validation). The Planner should check both before release.
Question:    D5 has a click enqueue `SetServersOpen` through the command
             queue. Five things needed for that are not specified:
             (1) the **payload byte layout** of `SetServersOpen`. `09` §9.8
             names the fields (`NodeId`, `int32 count`) but not their
             encoding, so `app.ui` (encoder) and `sim.flow` (decoder) would
             each invent one;
             (2) **`PlayerId`** is used by `Command` (`01`, `08` §8.7) but
             never defined;
             (3) **who adds `CommandKind.SetServersOpen`** to `sim.core`.
             T-023 writes only `src/sim/flow/**`, which is the same scheduling
             gap T-026 closed for event payload types;
             (4) **dispatch**: `08` §8.7 says an applied command reaches its
             owning system "through an interface that system publishes", and
             that validation happens at admission. `sim.flow` publishes no such
             interface, and nothing says how `sim.core` validates a payload it
             does not understand;
             (5) **lane state**: "one more lane" needs the node's current
             `ServersOpen` and its `ServerCount`, and needs to know whether
             the node is a `Queue` at all. `IFlowSystem` exposes none of
             them.
Why it matters: Without (1) to (4), T-023 and `app.ui` can each pass their
             own tests and still disagree on the wire. Without (5), the player
             clicks blind, and a click on a non-queue node does something the
             spec does not define.
Proposed:    (1) 8 bytes: `NodeId.Value` as `uint32` little-endian, then
             `count` as `int32` little-endian; any other length is
             `MalformedPayload`. (2) `struct PlayerId { uint16 Value }`, with
             the single Phase 1 player as 0. (3) a small `sim.core` task, like
             T-026. (4) `sim.core` publishes a per-kind handler contract
             (validate the payload at admission, apply at phase 1); `sim.flow`
             registers one for `SetServersOpen`. (5) a read-only
             `bool IFlowSystem.TryGetQueue(NodeId node, out QueueConfig
             config)` returning false for non-`Queue` nodes; `app.ui` clamps
             `ServersOpen ± 1` into `[0, ServerCount]` before submitting.
             Item (5) widens `sim.flow`'s interface, which D5 did not name, so
             it needs the owner's (or delegate's) nod.
Status:      OPEN — Architect to answer (1)–(4); (5) needs owner approval of
             the query
