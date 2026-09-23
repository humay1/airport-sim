# T-012 — `sim.world`: fixed landside walk graph

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.world` |
| Assigned role | worker |
| Depends on | T-001, T-003 |
| Spec source | `spec/00-overview.md` build order; `spec/18-interfaces-world.md` (answers Q-012) |
| Blocked by | — |

## Why this task exists, and why it gates T-007

`sim.flow` (T-007) routes cohorts over a landside graph it does not own
(`09-interfaces-flow.md` §9.6). Until Q-012, nothing specified that graph, so
T-007 was unbuildable without inventing it. `spec/18-interfaces-world.md`
answers Q-012 with a fixed, load-time-only `sim.world` subset. **This task
must land before T-007** — T-007's own routing code compiles against
`IWorldSystem`.

## Writable paths

```
src/sim/world/**, tests/sim/world/**, tests/fixtures/world/**
```

`NodeId` and `EdgeId` are `sim.core` types (`18` §18.2: "because events carry
them") — this task **references** them, it does not declare them. They are
authored in `src/sim/core/**` by T-026 (extended for this purpose); do not
redeclare them locally.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/18-interfaces-world.md`,
`spec/09-interfaces-flow.md` §9.6 (its one consumer, for context only —
`sim.world` calls nothing in `sim.flow`)

## Interface to implement

```
readonly struct WalkNodeDef { NodeId Id; uint32 LengthMetres }
readonly struct WalkEdgeDef { EdgeId Id; NodeId From; NodeId To }

readonly struct WalkGraph {
  IReadOnlyList<WalkNodeDef> Nodes       // ascending NodeId
  IReadOnlyList<WalkEdgeDef> Edges       // ascending EdgeId
  uint64                     FixtureHash // FNV-1a-64 over the raw file bytes
}

interface IWalkGraphLoader {
  WalkGraph Load(ReadOnlySpan<byte> file, string sourceName)
}

interface IWorldSystem : ISimSystem {
  IReadOnlyList<NodeId> Nodes()                                  // ascending NodeId
  uint32                LengthMetres(NodeId node)
  IReadOnlyList<EdgeId> OutEdges(NodeId node)                    // ascending EdgeId
  NodeId                EdgeTo(EdgeId edge)
  bool                  CanReach(NodeId from, NodeId destination)
  bool                  CanReachVia(EdgeId firstEdge, NodeId destination)
  IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination)
      // shortest path starting with firstEdge: EdgeTo(firstEdge) ... destination, inclusive;
      // empty if !CanReachVia
}
```

Construction (`18` §18.4):

```
WorldFactory.CreateGraphLoader() -> IWalkGraphLoader
WorldFactory.CreateSystem(in SystemServices services, in WalkGraph graph) -> IWorldSystem
```

Binding, copied from `spec/18-interfaces-world.md`, not paraphrased:

- **Load-time validation** (§18.2): node/edge ids unique; every edge's
  `From`/`To` resolves to a declared node; no self-loops, no duplicate
  `(From, To)`. Each a hard failure naming the file and the offending id.
- **Routes** (§18.3): computed once at load, over every ordered pair of
  nodes. Cost is the sum of `LengthMetres` of nodes entered after the start,
  including the destination. Ties broken by the lexicographically smallest
  sequence of `EdgeId`s. No RNG, no dependence on file/edge-list order.
  Every query is read-only, allocates nothing, and a query naming an unknown
  id is a programmer error: throw.
- **State and hash** (§18.4): no runtime state; `Tick` does nothing.
  `ComputeStateHash()` feeds `WalkGraph.FixtureHash` only — an edited graph
  fails the determinism gate loudly, the same posture as `sim.schedule`
  (`11` §11.9).
- **No RNG.**
- **Registry position 1** (`08` §8.5), before every other Phase 0/1 system.
  `sim.flow` depends on `sim.world` and is not constructed without it.
- **What this module explicitly does not own** (§18.1): node behaviour
  (`NodeKind`, queue config — that is `sim.flow`'s `FlowGraph`); gate
  assignment (deferred to the owner, §18.5, pooled at Phase 0/1); construction,
  grids, rooms, flow-field recomputation (all deferred); anything airside
  (`sim.airside` keeps its own graph, `12` §12.1).

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/sim/world/**
```

Written by the Test Author, against `tests/fixtures/world/phase0-landside.*`
whose binding requirements are `18` §18.6 — shared by T-007's, T-011's and
T-023's flow fixtures and by the render layout (`15` §15.12): every schedule
`entry_node` reaches the single pooled `Gate` node through at least one
security `Queue` node; at least one place where two security queues are
alternative routes; at least one corridor node with `LengthMetres > 0`.
Expect at least:

- `test_walk_graph_rejects_unknown_edge_endpoint`
- `test_walk_graph_rejects_duplicate_edge`
- `test_route_is_shortest_by_length_ties_by_edge_sequence`
- `test_path_via_includes_endpoints_and_is_empty_when_unreachable`
- `test_routes_independent_of_file_order`
- `test_world_hash_is_fixture_hash_and_tick_consumes_no_rng`

**Do not edit them.** If a test contradicts `spec/18-interfaces-world.md`,
file an open question and stop.

## Performance budget

`0.10` ms/tick at max tier (`spec/03-module-map.md`, `18` §18.4). Spent by
callers' queries, not by `Tick`. Route tables are O(nodes² + edges × nodes)
in memory, computed once at load, off the tick path; at max tier (~200
landside nodes) this is small.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This is deliberately the smallest possible `sim.world`. Do not add grid,
room, construction or flow-field code speculatively — every one of those is
explicitly deferred (§18.5) and arrives later by amendment. Gate assignment
is a gameplay system left to the owner; Phase 0/1 pools every gate into one
node, and this module's only obligation is that `CanReach`/`PathVia` work
correctly against whatever fixture graph the Test Author builds. `T-026`
(extended, `src/sim/core/**`) must merge before this task, since `NodeId`
and `EdgeId` are declared there.
