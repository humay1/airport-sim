# T-010 — Cohort→agent promotion + demotion, outcome-neutral

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-007 |
| Spec source | `spec/01-architecture.md` "Hierarchical simulation" rules 1–4; `spec/09-interfaces-flow.md` §9.1, §9.7 |
| Blocked by | — |

## Writable paths

```
src/sim/flow/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory exclusively; the path guard already blocks a worker grant there. Dropped.

Same directory as T-007; do not release concurrently with T-007 or T-011.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/09-interfaces-flow.md`

## Interface to implement

```
interface IFlowSystem : ISimSystem {
  int32  Population(NodeId node)
  Fx     PredictedWaitMinutes(NodeId node)
  int32  PopulationForFlight(FlightId flight, FlowDirection direction)
  IReadOnlyList<CohortId> CohortsAt(NodeId node)          // ascending CohortId
  bool   TryGetCohort(CohortId id, out PassengerCohort cohort)

  CohortId Inject(in CohortKey key, int32 count, NodeId at)   // sim.schedule, sim.airside
  int32    Absorb(NodeId sink, FlightId flight)               // boarding / exit

  void SetPromoted(NodeId node, bool promoted)
  IReadOnlyList<AgentView> AgentsAt(NodeId node)              // empty if not promoted
}

readonly struct AgentView {
  PassengerRef Ref
  NodeId       Node
  Fx           ProgressAlongEdge            // 0..1
}

readonly struct PassengerRef { CohortId Cohort; int32 Index }
```

Binding rule (`09-interfaces-flow.md` §9.1): the cohort is the only
authoritative state; an agent is a derived view and can never feed a value
back into it. Agent-level detail is derived from `flow.presentation`, an RNG
stream **excluded from the state hash**, readable by no other system.
`PassengerRef = (CohortId, indexWithinCohort)` is derived, never drawn — no
RNG consumption for naming a passenger. `SetPromoted` may be called any tick
and, by construction, changes no hashed state.

## Events

Emitted: none new (promotion/demotion emits nothing per §9.1 — it is
outcome-neutral by construction, not by discipline)
Consumed: none

## Tests to pass

```
tests/sim/flow/**
```

Written by the Test Author. Expect `determinism_promotion`-shaped tests: run
one sim-day twice, once with a node promoted throughout and once never
promoted, and assert identical `PassengerCohort` state and identical world
hash at every checkpoint. Also expect a test that `AgentsAt` returns empty
for a non-promoted node and a stable, index-derived (not drawn) set of
`AgentView`s for a promoted one. **Do not edit them.**

## Performance budget

Still within `sim.flow`'s `2.5` ms/tick total (`spec/03-module-map.md`).
Promotion/demotion bookkeeping itself must not allocate on the hot path and
must not appear in the hashed-state cost, since `AgentView`/promoted-set
state is explicitly not hashed (§9.10).

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task is also what T-006's `promotion` harness subcommand needs to stop
being a no-op; coordinate with whoever picks up T-006 if it lands first.

Unaffected by the Q-012 routing rewrite (`09-interfaces-flow.md` §9.6,
T-012's `sim.world`): promotion/demotion and `AgentsAt` operate on a node's
already-resident cohorts and never touch routing or corridor traversal.
