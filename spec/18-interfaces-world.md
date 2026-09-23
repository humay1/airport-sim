# 18 — Public interfaces: `sim.world` (Phase 0/1 subset)

Implements the smallest part of the `sim.world` row of `03-module-map.md`
that lets `sim.flow` route. It answers `open-questions.md` Q-012. At Phase 0/1
the landside navigation graph is **fixed**: it is loaded from a fixture and is
immutable for the session. Everything else in the row (grid, construction,
rooms, flow-field recomputation) is **deferred**, and nothing here commits
to its shape. Notation is as in `08-interfaces-core.md`. Where this file
appears to contradict `01-architecture.md` or `02-determinism.md`, those win
and it is a spec bug.

Reading order for a `sim.world` worker: `01`, `02`, `07`, `08`, this file,
then `09` §9.6.

---

## 18.1 What the module is, and what it is not

At Phase 0/1, `sim.world` owns:

- the **walk graph**: landside and terminal nodes, their walking lengths, and
  directed edges (§18.2);
- the **routes** over that graph, precomputed once at load: reachability,
  shortest path length, and the node list of the shortest path through a
  given first edge (§18.3).

`sim.world` explicitly does **not** own, at Phase 0/1:

- what a node *does* (its `NodeKind`, queue behaviour, capacity). That is
  `sim.flow`'s, in its `FlowGraph` (`09` §9.11);
- **which gate serves which flight.** That is a gameplay system and is
  deferred to the owner (§18.5);
- construction, grids, rooms, and flow-field recomputation. The 50 ms
  recompute budget of `01-architecture.md` applies once construction exists.
  Here the routes are computed once at load, off the tick path;
- anything airside. `sim.airside` keeps its own graph (`12` §12.1).

---

## 18.2 Types and the fixture

`NodeId` and `EdgeId` are **`sim.core` types**, because events carry them
(`10` §10.6). `09` §9.2 describes their meaning; this file creates the
graph they index.

```
readonly struct WalkNodeDef { NodeId Id; uint32 LengthMetres }   // walked when crossing the node; 0 = none
readonly struct WalkEdgeDef { EdgeId Id; NodeId From; NodeId To } // directed; a two-way link is two edges

readonly struct WalkGraph {
  IReadOnlyList<WalkNodeDef> Nodes       // ascending NodeId
  IReadOnlyList<WalkEdgeDef> Edges       // ascending EdgeId
  uint64                     FixtureHash // FNV-1a-64 over the raw file bytes
}

interface IWalkGraphLoader {
  WalkGraph Load(ReadOnlySpan<byte> file, string sourceName)
}
```

The file format is the worker's choice, with the same posture as `12` §12.13.
Load-time validation, each a hard failure naming the file and the id
(`07-conventions.md`):

- node and edge ids are unique;
- every edge's `From` and `To` is a declared node;
- there are no self-loops and no duplicate `(From, To)` pairs.

Lengths are integer metres. That is enough resolution for walking a
terminal, and it keeps the fixture free of floats (`04-data-schemas.md`).

---

## 18.3 Routes

Computed at load for every ordered pair of nodes. Cost is the sum of
`LengthMetres` of the nodes entered after the start, including the
destination. Ties are broken by the lexicographically smallest sequence of
`EdgeId`s. There is no RNG and no dependence on edge-list or file order.

```
interface IWorldSystem : ISimSystem {
  IReadOnlyList<NodeId> Nodes()                                  // ascending NodeId
  uint32                LengthMetres(NodeId node)
  IReadOnlyList<EdgeId> OutEdges(NodeId node)                    // ascending EdgeId
  NodeId                EdgeTo(EdgeId edge)
  bool                  CanReach(NodeId from, NodeId destination)
  bool                  CanReachVia(EdgeId firstEdge, NodeId destination)
  IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination)
      // the shortest path starting with firstEdge: EdgeTo(firstEdge) ... destination, inclusive;
      // empty if !CanReachVia
}
```

- Every query is read-only, O(1) or O(path length), and allocates nothing:
  the lists are views into load-time tables.
- The tables are load-time data. Their memory is O(nodes² + edges × nodes).
  The max tier (about 200 landside nodes) keeps that small.
- A query naming an unknown id is a programmer error and throws.

---

## 18.4 State, hashing, RNG, budget, registry

- **State:** none at runtime. `Tick` does nothing at Phase 0/1.
- **Hash:** `ComputeStateHash()` feeds `WalkGraph.FixtureHash` only, so an
  edited graph fails the determinism gate loudly (`11` §11.9's posture).
- **RNG:** none.
- **Budget:** 0.10 ms/tick (`03-module-map.md`). It is spent by callers'
  queries, not by `Tick`.
- **Registry position 1** (`08` §8.5). `sim.flow` calls it downward
  (`03-module-map.md`: flow depends on core and world).

### Construction

```
WorldFactory.CreateGraphLoader() -> IWalkGraphLoader
WorldFactory.CreateSystem(in SystemServices services, in WalkGraph graph) -> IWorldSystem
```

This follows `08` §8.11a's factory rule. `sim.flow` is not constructed without
`sim.world`.

---

## 18.5 Deferred, explicitly

- **Gate assignment** (which `Gate` node serves which flight, and how it
  follows the stand) is a gameplay system with player-facing consequences.
  It is **not** decided here and is left to the owner. Until it is, Phase 0/1
  pools gates (`09` §9.6): a departing cohort may go to any reachable `Gate`,
  and Phase 0/1 fixtures declare **one** `Gate` node, a shared departure
  lounge.
- **Construction and flow fields.** When construction arrives, "routes
  recomputed per construction change" (`01-architecture.md`) replaces §18.3's
  load-time computation, by amendment.
- **Walking-speed variety, crowd density slowing and wayfinding** are not
  modelled.

---

## 18.6 Fixture and tests

`tests/fixtures/world/phase0-landside.*`, binding on the Test Author. It is
shared by T-007's, T-011's and T-023's flow fixtures and by the render layout
(`15` §15.12):

- every landside path from each schedule-fixture `entry_node` (a `Source`)
  reaches the single `Gate` node through at least one security `Queue`
  node;
- at least one place where two security queues are alternative routes, so
  §9.6's choice is exercised;
- at least one corridor node with `LengthMetres > 0`.

Done-condition tests, phrased per `07-conventions.md`:

- `test_walk_graph_rejects_unknown_edge_endpoint`
- `test_walk_graph_rejects_duplicate_edge`
- `test_route_is_shortest_by_length_ties_by_edge_sequence`
- `test_path_via_includes_endpoints_and_is_empty_when_unreachable`
- `test_routes_independent_of_file_order`
- `test_world_hash_is_fixture_hash_and_tick_consumes_no_rng`
