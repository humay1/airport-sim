# 09 — Public interfaces: `sim.flow`

Implements the `sim.flow` row of `03-module-map.md`: passenger cohorts, queue
nodes, promotion/demotion, corridors. Depends on `sim.core` and `sim.world`
(`18-interfaces-world.md`), and on nothing else. Notation and binding rules are as in `08-interfaces-core.md`.

`sim.flow` is the largest consumer of the frame budget (`01-architecture.md`) and
the module the Phase 0 kill gate tests. Every decision below is made to keep the
per-tick cost proportional to **nodes**, not to passengers.

---

## 9.1 The representation rule

`01-architecture.md` defines two representations, and rule 4 there requires
promotion to be outcome-neutral. This spec satisfies that rule by construction
rather than by care:

> **The cohort is the only authoritative state. An agent is a derived view of a
> cohort and can never feed a value back into it.**

Consequences, all binding:

1. Simulation results depend on cohort state only. Promotion adds no state that
   any cohort update reads.
2. Agent-level detail (per-body position, gait, jitter) is derived from
   `flow.presentation`, an RNG stream that is **excluded from the state hash** and
   may be read by no other system. It is the only stream in the sim with that
   property, and it exists so that visual variety cannot move the sim.
3. Demotion is free: there is nothing to fold back. Aggregate state was never
   left.
4. Individual identity, when the delay tree must name a passenger
   (`01-architecture.md` promotion rule 2), is **derived, not drawn**:
   `PassengerRef = (CohortId, indexWithinCohort)`. Deriving instead of drawing is
   what keeps naming a passenger free of RNG consumption, and therefore
   outcome-neutral.

If a future system cannot live with this, `01-architecture.md` says it does not
get promotion; it does not get an exception here.

---

## 9.2 Types

```
struct NodeId    { uint32 Value }        // sim.core type (events carry it); the graph is sim.world's, 18 §18.2
struct CohortId  { uint64 Value }        // from IIdAllocator, owner sim.flow; compiled in sim.core (10 §10.9, Q-018)
struct EdgeId    { uint32 Value }        // sim.core type; a sim.world walk edge

enum NodeKind {
  Source,          // kerbside, rail box, arriving aircraft door
  Corridor,        // walkable link with a traversal time
  Hall,            // unqueued dwell space with a capacity
  Queue,           // served node with a throughput model, §9.4
  Gate,            // boarding hold
  Sink             // departed aircraft, landside exit
}

enum FlowDirection { Departing, Arriving, Transferring }

readonly struct CohortKey {              // the discriminator, §9.3
  FlightId      Flight
  FlowDirection Direction
  ContentId     PaxProfile               // data-driven, data/schemas
  bool          HasHoldBaggage
  bool          RequiresAssistance
}

readonly struct PassengerCohort {
  CohortId   Id
  CohortKey  Key
  NodeId     Node
  int32      Count                       // > 0 always; a zero cohort is deleted
  Tick       EnteredNodeAt
  Tick       DueAt                       // corridor release / gate close
  Fx         ServiceCredit               // §9.4, queue nodes only
}
```

`Count` is an integer. Passengers are never fractional; the fractional part of a
service rate lives in `ServiceCredit`, never in a head count. A test asserts the
total head count is conserved across every operation except `Source` and `Sink`,
and the missed-passenger removal in `Absorb` (§9.7).

---

## 9.3 Cohort identity, splitting and merging

A cohort exists because its members are interchangeable. Therefore:

- Two cohorts **may merge** only if they are on the same node and their
  `CohortKey` compares equal in every field. Merge sums `Count`, takes the earlier
  `EnteredNodeAt` and sums `ServiceCredit`. On a `Corridor`, two cohorts
  merge only if their `DueAt` is also equal, so a merge never moves a
  release tick. Elsewhere the merged `DueAt` is the merged `EnteredNodeAt`,
  as §9.12 has for every non-`Corridor` node (Q-033).
- Merging is **mandatory** at end of update, not optional: without it, cohort
  count grows without bound over a day and the O(nodes) cost claim fails. This is
  a budget requirement, not tidiness.
- A cohort **splits** when part of it is served, released or diverted. The
  remainder keeps the original `CohortId`; the moving part gets a new one. Keeping
  the id on the remainder means a `PassengerRef` into a waiting cohort stays valid
  for the delay tree.
- Iteration over cohorts is **always** in ascending `CohortId` order
  (`02-determinism.md` rule 5). Iteration over nodes is in ascending `NodeId`.

---

## 9.4 Queue nodes: the throughput model

A queue is a throughput model with a population, not a crowd
(`01-architecture.md`). Per tick, for each queue node, in `NodeId` order:

```
readonly struct QueueConfig {
  int32 ServerCount            // lanes/desks physically built
  int32 ServersOpen            // <= ServerCount, set by command or staffing
  Fx    ServiceRatePerServer   // passengers per sim-minute, from content
  int32 CapacityStanding       // spillback threshold, §9.5
}
```

1. `capacityThisTick = ServersOpen * ServiceRatePerServer * (SIM_SECONDS_PER_TICK / 60)`
   in `Fx`, computed with `Fx.Mul` and `Fx.FromRatio` only.
2. Add to `ServiceCredit`. Whole passengers are served:
   `served = Fx.Floor(ServiceCredit)`, then `ServiceCredit -= Fx.FromInt(served)`.
   The carried remainder is what makes a 2.5 pax/min lane behave as 2.5 and not as
   2 or 3, and it is state: it is saved and hashed.
3. Serve `min(served, population)` passengers, drawing from cohorts in **FIFO
   order of `(EnteredNodeAt, CohortId)`**. Per-cohort service must not depend on
   cohort *size*, or splitting a cohort would change outcomes.
4. Served passengers move to the node's outbound edge per §9.6.
5. Unused credit is capped at one server-tick's worth. Without the cap a queue
   left closed overnight discharges a burst the instant it opens, which is a
   visible absurdity and an attribution lie.

Steps 1 to 5 are made exact in §9.12 (Q-032).

`ServiceRatePerServer` comes from content (`04-data-schemas.md`) and is never
hardcoded. Posture modifiers (`05-policy-system.md`, security posture) arrive as
a multiplier through the policy effect path, not as a branch inside `sim.flow`.

Wait time is **measured, not modelled**: `PredictedWaitMinutes` is
`population / max(capacityPerMinute, epsilon)`, published for UI and for the
service-standard KPI, and never fed back into the sim.

---

## 9.5 Capacity and spillback

- Every node has a capacity. Population above `CapacityStanding` does not vanish
  and does not stack invisibly: the upstream edge stops releasing into it, which
  backs the pressure up the graph. Exactly (Q-032): at Phase 0/1 only a
  `Queue` node has a capacity. It is **full** when its start-of-tick
  population is `>= CapacityStanding`. `Source`, `Corridor`, `Hall`, `Gate`
  and `Sink` nodes are never full. A `Hall` capacity is later scope, by
  amendment.
- Spillback is evaluated in a single pass in `NodeId` order using the population
  **at the start of the tick**. Using in-tick populations would make results
  depend on evaluation order in a way that a later refactor would silently change.
- A cohort blocked by spillback emits `FlowBlocked` once per blocking episode, not
  once per tick (`10-events.md` §10.3), and emits `FlowUnblocked` when released.
  Those two events are what let `sim.delay` attribute landside delay to the true
  upstream constraint rather than to the door the passenger was standing at.

---

## 9.6 Corridors and routing

- The walkable graph, its lengths and its routes are owned by `sim.world`
  (`18-interfaces-world.md`, the fixed-graph Phase 0/1 subset). `sim.flow`
  **reads** them and never computes a path. Per-agent A* is a review
  rejection (`01-architecture.md`).
- A corridor traversal is a delay line: on entry the cohort's `DueAt` is set to
  `EnteredNodeAt + traversalTicks`, where
  `traversalTicks = max(1, ceil(LengthMetres / (walk_speed_mps × SIM_SECONDS_PER_TICK)))`
  in `Fx`. `LengthMetres` comes from `IWorldSystem.LengthMetres`, and
  `walk_speed_mps` from the cohort's pax profile. On or after `DueAt` the
  cohort is released to the next node, subject to §9.5. A `Source` or `Hall`
  node that is not the cohort's destination releases on the tick after entry.
  Dwell behaviour is later scope.
- **Destinations (Q-012).** A `Departing` cohort's destination set is every
  `Gate` node it can reach (`CanReach`). Gates are pooled at Phase 0/1, and
  gate assignment is deferred (`18` §18.5). A cohort on a `Gate` node stays
  there until `Absorb` or missed-flight handling (§9.9).
- **Routing (Q-012).** When a cohort is released from a node, it takes the
  outbound edge chosen by a **declared, deterministic rule**. Over every pair
  `(e, g)` with `e` in `OutEdges(node)`, `g` in the destination set and
  `CanReachVia(e, g)`:
  `cost = Σ traversalTicks of the nodes on PathVia(e, g)`
  `+ Σ PredictedWaitMinutes × TICKS_PER_SIM_MINUTE of the Queue nodes on PathVia(e, g)`.
  Waits are read at the start of the tick (§9.5's rule). The lowest cost wins,
  with ties broken by ascending `g` `NodeId`, then ascending `EdgeId`. "Two
  security halls" are therefore two routes to the gate, and the rule chooses
  between them. No randomness. Passenger "choice" as a behaviour model is out
  of scope at Phase 0 and needs a spec amendment, not a local invention.
- Cost: O(out-degree × gates × path length) per released cohort. The budget
  test (§9.10) is what shows whether this needs caching.

> **LOW CONFIDENCE — deterministic least-cost routing.** It is correct and cheap,
> but every passenger taking the same door can look wrong on screen and can make
> two queues oscillate. The mitigation, if playtest shows it, is a fixed
> proportional split declared in content — not an RNG draw. Flagged for the human
> owner.

---

## 9.7 Module interface

```
interface IFlowSystem : ISimSystem {

  // ---- queries, read-only, for presentation, delay and baggage ----
  int32  Population(NodeId node)
  Fx     PredictedWaitMinutes(NodeId node)
  int32  PopulationForFlight(FlightId flight, FlowDirection direction)
  IReadOnlyList<CohortId> CohortsAt(NodeId node)          // ascending CohortId
  bool   TryGetCohort(CohortId id, out PassengerCohort cohort)
  bool   TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)   // §9.7a; false iff none
  bool   TryGetLaneState(NodeId node, out LaneState lanes)                           // §9.7b; false unless a Queue node

  // ---- injection, called only by the systems named ----
  CohortId Inject(in CohortKey key, int32 count, NodeId at)   // sim.schedule, sim.airside
  int32    Absorb(NodeId sink, FlightId flight)               // boarding / exit

  // ---- promotion, driven by presentation, outcome-neutral by §9.1 ----
  void SetPromoted(NodeId node, bool promoted)
  IReadOnlyList<AgentView> AgentsAt(NodeId node)              // empty if not promoted
}

readonly struct AgentView {                 // presentation only, never sim state
  PassengerRef Ref
  NodeId       Node
  Fx           ProgressAlongEdge            // 0..1
}

readonly struct PassengerRef { CohortId Cohort; int32 Index }
```

`Inject` and `Absorb` are the only mutating entry points and they are **downward
calls** from `sim.schedule` and `sim.airside` (`03-module-map.md`). Nothing calls
upward; `sim.baggage` and `sim.delay` use the queries and the event bus only.

`Inject` requires `at` to be an existing node of kind `Source`, and `count > 0`.
An unknown node, a node of any other kind, or a non-positive count is a
**programmer error** and throws (`07-conventions.md`) — it is never clamped and
never silently dropped, because a swallowed injection loses passengers and the
head-count conservation test would then be asserting nothing.

`Absorb(sink, flight)` boards the flight's `Departing` passengers that are on
any `Gate` node, since gates are pooled (§9.6), into `sink`. It returns their
count, and it reports everyone else of the flight as `PassengersMissedFlight`
(§9.9). `sink` must be a `Sink` node; anything else throws.

**Exact rules (Q-033).**

- **Exceptions.** In `Inject`, an unknown `at` or one that is not a `Source`
  throws `ArgumentException`, and `count <= 0` throws
  `ArgumentOutOfRangeException`. In `Absorb`, an unknown `sink` or one that
  is not a `Sink` throws `ArgumentException`. An unknown `flight` is not an
  error, and it returns 0.
- **`EnteredNodeAt` of an injected cohort** is `N`, the number of `Tick`
  calls `sim.flow` has completed. Called during tick `t` by an earlier
  system (`sim.schedule`, `sim.airside`), that is `t`. Called between ticks,
  it is the next tick to run (`ISimHost.CurrentTick`). In both cases the
  cohort leaves its `Source` no earlier than the tick after `N` (§9.12).
- **`Absorb`, in this order.** It boards the flight's `Departing` cohorts on
  every `Gate` node, which leave the simulation. The flight's other
  `Departing` cohorts, on any non-`Gate` node, are the **missed** passengers.
  For each missed cohort in an open blocking episode, in ascending
  `CohortId`, it publishes `FlowUnblocked`. Then, if the missed count is
  above 0, it publishes **one** `PassengersMissedFlight` for the flight
  (never one per cohort), with `Count` = the missed total. Then it removes
  the missed cohorts from the simulation and returns the boarded count.
  `LastBlockedAt` is the node holding the most missed passengers, with
  ties broken by ascending `NodeId`, which is §9.7a's `MostHeldAt` rule. So a
  never-blocked cohort still names a real node.
- **Outside a tick.** `Absorb` publishes through `services.Events` before
  it changes any state. Outside phases 1–3, that first `Publish` throws
  `InvalidOperationException` (`08` §8.6), and nothing has changed. An
  `Absorb` with nothing to publish is not detected outside a tick. Callers
  call it only from their own `Tick`. The envelope's `Source` is the
  calling system, since that is whose code runs (`08` §8.6), while the
  event's owner in `10` §10.6 stays `sim.flow`. `Inject` publishes nothing,
  and it may be called between ticks, which tests use to seed a fixture.

`SetPromoted` may be called at any tick and, by §9.1, changes no hashed state.
`determinism_promotion` asserts exactly this.

### 9.7a Outstanding passengers (D6) — LOW CONFIDENCE

```
readonly struct OutstandingPassengers {
  FlightId Flight
  int32    Count          // > 0
  NodeId   MostHeldAt     // the node holding most of them; ties by ascending NodeId
}
```

`TryGetOutstanding(flight)` counts the flight's passengers that are still in
the terminal and have not reached a gate. These are the passengers in cohorts
with `Key.Flight == flight` and `Key.Direction == Departing` on any node
whose `NodeKind` is not `Gate`. It returns false when that count is 0. Only
`Departing` counts at Phase 1; `Transferring` is added when transfers exist,
by amendment. `MostHeldAt` is the node holding the largest share of those
passengers, with ties broken by ascending `NodeId`.

- It is a **query**: read-only, derived from hashed state, and not itself
  hashed (§9.10). It consumes no RNG and is unaffected by promotion (§9.1).
- Cost: O(the flight's cohorts), served from the per-flight index §9.10
  already anticipates. It never scans all nodes or all cohorts, and it does
  not allocate.
- Its caller is `sim.airside`'s boarding hold
  (`12-interfaces-airside.md` §12.8). `sim.airside` already calls downward
  into `sim.flow` (`Absorb`), so no new dependency edge is created.

> **LOW CONFIDENCE — the smallest addition D6 needed.** `sim.flow` already
> knows each passenger's flight (`CohortKey.Flight`), and
> `PopulationForFlight` counts them. What it could not say is how many are
> still *upstream of the gate*, or where they are. Without that, a departure
> cannot know whom it is waiting for. That is this one query and nothing
> else: no per-passenger identity, no new state, no new event from
> `sim.flow`. Blaming the node that holds the *most* outstanding passengers
> (rather than, say, the node of the last passenger in FIFO order, which
> `sim.flow` cannot define across nodes) is the Architect's choice under D6.
> It usually names the security queue, which is the lever the player has.
> Flagged for the owner with D6.

---

### 9.7b Lane state (Q-010) — LOW CONFIDENCE

HUMAN DECISION — owner (delegated), 2026-09-23, consequence of D5,
reversible. A lane control the player cannot see the state of is not a
usable control.

```
readonly struct LaneState { int32 ServerCount; int32 ServersOpen }
```

`TryGetLaneState(node)` returns the node's current `ServerCount` and
`ServersOpen`, reflecting every command applied so far. It returns false for
an unknown node or one that is not a `Queue`. It is read-only and O(1). It is
not itself hashed: both fields are already in node runtime state (§9.10). It
consumes no RNG. Its callers are `app.render` (lane pips, `15` §15.5) and
`app.ui`'s lane sink (`17` §17.5). It deliberately exposes no service rate,
capacity or wait figures; `PredictedWaitMinutes` already covers the wait.

> **LOW CONFIDENCE — the shape.** Two integers are the least the lane click
> needs: the base for ±1, the clamp bound, and whether the node is a lane at
> all. If later UI needs more of `QueueConfig`, it widens by amendment. It is
> not to be replaced by returning `QueueConfig` itself, which would publish
> content-derived rates as a presentation contract.

## 9.8 Commands consumed

Declared here, defined as `CommandKind` values in `sim.core` (§8.7).

| Command | Payload | Effect |
|---|---|---|
| `SetServersOpen` | `NodeId`, `int32 count` — byte layout `08` §8.7 | Clamped to `[0, ServerCount]`; takes effect at the next tick boundary. Backs T-023. |

`sim.flow` registers its `ICommandHandler` for `SetServersOpen`
(`08` §8.7) in `FlowFactory.CreateSystem` (§9.11). `Validate`: a length
other than 8 is `MalformedPayload`; an unknown `NodeId` is
`MalformedPayload`; a node that is not a `Queue` is `NotPermitted`. Any
`count` is admitted, because the clamp happens at `Apply`. `Apply` sets
`ServersOpen` to the clamped value.

Staffing may later constrain `ServersOpen`; that arrives from `sim.staff` through
the same field, and this table grows by amendment only.

---

## 9.9 Events emitted

Full field lists in `10-events.md`.

| Event | When |
|---|---|
| `QueueThresholdExceeded` | Predicted wait crosses a content-declared threshold upward |
| `QueueThresholdCleared` | It crosses back down, with hysteresis |
| `FlowBlocked` | A cohort is held by spillback, once per episode |
| `FlowUnblocked` | That episode ends |
| `PassengersArrivedAtGate` | A cohort reaches its `Gate` node |
| `PassengersMissedFlight` | Passengers still upstream at gate close |

`PassengersMissedFlight` carries a real node, so `sim.delay` never has to
guess (`06-delay-attribution.md` rule 3). That node is the one holding most
of the missed passengers (§9.7 "Exact rules", Q-033). It replaces the earlier
"the node at which the cohort was last blocked", which would have needed
per-cohort history that `PassengerCohort` does not hold.

---

## 9.10 State, hashing and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9):

1. Node runtime state in ascending `NodeId`: `ServersOpen`, `ServiceCredit`,
   population, blocked flag, threshold flag. The blocked flag is true if and
   only if a cohort on the node is in an open blocking episode. The threshold
   flag is true between a `QueueThresholdExceeded` and its `Cleared`, and it
   is always false on a non-`Queue` node (§9.12, Q-032). Each flag is fed as
   a `bool`.
2. Cohorts in ascending `CohortId`: every field of `PassengerCohort`.
3. The `sim.flow` RNG stream states, excluding `flow.presentation`.

Not hashed, because derived: predicted waits, agent views, per-flight population
indexes, `TryGetOutstanding` and `TryGetLaneState` results, any cached
routing result.

Budget: **2.5 ms/tick at max tier** (`03-module-map.md`). The shape that budget
demands, stated so it is not discovered late:

- Per-tick work is O(nodes + cohorts), never O(passengers). A loop over
  individuals anywhere in the update path is a review rejection.
- Cohort count is bounded by mandatory merging (§9.3). The budget test asserts a
  ceiling on live cohorts as well as on time, because a passing time with an
  unbounded cohort count only means the fixture was short. The ceiling's
  value is **fixture sizing**, not balance and not a sim constant (Q-033).
  The Test Author sets it per fixture, as for `11` §11.10, and states its
  derivation in the test.
- No allocation in the update path (`07-conventions.md`). Cohort storage is a
  pooled, index-stable structure; split and merge reuse slots.

---

## 9.11 Construction (Q-009)

```
interface IFlowGraphLoader {
  FlowGraph Load(ReadOnlySpan<byte> file, string sourceName, IWorldSystem world)   // parse and validate
}

FlowFactory.CreateGraphLoader() -> IFlowGraphLoader
FlowFactory.CreateSystem(in SystemServices services, in FlowGraph graph,
                         IWorldSystem world) -> IFlowSystem
```

`FlowGraph` is **opaque outside `sim.flow`**. Its file format is pinned
below (Q-032). It carries **node behaviour only**, over `sim.world`'s nodes (Q-012). For each
node that is its `NodeKind`, and for a `Queue` node its `ServerCount`,
initial `ServersOpen`, and the `ContentId` of its queue profile. The profile
holds the service rate, capacity, threshold, hysteresis and delay category
(§9.4, `10` §10.6), resolved through `services.Content`. Topology and lengths
are **not** in it; they come from `world`. Load-time validation, each a hard
failure:

- every `world.Nodes()` entry has exactly one node definition, and no
  definition names an unknown node;
- every `Source` can reach at least one `Gate`;
- a `Gate` has at least one outbound edge to a `Sink`, and a `Sink` has no
  outbound edge.

### File format (Q-032)

Binding. It replaces the earlier "the worker's choice". The syntax is that
of `18` §18.2 "File format": `08` §8.11's strict JSON subset, with the same
rules, a hand-written parser inside `sim.flow`, and no package. The exact
shape is:

```
{
  "schema_version": 1,
  "nodes": [
    { "id": <node id>, "kind": "source" | "corridor" | "hall" | "gate" | "sink" },
    { "id": <node id>, "kind": "queue", "server_count": <int32 ≥ 1>,
      "servers_open": <int32, 0 ≤ v ≤ server_count>, "queue_profile": "<ContentId>" }
  ]
}
```

- A `queue` node has exactly those five keys. Every other kind has exactly
  `id` and `kind`. An extra or missing key is a load failure.
- `nodes` may be in any order. Node ids follow `18` §18.2's integer rule.
- `queue_profile` names a `QueueProfileDefinition`. It is resolved in
  `CreateSystem` through `services.Content`, not in `Load`.
- The Phase 0 fixture over `tests/fixtures/world/phase0-landside.json` is the
  Test Author's `tests/fixtures/flow/phase0-landside.flow.json`.

**Failures.** `Load` throws `FormatException` for every syntax, shape, range
or validation failure (`07` "Error handling"). The message starts with
`sourceName` followed by `": "`. A syntax or shape failure contains
`line <n>`. A validation failure contains the offending node id in decimal:

- a world node without a definition, or a definition of an unknown node:
  that node id;
- a duplicate definition: that node id;
- a `Source` that reaches no `Gate`: the `Source` id;
- a `Gate` without a `Sink` successor, or a `Sink` with an outbound edge:
  that node's id;
- `servers_open > server_count`: the node id and the field.

A `null` `sourceName` or `world` throws `ArgumentNullException`. In
`CreateSystem`, an unresolved `queue_profile` throws `FormatException` whose
message starts with `sim.flow: ` and contains the node id and the profile
id.

---

## 9.12 Exact tick semantics (Q-032)

Binding on the flow system's `Tick` at tick `t`. Where §9.4 to §9.6 read
loosely, this section governs.

**Order within `Tick`.**

1. **Snapshot.** Record every node's population and every `Queue` node's
   `PredictedWaitMinutes`, both from the state at the start of `Tick`. The
   §9.5 fullness test and the §9.6 routing costs read only this snapshot.
2. **Movement.** Nodes are processed in ascending `NodeId`. Only a cohort
   with `EnteredNodeAt < t` is eligible to leave its node or to be served at
   `t`, so **each passenger moves at most one node per tick**. A cohort that
   arrives at node `m` during `t`, whether `m` is above or below its old
   node, gets `EnteredNodeAt = t` and waits for `t + 1`. On a non-`Corridor`
   node, `DueAt = EnteredNodeAt`.
   - `Source` and `Hall`: every eligible cohort not at its destination tries
     to leave, in ascending `CohortId`.
   - `Corridor`: every cohort with `DueAt <= t` tries to leave, in ascending
     `CohortId`.
   - `Queue`: §9.4 with the exact arithmetic below. Eligible cohorts are
     served in FIFO order `(EnteredNodeAt, CohortId)`, and service stops at
     the first cohort whose target is full.
   - `Gate` and `Sink`: nothing leaves (§9.6, `Absorb`).

   To **leave**, the cohort, or its served part, takes the §9.6 route and
   moves **whole** into the target node `m`, unless `m` is full in the
   snapshot. There is no partial admission, so a node can end a tick above
   its capacity by at most one tick's inflow. A refused cohort stays where
   it is. A served part moves under a new `CohortId`, and the remainder
   keeps its id (§9.3).
3. **Merge** (§9.3), except that a cohort in an open blocking episode does
   not merge. The survivor is the lowest `CohortId`.
4. **Thresholds.** For each `Queue` node in ascending `NodeId`, compute
   `PredictedWaitMinutes` from the post-merge state and apply the threshold
   rule below.

**Queue arithmetic** (§9.4 steps 1 to 5). All operations are `Fx`, which
floors (`08` §8.3). The order of operations is fixed, so that
`2.5 pax/min` gives exactly `0.25` per tick:

- `capacityThisTick = Fx.Div(Fx.Mul(Fx.FromInt(ServersOpen × SIM_SECONDS_PER_TICK), rate), Fx.FromInt(60))`
- `serverTick       = Fx.Div(Fx.Mul(Fx.FromInt(SIM_SECONDS_PER_TICK), rate), Fx.FromInt(60))`
- `ServiceCredit += capacityThisTick`. Then `served = Fx.Floor(ServiceCredit)`
  and `ServiceCredit -= Fx.FromInt(served)`.
- Serve FIFO as above. Let `moved` be the number of passengers that actually
  left.
- **Cap.** If `moved < served`, the `served − moved` whole passengers of
  credit are **discarded**, and then
  `ServiceCredit = Fx.Min(ServiceCredit, serverTick)`. The shortfall can
  come from too few eligible passengers or from a full target. Otherwise
  there is no cap, which keeps a busy 2.5 pax/min lane at 2.5.
- `PassengerCohort.ServiceCredit` is always zero at Phase 0/1. The credit
  lives on the node (§9.10 item 1).

**Predicted wait** (§9.4). For a `Queue` node,
`capacityPerMinute = Fx.Mul(Fx.FromInt(ServersOpen), rate)` and
`PredictedWaitMinutes = Fx.Div(Fx.FromInt(population), Fx.Max(capacityPerMinute, EPSILON))`,
with `EPSILON = Fx.FromRatio(1, 1000)`. It is floored. For a population above
2 147 483 with no open server, it overflows and throws (`08` §8.3). This is a
fixture error. For any non-`Queue` node, it is `0`. An unknown node, passed to
`Population` or `PredictedWaitMinutes`, throws `ArgumentException`.

**Traversal and route cost** (§9.6).
`traversalTicks(node, cohort) = max(1, Fx.Ceil(Fx.Div(Fx.FromInt(LengthMetres), Fx.Mul(walkSpeed, Fx.FromInt(SIM_SECONDS_PER_TICK)))))`.
`Div` floors first, then `Ceil`. Only a `Corridor` applies it as a delay
(`DueAt = EnteredNodeAt + traversalTicks`). In the route cost, every node on
`PathVia(e, g)` of any kind, the destination `g` included, contributes its
`traversalTicks`, which is at least 1, matching one node per tick. The cost
is an `Fx`:
`Σ Fx.FromInt(traversalTicks) + Σ Fx.Mul(snapshotWait, Fx.FromInt(TICKS_PER_SIM_MINUTE))`
over those nodes, the waits being those of the `Queue` nodes. It is compared
by `Raw`, with §9.6's tie-break.

**Thresholds** (§9.9, `10` §10.3 rule 4). For each `Queue` node, with `T` =
`ThresholdWaitMinutes`, `h` = `HysteresisMinutes` and `w` = the step-4 wait:

- if the flag is clear and `w > T`, set it and emit `QueueThresholdExceeded`;
- if the flag is set and `w < T − h`, clear it and emit `QueueThresholdCleared`.

Each event carries `w` and the node's current `ServersOpen` and
`ServerCount`. The flag starts clear. Strict inequalities on both sides mean
a constant `w` never flaps, `h = 0` included.

**Blocking episodes** (§9.5, `10` §10.3 rule 2).

- A cohort's release is **refused** when its target is full. At the first
  refusal, it emits `FlowBlocked { Cohort, Held = the node it is on,
  BlockedBy = the target node }`. The target is the **immediate** next node,
  never a node further downstream. A cohort waiting on a `Corridor` past its
  `DueAt` has `Held` = that corridor.
- While refused, it emits nothing further. If routing picks a different
  target that is also full, it emits `FlowUnblocked` for the old pair, then
  `FlowBlocked` for the new pair, both at that tick.
- On release it emits `FlowUnblocked` with the same `Held` and `BlockedBy`,
  at the release tick, before it moves.
- A cohort removed while its episode is open (missed flight, §9.9) emits
  `FlowUnblocked` first, at the same tick.

**Event order within `Tick`.** First the movement-step events, in
processing order: node by ascending `NodeId`, then the cohort order above,
and for each cohort `FlowUnblocked`, then `FlowBlocked`, then
`PassengersArrivedAtGate`. `PassengersArrivedAtGate` is emitted once per
cohort that enters a `Gate`, with that cohort's count. Then the threshold
events in ascending `NodeId`. `PassengersMissedFlight` comes from `Absorb`,
not from `Tick`.

> **LOW CONFIDENCE — Q-032 choices that shape behaviour.** `EPSILON` sets the
> wait a closed lane shows (10 passengers read as 10 000 minutes). The cap of
> one `serverTick` sets the largest burst after an idle spell. Unlimited
> `Hall` capacity and head-of-line blocking in a `Queue` are also Architect
> choices. None of them is a content balance value, but each is visible to
> the player. Flagged for the owner.

