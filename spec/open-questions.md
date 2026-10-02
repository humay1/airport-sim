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
Answer:      Definition types and a loader in `sim.core` (`08` §8.11):
             - `ContentId` (an ordinal string), `ContentKind`, and the
               definitions `SizeCategory`, `Aircraft`, `PaxProfile` and
               `QueueProfile`;
             - `IContentLoader` over an `IContentSource`: it maps four
               directories to kinds, reads files in ordinal path order, and
               hand-parses a strict JSON subset with no package and no float
               (decimals are strings read by `Fx.Parse`, and unknown or
               missing keys fail);
             - validation rules as listed there.
             `04` lists the fields and renames the pax profile schema to
             `pax_profiles.schema.json` so that its name matches the validator.
             `16` loads content from a build-time copy of `data/`. Pax-profile
             and queue-profile *values* are balance, authored by the owner.
Status:      ANSWERED (spec/08-interfaces-core.md#definition-types-q-011);
             the Phase 1 balance values are the owner's

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
Answer:      A minimal `sim.world` is published, not a flow-owned graph, since
             `sim.world` already sits in `03`'s map (`18-interfaces-world.md`,
             the fixed-graph Phase 0/1 subset):
             - a fixture-loaded walk graph: nodes with integer
               `LengthMetres`, directed edges;
             - load-time routes: `CanReach`, `CanReachVia`, and `PathVia`
               (the shortest path through a given first edge, ties by edge
               sequence);
             - a hash that is the fixture hash; no runtime state; registry 1.
             `sim.flow` (`09` §9.6):
             - traversal is `ceil(LengthMetres / (walk_speed_mps ×
               SIM_SECONDS_PER_TICK))`;
             - a departing cohort's destinations are every reachable `Gate`;
             - on release it takes the `(edge, gate)` pair with the lowest
               traversal plus predicted queue wait along `PathVia`, with ties
               by `NodeId`, then `EdgeId`. Two security queues are two routes.
             `FlowGraph` now carries node behaviour only over `sim.world`'s
             nodes (`09` §9.11). `Absorb` boards from any `Gate`.
             **Deferred, explicitly:** gate assignment. Which gate serves which
             flight is a gameplay system for the owner, so Phase 0/1 pools
             gates with one shared lounge in its fixtures. Construction, grid
             and flow-field recomputation are deferred too (`18` §18.5).
Status:      ANSWERED (spec/18-interfaces-world.md); gate assignment deferred
             to the owner

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
Answer:      (1)–(4), the Architect's, in `08` §8.7:
             (1) payloads are fixed-layout little-endian with no padding;
             `SetServersOpen` = `uint32 NodeId.Value, int32 count` (8 bytes).
             (2) `PlayerId { uint16 Value }`, with `PLAYER_LOCAL = 0`.
             (3) `CommandKind : uint16 { NoOp = 0, SetServersOpen = 1,
             ReassignStand = 2 }`, never renumbered. `ReassignStand`
             (`12` §12.10) had the same gap and is closed too: 10 bytes. A
             `sim.core` task authors these, following the T-026 pattern.
             (4) `ICommandHandler { Kind, Validate(payload), Apply(cmd, ctx) }`,
             registered through `SystemServices.Commands` (`08` §8.11a) at
             construction. Admission runs `TooLate`, then `UnknownKind`,
             then `Validate`, which is pure over the payload and load-time
             data. A state-dependent impossibility found at `Apply` is a
             logged no-op, not a rejection; `12` §12.10's `ReassignStand` is
             amended to match.
             (5) HUMAN DECISION — owner (delegated), 2026-09-23, consequence of
             D5: read-only `IFlowSystem.TryGetLaneState(NodeId, out LaneState
             { ServerCount, ServersOpen })`, false for non-`Queue` nodes
             (`09` §9.7b, LOW CONFIDENCE). `app.render` draws it as lane pips
             (`15` §15.5). `app.ui`'s sink uses it, plus a pending target so
             that quick clicks do not collapse (`17` §17.5).
Status:      ANSWERED (spec/08-interfaces-core.md#issuer-kinds-and-payloads-q-010,
             spec/09-interfaces-flow.md#97b-lane-state-q-010--low-confidence)

### Q-013 — No solution layout, test framework or project ownership
Raised by:   coordinator / Phase 0 release (T-001, T-003, Test Author)
Blocking:    T-001, T-003, and every test branch
Question:    `08` Notation left namespaces, access modifiers and file layout
             to the worker, and `07` "Testing" named no framework. Nothing
             fixed the project paths or names (csproj, AssemblyName,
             RootNamespace), the test and property-testing packages and their
             versions, whether tests see internals, who creates each project
             file and the solution given the writable paths, or how
             `test_<subject>_<condition>_<expectation>` maps to C#.
Why it matters: The Test Author and the workers cannot agree on a type name,
             a namespace or a project reference. T-001 and T-003 both write
             `src/sim/core/**` concurrently, and would each invent a
             `.csproj`.
Answer:      `07-conventions.md` "Solution layout and build", rules L1 to L11.
             One project per module at a fixed path, named
             `AirportSim.Sim.<M>` (tests `AirportSim.Sim.<M>.Tests`, harness
             `AirportSim.Tools.SimHarness`). The sim and test `.csproj` files
             are given byte for byte. xUnit 2.9.3, xunit.runner.visualstudio
             2.8.2 and Microsoft.NET.Test.Sdk 17.12.0, with no property-testing
             library; property tests are seeded SplitMix64 loops. Public
             surface only, with no `InternalsVisibleTo`, and `public` if and
             only if the spec names it. Test names are the C# method names
             verbatim. There is an ownership table for every project file and
             for `AirportSim.sln`. Tests merge with their implementation. The
             IDL-to-C# mapping and budget-test timing are fixed too.
             Verified: the L2/L3 files build with `-warnaserror` and pass
             `dotnet test` on SDK 8.0.425.
Status:      ANSWERED (spec/07-conventions.md#solution-layout-and-build-q-013)

### Q-014 — The `sim.core` loop, host and bus contract is ambiguous at T-001's edges
Raised by:   Test Author, worker-1, worker-2 (via coordinator) / T-001
Blocking:    T-001
Question:    (A1) Does the first `Step(1)` run tick 0 or tick 1, and is there
             a checkpoint at tick 0? (A2) What are the legal `SystemId`
             values, and may tests register probe systems? (A3) T-001's
             signatures need types owned by other tasks, and nobody owns the
             `ISimLog` family. (A4) The `EventHandler<T>` signature is
             missing, as is how the envelope travels and how `Publish`
             assigns the id, and no task owns full dispatch. (A5) What do
             `MinutesBetween` with `b < a` and `TickOfDayTime` do with
             non-multiple or out-of-range seconds? (A6) May the world hash be
             a stub? (A7) May config members be null? (A8) Is replay a valid
             save/load stand-in? Plus which exception types are thrown (A9,
             shared with Q-015).
Why it matters: Each one is a coin flip that the Test Author and the worker
             would call differently.
Answer:      `08-interfaces-core.md`:
             (A1) §8.2 "Tick numbering": `Step(n)` executes ticks
             `CurrentTick … CurrentTick+n−1`. The first `Step(1)` runs tick 0.
             Chunking is invisible. §8.9: a checkpoint in phase 4 of every
             tick `t % 600 == 0`, so a day has 24 checkpoints, and there is
             none at `Build`. The world hash feeds the ticks-executed count,
             so a checkpoint's hash equals `WorldStateHash()` right after its
             `Step`.
             (A2) §8.4/§8.5: `Value` is the registry position, 1 to 14 except 8.
             0 is `SYSTEM_CORE`. Probes may use any legal position whose
             module is absent. `Name` is not checked.
             (A4) §8.6: `delegate void SimEventHandler<T>(in EventEnvelope,
             in T, in TickContext)`, renamed so it does not collide with
             `System.EventHandler<T>`. Event structs hold payload only, and
             the bus carries and fills the envelope. `Publish` gains an
             `in EventRef cause` argument. Passes, the 4097th publish, one
             handler per (subscriber, type), and `Build` checks subscribers.
             (A5) §8.2: `MinutesBetween` is signed, and `TickOfDayTime`
             floors and throws at ≥ 86400.
             (A7) §8.11a: no nulls, and no null objects published. Tests
             write their own doubles.
             (A9) `07` "Error handling" and the new §8.5a
             `SimInvariantException`: the host wraps any exception that
             escapes a tick, with the tick and world hash.
             (A3, A6, A8), which are task-level and go to the Planner:
             - **T-003 before T-001** (coordinator decision), because
               `SimMinutes = Fx`. T-003 creates `AirportSim.sln` (`07` L8).
             - T-001 declares these **shape only**, and its tests do not
               exercise their behaviour: `Command`, `PlayerId`
               (+`PLAYER_LOCAL`), `CommandKind { NoOp = 0 }`, `CommandRejection`
               (all five), `ICommandHandler`, `ICommandHandlerRegistry` (T-005
               adds the other kinds, admission, order and `LogSince`; T-001's
               `TrySubmit` returns false with `UnknownKind`);
               `IRandomService`, `IRandomStream`, `RngStreamName`, with a
               placeholder whose `MasterSeed` is the config's and whose `Stream`
               throws `InvalidOperationException` (T-002 replaces it);
               `IContentIndex`, `IContentDefinition`, `ContentId`,
               `ContentKind` (T-026 adds the definition structs; `ContentIndexFactory`
               needs an owner); `IIdAllocator`, `EntityId` (the allocator
               behaviour has **no owning task**).
             - T-001 implements **in full**: `SimConstants`, `ISimClock`,
               `SystemId`/`SYSTEM_CORE`, the builder and factory, the phase
               loop, `SimInvariantException`, `ISimLog`/`LogLevel`/`LogKey`/`LogArgs`
               (the shape is all there is), `Checkpoint`/`ICheckpointSink`
               with the real phase-4 cadence of §8.9, and the world hash per
               §8.9 as written, which is **not a stub** (A6).
             - T-001's tests may rely on the checkpoint cadence, `Tick`
               values, the length, order and values of `SystemHashes`, a
               checkpoint equalling `WorldStateHash()`, and the hash being
               deterministic and changing with the tick and with any system
               hash. They may **not** rely on the exact `WorldHash` value,
               which T-004 pins with `IStateHasher`.
             - **Dispatch (§8.6) has no owning task.** Recommended: T-001
               implements it in full, since it is phase 3 of the loop T-001
               owns, and its tests then cover §8.6. Planner's call.
             - (A8) Yes. Two hosts built from equal configs and equal
               systems, stepped the same total with different chunkings,
               have equal hashes and checkpoint sequences. Save/load proper is
               `sim.save`.
Status:      ANSWERED (spec/08-interfaces-core.md#tick-numbering-and-clock-arithmetic-q-014)
             — task-level items above need the Planner

### Q-015 — `Fx` edge semantics and C# shape
Raised by:   Test Author, worker-2 (via coordinator) / T-003
Blocking:    T-003
Question:    (A9) Which exceptions? (A10) `FromRaw`, the `Raw` accessor,
             static or instance, operators? (A11) Overflow on `Add`/`Sub`/
             `Neg`/`Abs`/`FromInt`, and `Clamp` with `lo > hi`? (A12) The
             `Parse` grammar and `ToDisplayString` rounding? (A13) Negative
             `Sqrt`, and what "exact-stable" means? (A14) The direction of
             `RoundHalfUp`? (A16) Where the cross-process bit-exactness
             test's child process lives?
Why it matters: A test suite and an implementation can disagree on every one,
             and each is visible in content parsing or a golden hash.
Answer:      `08-interfaces-core.md` §8.3 "C# shape and edge cases": `FromRaw`
             is added; `Raw` is a get-only property; operations are static
             and `ToDisplayString` is an instance method; operators are
             required, with no conversions; `Fx` throws and never wraps or
             saturates (the table gives every operation);
             `OverflowException`/`DivideByZeroException`/`FormatException`/
             `ArgumentException`/`ArgumentOutOfRangeException` as tabled;
             `RoundHalfUp` rounds toward +∞; `Sqrt` is the exact floor
             integer root and throws for negatives; the `Parse` grammar is
             `-?(0|[1-9][0-9]*)(\.[0-9]{1,10})?`, exact then floored;
             `ToDisplayString` floors, with 0 to 10 decimals. (A16) There is
             no child process. Bit-exactness is golden vectors committed in
             the test. T-003's task text ("two independent process runs")
             is stale.
Status:      ANSWERED (spec/08-interfaces-core.md#c-shape-and-edge-cases-q-015)

### Q-016 — No task before T-006 can pass the full `ci/run-checks.sh`
Raised by:   coordinator / Phase 0 release
Blocking:    the "done" definition of T-001 to T-005, T-026 and T-027
Question:    `ci/run-checks.sh` without `--fast` runs
             `dotnet run --project tools/SimHarness -- determinism|saveload|
             promotion|budget`, and those subcommands are T-006's. `CLAUDE.md`
             "Definition of done" requires the determinism gate to pass. What
             is "green" for the tasks that land before T-006?
Why it matters: Either no Phase 0 task can ever be done, or each agent makes
             up its own exception to the gate.
Proposed:    Until T-006 merges, a task is green when the CI jobs `path-guard`
             and `build-and-test` pass. `build-and-test` runs
             `ci/run-checks.sh --fast`, which builds and tests through
             `AirportSim.sln` (`07` L9). The `determinism` job is expected to
             fail, is not required for those merges, and becomes required
             the moment T-006 merges. T-006 itself must pass the full script.
Answer:      HUMAN DECISION — owner, 2026-09-24: adopted as proposed. Until
             T-006 merges, green = `path-guard` + `build-and-test`
             (`ci/run-checks.sh --fast`, with build and tests through
             `AirportSim.sln`). From the moment T-006 merges, the full
             `ci/run-checks.sh` is mandatory. The Architect recorded it and
             did not decide it.
Status:      ANSWERED — HUMAN (spec/open-questions.md#q-016)

### Q-017 — The world hash omits `sim.core`'s own state; the hasher is unconstructible and its encoding is ambiguous
Raised by:   worker (via coordinator) / T-004, T-005, T-001 (batch 2 H1–H7, C10; batch 3 E3)
Blocking:    T-001, T-004, T-005
Question:    The world hash is the tick plus the system hashes, but core is
             not a system, so a `NoOp` or an id allocation could never change
             it. How are `bool` and spans encoded, and is a span
             length-prefixed? What are the constants? Is `IStateHasher` a
             struct or a class, how is it constructed, and who ships it? Is
             `SystemHashes` fixed-length? Who owns the array? Is there an
             index API? Also: what are the `IIdAllocator` semantics?
Why it matters: A `NoOp` could not prove the queue participates. Unprefixed
             spans collide. Every system hash would depend on a type nobody
             can construct.
Answer:      `08` §8.9 "Encoding, the concrete hasher and the core section":
             standard FNV-1a-64 constants, 8-byte little-endian integers,
             1-byte bool, spans prefixed with a `uint64` length, and golden
             vectors. `public struct StateHasher`, where `default` is fresh
             and `Result` is readable at any time. It **ships in T-001**
             (coordinator proposal accepted), and T-004 proves the vectors.
             A `CoreHash` section covers the next sequence, the pending
             commands and the id counters. It is fed after the tick and
             before the systems, and it is carried on `Checkpoint.CoreHash`.
             `SystemHashes` covers registered systems only, in a fresh array
             each time, and has no index API (H4–H6, part in Q-014). `08`
             §8.4: `EntityId = (owner << 48) | counter`, and counters start
             at 1. H7: a same-tick-dispatch fixture comparing
             "correct order ≠ swapped order" is an acceptable test of the
             phase order. Its design is the Test Author's.
Status:      ANSWERED (spec/08-interfaces-core.md#encoding-the-concrete-hasher-and-the-core-section-q-017)

### Q-018 — No task declares the event structs, and their field types have no `sim.core` home
Raised by:   worker-1, worker-3 (via coordinator) / T-026, T-008, T-007 (batch 3 E1, E2; batch 4 S1, S2)
Blocking:    T-007, T-008, T-021, T-022, T-024
Question:    `03` puts events in `sim.core`, but no task authors the structs
             (`FlightPlanPublished`, `FlightMilestoneReached`, the flow
             events, `DelayEvent` and so on). `AirlineId`, `MovementKind` and
             `CohortId` are declared in module files, while core events carry
             them.
Why it matters: T-008 may write only `src/sim/schedule/**`, yet it emits
             core-owned events. `sim.delay` may reference only `sim.core`.
Answer:      Option (a). `10` §10.9 declares every Phase 0 and Phase 1 event
             struct exactly, and `sim.core` owns them all. `AirlineId`,
             `MovementKind`, `CohortId`, `FlightMilestone`, `DelayNode` and
             `DelayNodeKind` are relocated to `sim.core` (compiled home only;
             shapes unchanged). `DelayEvent { DelayNode Node }`.
             `FlightPlanRevised`, `FlightCancelled` and the Phase 2 rows stay
             undeclared until their untyped fields are pinned. E2 is Q-014's
             `SimEventHandler<T>`. For the Planner: T-026 authors them, and
             T-007's "`CohortId` stays `sim.flow`'s type" is stale.
Status:      ANSWERED (spec/10-events.md#109-declared-event-structs-q-018)

### Q-019 — The RNG reference is not pinned tightly enough to test
Raised by:   worker (via coordinator) / T-002 (batch 2 R1–R9)
Blocking:    T-002
Question:    Several details were unpinned: the byte encoding of the name
             hash, the SplitMix64 constants, the fill order, the zero-state
             replacement, the Lemire variant, `min >= max`, `Chance`'s draw
             count, the `Shuffle` loop, the stream hash layout, calling
             `Stream` twice, a factory, name validation and uniqueness, and a
             save seam. There were no golden vectors either.
Why it matters: A test oracle that is someone's reading of the spec, rather
             than the spec itself.
Answer:      `08` §8.8 "Exact reference". It pins everything, adds
             `RandomServiceFactory.Create`, and gives three golden vectors
             plus `NextInt` and `NextFx01` checks, computed by the Architect
             from the pinned algorithms. The xoshiro and SplitMix64 cores
             were checked against their published reference outputs. Name
             grammar `sim.<module>.<purpose>` makes uniqueness structural,
             replacing the unimplementable "CI asserts". `Stream` returns
             the same live stream. There is no save seam until `sim.save`.
Status:      ANSWERED (spec/08-interfaces-core.md#exact-reference-q-019)

### Q-020 — Command queue semantics are underspecified
Raised by:   worker (via coordinator) / T-005 (batch 2 C1–C11)
Blocking:    T-005
Question:    How do tests reach `LogSince`? Is `ApplyDue` public? Is
             `Sequence` global? What is its start value, and does a rejection
             consume one? What is the payload type, and what about `null`?
             What does `NoOp` do with a payload? How is the owner enforced,
             what about a foreign issuer, who logs an impossible `Apply`,
             and what does `LogSince` include? How often is `Validate`
             called?
Why it matters: Replay and save depend on every one of them.
Answer:      `08` §8.7 "Queue semantics".
             - `ICommandQueue` is internal, and
               `ISimHost.CommandLogSince(Tick)` is added.
             - The payload is a `byte[]`, copied on admission. A `null`
               payload throws.
             - Admission runs TooLate → NotPermitted (issuer) → UnknownKind
               → Validate. `Validate` runs exactly once, and `NoOp` requires
               an empty payload.
             - `Sequence` is global and starts at 1. A rejection does not
               consume one.
             - `LogSince` is inclusive by `cmd.Tick`, includes pending
               commands, and is ordered by (Tick, Issuer, Sequence).
             - The owner is fixed by the payload table. Mismatches and
               duplicates throw.
             - The handler logs an impossible `Apply` at Info. Core does not
               catch it.
             - `TrySubmit` during `Step` throws.
Status:      ANSWERED (spec/08-interfaces-core.md#queue-semantics-q-020)

### Q-021 — Who writes `tests/fixtures/**`?
Raised by:   worker-3 (via coordinator) / T-008 (batch 4 S3)
Blocking:    no
Question:    `11` §11.10 makes the fixture "binding on the Test Author", but
             T-008's writable paths include `tests/fixtures/schedule/**`.
Why it matters: Two authors for one fixture.
Answer:      `07` "Solution layout and build": everything under `tests/**`,
             fixtures included, is the Test Author's. The path guard
             already blocks workers from writing there, so worker grants
             under `tests/**` grant nothing. The Planner may drop them.
Status:      ANSWERED (spec/07-conventions.md#solution-layout-and-build-q-013)

### Q-022 — `ComposedSim` lacks `World`; `app.host`'s references are an umbrella
Raised by:   worker-3 (via coordinator) / T-031 (batch 5 W1, W2)
Blocking:    T-031
Question:    `16` §16.4 `ComposedSim` has no `World` field, although the
             composer constructs `sim.world` (Q-012 was never threaded
             through `16`), and T-031's task file adds one. `03`'s `app.host`
             row, "sim, render, ui", cannot drive `07` L2's per-module
             `ProjectReference` rule.
Why it matters: The task file and the spec disagree, and a project file
             cannot be derived from the spec.
Answer:      (W1) `16` §16.4 gains `IWorldSystem? World`, which matches T-031.
             `RenderSources` is unchanged, since render reads no world
             interface. (W2) `07` L2 now fixes the direct references from
             each project's published interface file, with a binding
             Phase 0/1 table. `03`'s `app.host` row points to that table.
             `03`'s `sim.turnaround` row gains `schedule`, because
             `TurnaroundFactory` takes `IScheduleSystem` (`13`), which `03`
             did not allow.
Status:      ANSWERED (spec/07-conventions.md#solution-layout-and-build-q-013)

### Q-023 — Small RNG and constant gaps left by Q-019/Q-020
Raised by:   coordinator / T-002, T-005 (G1–G5)
Blocking:    T-002
Question:    (G1) Must the `RngStreamName` pattern match the whole string,
             and which exceptions cover `null` and a malformed name? (G2) The
             xoshiro `{1, 2, 3, 4}` check and the all-zero replacement cannot
             be reached through the public API: are they tested, or is there
             a seam? (G3) Is there a per-call time budget for draws? (G4)
             Where does `PLAYER_LOCAL` live? (G5) Are T-002's cross-process
             test and "CI uniqueness check" superseded?
Why it matters: Each one is a test the Test Author and the worker could
             write differently.
Answer:      `08` §8.8 "Exact reference" and §8.7 "Issuer, kinds and
             payloads".
             - (G1) The whole string, as `\A…\z`. `null` throws
               `ArgumentNullException`, a malformed name throws
               `ArgumentException`, and `Stream(default)` throws
               `ArgumentException`.
             - (G2) The golden vectors are the sole required proof. There is
               no seam. The zero state is unreachable, because SplitMix64
               never emits four zeros in a row. Both are untested by design.
             - (G3) There is no per-call budget. Draws allocate nothing and
               are charged to the calling system's budget.
             - (G4) `SimConstants.PLAYER_LOCAL`, a `static readonly
               PlayerId`. `SYSTEM_CORE` follows the same rule.
             - (G5) Yes. There is no child process (as in Q-015 A16) and no
               CI uniqueness check (Q-019). T-002's text is stale.
Status:      ANSWERED (spec/08-interfaces-core.md#exact-reference-q-019)

### Q-024 — Which tick count feeds the `WorldHash` of a wrapped exception?
Raised by:   test-author-t001 (via coordinator) / T-001
Blocking:    T-001 tests
Question:    §8.5a computes `WorldHash` "at that moment" when an exception
             escapes tick `t`. Is the count fed `t` (the tick did not
             complete) or `t + 1`? Also: is the `IIdAllocator` 2^48 overflow
             tested?
Why it matters: The two readings give different hashes for the same failure.
Answer:      `08` §8.5a: `t`, the number of ticks completed. `CurrentTick`
             stays `t`, and the partial state is hashed as it stands, with
             no rollback. `08` §8.4: the 2^48 overflow cannot be reached
             through any public API, so it is untested by design.
Status:      ANSWERED (spec/08-interfaces-core.md#85a-broken-invariants-q-014)

### Q-025 — `tools.simharness` has no test project
Raised by:   test-author-2 (via coordinator) / T-006 (H1)
Blocking:    T-006
Question:    `07` L1 has no test-project row for `tools.simharness`. T-006
             says its tests go in `tests/sim/core/**`, but L3 lets a test
             project reference only its own module's production project.
             Should there be a harness test project, or should the tests
             spawn the harness process?
Why it matters: Without a row, harness tests either break L3 or have no home.
Answer:      `07` L1 gains `tests/tools/simharness/AirportSim.Tools.SimHarness.Tests.csproj`,
             and L3 names its one reference, the harness project. Tests run
             in process and never spawn a process. `07` L8: T-006 adds the
             project to `AirportSim.sln`. T-006's `tests/sim/core/**` is
             stale.
Status:      ANSWERED (spec/07-conventions.md#solution-layout-and-build-q-013)

### Q-026 — The harness CLI contract and a divergence seam are unspecified
Raised by:   test-author-2 (via coordinator) / T-006 (H2)
Blocking:    T-006
Question:    What exit codes and stdout do `determinism`, `saveload`,
             `promotion` and `budget` produce (`--hash-only` included), as
             `ci/run-checks.sh` invokes them? How can a test prove that a
             gate fails on nondeterminism without a CLI flag the script
             does not use?
Why it matters: The script diffs stdout and reads the exit code. A gate that
             is never shown to fail may be one that always passes.
Answer:      `19` (new). The public surface is `HarnessCli.Run`,
             `HarnessGates` and `SimComposer`/`GateResult` (§19.1). Every
             run submits a NoOp script, and runs are compared by
             checkpoint, then by final hash (§19.2). The exact CLI forms,
             exit codes and one-line stdout grammar are in §19.3:
             0 pass, 1 gate failed, 2 usage, 3 harness error, and no "not
             implemented" code. `budget` is covered in §19.4. The divergence
             seam is an injected composer.
Status:      ANSWERED (spec/19-interfaces-harness.md#191-public-surface-q-026)

### Q-027 — What does `determinism_save_load` do before `sim.save`?
Raised by:   test-author-2 (via coordinator) / T-006 (H3)
Blocking:    T-006
Question:    T-006 says `saveload` snapshots RNG stream state, but `08` §8.8
             says no save seam exists. Should the gate use replay
             equivalence or be deferred?
Why it matters: The worker would otherwise invent a seam, or stub a gate
             that `02` says can never be disabled.
Answer:      `19` §19.2: replay form. The "save" at `saveAt` is the seed,
             the content, the composition and `CommandLogSince(0)`. A fresh
             run resubmits the log and must match at `saveAt` and to the
             end. No RNG or system snapshot is taken. T-006's "snapshots RNG
             stream state" is stale. The gate's meaning in the locked `02`
             was referred to the owner (§19.5).
             HUMAN DECISION — owner, 2026-09-26: approved. Until `sim.save`
             exists, `determinism_save_load` is satisfied by the replay
             check, and the real snapshot round-trip replaces it when
             `sim.save` is specified.
Status:      ANSWERED — HUMAN (spec/19-interfaces-harness.md#195-saveload-before-simsave--human-decision-q-027)

### Q-028 — Content index edge cases, enum member casing, test file naming
Raised by:   Test Author (via coordinator) / T-026
Blocking:    T-026 tests
Question:    Which exception does `ContentIndexFactory.Create` throw for a
             duplicate id, and for a `null` element? Does `TryGet<T>` return
             false or throw for a definition of another type, and for
             `T = IContentDefinition`? Do snake_case IDL enum members, such
             as `06`'s `DelayCategory`, stay snake_case in C#? How is a
             multi-word test subject named under L6?
Why it matters: Each one is a test assertion or a public name, and enum
             casing binds every module.
Answer:      `08` §8.11a: a `null` list throws `ArgumentNullException`. A
             `null` element, a `null` `Id.Value` or a duplicate id throws
             `ArgumentException`, and the input is copied. `08` §8.11:
             `TryGet<T>` is true if and only if the id exists and its
             definition is a `T`, otherwise false with `default`. The type
             never throws, `IContentDefinition` matches anything, and a
             `null` id value throws `ArgumentException`. `07` L10: enum
             members are PascalCase in C# (`late_inbound` → `LateInbound`),
             and snake_case survives only in data. `07` L6: a subject may be
             several words, and each test in `<Subject>Tests` starts with
             `test_<subject in snake_case>_`, going to the longest matching
             subject.
Status:      ANSWERED (spec/07-conventions.md#solution-layout-and-build-q-013)

### Q-029 — `SaveLoad` order, the script length in A, unreachable reports
Raised by:   test-author-2 (via coordinator) / T-006
Blocking:    T-006 tests
Question:    In what order do U, A and B run, and is `reload` checked before
             B is compared with U? Does A's script cover the gate's full
             `ticks` or only `saveAt`? How are `at=world`, `at=count` and
             CLI exit codes 1 and 3 tested, when the CLI composition is
             empty? Does T-006 depend on T-005?
Why it matters: A test can pin a report only if the order is fixed. If A's
             script stopped at `saveAt`, B would lack U's later commands and
             no deterministic sim could pass.
Answer:      `19` §19.2:
             - The order is U, then A, then B. `reload` is checked first,
               after B's first `saveAt` ticks, and a mismatch fails at once
               with `tick=saveAt at=reload`.
             - The script always uses the gate's full `ticks`, in A too.
             - `world` and `count` are unreachable by construction, and CLI
               exit codes 1 and 3 are unreachable until a composition
               exists. All are untested by design, with no seam added.
             - T-006 depends on T-005 (the script needs `TrySubmit`, and
               `SaveLoad` needs `CommandLogSince`). The Planner adds the
               edge. T-005 is already merged (#26).
Status:      ANSWERED (spec/19-interfaces-harness.md#192-what-each-gate-does-q-026-q-027)

### Q-030 — The walk-graph file format, load failures and unknown-id queries
Raised by:   Test Author (via coordinator) / T-012
Blocking:    part of T-012
Question:    `18` §18.2 left the file format to the worker, yet the Test
             Author writes the fixture and four tests feed `Load` raw bytes.
             (a) What is the exact format? (b) Which exception does a load
             failure throw, and what does its message carry? (c) Which
             exception does a query for an unknown `NodeId`/`EdgeId` throw?
Why it matters: A fixture cannot be written against a format that has not
             been chosen, and tests assert exact exception types.
Answer:      `18` §18.2 "File format":
             - (a) The `08` §8.11 strict JSON subset:
               `{"schema_version":1,"nodes":[{"id","length_metres"}],"edges":[{"id","from","to"}]}`.
               Ids are ≥ 1, and the arrays may be in any order. The fixture
               is `phase0-landside.json`, and `FixtureHash` is FNV over the
               exact bytes.
             - (b) `FormatException`, with a message that starts with
               `sourceName: ` and carries `line <n>` or the field and id.
               `07` "Error handling" makes this the rule for every loader.
             - (c) `ArgumentException` (§18.3).
Status:      ANSWERED (spec/18-interfaces-world.md#file-format-q-030)

### Q-031 — Walk-graph, schedule and content-loader details
Raised by:   Test Authors (via coordinator) / T-012, T-008, T-027
Blocking:    tests of T-012, T-008, T-027
Question:    T-012: (W1) the tie-break does not terminate on zero-length
             cycles; (W2) is `CanReach(n, n)` true?; (W3) which id is
             "offending" for a duplicate pair or a self-loop?; (W4) how do
             tests locate `tests/fixtures`? T-008: (S1) the fixture text
             contradicts itself on rotations; (S2) when does "highest day
             materialised" advance, and do lists take a count prefix?; (S3)
             what does a `CreateSystem` failure message start with?; (S4) is
             the budget a mean or a worst tick?; (S5) is
             `PendingInjectionCount` 0 before publication? T-027: (C1) which
             file does a cross-file duplicate name?; (C2) which id does an
             unresolved size category name?; (C3) nested directories and a
             `null` source?
Why it matters: Each one is a test assertion.
Answer:      `18` §18.3 and §18.2:
             - (W1) Simple paths only.
             - (W2) True, as the empty path. `CanReachVia` and `PathVia` are
               defined through it.
             - (W3) The edge id, and for a duplicate pair the larger one.
             `07` L4:
             - (W4) Walk up from `AppContext.BaseDirectory` to the directory
               holding `AirportSim.sln`.
             `11`:
             - (S1) 200 rows = 99 rotations + lone A + lone D.
             - (S2) Day 0 is materialised at construction, and day `D+1` at
               the first tick of day `D`. Lists 3 and 4 are count-prefixed.
             - (S3) The message starts with `sim.schedule: ` and carries
               `flight_ref`, the column and the id.
             - (S4) `03`'s mean ≤ 0.10 ms and p99 ≤ 0.20 ms over one
               sim-day. A mean over two days is not enough.
             - (S5) Confirmed, and it is 0 for an unknown id too.
             `08` §8.11:
             - (C1) The later file in ordinal order.
             - (C2) The aircraft file's path, with the unresolved category
               id.
             - (C3) A nested `*.json` is a load failure, and a `null` source
               throws `ArgumentNullException`.
             `04`: a note that `Fx.Parse` flooring makes tiny positive values
             0.
Status:      ANSWERED (spec/18-interfaces-world.md#183-routes)

### Q-032 — `sim.flow`: graph file format and exact tick semantics
Raised by:   T-007 Test Author (via coordinator) / T-007 (critical path)
Blocking:    T-007, and T-008 behind it
Question:    (A) The `FlowGraph` file format was "the worker's choice", but
             `Load` is the only constructor. (B) When does the credit cap
             apply, and at what value? (C) Can a cohort move twice in a
             tick? (D) What are the non-`Queue` capacities, and is "above"
             `>`? (E) What are the epsilon, rounding and non-`Queue` value
             of the predicted wait? (F) How is `traversalTicks` rounded, and
             does it apply to non-corridors in the route cost? (G) What are
             the threshold comparisons and timing? (H) What are the
             `FlowBlocked` fields and the event order?
Why it matters: T-007's tests cannot be written, and T-007 blocks T-008.
Answer:      `09` §9.11 "File format" and the new §9.12:
             - (A) The JSON subset with `nodes[id, kind]`. Queue nodes add
               `server_count`, `servers_open` and `queue_profile`. Failures
               are `FormatException` with `sourceName: ` and the node id.
             - (B) The cap applies only when `moved < served`. The unused
               whole passengers are discarded, and the credit is capped at
               `serverTick` = rate × 6 / 60.
             - (C) No cohort moves twice: only `EnteredNodeAt < t` is
               eligible, and an arriving cohort gets `EnteredNodeAt = t`.
             - (D) Only `Queue` has a capacity, and it is full at
               `>= CapacityStanding`, measured at the start of the tick.
             - (E) `EPSILON = 1/1000`, floored,
               `capacityPerMinute = ServersOpen × rate`, and 0 for a
               non-`Queue` node.
             - (F) `Div` floors, then `Ceil`, with a minimum of 1. Every
               node on the path counts in the route cost.
             - (G) Exceed when `w > T`, clear when `w < T − h`, evaluated
               after movement and merge in ascending `NodeId`. The flag is
               hashed.
             - (H) `BlockedBy` is the immediate full target, and `Held` is
               the node the cohort is on (the corridor itself, past
               `DueAt`). The event order is pinned.
             LOW CONFIDENCE for the owner: `EPSILON`, the cap value,
             unlimited `Hall` capacity and head-of-line blocking.
Status:      ANSWERED (spec/09-interfaces-flow.md#912-exact-tick-semantics-q-032)

### Q-033 — `sim.flow` entry-point details; JSON string escapes
Raised by:   T-007 Test Author and reviews (via coordinator) / T-007
Blocking:    T-007 tests
Question:    (1) Which exceptions do `Inject` and `Absorb` throw? (2) Is
             `PassengersMissedFlight` one event per flight or per cohort,
             and what is `LastBlockedAt` for a cohort never blocked? (3)
             What is the `DueAt` of a merged corridor cohort? (4) What is the
             `EnteredNodeAt` of an injected cohort? (5) What value does the
             live-cohort ceiling take? (6) May `Absorb` run outside a tick?
             (7) Which string escapes does the `08` §8.11 JSON subset allow?
Why it matters: Each one is a test assertion, and three loaders must agree
             on strings.
Answer:      `09` §9.7 "Exact rules", §9.3, §9.9, §9.10 and `08` §8.11
             "Strings":
             - (1) A wrong or unknown node throws `ArgumentException`, and
               `count <= 0` throws `ArgumentOutOfRangeException`. An
               unknown flight in `Absorb` returns 0.
             - (2) One event per flight, and only if the count is above 0.
               `LastBlockedAt` is the node holding the most missed
               passengers (the `MostHeldAt` rule). Missed cohorts are
               removed.
             - (3) Corridor cohorts merge only with an equal `DueAt`.
             - (4) `N`, the number of flow `Tick` calls completed: `t`
               during tick `t`, or `CurrentTick` between ticks.
             - (5) Fixture sizing, set by the Test Author. It is not
               balance.
             - (6) `Absorb` publishes before it mutates, so outside a tick
               `Publish` throws `InvalidOperationException` with no state
               changed. `Inject` may be called between ticks.
             - (7) JSON's eight escapes plus `\uXXXX`, which matches the
               merged T-027 loader. Surrogate escapes, raw control
               characters and invalid UTF-8 are load failures.
             Added from the T-010 Test Author (`09` §9.7 "Promotion rules",
             `19` §19.2):
             - (8) `AgentsAt` gives exactly one view per passenger, so its
               count equals `Population`.
             - (9) An unknown node throws `ArgumentException`. Every node is
               promotable.
             - (10) `SetPromoted` is callable at any time, inside a `Tick`
               too.
             - (11) `SetPromoted` never allocates, and `AgentsAt` allocates
               nothing after warm-up.
             - (12) A separate harness task, not T-010, makes the
               `Promotion` gate promote. It depends on T-010 and on the CLI
               composition including `sim.flow`.
Status:      ANSWERED (spec/09-interfaces-flow.md#97-module-interface)

### Q-034 — Player-adjustable graphics quality (D10)
Raised by:   owner, via coordinator, 2026-09-27
Blocking:    no (T-020, T-029, T-031, T-032, T-033, T-034 are to be amended)
Question:    The owner decided that "the final user should be able to
             increase or decrease graphics so the game can also be run on a
             low resource laptop". How do the presentation specs support
             it?
Why it matters: No presentation spec had a quality setting, a settings
             control or a player preference.
Answer:      `15` §15.14:
             - `GraphicsPreset` (Low/Medium/High/Custom) and
               `GraphicsSettings`, with six knobs: `DrawAgents`,
               `MaxDrawnAgentsPerNode`, `FrameRateCap`,
               `ResolutionScalePercent` and `AntiAliasing`, plus `Preset`.
             - Structural bounds and monotonicity. `High` is today's Phase 1
               behaviour.
             - The settings are passed to `Build` and `Update`, and the
               backend applies its knobs. `DrawAgents` gates only
               render-driven promotion, which is outcome-neutral.
             `17` §17.4a: a modal settings panel, which does not pause, plus
             three new inputs and a player-preference text codec. `16`
             §16.6: `IPreferenceStore`, written on change and read at
             assembly, never in the bundle.
             Owner addendum, 2026-09-27: graphics never affect gameplay or
             difficulty. A binding invariant in `15` §15.14 and `17` §17.4a:
             - every non-`Agent` primitive is identical at every setting;
             - no knob touches time, pacing, input or click targets;
             - scaling is presentation only.
             It has tests on both sides.
             HUMAN DECISIONS — owner, 2026-09-27, applied:
             - the minimum GPU is integrated graphics with no dedicated
               VRAM (`01`, the one authorised line; CPU and RAM
               unchanged);
             - `Low` holds the render target on it, and shared GPU memory
               counts against the 8 GB (`15` §15.11, `16` §16.10);
             - the settings panel pauses the sim while it is open (`17`
               §17.4, §17.4a);
             - the first-launch default is `Medium` (`15` §15.14).
             The Architect proposed the `Low` and `Medium` values (`15`
             §15.14) and a 2 GB process memory budget (`16` §16.10), both
             LOW CONFIDENCE — owner may revise.
Status:      ANSWERED (spec/15-interfaces-render.md#1514-graphics-quality--human-decision-owner-2026-09-27-d10)

### Q-035 — `sim.core`'s `EventBus` allocates on the first publish of an event type
Raised by:   worker / T-007, 2026-09-27. It was filed as "Q-034", which
             collides with the graphics question, and was renumbered by the
             coordinator.
Blocking:    T-007 (`FlowBudgetTests.test_flow_budget_update_path_allocates_nothing`)
Question:    `EventBus.Publish<T>` creates a `Channel<T>` the first time a
             type with no subscriber is published. In `FlowBudgetTests.Loaded`
             nothing subscribes to `sim.flow`'s events. The first
             `QueueThresholdExceeded` comes at tick 1695, inside the
             measured window (ticks 1201–1799), and allocates 368 bytes
             there. `08` §8.6 did not say whether that is allowed. It
             reproduces on `origin/main` without T-007's changes, and the
             fix is outside `sim.flow`'s paths. Is this a `sim.core` defect,
             or is the test's expectation wrong?
Answer:      A `sim.core` defect, from a gap in `08` §8.6. The test is right:
             `08` §8.5 and `07` "Performance" forbid allocation on the tick
             path, and neither makes an exception for a first publish.
             A module cannot pre-warm the bus, because `Publish` outside a
             tick throws. So a first-publish allowance would push an
             unfixable warm-up duty onto every module.
             `08` §8.6 "Allocation" now states:
             - after `Build`, the bus allocates nothing, from the first
               tick, whatever was published before and whatever the
               earlier per-tick peaks were;
             - the channel set is fixed at `Build`, because subscription
               closes there;
             - a type with no subscriber stores nothing, but its `Publish`
               still runs every check, consumes a `Sequence` and returns
               its `EventId`;
             - capacity for `MAX_EVENTS_PER_TICK` events is reserved at
               `Build`.
             Five new `sim.core` tests are named in §8.6. No outcome, hash
             or golden changes.
Status:      ANSWERED (spec/08-interfaces-core.md#86-event-bus)

### Q-036 — `sim.flow` routing cache: what a cache may and may not do
Raised by:   Test Author / T-011, via coordinator, 2026-09-28
Blocking:    no. T-011 passes: mean 0.50 ms and p99 2.60 ms against 2.5 and
             5.0 ms.
Question:    T-011's black-box runs show routing cost growing linearly with
             the number of pooled gates: p99 of 5.0 ms at 48 gates and
             9.9 ms at 96. The worst ticks follow show-up injections.
             `09` §9.6 says the budget test shows whether routing needs
             caching, and §9.10 says a cached result is not hashed. Neither
             says what a cache must preserve. Without that, a subtly wrong
             cache passes the time budget and silently changes routing.
Answer:      `09` §9.6 "Routing cache". A cache is optional, added only
             against a failing measurement, under six binding rules:
             - (1) observably identical to the uncached rule: the same
               cost `Raw` per pair and the same tie-break. Grouping the
               sums is free, since `Fx` addition and integer-multiple
               `Mul` are exact. The per-node `traversalTicks` and snapshot
               waits must each be that node's own §9.12 value;
             - (2) the static part may live for the run, and depends only
               on `IWorldSystem`'s load-time answers, the `FlowGraph`
               node kinds and the pax profiles' walk speeds;
             - (3) the wait-dependent part is memoised within one tick
               only. An entry serves a cohort only for the same node, the
               same walk speed and every cohort field that determines the
               destination set. At Phase 0/1 that is no field, because
               only `Departing` cohorts exist (Q-040), so the key is
               `(node, walk speed)`. The amendment that widens what
               determines the set must extend the key and the test;
             - (4) it is not hashed or saved, and is rebuilt from rule 2's
               sources on restore/replay with the same routes;
             - (5) no allocation in the update path;
             - (6) no dependence on promotion, presentation, iteration
               order or fill order.
             Required test when a cache is added, owned by the Test Author
             of whichever `sim.flow` task adds it:
             `test_flow_routing_cache_matches_uncached_reference`. It
             runs a lockstep, uncached reference model of §9.12's whole
             tick. Every input comes from the fixture, content and script
             the test authors, or from `IWorldSystem`. After every tick it
             compares:
             - the per-node, per-`CohortKey` head counts;
             - `Population` and `PredictedWaitMinutes`;
             - the multiset of `FlowBlocked`/`FlowUnblocked` as
               `(kind, Held, BlockedBy, Key)`.
             No `CohortId`s are compared. The reference's own id order
             follows §9.3's rule that every move takes a new id. The script
             must include each of
             these, so a cache wrong in that way fails:
             - distinct walk speeds released from one node in one tick,
               choosing different edges;
             - an exact-cost gate tie (NodeId, with the lower id only via
               the higher `EdgeId`) and an edge tie (EdgeId);
             - lane changes that flip the choice;
             - a blocked cohort re-routed;
             - a show-up spike;
             - a restart. Until `sim.save` exists this is replay from
               seed, which proves reproducibility only, not fill-order
               independence.
             HUMAN DECISION — owner, 2026-09-28: gate assignment is not
             brought forward, and the 90k max-tier measurement decides
             whether the performance task is released.
Status:      ANSWERED (spec/09-interfaces-flow.md#96-corridors-and-routing)

### Q-037 — Does `18` §18.5's single pooled `Gate` bind `sim.flow` stress fixtures?
Raised by:   Architect, from the Q-036 assessment, 2026-09-28
Blocking:    no. It affects the T-011 test branch
             (`test-author/T-011-stress-30k-tests`, whose `StressDay` has 24
             gates) and the future 90k max-tier fixture.
Question:    `18` §18.5 said "Phase 0/1 fixtures declare **one** `Gate`
             node". `StressDay` declares 24 pooled `Gate` nodes. Does the
             single-gate rule bind only the shared world fixture (§18.6),
             or every `sim.flow` fixture?
Answer:      Architect's authority: this is fixture sizing (Q-033), not
             balance or scope. The single `Gate` binds the shared file
             `tests/fixtures/world/phase0-landside.json` (`18` §18.6) and
             every fixture that loads it. A `sim.flow`-local stress or
             budget fixture builds its own walk graph and does not load
             that file. It may declare several pooled `Gate` nodes, and
             states the count and its derivation in the test (`09` §9.10,
             `18` §18.5). `18` §18.6's list of users is corrected to match.
             T-011's `StressDay` builds its own graph, so it is not one of
             them, and its 24 gates conform. T-011's other tests that load
             the shared file keep its single `Gate`. Nothing merged
             changes.
             Recommendation for the owner, not decided: the 90k max-tier
             fixture should pool **60** `Gate` nodes, one per max-tier
             stand (`01`). The owner said this measurement decides, and
             until gate assignment arrives, pooling over every gate is the
             cost Phase 1 content will actually pay. One gate would measure
             a cost the game does not have. Setting the count stays the
             Test Author's, with the derivation stated. **LOW CONFIDENCE —
             owner may revise.**
Status:      ANSWERED (spec/09-interfaces-flow.md#910-state-hashing-and-budget); recommendation for the owner

### Q-038 — `sim.schedule`: a show-up bucket due before its flight's publication
Raised by:   Reviewer, PR #54 (T-008) finding 1, via coordinator, 2026-09-28
Blocking:    T-008
Question:    §11.6 computes injections at publication, and `PublishTick` is
             one day before `ScheduledTick`. `minutes_before_std` is
             unbounded in content, so two failures are possible on day
             `d ≥ 2`:
             - with `minutes_before_std > 1440 + minute-of-STD`, a bucket's
               tick has already passed when the day is materialised, so
               those passengers are never injected;
             - between 1440 and that value, passengers are injected before
               `FlightPlanPublished`.
             Clamp, reject, or extend the horizon?
Answer:      Reject at load, which is the narrowest option. The new constant
             `MAX_SHOW_UP_MINUTES_BEFORE_STD = PLAN_PUBLISH_LEAD_TICKS /
             TICKS_PER_SIM_MINUTE` (1440) bounds every bucket of every
             referenced pax profile. The check is at `CreateSystem`, where
             profiles resolve (§11.9a), with the existing `FormatException`
             shape. With the bound, every injection tick is at or after
             `PublishTick` (§11.6, stated as an inequality), so nothing is
             queued for a past tick and conservation holds. The day-0
             `clamp to 0` is unchanged and is the day-0 case of the same
             inequality.
             The early-injection case is closed **by tick**: no passenger
             reaches `sim.flow` in an earlier tick than its flight's
             `FlightPlanPublished`. On a **shared tick** (a bucket exactly
             at the bound, or the day-0 clamp), `Inject` runs in phase 2
             and handlers see the event in phase 3 (`08` §8.5, §8.6). So
             `sim.flow` holds the cohort first. This is explicitly accepted
             (§11.6): no Phase 0/1 consumer depends on the opposite order.
             §11.6 also fixes the order of the calls inside
             `sim.schedule`'s `Tick` (materialise, publish, inject), which
             makes them deterministic, and states that this order is not
             observable across modules.
             Revision after the PR #57 review:
             - the test is rewritten to be observable (recorder tick
               against the fake flow's `Inject` tick). After round 2 it
               pins exact values: `pax=10`, buckets 60/400 and 1440/600,
               which split exactly as 4 + 6. It asserts all four
               occurrences the run publishes (days 0 to 3), with their
               ticks listed;
             - §11.9a names the reported bucket and the order of failures.
             Rejected alternatives:
             - clamping to `PublishTick` would silently reshape an owner's
               curve;
             - extending the horizon would change `PLAN_PUBLISH_LEAD_TICKS`
               and every publish tick.
             Two new tests (§11.10).
             The 1440-minute (24 h) bound is owner-confirmed 2026-09-28.
Status:      ANSWERED (spec/11-interfaces-schedule.md#expansion-to-injections)

### Q-039 — `sim.schedule`: the `FlightId` stride invariant
Raised by:   Reviewer, PR #54 (T-008) finding 2, via coordinator, 2026-09-28
Blocking:    T-008
Question:    `FlightId.Value = DayIndex × 100000 + RowOrdinal + 1`, and
             `RowOrdinal` counts across the whole file. The loader limited
             only rows per `day`, so 60000 + 60000 rows load, and two
             flights on adjacent days collide. What does the spec
             guarantee? Should the total be bounded, or the id redefined?
Answer:      Bound the total. `MAX_FIXTURE_ROWS_PER_DAY` is replaced by
             `MAX_FIXTURE_ROWS = FLIGHT_ID_DAY_STRIDE − 1` (99999) on the
             file's total data rows. It is counted in file order, and the
             failure names line `MAX_FIXTURE_ROWS + 2` (the first row over
             the limit), with no dictionary walk. §11.3 now states what the
             derivation guarantees for every loaded table, and nothing
             stronger:
             - ids are unique across all days;
             - `Value / STRIDE = DayIndex`;
             - `Value % STRIDE − 1 = RowOrdinal`;
             - ascending `FlightId` equals ascending `(DayIndex,
               RowOrdinal)`.
             The id formula is unchanged, so no id or golden changes for
             any fixture that loads today. Max tier is 800 daily movements
             (`01`), far below the bound. Two new tests (§11.10). This also
             settles PR #54 finding 3: the row-limit failure has a line.
Status:      ANSWERED (spec/11-interfaces-schedule.md#flight-id-derivation)

### Q-040 — `sim.flow`: how is a non-`Departing` cohort routed at Phase 0/1?
Raised by:   Architect, from the PR #55 review (finding 1), 2026-09-28
Blocking:    no production work. No Phase 0/1 production caller injects a
             non-`Departing` cohort: `11` §11.1 and `12` "Arriving
             passengers" have a pax count of zero. It was needed to make the
             Q-036 rules and test well-defined, and is answered in PR #55.
Question:    `Inject` (§9.7) accepts any `FlowDirection`, and tests may seed
             fixtures through it. But §9.6 "Destinations" defines a set only
             for `Departing` cohorts, so the uncached routing rule has no
             pairs for an `Arriving` or `Transferring` cohort on a `Source`
             or `Hall`. Does such a cohort stay put, go to a `Sink`, or is
             it rejected at `Inject`?
Options:     - (a) `Inject` with `Direction ≠ Departing` throws
               `ArgumentException` at Phase 0/1. This is the narrowest
               option, but it changes merged T-007 behaviour and may break
               merged tests that seed other directions, so that must be
               checked first.
             - (b) An empty pair set means the cohort stays on its node.
               That is additive and needs no merged change.
             - (c) Destinations for `Arriving` cohorts (a `Sink`). That is
               new behaviour and scope.
Answer:      **(a).** This is what the spec already implies. §9.6 defines
             destinations only for `Departing` cohorts, and Phase 0/1 has
             no arriving or transferring passengers (`11` §11.1, `12`). So a
             non-`Departing` cohort is a state the spec never gave a
             meaning. Rejecting it at `Inject` is the narrowest rule, and it
             removes the direction question from the Q-036 cache key and
             test. The checks:
             - on `main` and every open test-author or worker branch, the
               only non-`Departing` use is the query
               `PopulationForFlight(…, FlowDirection.Arriving)` in
               `FlowSystemTests.cs:52`, which stays valid, because queries
               still accept every direction;
             - (b) was rejected. It would keep an undefined state alive and
               make the route cache depend on direction;
             - (c) is scope, and waits for arriving passengers, which is
               the owner's call.
             `09` §9.6 "Only `Departing` cohorts exist at Phase 0/1" and
             §9.7 "Exceptions" now state it.
             **The merged T-007 code does not conform.** `Inject` accepts
             every direction, and `AttemptRelease` routes every cohort to
             the pooled gates whatever its direction. A `sim.flow` fix task
             is needed: `Inject` throws `ArgumentException` for
             `key.Direction ≠ Departing`, as the fourth check after the
             merged three (§9.7), with the Test Author's test
             `test_inject_rejects_non_departing_direction`.
Status:      ANSWERED (spec/09-interfaces-flow.md#96-corridors-and-routing)

<!-- Q-046 to Q-056 (T-021, PR #67) are listed before Q-041 to Q-045, which merged first from PRs #66 and #68. -->

### Q-046 — `sim.airside`: the layout fixture's file format
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    §12.13 made the format of
             `tests/fixtures/airside/phase1-single-runway.*` "the worker's
             choice". But only the Test Author writes `tests/fixtures`,
             and `Parse` has no specified input.
Answer:      Architecture. The format is pinned in §12.4 "File format":
             `phase1-single-runway.json`, in `08` §8.11's strict JSON
             subset under `18` §18.2's rules, with four arrays (`runways`,
             `nodes`, `edges`, `stands`), exact keys, and `true` or
             `false` only for `bidirectional`. The failures are
             `FormatException` with the `sourceName: ` prefix, and
             `line <n>` for syntax and shape. `Parse` calls `Load`.
             `Load` now also checks node kinds, ranges and non-empty lists,
             and returns lists sorted by id. `MaxAircraftSizeCategory` is
             resolved in `CreateSystem`. Two new tests (§12.13).
             Revision after the PR #67 review:
             - `Load`'s checks run in a fixed order (ranges, non-empty,
               unique ids, references, kinds, connected). Each names a
               pinned field and id: the referring id and the missing node
               for references, the lowest failing node for connectivity,
               and the list name for an empty list;
             - range failures (`"id": 0`) are `Load`'s, with no `line <n>`.
               Only parse failures (syntax, shape, C#-type range) carry
               it.
             One more test.
             Revision 2: check 1 covers each object's own `id` and its
             value fields only. A `0` in a node-reference field fails at
             check 4. Within one object, the first failing field in
             file-format key order is named.
Status:      ANSWERED (spec/12-interfaces-airside.md#file-format-q-046)

### Q-047 — `sim.airside`: the `DoorsOpen` delay has no value or field
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021 (the field). The value blocks only a build that parses
             `data/balance/airside_rules.json` with `sim.airside` (T-031).
Question:    §12.3 says `DoorsOpen` is a "fixed door delay" after
             `OnStand`, but no constant or content field holds it.
Answer:      The field is architecture: `AirsideRules.DoorsOpenDelayMinutes`
             (`uint32`, where 0 means the same tick). The JSON key is
             `doors_open_delay_minutes` in `airside_rules.json`, and it is
             required (`04`). `DoorsOpen` = `OnStand` +
             `DoorsOpenDelayMinutes × TICKS_PER_SIM_MINUTE`, both planned and
             actual. Under the §12.8 fallback it adds to every ground stay,
             so it moves on-time performance, and the value is balance.
             **HUMAN DECISION, owner, 2026-09-30: 2 sim-minutes.** The owner
             writes it to `data/balance/airside_rules.json`. Tests use their
             own fixture value. It is not a compiled constant, per §12.2's
             rule that thresholds are content.
Status:      ANSWERED (spec/12-interfaces-airside.md#124-layout-the-airside-graph)

### Q-048 — `sim.airside`: `InboundAirborne` before tick 0
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    `InboundAirborne` is at `STA − 1200`. For a day-0 `STA < 1200`,
             such as the 00:20 arrival in `phase0-200.csv`, that tick would
             underflow.
Answer:      Architecture. Clamp: `max(0, STA − CRUISE_LEAD_TICKS)` for both
             `PlannedTick` and `ActualTick`, which is the same clamp as `11`
             §11.6's show-up rule. Day 0 is materialised at construction,
             so the flight is visible at tick 0 (§12.3, §12.6). One new
             test.
Status:      ANSWERED (spec/12-interfaces-airside.md#126-the-taxiway-model)

### Q-049 — `sim.airside`: which runway does a movement use?
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021 (the max-tier budget layout has 3 runways)
Question:    The spec routes per runway but never picks one.
Answer:      Architecture stopgap. **HUMAN DECISION, owner, 2026-09-30:
             accepted.** The Architect had marked it LOW CONFIDENCE. The
             runway is chosen
             once at the request point (an arrival at its `Landed` request
             at `STA`, a departure at `Pushback`). It is the runway with
             the fewest aircraft in its hold queue, ties to the lowest
             `RunwayId` (§12.5 "Runway choice"). With one runway, nothing
             changes. A real runway-allocation system (modes, segregation,
             player control) is gameplay and is deferred to the owner, like
             gate assignment.
             Revision after the PR #67 review: the order of same-tick
             runway work is pinned (§12.5, step S7 of §12.8a). The hold
             queues go first, per runway in ascending `RunwayId`, then new
             requests in ascending `FlightId`. Each arrival sees the queue
             lengths left by the requests before it. One more test.
Status:      ANSWERED (spec/12-interfaces-airside.md#125-the-runway-model)

### Q-050 — `sim.airside`: is a stand reserved during taxi-in?
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    A stand is assigned at `OffRunway` but "occupies from
             `OnStand`". Can another aircraft take it in between?
Answer:      Architecture. No: assignment sets `StandState.Occupant` at
             once, and the stand is held from assignment to `Pushback`
             inclusive. Waiters form one airport-wide stand-wait queue,
             with no head-of-line blocking. The queue is walked each tick
             before new assignments, and it is hashed after stand state
             (§12.7, §12.12).
             Revision after the PR #67 review:
             - the queue is in joining order, not `EventId` order, since a
               departure entry has no event;
             - same-tick new requests (arrivals at `OffRunway` and
               rotation-less departures at their start tick, §12.11) are taken in
               ascending `FlightId` (S5 of §12.8a);
             - a waiting arrival is `HeldOnTaxiway` at its threshold node;
             - the queue's cost is in §12.12.
             Revision 2: the queue has a hard bound, `STAND_WAIT_CAPACITY =
             1024`. It is preallocated and never grows, and overflow is
             `SimInvariantException` (§12.2, §12.12).
Status:      ANSWERED (spec/12-interfaces-airside.md#127-stands)

### Q-051 — `sim.airside`: "earliest-declared, ties by ascending `StandId`"
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    Declaration order and id order can disagree, and ids are
             unique, so there is never a tie to break.
Answer:      Architecture. It becomes the compatible free stand with the
             **lowest `StandId`**, and "earliest-declared" is dropped. `Load`
             sorts stands by id, so declaration order is invisible (§12.4,
             §12.7). The test name
             `test_stand_assignment_prefers_lowest_id_among_compatible_free_stands`
             already says this.
Status:      ANSWERED (spec/12-interfaces-airside.md#127-stands)

### Q-052 — `sim.airside`: when is a landing requested?
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    §12.6's "early aircraft holds off-graph with `queuePosition =
             0`" implies a request before `STA`. When is `Landed` requested?
Answer:      Architecture. At `STA`, exactly, in `sim.airside`'s `Tick`. A
             departure requests `TakeoffRoll` on reaching its threshold
             node. `InboundAirborne` is fixed at Phase 0/1, so no aircraft
             is early and no `queuePosition = 0` hold is ever emitted. The
             clause is removed until upstream delay exists (§12.5, §12.6).
             Same-tick requests are ordered as in Q-049's revision.
Status:      ANSWERED (spec/12-interfaces-airside.md#125-the-runway-model)

### Q-053 — `sim.airside`: a rotation-less departure with no free stand
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    It is created on a stand at `STD − MinTurnaround`. What if
             none is free?
Answer:      Architecture, **revised after the PR #67 review**. The first
             design, a track in `AwaitingApproach` with no stand,
             contradicted §12.3, §12.9 and `14` §14.6, and left the
             fallback's `Absorb` without a sink. The new design:
             - **no track until a stand is assigned.** The flight joins the
               stand-wait queue as a departure entry, and emits nothing;
             - in the S5 where it gets a stand, its track is created in
               `OnStand` at that stand, and `OnStand` fires with the
               planned tick unchanged (the due tick, `max(0, STD −
               MinTurnaround)`) and the actual tick late;
             - its "creation tick" is that actual `OnStand` tick. It
               anchors the fallback's doors-close point and the boarding
               hold, and `Absorb` uses that stand's sink;
             - its lateness goes to `Unexplained` under `14` §14.6 step 3,
               unchanged. Attributing it to stand shortage would need `14`
               amended, and it is not proposed here.
             §12.3's and §12.9's "a departure track starts in `OnStand`"
             stays true. One test.
             Revision 2: rotation-less departures are found without a scan,
             through §12.11's pending list. That list is fed by a day-0
             read at `CreateSystem` and a `FlightPlanPublished`
             subscription, capped at `PENDING_FLIGHTS_CAPACITY`, and
             hashed. The fallback chain after a late `OnStand` runs at
             once, in S5 (§12.8a "Chains").
             Revision 3, after the fourth PR #67 review:
             - an entry leaves the pending list when it is taken at its
               start tick: an arrival in S2, a rotation-less departure in
               S5, whether it gets a stand or moves to the stand-wait
               queue. So no flight is in both. The bound argument now
               follows from that rule: a one-day window spanning two
               calendar days holds at most 1 600 flights at max tier;
             - a departure due at or before its publication starts at
               `PublishTick + 1`. Its `OnStand` keeps `PlannedTick` = the
               §12.3 formula and has a later `ActualTick`. §12.3, §12.7 and
               §12.11 now agree;
             - overflow in the day-0 read at `CreateSystem` throws
               `ArgumentException`, since there is no tick for a
               `SimInvariantException`.
Status:      ANSWERED (spec/12-interfaces-airside.md#rotation-less-flights-no-rotation-counterpart)

### Q-054 — `sim.airside`: same-tick release of a taxi edge and a stand
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    (a) Does a taxi hold release on the tick the blocker leaves
             the edge? (b) Is `StandAssigned` for a waiter on the
             `Pushback` tick, or the next tick?
Answer:      Architecture. `sim.airside`'s `Tick` now has a pinned step
             order, S1 to S7 (§12.8a, revised after the PR #67 review).
             Both edges and stands read one start-of-tick snapshot, S1, as
             `sim.flow`'s does (`09` §9.12):
             - (a) an edge left during `t` is enterable, and its hold
               released, at `t + 1`. A free edge goes to its queue head,
               else to the lowest requesting `FlightId`, and the others
               queue in ascending `FlightId`;
             - (b) a stand vacated by `Pushback` at `t` is assigned at
               `t + 1`. A stand vacated by `ReassignStand` at the boundary
               of `t` is free at `t`.
             An aircraft placed on the graph earlier in the tick (by
             `OffRunway` in S3 or `Pushback` in S4) asks for an edge in S6
             of the same tick. An aircraft holding at a node occupies no
             edge. No cross-tick state results. Two new tests.
             Revision 2:
             - runways are the exception to S1. S7 reads them live, so a
               runway cleared in S3 is claimable in S7;
             - **chains.** An action that makes another action due at the
               current tick runs it at once, in the same turn. That pins
               zero `DoorsOpenDelayMinutes` and zero `MinTurnaround`, and
               it keeps a departure created in an arrival's turn inside
               that turn. One more test,
               `test_zero_door_delay_and_turnaround_chain_in_one_tick`.
Status:      ANSWERED (spec/12-interfaces-airside.md#128a-order-within-tick-q-054)

### Q-055 — `sim.airside`: `AirsideRules` is not "two integers"
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    no
Question:    §12.12a said "two integers", but the struct had one field.
Answer:      With Q-047, `AirsideRules` has two fields,
             `BoardingHoldMaxMinutes` and `DoorsOpenDelayMinutes`, so the
             sentence is now true. §12.12a names both.
Status:      ANSWERED (spec/12-interfaces-airside.md#1212a-construction-q-009)

### Q-056 — `sim.airside`: the `ReassignStand` no-op `LogKey`
Raised by:   Test Author / T-021, via coordinator, 2026-09-29
Blocking:    T-021
Question:    `08` §8.7 says a no-op logs "with its own module's `LogKey`,
             appended by amendment", but no airside key exists.
Answer:      Architecture. `LogKey.AirsideReassignStandNoOp = 1` (`08`
             §8.10). The line is `Info`, `SystemId(3)`, at the applying
             tick, with `LogArgs(flight, newStand, reason)`. The reason is
             1 (not tracked, not `OnStand`, or not its stand's current
             occupant, which covers an arrival after handoff), 2
             (occupied, including its own stand) or 3 (incompatible),
             checked in that order (§12.10). `LogKey` is in `src/sim/core/LogKey.cs`, so T-021's
             worker needs that one file added to its writable paths, as
             `08` says: "appended with the module that writes it". The
             Planner must serialise it with any open `src/sim/core/**`
             task.
Status:      ANSWERED (spec/12-interfaces-airside.md#1210-commands-consumed)

### Q-041 — T-009: where do the kill-gate tests live?
Raised by:   Test Author / T-009, via coordinator, 2026-09-29
Blocking:    T-009
Question:    The task file says `tests/sim/core/**`. Under `07` L3, that
             project may reference only `src/sim/core`, so it cannot see
             `WorldFactory`, `ScheduleFactory`, `FlowFactory` or
             `HarnessGates`. The Test Author put the tests in
             `tests/tools/simharness` instead (branch
             `test-author/T-009-100-day-gate-tests`, `f531ca7`). Is that
             right?
Answer:      **Confirmed.** Every T-009 test, the two kill-gate tests
             included, is in `tests/tools/simharness/`. The harness test
             project sees the three modules through
             `tools/SimHarness`'s own `ProjectReference`s, which T-009's
             worker adds (`07` L3, L8). `19` §19.6 now states it. No `07`
             change is needed, since L3 already says it.
             **The Planner corrects `tasks/T-009-100-day-run.md`:**
             - "Tests to pass" becomes `tests/tools/simharness/**`;
             - "Writable paths" gains `tests/tools/simharness/**` and
               `tests/fixtures/harness/**`. The path guard checks a
               `test-author/T-009-*` branch against T-009's writable
               paths. This is the T-011 precedent, and it does not reopen
               Q-021, because `protected_for_role` still blocks a worker
               from `tests/`;
             - the worker still writes `tools/SimHarness/**` only. That
               includes the three new `ProjectReference`s in the harness
               `.csproj` (`07` L8). `AirportSim.sln` does not change;
             - "Readable specs" gains `07`, `12` §12.3 and §12.7, `18` and
               `19`. The description gains the boarding stand-in at
               registry position 3 (Q-043).
Status:      ANSWERED (spec/19-interfaces-harness.md#196-tests-of-the-phase-0-composition-and-the-kill-gate-q-041-to-q-043)

### Q-042 — T-009: the CLI composition once modules are composed
Raised by:   Test Author / T-009, via coordinator, 2026-09-29
Blocking:    T-009
Question:    `19` §19.2 said the first task that composes modules into the
             harness amends the CLI composition line, but it gave no
             amendment. Which content and fixtures do `determinism --days
             N` and the other subcommands use, and how does the harness
             find them? What happens to T-006's `HarnessCliTests`, which
             assert `EmptyCompositionFinalHash`?
Answer:      `19` §19.2a, the smallest composition that closes it:
             - **One composition for every subcommand.** `determinism`,
               `saveload`, `promotion` and `budget` all use it. No flag,
               option or environment variable selects another. The empty
               composition is not kept behind an option. It stays reachable
               only through `HarnessGates` with a composer that registers
               nothing, which is how `HarnessGatesTests` already use it.
             - **Four fixtures, all the Test Author's.** They are
               `tests/fixtures/world/phase0-landside.json`,
               `tests/fixtures/flow/phase0-landside.flow.json` and
               `tests/fixtures/schedule/phase0-200.csv`, plus a **new**
               content fixture: the manifest
               `tests/fixtures/harness/phase0-content.files` over
               `tests/fixtures/harness/phase0-content/`. That content is
               loaded with `08` §8.11's `IContentLoader`, and it holds
               only the definitions the other three reference. Its values
               are fixture sizing. `data/` is never read, because `data/`
               values are the owner's balance values. Binding CI hashes to
               them would turn every balance edit into a harness-test
               failure, and a slower security value could stop the queues
               draining over 100 days.
             - **Locating them.** `07`'s Q-031 rule, applied to the
               harness: the nearest ancestor of `AppContext.BaseDirectory`
               that holds `AirportSim.sln`. Never the working directory, a
               flag or an environment variable. Usage errors (exit 2) are
               decided before any file is read. Every load failure is exit
               3.
             - **Composition.** Construct world, flow, schedule, then the
               stand-in (Q-043). Register world (1), schedule (2),
               stand-in (3), flow (4), and nothing else.
             - **`HarnessCliTests`.** Its expected hashes are **replaced**:
               each `EmptyCompositionFinalHash(n)` for a CLI run becomes
               `HarnessGates.FinalHash` of the Test Author's kit
               composition (§19.6) at the same seed and ticks.
               `test_harness_cli_hash_only_matches_final_hash_gate` becomes
               the equivalence test between the kit and the CLI.
               `test_harness_cli_budget_core_only_day_passes` is renamed
               `test_harness_cli_budget_phase0_day_passes`. Nothing else
               in `HarnessCliTests.cs` changes. `HarnessGatesTests.cs` and
               `EmptyCompositionFinalHash` do not change at all.
             - **Withdrawn.** §19.2 said the composing task would make exit
               codes 1 and 3 reachable through the CLI. It does not. Code 1
               needs a nondeterministic composition, and code 3 needs a
               broken repository fixture. Both stay untested through the
               CLI, and no seam is added.
             - **`budget --tier max`** times day 0 of this composition,
               which is far below max tier. The §19.4 LOW CONFIDENCE note
               is updated.
             The Test Author's `KillGateKit` builds its content in C#
             today. It moves those values into the content fixture and
             loads them through `ContentLoaderFactory`, so the CLI and the
             kit read the same bytes.
Status:      ANSWERED (spec/19-interfaces-harness.md#192a-the-phase-0-cli-composition-q-042-q-043)

### Q-043 — T-009: at Phase 0 nothing boards, so is the kill gate measuring anything real?
Raised by:   Test Author / T-009, via coordinator, 2026-09-29
Blocking:    T-009
Question:    One 100-day run ran for more than 10 minutes (572 CPU-s) and
             did not finish. The Test Author's guess: nothing calls
             `Absorb` at Phase 0, because `sim.airside` and its boarding
             hold are absent. So departing cohorts pile up on the `Gate`
             for 100 days, while `Tick` is O(cohorts) (`09` §9.10). Is
             that right? If it is, what does the kill gate measure at
             Phase 0?
Finding:     **Confirmed, from the spec and from `main`.**
             - The spec: `Absorb` is the only removal, both boarding and
               missed-flight (`09` §9.7). Its only production caller is
               `sim.airside` at `DoorsClosed` (`12` §12.7, §12.8).
               `sim.schedule` only injects (`11` §11.6), and T-009
               composes no `sim.airside`. A `Gate` keeps what it holds
               (§9.6, §9.12 "Gate and Sink: nothing leaves").
             - The code: on `main` the only `Absorb` calls are in
               `src/sim/flow` itself and in `tests/sim/flow`, where
               T-011's `StressDay` calls it at STD from its own driver.
               `src/sim/schedule` calls only `Inject`
               (`ScheduleSystem.cs:281`).
             - The growth: cohorts on the `Gate` merge only per
               `CohortKey`, which is flight, profile, bag and assistance.
               `phase0-200.csv` has 100 departures a day, and every one
               has bag and assistance shares strictly between 0 and 1000,
               so up to four classes each. The `Gate` gains up to about
               400 cohorts a sim-day. That is about 40 000 by day
               100, and the count grows without bound. `09` §9.10's
               bounded-cohort premise fails.
             - The cost: `FlowSystem.MergeNode` compares a node's cohorts
               pairwise, which is about k²/2 comparisons a tick on the
               `Gate`, around 8 × 10⁸ by day 100. The snapshot also sums
               every cohort every tick. That explains the run that did not
               finish. CI's `determinism --days 10` would stall the same
               way, at about 4 000 cohorts.
Options:     - (a) **A harness boarding stand-in.** A harness-internal
               system in `sim.airside`'s empty slot, position 3, calls
               `Absorb(sink, flight)` for each departure at its planned
               doors-close tick, STD (`12` §12.3). It has no hold. It
               hashes 0, and it is removed when `sim.airside` joins. It
               follows T-011's `StressDay` driver, and it makes the gate
               measure the cost that a build with `sim.airside` pays.
             - (b) Keep nothing boarding, and make `sim.flow`'s merge
               linear. The cohorts stay unbounded (about 40 000), a linear
               pass over them for 1 440 000 ticks is still about 3 × 10¹⁰
               cohort visits, and the gate would measure a state that no
               build ever reaches. Rejected.
             - (c) `sim.flow` clears the `Gate` itself on a timer. That
               changes merged `sim.flow` behaviour and duplicates `12`
               §12.7's responsibility, and once `sim.airside` exists it
               would remove passengers twice. Rejected.
             - (d) Shorten the gate, relax the 60 s, or wait for
               `sim.airside` (T-021). Each changes the gate's scope, the
               budget or the build order, which are the owner's. None is
               needed.
Answer:      **(a)**, specified in `19` §19.2a and tested per §19.6. The
             kill gate at Phase 0 measures `sim.world`, `sim.schedule` and
             `sim.flow` over the Phase 0 fixtures for 100 sim-days, with
             departures boarded at STD. Its modules, fixtures, day count
             and 60 s budget are unchanged. `08` §8.5 names the stand-in
             as the only non-test probe, and `09` §9.10 now says that
             `Absorb` bounds the keys. No `sim.flow` or `sim.schedule`
             code changes, so no merged work is invalidated. The
             `KillGateKit` load check "every injected passenger is still
             on a node" becomes the conservation check of §19.6.
             **HUMAN DECISION — owner, 2026-09-29: accepted.** The
             boarding stand-in, decision (a), is a valid reading of the
             kill gate. The gate's scope is unchanged. (The Architect had
             marked this LOW CONFIDENCE, and the owner confirmed it.)
             **Not decided, and flagged for the owner:** 60 s over
             1 440 000 ticks is about 41.7 µs a tick for the whole
             composition, about 1/144 of `01`'s 6 ms whole-sim tick. It
             is unmeasured with the stand-in. If T-009's worker cannot
             meet it, that is the task's "escalate to the human owner"
             path, not an agent decision to relax it.
Status:      ANSWERED (spec/19-interfaces-harness.md#192a-the-phase-0-cli-composition-q-042-q-043)

<!-- Q-046 to Q-056 appear above, before Q-041. -->

### Q-044 — Budget tests: may a module test sample fewer ticks than one sim-day?
Raised by:   Planner, filing T-041 (the `WorldBudgetTests` fix), via coordinator, 2026-09-29
Blocking:    T-041
Question:    `03` "How a budget is measured" takes the statistic over "one
             full sim-day" (14 400 ticks). `WorldBudgetTests` samples 2 000
             ticks. May a module test use a shorter window, and if so, what
             is the minimum? And does the statistic bind `sim.core`, which
             has no `Tick`?
Answer:      Architecture (measurement protocol, not balance). In `03`'s
             new "Budget tests: window and arithmetic":
             - **No shorter window.** Exactly `TICKS_PER_SIM_DAY`
               consecutive per-tick samples. Warm-up is allowed and not
               sampled, and several days are judged day by day. This
               generalises `11` §11.9 (Q-031). p99 exists to catch the
               bank peak, and with 2 000 samples it is only the 20th-worst
               tick of a window that may miss the bank.
             - **Alignment (revision after the PR #68 review).** Any 14 400
               consecutive ticks after warm-up form a window. It need not
               start on a sim-day boundary, because 14 400 consecutive ticks
               always span every hour of the day once.
             - **Scope.** It binds every xUnit test that asserts a time
               against a per-tick budget of `03`'s table, and `19` §19.4.
               It does not bind allocation-only `Budget` tests, or a
               whole-run wall-clock gate such as T-009's kill gate. Any
               other timed check carries no `Budget` trait. There is no
               "extra check" category.
             - **`sim.core`: decided, it binds.** `03` gives `sim.core` 0.25
               ms for "loop, commands, event dispatch". Its sample is one
               `ISimHost.Step(1)` of a host whose systems are the test's
               probes, with checkpoint ticks included (`03` "Measured").
Status:      ANSWERED (spec/03-module-map.md#budget-tests-window-and-arithmetic-q-044-q-045)

### Q-045 — Budget tests: how is p99 computed from N samples?
Raised by:   Planner, filing T-041, via coordinator, 2026-09-29
Blocking:    T-041
Question:    `03` does not define p99 for N samples. T-011's
             `FlowStressBudgetTests` uses nearest rank at index ⌈0.99·N⌉−1.
             Pin one definition for every budget test.
Answer:      Architecture. `03` "Arithmetic", in `long` only as `07` L11
             requires, with no `Int128`:
             - each raw `Stopwatch` sample is rounded **up** to whole µs,
               as `(d × 10^6 + f − 1) / f`, and capped at `B·n + 1`, with
               an overflow guard. The cap changes no verdict for any
               `f ≤ long.MaxValue / (B·n + 2)`, about 1.07 × 10^11 Hz at
               `B = 6000`, far above real `Stopwatch` frequencies (10^7,
               10^9). It keeps `Σu` within `long`;
             - the mean passes iff `Σu ≤ B × n`;
             - p99 is nearest rank, `u[(99n + 99)/100 − 1]` of the sorted
               samples, and passes iff `p99 ≤ 2B`;
             - a reported mean is rounded up, so the printed value and the
               verdict always agree.
             `19` §19.4 now uses exactly this condition, with `B = 6000`.
             Its old flooring of the samples and the mean accepted a mean
             just over 6000 µs, so it is replaced. There is one pass
             condition, shared. §19.4's index rule is unchanged. The
             rounding and the comparison are new.
Conformance: checked against every `[Trait("Category", "Budget")]` method
             on `main` (`273f2ce`), 26 in all:
             - **Out of scope, 15.** These assert allocation only and time
               nothing:
               - `sim.core` `BudgetTests`:
                 `test_budget_step_with_no_systems_allocates_nothing` and
                 `test_budget_step_with_events_and_ids_allocates_nothing_in_steady_state`;
               - `BusAllocationTests` (3);
               - `CommandQueueTests.test_command_queue_apply_due_allocates_zero_bytes`;
               - `FxTests` (2);
               - `RandomServiceTests` (1);
               - `RandomStreamTests` (1);
               - `StateHasherTests` (1);
               - `PromotionAllocationTests` (4).
             - **Conforming, 2.** The `HarnessCliTests` budget tests,
               `test_harness_cli_budget_max_prints_budget_line_consistent_with_exit`
               and `test_harness_cli_budget_core_only_day_passes`. They
               parse §19.4's line, and they hold under rounded-up
               reporting. The harness code that prints the line must
               change (Impact).
             - **Non-conforming, 9.** Each needs a Test Author rewrite to
               `03`'s rule:
               - `sim.core` `BudgetTests.test_budget_day_with_no_systems_within_quarter_ms_per_tick`
                 and `test_budget_day_with_busy_probes_within_quarter_ms_per_tick`:
                 a whole-day total, floored, with no per-tick samples and
                 no p99;
               - `CommandQueueTests.test_command_queue_day_with_command_every_tick_within_core_budget`:
                 a whole-day total, with no p99;
               - `FlowBudgetTests.test_flow_budget_max_tier_within_two_and_a_half_ms_and_bounded_cohorts`:
                 a one-hour total, floored, with no p99. It becomes a
                 full-day per-tick test;
               - `FlowStressBudgetTests.test_flow_stress_30k_day_tick_within_budget_and_bounded_cohorts`:
                 the right window, but `Int128` arithmetic;
               - `PromotionBudgetTests.test_promotion_budget_one_day_with_every_node_promoted`:
                 the right window and index, but raw-tick rescaled
                 comparisons instead of rounded-up µs;
               - `sim.schedule` `BudgetTests.test_budget_one_day_mean_and_p99_within_budget_with_flow`
                 and `test_budget_one_day_mean_and_p99_within_budget_without_flow`:
                 the same as promotion;
               - `WorldBudgetTests.test_world_budget_tick_and_queries_at_max_tier_within_point_one_ms`:
                 2 000 ticks, a loop total, and no p99. This is T-041.
             - **Open test branches (revised after the PR #68 review).**
               - T-021's `AirsideBudgetTests`
                 (`test-author/T-021-airside-tests`, `d10a9be`) is
                 **non-conforming**. It uses rescaled raw-tick comparisons
                 with no per-sample µs round-up, the same pattern as
                 `PromotionBudgetTests`. The T-021 Test Author rewrites it
                 to `03`'s rule before T-021 merges.
               - T-009's kill-gate (a) is a whole-run gate, out of scope
                 (`19` §19.6).
Status:      ANSWERED (spec/03-module-map.md#budget-tests-window-and-arithmetic-q-044-q-045)

<!-- Q-060 to Q-062 (T-021, PR #73) are listed before Q-057 to Q-059, which merged first from PR #70. -->

### Q-060 — `sim.airside`: which flight does a taxi hold's `Blocking` name?
Raised by:   Test Author / T-021, via coordinator, 2026-10-01
Blocking:    T-021
Question:    An edge is free in the S1 snapshot, but in S6.2 it is granted
             to another asker, for example a lower `FlightId`. Does the
             held flight's `AircraftHeldOnTaxiway.Blocking` name the
             grantee, or is it null? `Blocking` is an event payload, so it
             is logged and must be pinned exactly.
Why it matters: The two readings give different payloads, and `sim.delay`
             copies the value into `DelayExplanation.B`.
Answer:      Architecture. A hold is only emitted in S6.2, and `Blocking`
             names the flight that denied the grant. It is never null at
             Phase 1:
             - edge occupied in the snapshot: the snapshot occupant, even
               if it left the edge in S6.1 of the same tick;
             - edge free in the snapshot: this step's grantee, the queue
               head or else the lowest-`FlightId` asker, whatever its id
               relative to the held flight.
             `Blocking` is fixed at emission. On
             `AircraftHeldOnTaxiwayReleased`, `Blocking` is always null,
             as `HeldAt` is on `DepartureHeldForPassengersReleased`,
             because `sim.delay` reads the opening event and a copy would
             need new per-hold state. `10` §10.6 and
             `14` §14.3 now say so. Two new tests in §12.13.
Status:      ANSWERED (spec/12-interfaces-airside.md#126-the-taxiway-model)

### Q-061 — Does "no allocation in the update path" include event handlers?
Raised by:   Test Author / T-021, via coordinator, 2026-10-01
Blocking:    T-021
Question:    `03` times a module's `Tick` only, so the phase-3
             `FlightPlanPublished` handler that appends to the
             preallocated pending list is outside the allocation test.
             Does "no allocation in the update path" (`08` §8.6, `03`)
             cover handlers, and how is it measured?
Why it matters: Without a rule, a handler can allocate on every
             publication and every test still passes. `sim.delay` does
             nearly all of its work in handlers.
Answer:      Yes, for every module. `03` "How a budget is measured" now
             defines a module's update path as all of its code that runs
             in phases 1 to 3: command `Apply`, `Tick`, event handlers,
             and its queries called by other systems then. Outside it are
             construction, `Build`, `Validate` at admission, phase 4, and
             ticks the module's spec allows to allocate (`11` §11.9).
             Q-061 itself left timing unchanged. Q-064 later extends
             "Measured" to the same handler work.
             An allocation test, with or without the `Budget` trait,
             asserts exactly 0 with the T-037 meter. It meters either
             `ISimHost.Step` windows with no
             checkpoint and no allocating tick, or a direct rig that also
             delivers the module's commands and events in the same
             window. Every module with a handler has at least one such
             test in which each handler runs inside the window, after a
             warm-up that already ran it. `07` "Performance", `08` §8.6,
             `09` §9.10, `12` §12.12 and `14` §14.13 point to it, and
             `13` §13.11 names T-022's test.
             Revision, after the PR #73 review: the trait is not required.
             `07` L11 binds timed assertions, and an allocation test times
             nothing. So `sim.flow`'s existing untagged allocation tests
             count.
Status:      ANSWERED (spec/03-module-map.md#how-a-budget-is-measured)

### Q-062 — `sim.airside`: the handed-off arrival's track
Raised by:   Test Author / T-021, via coordinator, 2026-10-01
Blocking:    T-021
Question:    §12.10's reason 1 covers an arrival whose stand was handed
             off to its departure. But nothing pins that arrival track's
             phase or `Stand` once the departure pushes back and the
             stand is reused.
Why it matters: The track is hashed (§12.12 item 4), drawn by `15` §15.4,
             and scanned every tick. If it stayed, it would grow without
             bound and draw a second aircraft at the stand.
Answer:      Architecture. At the handoff, on either path, the arrival
             leaves tracked state, as a departure does at `Airborne`. `10`
             §10.3 rule 2 already treats the handoff as the arrival's exit.
             The order is: create the departure's track, reassign the
             occupant, remove the arrival's track, fire the departure's
             `OnStand`. After that, `TryGetTrack(arrival)` is false and the
             arrival is not hashed, so it has no phase or stand to pin.
             `ReassignStand` naming it fails check 1, "not tracked". The
             "not its stand's occupant" clause stays, but nothing reaches
             it now. To keep every arrival milestone before the removal,
             the handshake's handoff never runs before the arrival's
             `DoorsOpen`. If `DeboardComplete` comes first, the handoff
             chains right after `DoorsOpen` (§12.8a "Chains"). A
             rotation-less arrival is never handed off, and stays tracked.
             Revision, after the PR #73 review:
             - the wait for `DoorsOpen` keeps a consumed `DeboardComplete`
               across ticks and checkpoints, together with the `EventRef`
               that becomes the departure `OnStand`'s `Cause`. That is now
               explicit state: a new `AircraftTrack.RecordedCause`, set by
               the phase-3 handler, cleared by the action it waits for,
               and hashed and saved with the track. The same field holds a
               `BoardingComplete` for its one-tick wait, which closes the
               older, unhashed gap. Q-060 still adds no per-hold state,
               because no behaviour reads a copied blocker;
             - §12.8a S4, which governs, now runs `DoorsOpen` first, and
               the handoff only once `DoorsOpen` has fired. §12.3 and
               the §12.11 table say the same.
Status:      ANSWERED (spec/12-interfaces-airside.md#the-handed-off-arrival-q-062)

<!-- Q-060 to Q-062 appear above, before Q-057. -->

### Q-057 — `tools.simharness`: no `soak` subcommand
Raised by:   Planner, syncing task files (PR #69), via coordinator, 2026-10-01
Blocking:    T-013
Question:    §19.2a says every §19.3 subcommand uses the Phase 0
             composition, but §19.3 has no `soak`. Its grammar, exit codes,
             composition and golden handling are unspecified. The nightly
             workflow already runs `soak --days 500 --golden
             tests/golden/soak-500.hashes` and fails with "unknown
             subcommand 'soak'".
Why it matters: T-013 cannot be written, and the nightly `soak_500_days`
             gate (`02`) cannot pass.
Answer:      Architecture. `19` §19.2b and §19.3, matching the existing
             nightly invocation, so `ci/` and `.github/` need no change:
             - `soak --days D --golden P` makes one run of `D` sim-days with
               the §19.2a composer over a separate **soak fixture set**
               under `tests/fixtures/soak/` (`03`), with seed 12345 and the
               §19.2 command script. It passes iff the run's `16` §16.8
               checkpoint dump is byte-identical to `P`. Otherwise it
               prints `FAIL soak line=<L>`, with exit 1;
             - `soak --days D --out P` writes that dump to a new file. It
               exits 3 if `P` exists, so it never overwrites. This is how a
               golden is authored. Committing it is governed by
               `tests/golden/README.md`;
             - `P` is either repository-relative, resolved from the
               `AirportSim.sln` root, or fully qualified
               (`Path.IsPathFullyQualified`), which lets tests use a
               temporary directory. A path that is rooted but not fully
               qualified, such as `/x` or `C:x` on Windows, is a usage
               error. The cwd is never used. There is no
               `--seed`, and `soak-500.meta.json` is never read;
             - `soak` times nothing. The 0.1 ms bound is checked by a
               whole-run sizing test (§19.7). Until the golden is
               committed, the nightly job exits 3, never 0.
             The soak set's flow-graph `Sink` is `NodeId(9)`, so one
             stand-in constant serves both sets. The stand-in's `Name` is
             in the golden's `systems` line, so whatever T-009 merges is
             then kept.
Status:      ANSWERED (spec/19-interfaces-harness.md#192b-soak-the-soak-fixture-set-and-the-golden-q-057)

### Q-058 — `tools.simharness`: no timer seam to test `budget` rounding
Raised by:   Planner, filing T-045 (PR #69), via coordinator, 2026-10-01
Blocking:    T-045
Question:    §19.4 (Q-045) makes `budget` round each sample up, but `19`
             gives the harness no seam for injecting samples. Through
             `HarnessCli.Run`, real timings cannot tell rounding up apart
             from flooring.
Why it matters: T-045 has no deterministic done-test. A test of the real
             line passes under both rules.
Answer:      Architecture. One pure public member,
             `HarnessGates.BudgetFromSamples(IReadOnlyList<int64> samples,
             int64 frequency) -> GateResult` (§19.1, §19.4). It applies
             `03`'s rule with `B = 6000`, and its `Report` is the §19.3
             budget line. `budget --tier max` makes exactly one call with
             its 14 400 samples and `Stopwatch.Frequency`, prints `Report`,
             and exits on `Passed`. It throws for a count other than
             `TICKS_PER_SIM_DAY`, a negative sample, or a frequency outside
             `03`'s bound. An internal function was rejected because `07` L5
             forbids `InternalsVisibleTo`. A CLI flag that injects samples
             was rejected because it would put a seam in the gate. §19.7
             gives six tests: five with an exact expected `Report`, and one
             argument test. In one of the five,
             every sample is 60 001 ticks at 10^7 Hz, which fails under
             rounding up and passes under flooring.
Status:      ANSWERED (spec/19-interfaces-harness.md#194-budget---tier-max-q-026)

### Q-059 — `tools.simharness`: nightly `budget --tier max --report`
Raised by:   Architect, answering Q-057, 2026-10-01
Blocking:    no (the nightly "Performance trend" step only)
Question:    `.github/workflows/nightly.yml`'s "Performance trend" step runs
             `budget --tier max --report`. `19` §19.3 has no `--report`
             flag, so this is a usage error, exit 2. It is not reached
             today only because the soak step before it fails first.
             `ci/gates.md` lists a nightly "Performance trend report", but
             nothing says what that report contains, where it goes, or
             whether a budget over 6 ms should fail the nightly job.
Why it matters: Once the soak passes, this step will fail every night.
Proposed:    (A) `--report` is accepted with `budget --tier max` and
             changes nothing: the same run, line and exit code. The trend
             is then the nightly log's budget line. (B) The owner removes
             `--report` from the workflow. (C) The owner specifies a trend
             report, which is new scope. The Architect recommends (A) or
             (B). Both are small, but which one is right depends on what
             the owner meant by the CI step, and `ci/` and `.github/` are
             the owner's.
Answer:      **HUMAN DECISION — owner, 2026-10-01: (B).** The owner
             removes `--report` from `.github/workflows/nightly.yml`. The
             spec does not change. §19.3 keeps no `--report` flag, so
             `budget --tier max --report` stays a usage error. The
             Architect recorded this and did not decide it.
Status:      ANSWERED (spec/19-interfaces-harness.md#193-the-command-line-q-026) — HUMAN DECISION

<!-- Q-060 to Q-062 (PR #73) appear above, before Q-057. -->

### Q-063 — `sim.airside`: the base of `queuePosition`, and its value on `Released`
Raised by:   Architect, while answering Q-060 (PR #73), 2026-10-01
Blocking:    T-021
Question:    `AircraftHeldForRunway.QueuePosition` has no base. Is it 0-
             or 1-based, does it count the flight itself, and does it
             count pacing and occupancy holds together? Nothing says what
             `AircraftHeldForRunwayReleased.QueuePosition` carries.
Why it matters: Both are event payloads, so they are logged and must be
             exact. `sim.delay` copies the opening value into
             `DelayExplanation.B`.
Answer:      Architecture. It is 1-based and counts the flight itself. It
             is the runway's one hold queue's length just after the
             flight joins, which is what `RunwayQueueLength` reports then,
             so `1` means nobody was ahead. The retired "early aircraft
             with `queuePosition = 0`" case (Q-052) already treated 0 as
             "not in the queue". The value is fixed at emission. On
             `Released` it is always 0, the field's empty value, as `HeldAt`
             is null on `DepartureHeldForPassengersReleased`. `sim.delay`
             reads the opener. A released flight is always the head, and
             copying the opening value would need per-hold state. `10`
             §10.6 and `14` §14.3 say so. One new §12.13 test.
Status:      ANSWERED (spec/12-interfaces-airside.md#125-the-runway-model)

### Q-064 — Budgets: handler work that `03` does not time
Raised by:   Architect, while answering Q-061 (PR #73), 2026-10-01
Blocking:    T-024
Question:    `03` "Measured" times a module's `Tick` only. `sim.delay`
             does nearly all of its work in phase-3 event handlers
             (`14` §14.4–§14.8), so its 0.40 ms budget would time almost
             nothing. Whose budget does handler time belong to, and how
             is it timed?
Why it matters: A budget that does not time the work cannot fail, and the
             6 ms frame total would be under-counted by every handler.
Answer:      Architecture. `01` leaves the split of its 6 ms to `03`, so
             this is the Architect's apportionment, and no budget value
             changes. A module's measured time is now its `Tick` plus the
             bodies of its command `Apply`s and event handlers. The bus's
             call into a handler stays `sim.core`'s "event dispatch". To
             time them, the test builds the module with a
             `SystemServices` whose `Events` and `Commands` wrap each
             registered handler in a non-allocating timing shim. The
             tick's sample is the sum of the differences around `Tick`
             and every shimmed call, and `03`'s arithmetic applies to the
             sum. A module with no handler is timed as before. Marked LOW
             CONFIDENCE so the owner sees that `sim.delay`'s 0.40 ms now
             covers its handlers. If T-024 measures over, the remedies
             are the reserve or the owner reopening `01`'s split.
             After #73 merged: the rule also covers `sim.airside`'s
             handlers, which now append to the pending list and record
             into `RecordedCause` (Q-062), and `sim.turnaround`'s.
             `12` §12.12 and `13` §13.10 say so.
Status:      ANSWERED (spec/03-module-map.md#how-a-budget-is-measured)

### Q-065 — `sim.core`: does applying a command allocate?
Raised by:   Test Author / T-021, via coordinator, 2026-10-01
Blocking:    T-021
Question:    `12` §12.12 (Q-061) requires `sim.airside`'s allocation test
             to meter `ISimHost.Step` with one `ReassignStand` applied and
             one a no-op inside the window. `08` §8.6's zero-allocation
             rule covers the bus only. §8.7 says nothing about the command
             queue's own allocation when `ApplyDue` finds a due command
             and dispatches it. Is command application allocation-free,
             or how does a module test exclude it?
Why it matters: If `ApplyDue` allocates, a module's allocation test fails
             for a `sim.core` reason, and no module can fix it.
Answer:      Architecture. On a tick that completes normally, command
             application allocates nothing, and no module test excludes
             anything. `08` §8.7 "Allocation" pins `ApplyDue` and its
             dispatch (finding the due commands, skipping `NoOp`, the
             handler lookup, setting `Source`, calling `Apply`) as
             allocation-free for every kind, any number due per tick and
             any log size, from the first tick after `Build`. Growth
             happens only at admission, outside `Step`. `Apply` gets the
             admitted payload copy, and nothing else is copied. §8.5 adds
             that, on such a tick, the loop allocates nothing outside a
             checkpoint tick's phase 4. The bound mirrors Q-035's on
             §8.6. A tick that throws, from `Apply` or from a broken
             `sim.core` limit, is wrapped by §8.5a in a new
             `SimInvariantException`, which may allocate. That tick is
             outside the rule and never in an allocation test's window.
             `03`'s `Step` meter bullet says that it therefore excludes
             nothing for `sim.core`. This matches merged T-005 code
             (`CommandQueue.ApplyDue`), which is already allocation-free
             on such a tick. The merged tests show it only as a
             difference between two hosts, for `SetServersOpen` and
             `NoOp`. So one new `tests/sim/core` test asserts exactly 0
             for both kinds. It is a separate small test-only task after
             T-042, not part of T-042.
Status:      ANSWERED (spec/08-interfaces-core.md#87-commands)

<!-- Q-066 to Q-076: the T-030 Test Author's eleven gaps, via coordinator. -->

### Q-066 — `tools.simharness` `checkpoints`: grammar and usage errors
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    `19` §19.3 lists "exactly these forms" without
             `checkpoints`. Flag order, repeats, missing flags, `--days 0`
             and `Days × TICKS_PER_SIM_DAY` above `uint32` are undefined.
Why it matters: Usage errors are exit 2 everywhere else. Guessing gives
             tests and code different grammars.
Answer:      Architecture. §19.3 gains `checkpoints --bundle B --content
             C --days D --out P`. Flags in any order, each exactly once,
             all four required. `D` follows every other `--days`: 0 and
             `D > 298 261` are usage errors. `B`, `C` and `P` follow
             §19.2b's path rules. `--seed` and `--golden` are unknown
             flags. `--content` is new (Q-072). Usage is decided before
             any file is touched.
Status:      ANSWERED (spec/19-interfaces-harness.md#193-the-command-line-q-026)

### Q-067 — `checkpoints`: success stdout and exit codes
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    What does a successful `checkpoints` print, and which exit
             codes do a missing or bad bundle, a listed system without its
             file, a load failure and a write failure return?
Why it matters: §19.3's stdout grammar has no line for it, and its
             exit-3 cases name only §19.2a and §19.2b failures.
Answer:      Architecture. Success is exit 0 with one line, `WROTE
             checkpoints ticks=<n> checkpoints=<k> final=<hex16>`, which
             mirrors `soak --out`. It never exits 1. Every failure after
             usage is exit 3, with stdout empty and one stderr message.
             §19.2c lists them, in order: root, `--out` checks, bundle,
             composability, files, content, then composition and run,
             then the write. `P` is created only after the run, so an
             earlier exit 3 leaves no file.
Status:      ANSWERED (spec/19-interfaces-harness.md#192c-checkpoints-a-bundles-checkpoint-dump-q-066-to-q-076)

### Q-068 — `checkpoints`: path rules for `--bundle` and `--out`
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    Do §19.2b's rules (repository-relative or fully qualified,
             never overwrite) apply to `--bundle` and `--out`?
Why it matters: Tests need temporary directories, and CI must not
             depend on the working directory.
Answer:      Architecture. Yes, to `--bundle`, `--content` and `--out`
             alike. `--out` is never overwritten. The root is looked for
             only when a path is repository-relative. Bundle files are
             read by exact name and `B` is never listed.
Status:      ANSWERED (spec/19-interfaces-harness.md#192c-checkpoints-a-bundles-checkpoint-dump-q-066-to-q-076)

### Q-069 — `checkpoints`: which composition, and which systems?
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030; T-031 (its D7 test)
Question:    §19.2 says no flag selects a composition, but `--bundle`
             does. Which `systems` must the harness support? D7 needs the
             Phase 1 bundle (airside, turnaround, delay), but T-030
             depends only on T-004, T-006 and T-009. What happens with a
             system the harness can't compose?
Why it matters: As filed, T-030 cannot build the Phase 1 half of D7.
Answer:      Architecture, plus an ordering question for the Planner.
             `checkpoints` is §19.2's one exception: it composes the
             bundle by `16` §16.4 in its own code (§19.2c). The support
             is **staged**. T-030 composes `sim.world`, `sim.schedule`
             and `sim.flow`. A bundle listing another Phase 1 system is
             exit 3, naming it, and no test asserts that. A **Phase 1
             stage** harness task, after T-021, T-022 and T-024 merge,
             adds the other three and must merge before T-031. So T-030
             is not mis-ordered for its own scope. The Phase 1 half of
             D7 needs work after those three tasks, which is a pure
             ordering question and belongs to the Planner: either file
             the Phase 1 stage as its own task (recommended, because
             T-013, T-014 and T-045 queue behind T-030 as harness
             writers, and the nightly soak waits on T-013), or move T-030
             after T-021, T-022 and T-024, so that it covers both stages.
             §19.2c holds under either choice. `16` §16.3 also gets a
             strict `bundle.json` form: exact keys, a digit-string seed,
             and a non-empty list of distinct Phase 1 names, with each
             `Name` equal to its module name. The playtest bundle is the
             Unity shell's, which comes after T-031, so D7's Phase 1 half
             uses a test bundle, `tests/fixtures/harness/checkpoints-phase1/`
             (LOW CONFIDENCE, `16` §16.8).
Status:      ANSWERED (spec/19-interfaces-harness.md#192c-checkpoints-a-bundles-checkpoint-dump-q-066-to-q-076)

### Q-070 — `checkpoints`: the boarding stand-in, and §16.8's Phase 0 bundle
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    §19.2a registers the stand-in at slot 3, and no production
             composition does, so a T-009-style composition cannot be
             byte-identical to `IHeadlessRun`. §16.8's "Phase 0 bundle
             with only `sim.schedule` and `sim.flow`" contradicts §16.4
             (flow without world is a load failure) and T-009's real
             composition.
Why it matters: D7 would fail by construction.
Answer:      Architecture. `checkpoints` registers no stand-in and no
             probe, only the listed systems. The stand-in stays in the
             §19.2a CLI composition only. §16.8 now names the Phase 0
             bundle `tests/fixtures/harness/checkpoints-phase0/`, listing
             `sim.world`, `sim.schedule` and `sim.flow`: T-009's
             composition without the stand-in.
Status:      ANSWERED (spec/16-interfaces-host.md#168-the-headless-checkpoint-run-and-the-dump-format)

### Q-071 — `checkpoints`: does it submit §19.2's NoOp script?
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    The command queue is hashed, and `IHeadlessRun` has no
             commands.
Why it matters: A script on one side only breaks D7's byte identity.
Answer:      Architecture. No. Neither `checkpoints` nor `IHeadlessRun`
             submits any command. §19.2's script excludes `checkpoints`,
             and §16.8 says so for `Run`.
Status:      ANSWERED (spec/19-interfaces-harness.md#192c-checkpoints-a-bundles-checkpoint-dump-q-066-to-q-076)

### Q-072 — `checkpoints`: which content?
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    A bundle has no content file. Which content does
             `checkpoints` load?
Why it matters: The content index enters composition. Both sides of D7
             and of `16` §16.9 must load the same definitions.
Answer:      Architecture. A required `--content C` names a content
             directory laid out like `data/`. The harness lists every
             file under it, and `08` §8.11's loader orders and filters
             them. `--content data` is the player's content. `16` §16.9
             step 1 passes the build step's copy of it,
             `unity/AirportSim/Assets/StreamingAssets/Content`, beside
             the build step's copy of the playtest bundle (review of #83
             at `8df4681`). LOW CONFIDENCE: §19.2a
             uses a manifest, but there is none for `data/`, and here the
             directory is the whole input on both sides.
Status:      ANSWERED (spec/19-interfaces-harness.md#192c-checkpoints-a-bundles-checkpoint-dump-q-066-to-q-076)

### Q-073 — `checkpoints`: the loader `sourceName` per bundle file
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    Which `sourceName` does each loader get?
Why it matters: Loader messages start with it, and tests assert the
             prefix.
Answer:      Architecture. Exactly the bundle file name, such as
             `schedule.csv`, on both sides (`16` §16.4 step 2, `19`
             §19.2c).
Status:      ANSWERED (spec/16-interfaces-host.md#164-composition)

### Q-074 — `checkpoints`: no seam for the batch-size test
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    `test_checkpoints_result_independent_of_step_batch_size` has
             no batch flag and no public checkpoints member in §19.1.
Why it matters: The test cannot vary anything.
Answer:      Architecture. No seam is added. Both sides pin their
             stepping to `Days` calls of `Step(TICKS_PER_SIM_DAY)`. The
             test builds a checkpoints kit from the same bundle through
             the published factories, steps it as `Step(1)`, as one
             `Step(14 400)` and as `Step(997)` with a remainder, and
             asserts that all three dumps equal the CLI's file (§19.8).
             `IHeadlessRun`'s test does the same through `ISimComposer`
             (`16` §16.8).
Status:      ANSWERED (spec/19-interfaces-harness.md#198-tests-of-checkpoints-q-074-to-q-076)

### Q-075 — `checkpoints`: a checkable "published factories only" rule
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    `test_checkpoints_subcommand_composes_through_published_factories_only`
             has no checkable rule.
Why it matters: A test with no rule asserts nothing.
Answer:      Architecture. Two parts, both in the test (§19.8).
             Behavioural: the default invocation exits 0, and its file
             is byte-identical to the published-surface kit's dump, so
             the test fails without a working `checkpoints`. Static, by
             reflection: the harness assembly references no
             `AirportSim.App.*` assembly, so D7 compares two independent
             compositions, and no `AirportSim.*` assembly it references
             carries `InternalsVisibleTo`. The static part guards against
             regression, and today's `main` already passes it. The
             Reviewer checks that no non-public member is reached by
             reflection.
Status:      ANSWERED (spec/19-interfaces-harness.md#198-tests-of-checkpoints-q-074-to-q-076)

### Q-076 — `checkpoints`: where the bundle fixture lives
Raised by:   Test Author / T-030, via coordinator, 2026-10-01
Blocking:    T-030
Question:    Where is the bundle fixture, and do T-030's writable paths
             need it?
Why it matters: The path guard blocks unlisted writes.
Answer:      Architecture. `tests/fixtures/harness/checkpoints-phase0/`
             holds `bundle.json` (seed `"12345"`, the three Phase 0
             systems), and `world.fixture`, `flow.fixture` and
             `schedule.csv` as byte copies of the Phase 0 set. Its
             content is `tests/fixtures/harness/phase0-content/`. The
             Test Author writes them, so the worker's writable paths stay
             `tools/SimHarness/**`. The Planner adds
             `tests/fixtures/harness/checkpoints-phase0/**` to the Test
             Author's grant. The Phase 1 stage's Test Author writes
             `tests/fixtures/harness/checkpoints-phase1/`.
Status:      ANSWERED (spec/19-interfaces-harness.md#192c-checkpoints-a-bundles-checkpoint-dump-q-066-to-q-076)

### Q-077 — `app.host`: the D7 test cannot reach the harness
Raised by:   Architect, answering Q-066 to Q-076 (PR #83), 2026-10-01
Blocking:    T-031 (`test_host_composition_matches_harness_checkpoints`)
Question:    `16` §16.8 runs the D7 test in process. `07` L3 lets
             `tests/app/host` reference only `src/app/host`, and
             `app.host` does not reference the harness, nor the harness
             `app.host`. No test project can therefore call both
             `HarnessCli.Run` and `IHeadlessRun` in process. How does
             the D7 test run both sides?
Why it matters: As specified, the test cannot be written.
Answer:      Architecture, option (A). `07` L1 gains one integration test
             project, `tests/integration/AirportSim.Integration.Tests.csproj`.
             Under L3 it is the only test project with two references:
             `src/app/host`, then `tools/SimHarness`. It calls both in
             process and spawns nothing. It holds only tests that compare
             `app.host` with the harness, which at Phase 1 is exactly
             the D7 test. Any other test there needs an amendment. L8:
             T-031's Test Author writes the project file, and T-031 adds
             it to `AirportSim.sln`. `16` §16.8 and §16.11 say where the
             test lives. Rejected:
             (B) committed golden dumps compared by a harness test and a
             host test. Every hash-moving change would re-author them,
             with owner confirmation (`tests/golden/README.md`), for
             nothing that (A) does not check directly;
             (C) spawning the built harness from the host test. Q-025
             binds harness tests only, so nothing forbids it outright.
             But it drops §16.8's in-process run, and the host test
             would have to locate a build output that its project does
             not reference, with its configuration and runtime, which
             ties it to the build layout;
             (D) the harness referencing `app.host`, which pulls
             `app.render` and `app.ui` into a CI tool and makes "two
             independent compositions" uncheckable.
             No option was chosen that needs an owner decision.
Status:      ANSWERED (spec/07-conventions.md#solution-layout-and-build-q-013)

<!-- Q-078 and Q-079: the T-021 worker, via coordinator, against PR #78's tests. -->

### Q-078 — `sim.airside`: the `Cause` of hold-release events
Raised by:   Worker / T-021, via coordinator, 2026-10-02
Blocking:    T-021
Question:    PR #78's helper `AirsideAsserts.Pairs<THold, TRelease>`
             (`tests/sim/airside/AirsideAsserts.cs`, lines 108-109)
             requires every release's `Cause` to be its opening hold
             event. `12` §12.7 says `StandAssigned`'s `Cause` is the
             freeing `Pushback`, and other tests in the same PR assert
             that. Which is it, per pair?
Why it matters: The stand family cannot satisfy both, so the approved
             tests cannot all pass.
Answer:      The spec was already unambiguous; the helper is wrong for
             the stand family. Runway, taxiway and passenger holds close
             with `Cause` = their opening event (`12` §12.5, §12.6,
             §12.8). `StandAssigned` closes with the `Pushback` that
             freed the stand, or `EventRef.None` after a `ReassignStand`
             (`12` §12.7), and `14` §14.5 already said so. This amendment
             makes it explicit in all three files: `10` §10.3 rule 2 now
             says pairs are never matched by `Cause` and a closing
             `Cause` is per family. `10` §10.2 now says what an emitter
             "knows": the event that made the emission due, published
             earlier in the same tick or kept in hashed state. Conditions
             are not causes, and no id is kept across ticks only to be
             named. `12` §12.11 gains a binding table
             with the `Cause` of every event `sim.airside` publishes, and
             `14` §14.5 cites it. `sim.delay` never reads a closing
             `Cause` (§14.5, §14.7), so it is unaffected. The Test Author
             fixes `Pairs` to pair by kind and flight only, and asserts
             `Cause` per family, or leaves `Cause` to the callers. T-021
             gains one test, `test_airside_event_causes_follow_cause_table`.
Status:      ANSWERED (spec/12-interfaces-airside.md#the-cause-of-each-emitted-event-q-078)

### Q-079 — `sim.airside`: cross-tick state holding event ids
Raised by:   Worker / T-021, via coordinator, 2026-10-02
Blocking:    T-021
Question:    A release's `Cause` (the hold event) and `StandAssigned`'s
             `Cause` (the freeing `Pushback`) are ids from an earlier
             tick. §12.12 lists no state for them, so an implementation
             keeps them unhashed and loses them on save and load, the
             same class as Q-062's `RecordedCause`. Where do they live,
             and how are they hashed?
Why it matters: `08` §8.6: events must never be the sole carrier of
             state. A run saved and loaded mid-hold would emit a
             different `Cause` from one that was not.
Answer:      Two new fields. `AircraftTrack.OpenHold` (`EventRef`, last
             field) holds the opening event of the flight's open runway,
             taxiway or passenger hold: set when the hold is published,
             read as the release's `Cause` and cleared in the release
             action. A flight has at most one such hold open.
             `StandState.VacatedBy` (`EventRef`, after `Occupant`) holds
             the `Pushback` that freed the stand, and is cleared whenever
             `Occupant` is set. Both are fed as `RecordedCause` is:
             `HasValue`, `Id.Tick`, `Id.Sequence` widened to `uint64`,
             with zeros for `EventRef.None`, at §12.12 items 4 and 3.
             They are saved with the state. `Tick` keeps no other
             cross-tick state. Also noted: `AwaitingPushbackClearance`
             and `Departed` are reserved and never set at Phase 0/1
             (§12.9 "Reserved phases"). That is intended. T-021 gains
             `test_open_hold_and_vacated_by_track_cross_tick_causes`.
Status:      ANSWERED (spec/12-interfaces-airside.md#129-module-interface)
