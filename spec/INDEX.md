# Spec index

**Read this first, then open only the sections your task cites.** It exists
so that a freshly spawned agent does not have to read all of `spec/`.

- **Navigation only.** The spec files are the truth. Where this index and a
  file disagree, **the file wins**, and the mismatch is an index bug: report
  it in `open-questions.md`.
- Always read `CLAUDE.md` and your role brief in `agents/`. Every sim worker
  also reads `01`, `02` and `07` in full. They are short, and they bind
  everything.
- **Maintenance rule:** every spec amendment updates this file in the same
  commit, whether it adds a section, moves a decision or adds or retires a
  LOW CONFIDENCE marker. A spec commit that leaves the index stale is
  incomplete.

Legend: **LC** = LOW CONFIDENCE marker; **HD** = HUMAN DECISION — owner
(delegated), 2026-09-23 (D1–D9), each reversible.

---

## Per-module quick map

| You are working on | Read, beyond `01`/`02`/`07` |
|---|---|
| Anyone constructing a system (harness, host, integration tests) | `08` §8.11a, then the module's Construction section: `09` §9.11, `11` §11.9a, `12` §12.12a, `13` §13.10a, `14` §14.13a, `15` §15.9, `17` §17.7 |
| `sim.core` (T-001–T-006, T-026) | `08` all; `10` §10.2, §10.3; `03` budgets. T-003: `08` §8.3. T-026: `10` §10.6 plus the type blocks of `12` §12.4, `13` §13.3, `14` §14.3 |
| `tools.simharness` (T-006, T-009) | `02` Gates; `08` §8.5, §8.9; `03` "How a budget is measured", "The soak fixture"; `16` §16.8 (the `checkpoints` subcommand) |
| `sim.world` (fixed walk graph) | `18` all; `08` §8.9, §8.11a; `09` §9.6 (its consumer) |
| `sim.flow` (T-007, T-010, T-011, T-023) | `09` all; `18` §18.2, §18.3, §18.5; `08` §8.3, §8.7; `10` §10.6 From `sim.flow`; `11` §11.6 (who calls `Inject`) |
| `sim.schedule` (T-008) | `11` all; `08` §8.2, §8.4; `09` §9.7; `10` §10.4, §10.6 |
| `sim.airside` (T-021 and the D6 hold) | `12` all; `11` §11.3, §11.7; `09` §9.7, §9.7a; `10` §10.3, §10.4, §10.6 |
| `sim.turnaround` (T-022) | `13` all; `12` §12.3, §12.8; `10` §10.4, §10.6 |
| `sim.delay` (T-024) | `14` all; `06` all; `10` all; `12` §12.3 and `13` §13.6 for milestone semantics only |
| `app.render` scene (T-020) | `15` §15.1–§15.12; `08` §8.5; `09` §9.1, §9.7; `12` §12.4, §12.9; `16` §16.6 (frame order) |
| `app.ui` scene | `17` all; `15` §15.4, §15.5, §15.8, §15.9; `16` §16.6; `08` §8.7; `09` §9.7b, §9.8 |
| `app.host` | `16` all; `15` §15.3, §15.9, §15.10; `17` §17.4, §17.7; `08` §8.5, §8.9 |
| Unity backends / project shell | `16` §16.2, §16.7; `15` §15.10; `17` §17.8 |
| Test Author | `07` Testing; the "fixtures and tests" section of your module's file (`11` §11.10, `12` §12.13, `13` §13.11, `14` §14.14, `15` §15.12, `16` §16.11, `17` §17.10) |
| Planner | `03`; `00` build order; `CHANGELOG.md` Impact lines; `open-questions.md` Blocking lines |

---

## Files

### `00-overview.md`
- Owns: pillars, 1.0 scope, non-goals, systems index, build order.
- Key: build order is dependency order, never reordered; delay attribution is
  built at step 6, before money; not an ATC sim, no multiplayer at 1.0.
- LC: none.
- Read if: planning, or checking a proposal against the non-goals.

### `01-architecture.md` — LOCKED
- Owns: platform decisions, layer separation, command pattern, hierarchical
  simulation (flow vs agent), pathfinding, content, saves.
- Key: **the sim targets `netstandard2.1` only, `LangVersion 9`, with zero
  engine references; tests and the harness are `net8.0` (HD, D1)**; 10 Hz at
  1x; 6 ms/tick at max tier; player actions enter only as commands;
  promotion must be outcome-neutral; never per-agent A\*.
- LC: none.
- Read if: any sim task (all of it).

### `02-determinism.md` — LOCKED
- Owns: the determinism contract, rules 1–8, the gates, state hashing, gate
  failure procedure.
- Key: no floats in sim state; single seeded RNG with per-system streams; no
  hash-order iteration; five gates block merges. It is unchanged by D1–D9.
  The proposed Mono-vs-CoreCLR gate is in `16` §16.9 and not yet adopted.
- LC: none.
- Read if: any sim or harness task (all of it).

### `03-module-map.md`
- Owns: modules, directories, dependencies, published-interface table,
  per-module budgets, measurement protocol, soak fixture.
- Key: downward calls only, upward information as events; `sim.airside`
  depends on `flow` (D6 fix); new module `app.host` (HD, D7); **the soak runs a
  mid-tier fixture under 0.1 ms/tick (HD, D3)**; budget pass = mean ≤ budget
  and p99 ≤ 2× budget.
- LC: the checkpoint-hashing ceiling (20 ms) and the snapshot-write ceiling
  (250 ms).
- Read if: planning; any budget test ("How a budget is measured"); the soak
  ("The soak fixture").

### `04-data-schemas.md`
- Owns: the content schema list, content conventions, balance ownership.
- Key: all content is data and validated; fixtures are not content;
  `data/balance/` is human-only; **the first balance file is
  `data/balance/airside_rules.json` (`boarding_hold_max_minutes` = 10, HD, D6)**.
- LC: none.
- Read if: content tasks; anyone about to write a number that might be
  balance.

### `05-policy-system.md`
- Owns: policy mechanics, categories, reputation meters. Phase 2+.
- Key: policies are content; effects arrive through the policy effect path,
  never as branches in other modules.
- LC: none.
- Read if: `sim.policy` or `sim.reputation` (not yet specified as
  interfaces).

### `06-delay-attribution.md`
- Owns: delay principles, the `DelayEvent` contract, `DelayCategory`, required
  tests.
- Key: one parent per minute and leaves sum exactly to the total (in ticks);
  `sim.delay` is read-only and event-only; live, never reconstructed; depth
  capped at 6.
- LC: none (see `10`, `14`).
- Read if: `sim.delay`, `app.ui` delay views (later).

### `07-conventions.md`
- Owns: naming, testing, comments, errors, logging, runtime portability,
  performance.
- Key: workers never edit tests; sim throws only on programmer error; **the
  Runtime portability rules (D1): no hash-order dependence, no `GetHashCode`
  in behaviour, total sort comparers, ordinal and invariant strings, no
  reflection order, no memory punning, netstandard2.1/C# 9 with no NuGet
  polyfills**.
- LC: none.
- Read if: every task (all of it).

### `08-interfaces-core.md` — `sim.core`
- Owns: constants, sim time, `Fx`, ids, `ISimSystem` and the phase order,
  registry order, `ISimHost`, the event bus, commands, RNG, hashing, logging,
  content access.
- Key: **`SIM_SECONDS_PER_TICK = 6`, so a day is 14 400 ticks, and goldens
  may be authored (HD, D2, §8.2)**; **`Fx` hand-rolls its 128-bit multiply,
  divide and leading-zero count (D1, §8.3)**; FIFO event dispatch with
  handlers in registry order (§8.6); commands admitted only at ≥ 1 tick of
  lead and never re-dated (§8.7); **command kinds, `PlayerId`, the
  little-endian payload table and `ICommandHandler` dispatch, with a pure
  `Validate` at admission and a logged no-op at `Apply` (§8.7, Q-010)**;
  xoshiro256\*\* + SplitMix64 (§8.8);
  FNV-1a-64 (§8.9); **construction (§8.11a, Q-009): `ISimHostBuilder`,
  `SystemServices`, and one stateless `<Module>Factory` per module; construct
  in dependency order, register in registry order**.
- LC: none left.
- Read if: T-001–T-006, T-026; §8.5 and §8.9 for the harness and `app.host`.

### `09-interfaces-flow.md` — `sim.flow`
- Owns: cohorts, queue throughput, spillback, corridors and routing,
  `IFlowSystem`, `SetServersOpen`.
- Key: the cohort is the only authoritative state and agents are derived
  views (§9.1); integer heads with `Fx` service credit (§9.4); **new query
  `TryGetOutstanding` for the boarding hold (§9.7a, D6)**; **read-only
  `TryGetLaneState` (§9.7b, HD, consequence of D5)**; the `SetServersOpen`
  handler's `Validate`/`Apply` rules (§9.8); mandatory merging
  is a budget requirement (§9.3); **routing over `sim.world`'s walk graph:
  pooled `Gate` destinations, lowest traversal plus queue wait along
  `PathVia` (§9.6, Q-012)**; factory plus `IFlowGraphLoader`, where
  `FlowGraph` is node behaviour only (§9.11).
- LC: least-cost routing (§9.6); `TryGetOutstanding` and its "most passengers"
  blame rule (§9.7a); the `LaneState` shape (§9.7b).
- Read if: T-007, T-010, T-011, T-023; §9.7, §9.7a and §9.7b for callers.

### `10-events.md` — event catalogue
- Owns: envelope, emission discipline, milestones, allocation rule, the event
  catalogue, `sim.delay`'s output.
- Key: milestones plus blocking intervals; `PlannedTick` is schedule-anchored
  and cumulative (§10.4); intervals come in pairs and may cross days
  (§10.3); **new pair `DepartureHeldForPassengers`/`Released`, category
  `passenger_late` (§10.6, D6)**.
- LC: first-blocker-wins (§10.5 rule 3).
- Read if: any emitter (§10.3, its §10.6 table); `sim.delay` (all of it).

### `11-interfaces-schedule.md` — `sim.schedule`
- Owns: flight records, derived `FlightId`, CSV fixture format, publication,
  show-up curve and injection.
- Key: query-only; no RNG; fixture hash fed into the state hash; runs
  identically with or without `sim.flow` (§11.6).
- LC: none.
- Read if: T-008; §11.3/§11.7 for consumers.

### `12-interfaces-airside.md` — `sim.airside`
- Owns: runways, taxiways, stands, movement, door milestones, the turnaround
  handshake and fallback, the boarding hold.
- Key: milestone ownership and `PlannedTick` table (§12.3); two `FlightId`s
  per rotation, with the stand handed off (§12.3, §12.8); **boarding hold:
  hold at the doors-close point while passengers are outstanding, at most
  `AirsideRules.BoardingHoldMaxMinutes`, then close and miss the remainder
  (§12.8, HD, D6)**; no RNG; factory with an explicit `turnaroundRegistered`,
  and `IAirsideLayoutLoader.Parse` (§12.12a); `ReassignStand`'s state checks
  happen at `Apply`, as a no-op (§12.10, Q-010).
- LC: `InboundAirborne` is a formality (§12.6); rotation-less departures get
  no ground time in the fallback (§12.7); hold timing measured from the
  actual doors-close point, and released at a zero count (§12.8).
- Read if: T-021 and the hold task; §12.3 for `sim.turnaround`/`sim.delay`;
  §12.4/§12.9 for `app.render`.

### `13-interfaces-turnaround.md` — `sim.turnaround`
- Owns: job catalogue, vehicle fleet, FIFO dispatch, `DeboardComplete`,
  `ReadyToBoard`, `BoardingComplete`.
- Key: a vehicle is its own crew at Phase 0/1; dispatch by lowest blocking
  `EventId`; `Boarding` waits on five jobs; unchanged by D6; factory plus
  `ITurnaroundSetupLoader` (§13.10a).
- LC: distance-blind dispatch (§13.5).
- Read if: T-022.

### `14-interfaces-delay.md` — `sim.delay`
- Owns: delay records, checkpoints, interval families, allocation, tree and
  ids, finalisation and retention, missed passengers, `IDelaySystem`.
- Key: integer-tick allocation, so leaves sum exactly (§14.6); `DelayEventId`
  is its own counter (§14.7); `DelayEvent` published only at finalisation
  (§14.8); **fifth interval family: passenger hold, `passenger_late`,
  `DelaySource.PassengerHold` (§14.3, §14.5, §14.9, HD, D6)**.
- LC (all accepted as provisional, HD, D8): 2-day retention (§14.2);
  the checkpoint set (§14.4); the cap and recovery order (§14.6).
- Read if: T-024.

### `15-interfaces-render.md` — `app.render`
- Owns: headless scene layer, layout, draw list, polled queries, promotion
  controller, tick pacer, backend contract.
- Key: logic is headless and the backend is thin; `SetPromoted` is only
  called between `Step`s; **pacer speeds pause, 1x, 2x, 4x (§15.8, HD, D4)**;
  the frame order moved to `16` §16.6 (D7); the scene layer targets
  `netstandard2.1` (§15.3, D1); all §15.13 decisions made; lane pips drawn
  from `TryGetLaneState` (§15.5, Q-010).
- LC (all accepted as provisional, HD, D8): zoom threshold 120 (§15.2); the
  split layout (§15.4); the 2 ms scene budget (§15.11). Also: pips drawn in
  render rather than as a UI overlay (`CHANGELOG`, Q-010).
- Read if: T-020; the render backend task (§15.10).

### `16-interfaces-host.md` — `app.host` (new, D7)
- Owns: the Unity project `unity/AirportSim/`, the headless composition root,
  the scenario bundle, the frame loop, the bootstrap, the checkpoint dump, the
  proposed cross-runtime gate.
- Key: **Mono backend and the CI-tested assemblies as plugins (§16.2)**;
  composition is a pure function of the bundle's bytes (§16.4); frame order
  is UI, then promotion, then `Step`, then build (§16.6); the byte-exact
  checkpoint dump and the harness `checkpoints` subcommand (§16.8); exact
  bundle file names and the composition steps (§16.3, §16.4), using the
  Q-009 factories. **The player build waits on Q-011 (content).**
- LC: the gate runs nightly on the real player (§16.9, proposed, not adopted).
- Read if: the host tasks, the harness `checkpoints` subcommand, and the
  cross-runtime gate.

### `17-interfaces-ui.md` — `app.ui`, Phase 1 (new, D5)
- Owns: pacing state, the lane click (hit test, then a request), screen-to-world
  mapping, the icon-only backend contract.
- Key: starts unpaused at 1x; primary click = one more server, secondary = one
  fewer; the topmost (highest `NodeId`) box wins; no text or panels at
  Phase 1; the production lane sink computes `clamp(base ± 1)` from a
  pending target or `TryGetLaneState`, and submits `SetServersOpen` for the
  next tick (§17.5, Q-010).
- LC: none marked; the +1/−1 click grammar is flagged in `CHANGELOG.md`.
- Read if: the UI scene-layer and UI backend tasks.

### `18-interfaces-world.md` — `sim.world`, Phase 0/1 subset (new, Q-012)
- Owns: the fixed landside walk graph (node lengths, directed edges) and
  its load-time routes.
- Key: `IWorldSystem` offers `CanReach`, `CanReachVia` and `PathVia` (shortest
  path, ties by edge sequence); no runtime state; the hash is the fixture
  hash; registry 1. **Gate assignment is deferred to the owner**, and Phase 0/1
  pools gates in one lounge (§18.5). Construction, grid and flow fields are
  deferred.
- LC: none marked; the gate-pooling stopgap is flagged in `CHANGELOG.md`.
- Read if: the `sim.world` task; `sim.flow` (§18.2, §18.3, §18.5).

### `CHANGELOG.md`
- Owns: every spec change with Reason, Raised by, Impact and Signed off; the
  running scope total (8, after Q-010 (5)).
- Read if: you are the Planner (Impact lines list stale tasks), you are
  reviewing the Architect, or you need why a rule exists.

### `open-questions.md`
- Owns: questions the spec does not answer, and their status.
- Open now: **Q-011** (content definitions and the `data/` loader; blocks
  the player build). Q-002 to Q-010 and Q-012 are answered; Q-001 was
  deleted (D9).
- Read if: before starting any task, check that your task is not blocked
  here.
