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
- **Only `Departing` cohorts exist at Phase 0/1 (Q-040).** `Inject` with
  `Key.Direction ≠ Departing` throws `ArgumentException` (§9.7). There are
  no arriving or transferring passengers at Phase 0/1 (`11` §11.1, `12`
  "Arriving passengers"), and this section defines no destination for
  them. Admitting them is an amendment that must also define their
  destinations. Queries that take a `FlowDirection`, such as
  `PopulationForFlight`, still accept every value and return 0 for a
  direction with no cohorts.
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

**Routing cache (Q-036).** `sim.flow` may cache routing results, but only
under all six rules below. A cache that breaks one is a review rejection,
even when every budget test passes. Adding a cache is optional. It is done
only against a failing budget measurement (`07` "Performance").

1. **Observably identical.** The cache is an implementation detail only
   because nothing can tell it from the uncached rule. For every released
   cohort at every tick, the chosen edge equals what the rule above and
   §9.12 "Traversal and route cost" choose: the lowest cost by `Raw`, then
   ascending `g` `NodeId`, then ascending `EdgeId`. What must match is
   each pair's cost `Raw` and the tie-break. The grouping of the sums does
   not need to match. Every term is non-negative, `Fx.Add` is exact, and
   `Fx.Mul` by `Fx.FromInt` of an integer is exact (`08` §8.3). So any
   order or grouping of the additions, and factoring
   `Fx.FromInt(TICKS_PER_SIM_MINUTE)` out of the wait sum, gives the same
   `Raw`. It also overflows exactly when the total does. The real
   rounding hazards are in the **per-node terms**, which floor and ceil:
   - each node's `traversalTicks` is §9.12's value for that node's own
     `LengthMetres`. It is never derived from a summed length, because
     `Ceil` of a sum differs from a sum of `Ceil`s;
   - each `Queue` node's wait is that node's own snapshot
     `PredictedWaitMinutes` (§9.12 "Predicted wait"). It is never derived
     from pooled populations or capacities, because `Div` floors.
2. **Static part: may live for the run.** Data may be kept from
   construction onwards only if it depends on nothing but these three
   sources, all fixed after construction:
   - the walk graph, through any of `IWorldSystem`'s load-time answers
     (`Nodes`, `OutEdges`, `EdgeTo`, `CanReach`, `CanReachVia`, `PathVia`,
     `LengthMetres`; `18` §18.3);
   - the `FlowGraph` node behaviour given to `CreateSystem` (§9.11), for
     which nodes are `Queue`, `Gate` and so on;
   - the loaded pax profiles' `walk_speed_mps` (content, `04`).
   That covers `CanReachVia(e, g)`, the `PathVia(e, g)` node list, which of
   its nodes are `Queue` nodes, and each path's per-node `traversalTicks`,
   or their sum, per distinct walk speed. Nothing that changes at runtime
   is static: not `ServersOpen`, not a population, not a wait. The walk
   graph is fixed at Phase 0/1 (`18` §18.3, §18.5). When construction
   arrives, a construction change is the invalidation point, by amendment.
3. **Wait-dependent part: never outlives its tick.** Wait terms, cost
   totals and chosen edges read §9.12's start-of-tick snapshot. They may be
   memoised **within one tick only**. That is how cohorts released
   together from one node, as after a show-up injection, share one
   computation. The memo is discarded before the next tick's snapshot.
   Lane changes (`SetServersOpen`, §9.8) change waits, so they are covered
   by this rule, with no invalidation of their own.
   **The key.** A memo entry may serve a cohort only if it was computed
   for the same node, the same walk speed, and the same value of **every
   cohort field that determines the destination set**. At Phase 0/1 that
   is no field at all, so the key is `(node, walk speed)`:
   - every cohort is `Departing`, because `Inject` rejects the other
     directions ("Only `Departing` cohorts exist", above, Q-040);
   - a `Departing` cohort's effective set depends only on the node. Only
     pairs with `CanReachVia(e, g)` from the current node's out-edges
     count, and anything reachable from the current node was also
     reachable earlier on the cohort's path.
   The amendment that makes the destination set depend on anything more
   must extend the key **and** add a differing-set case to the test below.
   That covers admitting another direction, gate assignment (`18` §18.5),
   and any per-cohort destination.
4. **Not state.** The cache is not hashed (§9.10) and not saved. A system
   restored from a save, or replayed from seed, starts with an empty
   per-tick memo. It rebuilds the static part from rule 2's three sources
   only. It must then choose exactly the routes the original run chose.
5. **No allocation in the update path** (§9.10, `07`). Static tables are
   sized at construction, and filled then or lazily into that storage. The
   per-tick memo is preallocated and reset each tick, never grown.
6. **No other inputs.** A cached result never depends on promotion state
   (§9.7), presentation, dictionary or hash-set iteration order, object
   identity, or the order in which entries were filled.

Required test, when a cache is added. Owner: the Test Author of whichever
`sim.flow` task adds it. A cache does not merge without this test:
`test_flow_routing_cache_matches_uncached_reference`.

**The reference.** The test contains a **reference model**, a lockstep,
uncached implementation of §9.12's whole `Tick` (snapshot, movement, merge,
thresholds), and of this section's routing rule, written from this spec.
It carries its own state from tick 0: cohorts, node `ServiceCredit`,
blocking episodes and threshold flags. Every one of its inputs is one that
the test itself authors or reads from a public interface. Nothing comes
from `FlowGraph`'s members, which are internal (§9.11), or from
`src/sim/flow`:
- **The graph.** Node kinds, `server_count`, initial `servers_open` and
  each `Queue`'s profile id come from the flow-graph JSON the test writes
  (§9.11 "File format"). The same bytes go to `IFlowGraphLoader`.
- **Content.** The queue profile definitions the test builds give each
  `Queue`'s `service_rate_per_server_per_minute`, `capacity_standing`,
  `threshold_wait_minutes` and `hysteresis_minutes` (`04`). The pax
  profiles give `walk_speed_mps`. Non-`Queue` nodes are never full (§9.5).
- **The walk graph.** `OutEdges`, `EdgeTo`, `CanReachVia`, `PathVia` and
  `LengthMetres` come from `IWorldSystem`, over the test's walk-graph
  fixture.
- **The script.** Every `Inject`, `Absorb` and `SetServersOpen` the test
  makes is applied to the reference at the same tick and in the same
  order.

So the expected state after every tick is fully determined by §9.12, the
fixture and the script. Two correct references agree. The fixture is
`sim.flow`-local (§9.10), pools several `Gate` nodes, and uses only
`Departing` cohorts (Q-040).

**Assertion.** After every tick of a scripted sim-day, the test compares
the system with the reference. It uses only `IFlowSystem` queries between
`Step`s and the tick's events:
- for every node and every `CohortKey`, the head count on that node, which
  is the sum of `Count` over `CohortsAt(node)` read through `TryGetCohort`,
  grouped by `Key`;
- `Population` for every node, and `PredictedWaitMinutes` by `Raw` for
  every `Queue` node;
- the tick's `FlowBlocked` and `FlowUnblocked` events, as a multiset of
  `(event kind, Held, BlockedBy, the cohort's Key)`.
`CohortId`s are not compared. The per-key head count on each node is what
a wrong route changes, whether it ends in a move, a refusal or a different
merge. A refusal the reference does not make, or a stale target, shows up
as a head count on the wrong node or as a `BlockedBy` mismatch.

The script must contain each of these cases at least once. Each one is
there so that a cache that is wrong in that way fails:
- **Walk speed.** Two or more pax profiles with distinct `walk_speed_mps`
  are released from the **same node in the same tick**, where the
  different traversal terms lead them to **different** edges. This fails a
  memo that leaves walk speed out of the key.
- **Gate tie-break.** Two gates reachable at exactly equal cost `Raw`, so
  that ascending `g` `NodeId` decides. The lower-id gate is reachable only
  through the **higher**-`EdgeId` out-edge. A search that walks edges in
  ascending id (`18` §18.3's order) and keeps the first tie it meets then
  picks the wrong gate.
- **Edge tie-break.** Two out-edges reaching the same gate at exactly equal
  cost `Raw`, so that ascending `EdgeId` decides.
- **Lane changes.** `SetServersOpen` on alternative security queues flips
  the choice between ticks. This fails a memo that outlives its tick.
- **Blocked re-route.** A cohort is refused by a full target (§9.12
  "Blocking episodes"). A later tick's routing picks a different target,
  which is either not full, so the cohort moves, or also full, so the
  `Unblocked`/`Blocked` pair names it. This fails a memo that keeps a
  stale target.
- **Show-up spike.** Several cohorts released from one `Source` in one
  tick.
- **Restart.** The run is restarted partway through the day, and the
  assertion keeps holding afterwards.

**What the restart case proves.** Until `sim.save` exists, a restart is a
replay from seed from tick 0 (`19` §19.5, owner-approved interim). That
replay fills the cache in the same order as the original run. So it proves
only that routing is reproducible. It cannot show independence from fill
order or history (rules 4 and 6). What guards those until then:
- **The per-tick assertion.** Any wrong choice changes a per-key head
  count or a `BlockedBy`, so it fails on any tick of the scripted day,
  whatever its cause.
- **Review.** A history-dependent cache can be right on every scripted
  tick and wrong on a state the script never reaches. Only review catches
  that until the restore arm exists.

Once `sim.save` exists, the restart case restores from a mid-day save into
a fresh system with a cold cache, and the reference continues from its own
state.

The existing `sim.flow` tests and budget tests must also still pass.

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
  throws `ArgumentException`, and so does `key.Direction ≠ Departing` at
  Phase 0/1 (Q-040, §9.6). The checks run in that order, and nothing
  changes on a throw. `count <= 0` throws
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

**Promotion rules (Q-033).**

- **Every node is promotable.** `SetPromoted` or `AgentsAt` on an unknown
  node throws `ArgumentException`. There is no other failure, and
  promoting a promoted node, or demoting a demoted one, is a no-op.
- **`AgentsAt(node)`** on a promoted node returns **exactly one view per
  passenger**, so its count equals `Population(node)`. It is sorted by
  `(Cohort, Index)`, and `Index` runs from 0 to `Count − 1` in each cohort.
  On a node that is not promoted, it is empty.
- **When.** `SetPromoted` may be called at any time, inside another
  system's `Tick` included, because it changes no hashed state. Its only
  production caller calls it between `Step`s (`15` §15.7).
- **Allocation.** `SetPromoted` never allocates. `AgentsAt` allocates
  nothing after warm-up. Its list is valid until the next `AgentsAt` or
  `Tick` call, and its buffer grows only when a node's population exceeds
  every earlier one.

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
routing result (§9.6 "Routing cache", Q-036).

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
- **Gate count is fixture sizing too (Q-037).** A `sim.flow`-local stress
  or budget fixture builds its own walk graph and does not load
  `tests/fixtures/world/phase0-landside.json`. It may declare several
  `Gate` nodes. They are pooled
  (§9.6), so every departing cohort routes over all the reachable ones,
  which is the current rule's worst case. The Test Author states the count
  and its derivation in the test. `18` §18.5's single `Gate` binds only
  that shared file and the fixtures that load it (`18` §18.6).
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

