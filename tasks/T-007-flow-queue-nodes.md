# T-007 — Statistical flow nodes: queue with throughput model

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-003 |
| Spec source | `spec/09-interfaces-flow.md` §9.1–§9.6, §9.10 |
| Blocked by | — |

## Writable paths

```
src/sim/flow/**, tests/sim/flow/**
```

Anything else is read-only. `sim.flow` depends on `sim.core` and `sim.world`
(`spec/03-module-map.md`); `sim.world` does not exist yet in Phase 0, so this
task's routing/corridor code must depend only on the flow-field query shape
described in §9.6 conceptually — it may not implement `sim.world` itself. If
a concrete `sim.world` query interface is needed before it exists, stop and
file a question rather than build a stand-in inside `sim.flow`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`spec/09-interfaces-flow.md`

## Interface to implement

```
struct NodeId    { uint32 Value }
struct CohortId  { uint64 Value }
struct EdgeId    { uint32 Value }

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
population semantics, and `FlowBlocked`/`FlowUnblocked` pairing tests.
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

`09-interfaces-flow.md` §9.6 flags deterministic least-cost routing as
**LOW CONFIDENCE** (may cause visible passenger-choice oddities). Build to
spec as written; do not substitute a proportional-split heuristic without a
spec amendment.
