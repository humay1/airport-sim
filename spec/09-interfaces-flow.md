# 09 — Public interfaces: `sim.flow`

Implements the `sim.flow` row of `03-module-map.md`: passenger cohorts, queue
nodes, promotion/demotion, corridors. Depends on `sim.core` and `sim.world`, and
on nothing else. Notation and binding rules are as in `08-interfaces-core.md`.

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
struct NodeId    { uint32 Value }        // stable for the life of the save
struct CohortId  { uint64 Value }        // from IIdAllocator, owner sim.flow
struct EdgeId    { uint32 Value }

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
total head count is conserved across every operation except `Source` and `Sink`.

---

## 9.3 Cohort identity, splitting and merging

A cohort exists because its members are interchangeable. Therefore:

- Two cohorts **may merge** only if they are on the same node and their
  `CohortKey` compares equal in every field. Merge sums `Count`, takes the earlier
  `EnteredNodeAt` and sums `ServiceCredit`.
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
  backs the pressure up the graph.
- Spillback is evaluated in a single pass in `NodeId` order using the population
  **at the start of the tick**. Using in-tick populations would make results
  depend on evaluation order in a way that a later refactor would silently change.
- A cohort blocked by spillback emits `FlowBlocked` once per blocking episode, not
  once per tick (`10-events.md` §10.3), and emits `FlowUnblocked` when released.
  Those two events are what let `sim.delay` attribute landside delay to the true
  upstream constraint rather than to the door the passenger was standing at.

---

## 9.6 Corridors and routing

- The walkable graph, its distances and its flow fields are owned by `sim.world`.
  `sim.flow` **reads** them and never computes a path. Per-agent A* is a review
  rejection (`01-architecture.md`).
- A corridor traversal is a delay line: on entry the cohort's `DueAt` is set to
  `EnteredNodeAt + traversalTicks`, where
  `traversalTicks = ceil(distance / walkSpeed)` with `walkSpeed` from the
  passenger profile in content. On or after `DueAt` the cohort is released to the
  next node, subject to §9.5.
- Routing picks the outbound edge from the `sim.world` flow field for the cohort's
  current destination. Where several destinations are valid (two security halls,
  three gates for one flight), the choice is a **declared, deterministic rule**:
  lowest predicted total traversal-plus-wait, ties broken by ascending `NodeId`.
  No randomness. Passenger "choice" as a behaviour model is out of scope at Phase
  0 and needs a spec amendment, not a local invention.

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

## 9.8 Commands consumed

Declared here, defined as `CommandKind` values in `sim.core` (§8.7).

| Command | Payload | Effect |
|---|---|---|
| `SetServersOpen` | `NodeId`, `int32 count` | Clamped to `[0, ServerCount]`; takes effect at the next tick boundary. Backs T-023. |

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

`PassengersMissedFlight` carries the node at which the cohort was last blocked, so
`sim.delay` gets a real parent and never has to guess (`06-delay-attribution.md`
rule 3).

---

## 9.10 State, hashing and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9):

1. Node runtime state in ascending `NodeId`: `ServersOpen`, `ServiceCredit`,
   population, blocked flag.
2. Cohorts in ascending `CohortId`: every field of `PassengerCohort`.
3. The `sim.flow` RNG stream states, excluding `flow.presentation`.

Not hashed, because derived: predicted waits, agent views, per-flight population
indexes, `TryGetOutstanding` results, any cached routing result.

Budget: **2.5 ms/tick at max tier** (`03-module-map.md`). The shape that budget
demands, stated so it is not discovered late:

- Per-tick work is O(nodes + cohorts), never O(passengers). A loop over
  individuals anywhere in the update path is a review rejection.
- Cohort count is bounded by mandatory merging (§9.3). The budget test asserts a
  ceiling on live cohorts as well as on time, because a passing time with an
  unbounded cohort count only means the fixture was short.
- No allocation in the update path (`07-conventions.md`). Cohort storage is a
  pooled, index-stable structure; split and merge reuse slots.
