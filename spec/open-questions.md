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
