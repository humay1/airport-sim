# T-007 — Statistical flow nodes: queue with throughput model

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-003, T-012, T-026 |
| Spec source | `spec/09-interfaces-flow.md` §9.1–§9.6, §9.10, §9.11 (answers, jointly with `18-interfaces-world.md`, Q-012) |
| Blocked by | — |

## Writable paths

```
src/sim/flow/**
AirportSim.sln
```

**Correction (Q-021):** `tests/**` is the Test Author's territory
exclusively; the path guard already blocks a worker grant there. The
earlier grant of `tests/sim/flow/**` is dropped.

**First task of a new module (`07` L8, Q-013):** this task creates
`src/sim/flow/AirportSim.Sim.Flow.csproj` and
`tests/sim/flow/AirportSim.Sim.Flow.Tests.csproj` (byte for byte per `07`
L2/L3) and adds both to `AirportSim.sln`. Never release this task
concurrently with any other "first task of a new module" (T-008, T-012,
T-020, T-021, T-022, T-024, T-029, T-031) — concurrent `.sln` edits
conflict (`07` L8).

Anything else is read-only. `sim.flow` depends on `sim.core` and `sim.world`
(`spec/03-module-map.md`). **`sim.world` now exists as a published interface**
(`spec/18-interfaces-world.md`, T-012) — this task depends on T-012 merged
and calls `IWorldSystem` downward for every walk-graph query. It never
computes a path itself; per-agent A* is a review rejection
(`01-architecture.md`).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/09-interfaces-flow.md`,
`spec/18-interfaces-world.md` §18.3 (the `IWorldSystem` queries this module
calls)

## Interface to implement

```
struct CohortId  { uint64 Value }        // from IIdAllocator, owner sim.flow

enum NodeKind { Source, Corridor, Hall, Queue, Gate, Sink }
enum FlowDirection { Departing, Arriving, Transferring }

readonly struct CohortKey {
  FlightId      Flight
  FlowDirection Direction
  ContentId     PaxProfile
  bool          HasHoldBaggage
  bool          RequiresAssistance
}

readonly struct PassengerCohort {
  CohortId   Id
  CohortKey  Key
  NodeId     Node
  int32      Count
  Tick       EnteredNodeAt
  Tick       DueAt
  Fx         ServiceCredit
}

readonly struct QueueConfig {
  int32 ServerCount
  int32 ServersOpen
  Fx    ServiceRatePerServer
  int32 CapacityStanding
}
```

Queue throughput algorithm, binding, exactly as `09-interfaces-flow.md` §9.4:

1. `capacityThisTick = ServersOpen * ServiceRatePerServer * (SIM_SECONDS_PER_TICK / 60)`,
   computed with `Fx.Mul` and `Fx.FromRatio` only.
2. `ServiceCredit += capacityThisTick`; `served = Fx.Floor(ServiceCredit)`;
   `ServiceCredit -= Fx.FromInt(served)`.
3. Serve `min(served, population)`, FIFO by `(EnteredNodeAt, CohortId)`.
   Per-cohort service must not depend on cohort size.
4. Served passengers move per §9.6.
5. Unused credit capped at one server-tick's worth.

Cohort identity rules (§9.3): merge mandatory at end of update for cohorts on
the same node with equal `CohortKey` in every field (sum `Count`, earliest
`EnteredNodeAt`, sum `ServiceCredit`); split keeps the original `CohortId` on
the remainder. Iteration is always ascending `CohortId` / ascending `NodeId`.

Capacity/spillback (§9.5): population above `CapacityStanding` stops the
upstream edge releasing into the node; evaluated in a single pass in `NodeId`
order using start-of-tick population.

**Corridors and routing** (§9.6, jointly answered with `18-interfaces-world.md`
by Q-012), binding, copied not paraphrased:

- The walkable graph, its lengths and its routes are owned by `sim.world`
  (T-012). `sim.flow` **reads** them through `IWorldSystem` and never
  computes a path.
- A corridor traversal is a delay line: on entry the cohort's `DueAt` is set
  to `EnteredNodeAt + traversalTicks`, where `traversalTicks = max(1,
  ceil(LengthMetres / (walk_speed_mps × SIM_SECONDS_PER_TICK)))` in `Fx`.
  `LengthMetres` comes from `IWorldSystem.LengthMetres`, `walk_speed_mps`
  from the cohort's pax profile. On or after `DueAt` the cohort releases to
  the next node, subject to §9.5. A `Source`/`Hall` node that is not the
  cohort's destination releases on the tick after entry.
- **Destinations.** A `Departing` cohort's destination set is every `Gate`
  node it can reach (`IWorldSystem.CanReach`). Gates are pooled at
  Phase 0/1 (`18` §18.5) — the Phase 0/1 fixture declares exactly one. A
  cohort on a `Gate` node stays there until `Absorb` or missed-flight
  handling (§9.9).
- **Routing.** When a cohort is released from a node, it takes the outbound
  edge chosen by a declared, deterministic rule: over every pair `(e, g)`
  with `e` in `IWorldSystem.OutEdges(node)`, `g` in the destination set and
  `IWorldSystem.CanReachVia(e, g)`, `cost = Σ traversalTicks of the nodes on
  IWorldSystem.PathVia(e, g)` `+ Σ PredictedWaitMinutes × TICKS_PER_SIM_MINUTE`
  of the `Queue` nodes on that path, waits read at the start of the tick.
  Lowest cost wins, ties broken by ascending `g` `NodeId`, then ascending
  `EdgeId`. No randomness — "two security halls" is two routes to the gate,
  and this rule chooses between them; passenger "choice" as a behaviour
  model needs a spec amendment, not a local invention.
- Cost: O(out-degree × gates × path length) per released cohort. If the
  budget test (§9.10) shows this needs caching, that is this task's problem
  to solve inside `sim.flow`, not `sim.world`'s to precompute — `sim.world`
  stays load-time-only.

## Construction (`09` §9.11, Q-009)

```
interface IFlowGraphLoader {
  FlowGraph Load(ReadOnlySpan<byte> file, string sourceName, IWorldSystem world)
}

FlowFactory.CreateGraphLoader() -> IFlowGraphLoader
FlowFactory.CreateSystem(in SystemServices services, in FlowGraph graph,
                         IWorldSystem world) -> IFlowSystem
```

`FlowGraph` is **opaque outside `sim.flow`** and carries **node behaviour
only**, over `sim.world`'s nodes: for each node, its `NodeKind`, and for a
`Queue` node its `ServerCount`, initial `ServersOpen`, and the `ContentId`
of its queue profile (resolved through `services.Content` at construction).
Topology and lengths are **not** in it — they come from `world`. File format
is this task's own choice, following the posture of `12` §12.13. `sim.flow`
is not constructed without `sim.world`.

## Events

Emitted: `QueueThresholdExceeded`, `QueueThresholdCleared`, `FlowBlocked`,
`FlowUnblocked` (`spec/10-events.md` §10.6 "From sim.flow"; fields per that
table)
Consumed: none in this task (`Inject`/`Absorb` land with T-010/T-023 callers;
this task builds the node model those calls act on)

## Tests to pass

```
tests/sim/flow/**
```

Written by the Test Author. Expect: throughput-model unit tests (2.5 pax/min
serves 2 or 3 passengers correctly across ticks via `ServiceCredit`), a
head-count-conservation property test across merge/split, a FIFO-serving
order test independent of cohort size, a spillback test with start-of-tick
population semantics, `FlowBlocked`/`FlowUnblocked` pairing tests, and a
routing test against `tests/fixtures/world/phase0-landside.*` (T-012, §18.6)
covering the "two alternative security queues" case with no randomness.
**Do not edit them.**

## Performance budget

`2.5` ms/tick at max tier (`spec/03-module-map.md`, `spec/09-interfaces-flow.md`
§9.10). Per-tick work O(nodes + cohorts), never O(passengers); no allocation
in the update path; cohort storage pooled and index-stable.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task was previously `BLOCKED` on Q-012 (`sim.flow` had no way to route
without `sim.world`). Q-012 is now answered by `spec/18-interfaces-world.md`
(T-012) plus this file's §9.6 rewrite; T-007 is **not** blocked and does not
build any part of `sim.world` itself — it calls `IWorldSystem` downward.

`NodeId` and `EdgeId` are now **`sim.core` types** (`09` §9.2, `18` §18.2:
"because events carry them"), authored in `src/sim/core/**` by T-026
(extended for this purpose), not declared locally here. Reference them from
`sim.core`. `CohortId` stays `sim.flow`'s own type, unaffected.

`09-interfaces-flow.md` §9.6 flags deterministic least-cost routing as
**LOW CONFIDENCE** (may cause visible passenger-choice oddities). Build to
spec as written; do not substitute a proportional-split heuristic without a
spec amendment.
