# 15 — Public interfaces: `app.render`

Implements the `app.render` row of `03-module-map.md` for Phase 1: the minimal
top-down, flat-colour renderer of T-020. Answers `open-questions.md` Q-008.
The headless part is fully specified here. The engine-side part is specified
as a contract only (§15.10). The human decisions it waited on are recorded in
§15.13. Notation is as in `08-interfaces-core.md`; where this file
appears to contradict `01-architecture.md` or `02-determinism.md`, those win
and it is a spec bug.

Reading order for an `app.render` worker: `01`, `02`, `07`, `08` §8.5 (the
host interface), `09` §9.1 and §9.7, `12` §12.4 and §12.9, this file.

---

## 15.1 What the module is, and what it is not

`01-architecture.md` puts presentation in Unity 6 and the simulation in a
headless .NET library, and makes the sim's tests the merge gate. A renderer
whose every line lives in the engine cannot satisfy "done means green"
(`CLAUDE.md` rule 5): CI has no engine, no licence and no display. So
`app.render` is split, and **everything with logic lives on the headless
side**:

- **The scene layer** (`app.render.scene`) — a plain C# assembly with no engine
  reference. It reads sim state through published read-only queries and turns
  it into a **draw list** of flat-colour primitives in world coordinates
  (§15.5). It also owns the promotion controller (§15.7) and the tick pacer
  (§15.8). It is built and tested by `dotnet test` like the sim.
- **The backend** (`app.render.unity`) — a thin engine adapter that draws the
  draw list and turns input into a camera. The frame order is run by
  `app.host` (`16-interfaces-host.md` §16.6). It contains no decisions. Its
  contract is §15.10, and its implementation is **not** part of T-020.

At Phase 1, `app.render` owns:

- the scene layer, the draw-list types and their ordering,
- the presentation layout that gives airside and landside nodes a position
  (§15.4),
- the promotion controller — the one caller of `IFlowSystem.SetPromoted`
  (§15.7),
- the tick pacer — how many ticks to `Step` per rendered frame at a given
  speed (§15.8),
- the backend contract (§15.10).

`app.render` explicitly does **not** own, and must not do:

- any text, label, panel, tooltip or delay-tree view — `app.ui`;
- any player command. `app.render` never calls `ISimHost.TrySubmit`; at
  Phase 1 `app.ui` does (`17-interfaces-ui.md`). Camera movement is not a
  command: it never enters the sim (`08-interfaces-core.md` §8.1);
- constructing the sim, owning the engine project it runs in, or running the
  frame loop. All three are `app.host`'s (`16-interfaces-host.md`);
- choosing the game speed or pausing. The pacer is told both (§15.8), and
  `app.ui` chooses them;
- any sim module's state beyond the queries in §15.6. In particular it reads
  nothing from `sim.turnaround` or `sim.delay` at Phase 1. From
  `sim.schedule` it reads `TryGetFlight` only, and only to choose an
  aircraft's sprite (§15.15, Q-130).

Since 2026-10-07 the draw list is drawn as **sprites**: flat top-down
vector art, made by agents, with no third-party assets (owner decision,
Q-130). The art, its atlas and the style guide are §15.15. "Flat-colour"
elsewhere in this file now means this flat style.

---

## 15.2 Constants

Presentation constants, declared in the scene layer. They are not sim
constants (`08-interfaces-core.md` §8.1 says so for `AGENT_ZOOM_THRESHOLD`)
and none of them affects a sim outcome.

| Constant | Value | Meaning |
|---|---|---|
| `AGENT_ZOOM_THRESHOLD` | 120 world units of view height | §15.7; `01-architecture.md` promotion rule 1 — **LOW CONFIDENCE** |
| `MAX_DRAWN_AGENTS_PER_NODE` | 256 | §15.5; bodies beyond this are shown only by the queue fill |
| `MAX_DRAWN_LANES_PER_NODE` | 32 | §15.5; lane pips per queue node (Q-010) |
| `MAX_CATCHUP_TICKS_PER_FRAME` | 3 | §15.8 |
| `REAL_MICROSECONDS_PER_TICK_1X` | `TICK_MS × 1000` = 100 000 | §15.8, from `01-architecture.md` |
| `ATLAS_SIZE` | 1024 | §15.15; side of the square sprite atlas, in pixels at mip 0 (Q-130) |
| `ATLAS_CELL_PIXELS` | 256 | §15.15; side of one sprite cell at mip 0 |
| `ATLAS_CELL_BORDER_PIXELS` | 8 | §15.15; bleed border inside each cell at mip 0 |
| `ATLAS_MIP_COUNT` | 5 | §15.15; mip levels 0 to 4 |
| `ART_UNITS` | 1024 | §15.15; design units across a sprite's visible square |

World units are metres at Phase 1, with +Y pointing up the screen. They mean
nothing to the sim.

**In C# (Q-099).** The table lives in `public static class RenderConstants`
in `AirportSim.App.Render`, as `public const` members with their IDL names
(`07` L10). The types are: `AGENT_ZOOM_THRESHOLD`, `MAX_DRAWN_AGENTS_PER_NODE`
and `MAX_DRAWN_LANES_PER_NODE` are `int`, `MAX_CATCHUP_TICKS_PER_FRAME` is
`uint` (the type `ITickPacer.Advance` returns),
`REAL_MICROSECONDS_PER_TICK_1X` is `long`, and the five atlas and art
constants (Q-130) are `int`. §15.7 compares `ViewHeight` with
`AGENT_ZOOM_THRESHOLD` converted to `float`, which is exact for 120.

> **LOW CONFIDENCE — `AGENT_ZOOM_THRESHOLD = 120`.** "Close enough to see
> individual passengers" has no measured value yet. The number trades what the
> player sees against how many agent views are derived per frame; it moves no
> sim outcome (§15.7), so it is cheap to retune after the first playable build.
> Flagged for the human owner's eye, not as a balance value.
> *Accepted as provisional — HUMAN DECISION — owner (delegated), 2026-09-23
> (D8). Revisit after the T-025 playtest; the marker stays until then.*

---

## 15.3 The two layers

| | Scene layer | Backend |
|---|---|---|
| Directory | `src/app/render/Scene/` | `src/app/render/Unity/` |
| Engine references | **none**, asserted by test | Unity 6 |
| Built by | `AirportSim.sln`, `dotnet test` | the Unity project owned by `app.host` (`16` §16.2) |
| Reads the sim | the queries in §15.6 only | never; it calls no sim member at all |
| Tested in CI | yes, all of §15.12 | build-checked only, by `unity-build` (`16` §16.2), which is not a required check; no behaviour test |
| In T-020 | yes | no — contract only |

Rules binding on the scene layer:

- Floats are **permitted** — this is presentation (`CLAUDE.md`, "Floating
  point"). But it reads no wall clock (elapsed time is passed in, §15.8), uses
  no `System.Random`, and holds no static mutable state. That is not a
  determinism rule, since presentation cannot move the sim; it is what makes
  its tests exact and repeatable.
- **Floats in its tests (Q-100).** `08` §8.3's ban covers sim assemblies
  and their tests. The no-floating-point sentence of `07` L4 binds every
  test project except `tests/app/render/`, where `float` may appear to
  build and check the
  values of this file's `float`-typed members (`WorldPoint`, `CameraView`,
  `DrawPrimitive.Size`). Expected values are written as the scene layer
  computes them from integer inputs, for example `(float)x`, and compared
  exactly, with no tolerance. Budget tests stay `long`-only (`07` L11).
- It depends on the sim **read-only**, following `03-module-map.md`'s
  `app.render` row. It never references `app.ui`.
- It targets `netstandard2.1` with `LangVersion 9`, the same as the sim
  (`01-architecture.md`, D1), because Unity consumes it as a precompiled
  plugin. `07-conventions.md` "Runtime portability" rules 3, 4 and 7 apply to
  it as well. Its tests target `net8.0`.

---

## 15.4 The presentation layout

The sim's airside graph (`12-interfaces-airside.md` §12.4) has no coordinates,
and `sim.flow` nodes have none either: positions belong to `sim.world`, which
is unspecified. Phase 1 therefore gives positions in a **presentation-owned
layout**, which is not sim state and not `data/` content.

```
readonly struct TaxiNodePosition { TaxiNodeId Node; int32 X; int32 Y }
readonly struct RunwayGeometry   { RunwayId Runway; int32 X0; int32 Y0; int32 X1; int32 Y1; int32 Width }
readonly struct FlowNodeBox      { NodeId Node; int32 MinX; int32 MinY; int32 MaxX; int32 MaxY; int32 FillCapacity }

readonly struct RenderLayout {
  IReadOnlyList<TaxiNodePosition> TaxiNodes
  IReadOnlyList<RunwayGeometry>   Runways
  IReadOnlyList<FlowNodeBox>      FlowNodes
  int32 StandSize                         // side of a stand box
  int32 AircraftSize                      // diameter of an aircraft dot
  int32 AgentSize                         // diameter of an agent dot
  int32 TaxiwayWidth
}

interface IRenderLayoutLoader {
  RenderLayout Load(ReadOnlySpan<byte> file, string sourceName, in AirsideLayout? airside)
}
```

Coordinates and sizes are integers so that a layout file carries no floats,
following the spirit of `04-data-schemas.md`.

### File format (Q-094)

Binding. It replaces the earlier "the worker's choice". The file is the
strict JSON subset of `08` §8.11 "The loader", with `18` §18.2's rules:
UTF-8 without a BOM, objects, arrays, strings and integers only, and no
fraction, exponent, `null`, `true` or `false`. Duplicate, unknown and
missing keys are parse (shape) failures, and keys may come in any order.
The scene layer hand-parses the file, with no package. The exact shape is:

```
{
  "schema_version": 1,
  "taxi_nodes": [ { "node": <uint16>, "x": <int32>, "y": <int32> }, ... ],
  "runways":    [ { "runway": <uint16>, "x0": <int32>, "y0": <int32>,
                    "x1": <int32>, "y1": <int32>, "width": <int32> }, ... ],
  "flow_nodes": [ { "node": <uint32>, "min_x": <int32>, "min_y": <int32>,
                    "max_x": <int32>, "max_y": <int32>, "fill_capacity": <int32> }, ... ],
  "stand_size": <int32>, "aircraft_size": <int32>, "agent_size": <int32>,
  "taxiway_width": <int32>
}
```

- Each object has exactly the keys shown. `schema_version` must be `1`.
- An integer is `0` or `-?[1-9][0-9]*`. One outside its C# type's range is
  a parse failure. Values inside the range parse, and the checks below
  apply to them.
- The arrays may be in any order, and any of them may be empty. `Load`
  returns `TaxiNodes`, `Runways` and `FlowNodes` in ascending id order.
- **Failures.** Every failure throws `FormatException` whose message starts
  with `sourceName` followed by `": "`. A parse failure (syntax, shape,
  C#-type range) contains the 1-based `line <n>`. A validation failure
  contains the named id in decimal, or for a size the key name. A `null`
  `sourceName` throws `ArgumentNullException`. Tests assert the exception
  type, the `sourceName` prefix and the named id or key, and nothing else
  in the message.
- The §15.12 fixture is `tests/fixtures/render/phase1-layout.json`.

Load-time validation. Each is a hard failure (`07-conventions.md`). They
are checked in this order, the first failure is thrown, and within one
check the lowest failing id is named (Q-094):

1. every size (`stand_size`, `aircraft_size`, `agent_size`,
   `taxiway_width`) is `> 0`, checked in that key order, naming the key;
2. `TaxiNodePosition.Node` ids are unique, and so are
   `RunwayGeometry.Runway` ids. A duplicate names the id. This is checked
   with or without `airside`;
3. every `RunwayGeometry.Width > 0`, naming the `RunwayId`; then
   `FlowNodeBox.Node` is unique and `≥ 1`, `MinX < MaxX`, `MinY < MaxY` and
   `FillCapacity > 0`, naming the failing box's `Node`;
4. only when `airside` is given, and in this order: no position names a
   `TaxiNodeId` absent from the layout (names that id); every `TaxiNodeDef`
   has a position (names the lowest one without); no geometry names an
   unknown `RunwayId` (names that id); every `RunwayDef` has a geometry
   (names the lowest one without).

`IFlowSystem` has no node enumeration, so the loader cannot check that a
`FlowNodeBox` names a real node. The integration test in §15.12 covers that.
Adding an enumeration query to `sim.flow` just for this check is not worth
widening `09-interfaces-flow.md`.

> **LOW CONFIDENCE — positions kept apart from the graph they position.** Two
> files must agree on ids, the airside fixture and this one. Load validation
> catches every airside mismatch, but only a test catches a landside one. The
> alternative, putting coordinates into the sim's own layout, would put
> presentation-only data into sim state and its hash. When `sim.world` is
> specified with real geometry, this layout is expected to shrink to sizes
> only, by amendment.
> *Accepted as provisional — HUMAN DECISION — owner (delegated), 2026-09-23
> (D8). Revisit after the T-025 playtest; the marker stays until then.*

---

## 15.5 What is drawn

Everything is a `DrawPrimitive` (§15.9). Draw order is `DrawLayer`
ascending, and within a layer by `SourceRef` ascending (§15.9), which gives a
total, stable order. `SourceRef` order is lexicographic over its declared
fields (Q-097): `Kind` ordinal, then `Id`, then `Sub`, each ascending.

| Source | Primitive | Geometry | `ColourRole` | `DrawLayer` | `Sprite` (Q-130) | `SourceRef` |
|---|---|---|---|---|---|---|
| each runway | `Segment` | `(X0,Y0)`–`(X1,Y1)`, width `Width` | `RunwayQueued` if `RunwayQueueLength > 0`, else `Runway` | `Runway` | `Solid` | `(Runway, id, 0)` |
| runway markings (Q-130) | edge lines, thresholds and centreline dashes, "Runway markings" below | below | `RunwayMarking` | `Runway` | below | `(RunwayMarking, id, Sub)` |
| each taxi edge | `Segment` | position of `From` to position of `To`, width `TaxiwayWidth` | `Taxiway` | `Taxiway` | `Solid` | `(TaxiEdge, id, 0)` |
| junction fill (Q-130): each taxi node with two or more incident edges in `Layout().Edges` | `Dot` | centred on the node's position, diameter `TaxiwayWidth` | `Taxiway` | `Taxiway` | `Disc` | `(TaxiNode, id, 0)` |
| taxiway centreline (Q-130): each taxi edge | `Segment` | the taxi edge's geometry, width `TaxiwayWidth` | `TaxiwayMarking` | `Taxiway` | `CentreStripe` | `(TaxiCentreline, id, 0)` |
| each stand | `Box` | centred on the stand's `Node` position, side `StandSize` | `StandOccupied` if `StandState.Occupant` is set, else `StandFree` | `Stand` | `Stand` | `(Stand, id, 0)` |
| each `FlowNodeBox` | `Box` | the box | `LandsideNode` | `LandsideNode` | `Terminal` | `(FlowNode, id, 0)` |
| queue fill, if `Population > 0` | `Box` | same `MinX`, `MinY`, `MaxY`; width = box width × `min(1, Population / FillCapacity)` | `QueueFill` | `QueueFill` | `Solid` | `(QueueFill, id, 0)` |
| lane pips of a `FlowNodeBox` whose node `TryGetLaneState` accepts | `Dot` | inside the box, one per server up to `MAX_DRAWN_LANES_PER_NODE`, diameter `AgentSize` | `LaneOpen` for the first `ServersOpen` pips, `LaneClosed` for the rest | `Lane` | `LanePip` | `(Lane, id, index)` |
| agents of a promoted `FlowNodeBox` | `Dot` | inside the box, one per agent, diameter `AgentSize` | `Agent` | `Agent` | `Passenger` | `(Agent, id, rank)` |
| each tracked aircraft that is on the graph | `Dot` | see below, diameter `AircraftSize` | by phase, below | `Aircraft` | by size category, below | `(Aircraft, flight, 0)` |

**Facing (Q-130).** Every primitive carries `Facing` (§15.9). It is
`(0, 0)` for every primitive except aircraft (below) and runway thresholds
(below). Boxes are never rotated, and `(0, 0)` on a `Dot` means unrotated.

**Draw order of the new rows (Q-130).** The appended `SourceKind`s sort
after the older ones, so within the `Runway` layer every runway surface is
drawn before any marking, and within the `Taxiway` layer every edge surface
comes first, then every junction fill, then every centreline. Markings are
never hidden by a surface of the same layer.

**Runway markings (Q-130).** For each runway, in integer arithmetic on the
layout's `int32` values, widened to `int64`: `dx = X1 − X0`, `dy = Y1 − Y0`,
`W = Width`, and `L = isqrt(dx² + dy²)`, the exact floor of the square root
(the largest `s` with `s² ≤ dx² + dy²`). `Along(d)` is the point
`(X0 + dx × d / L, Y0 + dy × d / L)`, with C# `long` division (truncating
toward zero), each coordinate then converted to `float`. If `L = 0` the
runway has no markings. Otherwise, all with layer `Runway`, colour
`RunwayMarking` and source `(RunwayMarking, runway id, Sub)`:

| `Sub` | What | Primitive | Geometry | `Sprite` | `Facing` |
|---|---|---|---|---|---|
| 0 | edge lines | `Segment` | the runway's own `A`, `B` and `Size` | `RunwayEdges` | `(0, 0)` |
| 1 | threshold at `(X0,Y0)` | `Dot` | centre `Along(W / 2)`, diameter `W` | `RunwayThreshold` | `(dx, dy)` |
| 2 | threshold at `(X1,Y1)` | `Dot` | centre `Along(L − W / 2)`, diameter `W` | `RunwayThreshold` | `(−dx, −dy)` |
| `3 + k` | centreline dash `k`, `0 ≤ k < n` | `Segment` | `Along(s_k)` to `Along(s_k + W)`, width `W` | `CentreStripe` | `(0, 0)` |

`W / 2` is integer division. The dashes: `M = 2W`, `R = L − 2M`;
`n = 0` if `R < W`, else `n = (R + W) / (2W)`; and
`s_k = M + (R − (2n − 1) × W) / 2 + 2W × k`. So `n` dashes of length `W`
with gaps of `W` are centred between the two threshold zones. For the
§15.12 fixture's runway (`L = 2000`, `W = 45`) that is `n = 20` and
`s_0 = 122`. A threshold's `Facing` points into the runway, so the
sprite's forward edge faces the runway's middle.

**Aircraft sprite by size category (Q-130).** At construction the scene
builder reads, when `RenderSources.Content` is not null,
`Content.AllOf(ContentKind.Aircraft)`, and for each id
`TryGet<AircraftDefinition>` and then `TryGet<SizeCategoryDefinition>` of
its `SizeCategory`. That gives a fixed map from aircraft type to sprite:
`AircraftA + min(Ordinal, 5)`, so ordinals 0 to 5 (`data/`'s `size_a` to
`size_f`) are `AircraftA` to `AircraftF`, and a larger ordinal is
`AircraftF`. A type whose definition or category does not resolve is left
out of the map. Per rebuild, an aircraft's sprite is its type's entry,
read through `Schedule.TryGetFlight(flight).AircraftType`. It is
`AircraftC` when `Schedule` or `Content` is null, the flight is not found
or its type is not in the map. The sprite never changes with phase:
phase is shown by the colour, as before.

**Aircraft facing (Q-130).** An integer direction vector taken from
layout positions, never an angle. Positions are the §15.4 `int32` values,
and the vector's components are their `int32` differences converted to
`float`, so the value is exact and needs no quantisation and no
trigonometry. The backend rotates continuously (§15.10). In this order:

1. `OnEdge` set: the edge's other endpoint's position minus `AtNode`'s,
   which is the direction of travel. A pushback is therefore drawn nose
   first, a stylisation accepted for Phase 1.
2. Else, `AtNode` set and `Phase` is `OnStand` or
   `AwaitingPushbackClearance`: nose in. `AtNode`'s position minus the
   position of the other endpoint of the lowest-`TaxiEdgeId` edge incident
   to `AtNode`.
3. Else, `AtNode` set and `Phase` is `OnRunway` or `HeldForRunway`: along
   the runway, away from its threshold. With `Runway` set, `T` the position
   of that runway's `RunwayDef.ThresholdNode`, and `P0 = (X0,Y0)`,
   `P1 = (X1,Y1)` from its geometry: `P1 − P0` if
   `|P0 − T|² ≤ |P1 − T|²` (in `int64`), else `P0 − P1`.
4. Else, `AtNode` set and `Phase` is `HeldOnTaxiway` or `Taxiing`: toward
   the destination, in a straight line. A `Departure`'s destination is the
   `ThresholdNode` of its `Runway`, an `Arrival`'s is the `Node` of its
   `Stand`. The vector is the destination's position minus `AtNode`'s.
5. In every other case, and whenever the rule's id is unset, a position
   or geometry is missing, or the vector is `(0, 0)`: `(0, 0)`.

> **LOW CONFIDENCE — rule 4 points along a straight line, not the
> route.** `IAirsideSystem` exposes no route, so an aircraft held at a
> junction faces its destination, which can be off the next edge by up to
> 90°. It holds only while the aircraft waits, and it is presentation
> only. A route query on `sim.airside` would fix it, and is not worth
> widening `12` for now.

**Which boxes are promoted (Q-101).** The scene builder holds no reference
to the promotion controller and reads no promotion state from `sim.flow`
(`IFlowSystem` has no such query). In `Build(camera, graphics)` it applies
§15.7's **desired promoted** predicate itself, to the `camera` and
`graphics` it is given. A `FlowNodeBox` is promoted for drawing iff that
predicate holds, and `AgentsAt` is called for exactly those boxes,
ascending `NodeId`. `16` §16.6 passes the same camera and graphics to
`Promotion.Update` and `Scene.Build` in one frame, so the two agree in a
playable build. If they ever disagree, `AgentsAt` on a node the sim has not
promoted returns empty (`09` §9.7), and the box simply draws no agents.

**Agents.** `AgentsAt(node)` is sorted by `(Cohort, Index)` and truncated to
`GraphicsSettings.MaxDrawnAgentsPerNode` (§15.14), which never exceeds
`MAX_DRAWN_AGENTS_PER_NODE`. The *k*-th agent's position is a pure function
of *k* and the box. The exact arrangement is the worker's choice, and tests
assert only count and containment. `ProgressAlongEdge` is not used at
Phase 1, because corridors are not drawn.

**Lane pips** (Q-010, the visible half of D5's lane control). The *k*-th pip's
position is a pure function of *k*, the pip count and the box. As with
agents, the arrangement is the worker's choice, and tests assert only count,
colour split and containment. A node for which `TryGetLaneState` returns false
gets no pips.

**Aircraft position** (from `AircraftTrack`, `12-interfaces-airside.md`
§12.9, as amended):

- if `OnEdge` is set: interpolate from the position of `AtNode` (the entry
  node) to the edge's other endpoint, by `EdgeProgress` converted to a float;
- else if `AtNode` is set: the node's position;
- else the aircraft is off-graph (approaching, or held off-graph before
  `Landed`, §12.6) and is **not drawn**.

**Aircraft colour** by `Phase`:

| `AircraftLegPhase` | `ColourRole` |
|---|---|
| `HeldForRunway`, `HeldOnTaxiway` | `AircraftHolding` |
| `OnRunway`, `Taxiing` | `AircraftMoving` |
| `OnStand`, `AwaitingPushbackClearance` | `AircraftOnStand` |
| `AwaitingApproach`, `Departed` | not drawn |

**Absent modules.** If `RenderSources.Airside` is null, no runway, taxiway,
stand or aircraft primitive is produced, and the airside half of the layout is
not checked against an airside layout (§15.4 check 4 is skipped; check 2
still runs). If `Flow` is null, no landside primitive is produced. A build
that has only some sim modules still renders what it has.

**Not drawn at Phase 1**, each additive by amendment: vehicles and turnaround
jobs, corridors and flow edges, delay state of any kind, text and labels,
terrain, weather, and interpolation between ticks.

`ColourRole` names a meaning, not a colour. Mapping roles to RGB is the
backend's palette (§15.10). It is aesthetic, not balance, and no test checks
it. The role **tints** the primitive's sprite (§15.15): sprites carry
value and coverage only, and the palette colour multiplies them. So phase,
occupancy, queue and lane states still read by colour, with one sprite
per thing drawn and never one per state.

---

## 15.6 Sim queries polled, and when

The complete list. Calling any other sim member from `app.render` is a review
rejection, and the fakes in §15.12 throw if one is called.

| Member | Spec | Called by | When |
|---|---|---|---|
| `ISimHost.CurrentTick` | `08` §8.5 | scene builder | every `Build` |
| `ISimHost.Step` | `08` §8.5 | **not called by `app.render`**; `app.host`'s frame loop is its only caller (`16` §16.6) | — |
| `IAirsideSystem.Layout` | `12` §12.9 | scene builder, loader | once, at construction |
| `IAirsideSystem.TrackedFlights`, `TryGetTrack` | `12` §12.9 | scene builder | per rebuild |
| `IAirsideSystem.TryGetStand`, `RunwayQueueLength` | `12` §12.9 | scene builder | per rebuild |
| `IFlowSystem.Population` | `09` §9.7 | scene builder | per rebuild, per `FlowNodeBox` |
| `IFlowSystem.AgentsAt` | `09` §9.7 | scene builder | per rebuild, per promoted `FlowNodeBox` |
| `IFlowSystem.TryGetLaneState` | `09` §9.7b | scene builder | per rebuild, per `FlowNodeBox` |
| `IFlowSystem.SetPromoted` | `09` §9.7 | promotion controller only | §15.7 |
| `IScheduleSystem.TryGetFlight` | `11` §11.7 | scene builder | per rebuild, per drawn aircraft (Q-130) |
| `IContentIndex.AllOf`, `TryGet` | `08` §8.11 | scene builder | once, at construction (Q-130) |

Cadence:

- `Build` is called at most once per rendered frame. It **rebuilds** only if
  `CurrentTick`, the camera or the `GraphicsSettings` (§15.14) differs from
  the previous `Build`; otherwise it returns the previous frame unchanged. At 60 fps and 1x, the sim advances
  every sixth frame, so most frames re-read nothing.
- No query is ever made while `Step` is running. Given §15.8's frame order and
  the fact that `Step` is synchronous (`08` §8.5), this holds by construction.
  It also means a frame never mixes two ticks' state.
- `WorldStateHash`, `TrySubmit`, `Inject`, `Absorb`, every query of
  `sim.turnaround` and `sim.delay`, and every `sim.schedule` query except
  `TryGetFlight` are **not** called.

---

## 15.7 The promotion controller

`01-architecture.md` promotion rules 1 and 3 are driven from here and nowhere
else. Rule 2 (identity needed by an incident or command) is not
`app.render`'s concern.

- A `FlowNodeBox` is **visible** if its box intersects the camera's view
  rectangle (§15.9), with closed intervals: touching counts.
- It is **desired promoted** if it is visible **and**
  `camera.ViewHeight <= AGENT_ZOOM_THRESHOLD` **and**
  `graphics.DrawAgents` (§15.14). The comparison is inclusive, matching "at
  or below" in rule 1. With `DrawAgents` false, no node is desired promoted,
  so the controller demotes every node it promoted and no agent views are
  derived.
- **First `Update`:** calls `SetPromoted(node, desired)` for **every**
  `FlowNodeBox`, in ascending `NodeId`. This establishes known state without
  assuming anything about the sim, for example after a load.
- **Every later `Update`:** calls `SetPromoted` only for nodes whose desired
  state changed, in ascending `NodeId`, with promotions and demotions
  interleaved in that one order.
- A node that is not in the layout is never promoted or demoted.

**Why this cannot break promotion neutrality.** `09-interfaces-flow.md` §9.1
makes promotion outcome-neutral by construction, and §9.7 states that
`SetPromoted` changes no hashed state. `app.render` adds three constraints so
that it cannot undo that from outside:

1. `SetPromoted` is its only call that changes anything in the sim;
2. it is made only between `Step`s (§15.8);
3. nothing `app.render` reads is fed back into the sim, because it issues no
   commands (§15.1).

The `determinism_promotion` gate (`02-determinism.md`) proves this for the sim.
`test_render_loop_is_outcome_neutral_with_scripted_camera` (§15.12) proves it
for this module's actual call pattern.

---

## 15.8 The tick pacer and the frame order

```
enum GameSpeed { X1 = 1, X2 = 2, X4 = 4 }          // the value is the multiplier

interface ITickPacer {
  uint32 Advance(int64 elapsedRealMicroseconds, bool paused, GameSpeed speed)   // ticks to Step this frame
}
```

- **Speeds are pause, 1x, 2x and 4x.** HUMAN DECISION — owner (delegated),
  2026-09-23 (D4), reversible. At 4x a sim-day lasts 6 real minutes. Higher
  speeds are deferred until T-011's budget results exist. Adding one is an
  amendment to `GameSpeed`, never a worker's choice.
- The pacer holds an integer accumulator of *speed-scaled* microseconds. That
  is presentation state, not sim state, and it is never saved.
- `paused`: returns 0, discards `elapsed` and leaves the accumulator
  unchanged. Unpausing does not replay the paused time, and the partial
  tick held before the pause is kept.
- Otherwise: `acc += elapsed × (int)speed`;
  `n = acc / REAL_MICROSECONDS_PER_TICK_1X`;
  `acc -= n × REAL_MICROSECONDS_PER_TICK_1X`. If
  `n > MAX_CATCHUP_TICKS_PER_FRAME`, then `n = MAX_CATCHUP_TICKS_PER_FRAME` and
  `acc = 0`. After a hitch the game runs briefly slower than real time rather
  than bursting ticks into one frame. The cap is the same at every speed: at
  4x and 60 fps a frame needs 0.67 ticks, so the cap binds only below about
  13 fps, and it limits one frame's sim work to 3 ticks (18 ms at max tier).
- Changing `speed` between calls keeps the accumulator, so no partial tick is
  lost or duplicated.
- A negative `elapsed`, or a `speed` outside the enum, is a programmer error
  and throws `ArgumentOutOfRangeException` (Q-098, `07` "Error handling"),
  with `ParamName` `elapsedRealMicroseconds` or `speed`. `elapsed` is
  checked first. Both are checked even when `paused` is true, before
  anything else, and a throwing call leaves the accumulator unchanged.
- Who chooses `paused` and `speed` is `app.ui` (`17-interfaces-ui.md` §17.4).
  `app.render` holds neither.

Pacing cannot change outcomes. The sim sees only a sequence of `Step` calls,
and a fixed-timestep sim run for N ticks is the same however those ticks were
grouped into frames (`02-determinism.md` rule 1).

### Frame order

The binding frame order now lives in `16-interfaces-host.md` §16.6
(`app.host`'s frame loop, D7). It moved there because a frame spans more than
one presentation module. `app.render`'s part of it is unchanged:
`IPromotionController.Update` runs before `Step`, so a node that comes into
view promotes before the tick that shows it, and `ISceneBuilder.Build` runs
after `Step`, so the frame shows the state just produced.

---

## 15.9 Types and interfaces (scene layer)

```
readonly struct WorldPoint { float X; float Y }

readonly struct CameraView {
  WorldPoint Centre
  float      ViewHeight        // world units; > 0
  float      Aspect            // width / height; > 0
}                              // view rectangle: Centre ± (ViewHeight × Aspect / 2, ViewHeight / 2)

enum DrawLayer     { Runway, Taxiway, Stand, LandsideNode, QueueFill, Lane, Agent, Aircraft }   // draw order
enum PrimitiveKind { Box, Segment, Dot }
enum ColourRole {
  Runway, RunwayQueued, Taxiway, StandFree, StandOccupied,
  LandsideNode, QueueFill, Agent,
  AircraftMoving, AircraftHolding, AircraftOnStand,
  LaneOpen, LaneClosed,                                 // appended, Q-010
  RunwayMarking, TaxiwayMarking                         // appended, Q-130
}
enum SourceKind {
  Runway, TaxiEdge, Stand, FlowNode, QueueFill, Agent, Aircraft, Lane,
  RunwayMarking, TaxiNode, TaxiCentreline               // appended, Q-130
}
enum SpriteId {                                         // Q-130; the value is the atlas cell (§15.15)
  Solid, Disc, RunwayEdges, CentreStripe, RunwayThreshold,
  Stand, Terminal, LanePip, Passenger,
  AircraftA, AircraftB, AircraftC, AircraftD, AircraftE, AircraftF
}

readonly struct SourceRef {
  SourceKind Kind
  uint64     Id                // RunwayId / TaxiEdgeId / TaxiNodeId / StandId / NodeId / FlightId value
  int32      Sub               // agent rank, lane index or runway marking index (§15.5); 0 otherwise
}

readonly struct DrawPrimitive {
  PrimitiveKind Kind
  DrawLayer     Layer
  ColourRole    Colour         // tints Sprite (§15.15)
  SpriteId      Sprite         // Q-130
  WorldPoint    A              // Box: min corner.  Segment: start.  Dot: centre.
  WorldPoint    B              // Box: max corner.  Segment: end.    Dot: unused.
  float         Size           // Box: unused.      Segment: width.  Dot: diameter.
  WorldPoint    Facing         // Q-130. Dot: the direction the sprite's forward (+V) points; (0,0) = +Y.
                               // Box and Segment: always (0,0); a Segment's forward is A to B.
  SourceRef     Source
}

readonly struct RenderFrame {
  Tick                         Tick
  CameraView                   Camera
  IReadOnlyList<DrawPrimitive> Primitives    // valid until the next Build
  GraphicsSettings             Graphics      // §15.14; what the backend applies (D10)
}

readonly struct RenderSources {
  ISimHost         Host
  IAirsideSystem?  Airside
  IFlowSystem?     Flow
  IScheduleSystem? Schedule    // Q-130; null: every aircraft uses AircraftC
  IContentIndex?   Content     // Q-130; null: every aircraft uses AircraftC
}

readonly struct AtlasRect { float U0; float V0; float U1; float V1 }   // Q-130; texture coordinates, 0..1, V up

readonly struct SpriteAtlas {                          // Q-130, §15.15; plain data, no engine type
  int32                    Size      // ATLAS_SIZE
  IReadOnlyList<byte[]>    Mips      // ATLAS_MIP_COUNT entries; Mips[m] is RGBA32, (Size >> m)² × 4 bytes, rows bottom to top
  IReadOnlyList<AtlasRect> Rects     // indexed by (int)SpriteId, one per SpriteId
}

interface ISceneBuilder        { RenderFrame Build(in CameraView camera, in GraphicsSettings graphics) }        // D10
interface IPromotionController { void Update(in CameraView camera, in GraphicsSettings graphics) }        // D10
// ITickPacer: §15.8.   IRenderLayoutLoader: §15.4.   RenderConstants: §15.2 (Q-099).
```

`ISceneBuilder` and `IPromotionController` are constructed from a
`RenderSources` and a validated `RenderLayout` (Q-009):

```
RenderFactory.CreateLayoutLoader() -> IRenderLayoutLoader
RenderFactory.CreateSceneBuilder(in RenderSources sources, in RenderLayout layout) -> ISceneBuilder
RenderFactory.CreatePromotionController(in RenderSources sources, in RenderLayout layout) -> IPromotionController
RenderFactory.CreatePacer() -> ITickPacer
RenderFactory.BuildSpriteAtlas() -> SpriteAtlas       // Q-130, §15.15; a fresh value per call
```

**C# shape (Q-130).** As for every type in this file, the fields above
are get-only properties and each struct has one constructor taking them in
declared order. `RenderSources` has a second constructor,
`(host, airside, flow)`, which sets `Schedule` and `Content` to null, so
every caller written before Q-130 compiles and behaves as before.
`DrawPrimitive`'s one constructor takes its nine fields in declared order.

`RenderFactory` follows `08` §8.11a's factory rule (stateless static
methods only). In a playable build,
`app.host`'s presentation composer builds the `RenderSources` from the
composed sim (`16-interfaces-host.md` §16.5). Tests build them from fakes and,
for the integration test, from the same composition the headless harness
uses.

---

## 15.10 The backend contract

Specified so that its eventual task cannot drift. **Not part of T-020.**

- It consumes `RenderFrame` only, and draws its primitives in list order. It
  maps `ColourRole` to colour through a palette asset.
- **Sprites (Q-130, §15.15).** Once, in its start-up, it calls
  `RenderFactory.BuildSpriteAtlas()` and uploads the result as **one**
  texture: `ATLAS_SIZE` square, RGBA32, `ATLAS_MIP_COUNT` mip levels, each
  level set from `Mips[m]` as given (never generated by the engine),
  trilinear filtering, clamped wrap, and not readable afterwards. It is
  the only texture it draws with. Each primitive's quad samples
  `Rects[(int)Sprite]`, and its vertex colour is the palette colour of
  `Colour`, which multiplies the texel (the tint). Quad corners map to the
  rectangle as follows. A `Box` is unrotated, with `(MinX, MinY)` at
  `(U0, V0)` and `(MaxX, MaxY)` at `(U1, V1)`. A `Segment`'s forward
  `f = (B − A) / |B − A|` maps to `+V`, and its right `r = (f.Y, −f.X)`
  maps to `+U`. The corner `A − r × Size / 2` is `(U0, V0)`, and
  `B + r × Size / 2` is `(U1, V1)`. A `Dot` does the same about its centre,
  with `f` = `Facing` normalised, or `(0, 1)` when `Facing` is `(0, 0)`, and
  half-side `Size / 2` along both `f` and `r`. That is the one place a
  facing becomes a float rotation. A zero-length `Segment` is drawn as a
  degenerate quad, which shows nothing.
- **Palette (Q-130).** The palette asset has one colour per `ColourRole`,
  including the appended ones. Its values are §15.15's style guide.
- It turns input (pan, zoom) into a `CameraView` and keeps `ViewHeight` and
  `Aspect` positive.
- It does **not** run the frame order. `app.host`'s frame loop does
  (`16-interfaces-host.md` §16.6), and the Unity bootstrap converts the frame
  delta (§16.7). The backend supplies the `CameraView` and draws the
  `RenderFrame` it is handed.
- It references the scene layer's types only. It calls **no** sim member and
  never branches on sim state. Anything that needs a decision belongs in the
  scene layer, where it can be tested.
- It issues no commands at Phase 1.
- **Graphics (D10, §15.14).** It applies `RenderFrame.Graphics`'s backend
  knobs (`FrameRateCap`, `ResolutionScalePercent`, `AntiAliasing`) through
  the engine's own settings, and only when they differ from those last
  applied. Which engine API it uses is its own choice. It reads no other
  source of quality settings, and never Unity's quality levels on their
  own. Resolution scale changes only the rendered image. The camera, the
  screen size in `FrameInput` and every reported click stay in full-screen
  pixels. It draws every primitive it is handed at every setting (the
  §15.14 invariant).
- **Draw calls (Q-034).** Integrated graphics is the minimum GPU (§15.11).
  The backend's number of draw calls per frame is bounded by the number of
  `DrawLayer`s and `ColourRole`s, never by the number of primitives. It
  creates no engine object per primitive and allocates no engine object per
  frame after the first. Sprites keep this (Q-130): one atlas, one
  material and one mesh, with UVs in a reused buffer that grows like the
  vertex buffer, so one draw call still covers every layer, role and
  sprite. No texture is created after start-up. **LOW CONFIDENCE**: this binds the implementation
  more tightly than the rest of the contract. It is the Architect's reading
  of what `Low` needs to hold budget on integrated graphics.
- **Packaging (Q-114).** It is the local package
  `com.airportsim.render.unity` at `src/app/render/Unity/`, with committed
  `.meta` files, referenced from the Unity project's
  `Packages/manifest.json` (`16` §16.2). Its task adds that one line.
- CI only compiles it (`unity-build`, `16` §16.2), and no CI test runs its
  behaviour, so it must stay small enough for the
  Reviewer to check against this list line by line.

---

## 15.11 Budget

`01-architecture.md` sets the render target at 60 fps at max tier on the
minimum spec, which is 16.7 ms per frame for everything. The sim's 6 ms per
tick lands on roughly one frame in six at 1x. This file claims, for the scene
layer:

- `ISceneBuilder.Build` (a rebuilding call) plus `IPromotionController.Update`:
  **mean ≤ 2.0 ms, p99 ≤ 4.0 ms per frame**. Measured with
  `03-module-map.md`'s protocol (reference machine, recorded scaling factor),
  against fakes sized to max tier: 3 runways, 60 stands all occupied, 100
  tracked aircraft on the graph, 200 `FlowNodeBox`es, 16 of them promoted with
  `MAX_DRAWN_AGENTS_PER_NODE` agents each, and with the `High` preset
  (§15.14), which is the most expensive. A lower preset never costs more
  (§15.14, monotonicity).
- **Window and arithmetic (Q-096).** `03`'s window and arithmetic
  ("Budget tests: window and arithmetic", Q-044, Q-045) apply with "tick"
  read as "frame". One sample is one frame's `IPromotionController.Update`
  plus one **rebuilding** `ISceneBuilder.Build`. The window is
  `n = 14 400` consecutive such frames, after warm-up, and `B = 2000` µs.
  The mean passes iff `Σu ≤ B × n`, and p99 passes iff `p99 ≤ 2 × B`
  (4.0 ms). To make every sampled `Build` a rebuild, the fake host's
  `CurrentTick` advances by one between frames. The camera is held fixed,
  so every frame promotes the same 16 nodes and the controller makes no
  `SetPromoted` call after warm-up. Whether the test is `Slow` is decided
  by `07` L11a rule (b) alone (it steps no sim ticks).
- **No allocation** in `Build` or `Update` after the first call. The primitive
  buffer is reused, which is why `RenderFrame.Primitives` is valid only until
  the next `Build`. Presentation is not bound by the sim's zero-allocation
  rule, but a GC pause at 60 fps is a visible hitch.
- The backend's draw cost is not budgeted here. It has no test to carry a
  number.
- **Sprites (Q-130).** The per-rebuild cost adds one `TryGetFlight` per
  drawn aircraft and the markings of §15.5, inside the same 2 ms. The
  atlas costs about 5.6 MB of GPU memory (1024² RGBA32 and its mips),
  which is shared memory on integrated graphics and counts against
  `16` §16.10's budget. `BuildSpriteAtlas` runs once at start-up, outside
  every frame. Its time is not budgeted, and the T-025 playtest notes it.
- **On minimum spec, the `Low` preset holds the render target** (HUMAN
  DECISION — owner, 2026-09-27, Q-034). The minimum GPU is integrated
  graphics with no dedicated VRAM (`01-architecture.md`). The whole frame,
  meaning the sim's share, the scene layer and the backend with the engine,
  fits 16.7 ms at max tier with `Low`. `Medium` and `High` are not bound to
  it on minimum spec. The check is a manual measurement on a minimum-spec
  machine, since CI has no GPU (§15.14). Integrated graphics uses shared
  memory, so the game's GPU memory counts against the 8 GB of RAM. The
  process's combined budget is in `16` §16.10.
- `app.render` adds nothing to the sim's 6 ms. The cost of `SetPromoted` and
  `AgentsAt` is `sim.flow`'s.

> **LOW CONFIDENCE — 2 ms per frame.** An estimate with no measurement behind
> it, sized to leave most of the frame to the engine's own rendering. T-020's
> budget test is the first measurement. If it is badly off, the number is
> corrected by amendment, never by a worker.
> *Accepted as provisional — HUMAN DECISION — owner (delegated), 2026-09-23
> (D8). Revisit after the T-025 playtest; the marker stays until then.*

---

## 15.12 T-020: scope, fixtures, tests

**Scope.** The scene layer only. Writable paths: `src/app/render/Scene/**`,
`tests/app/render/**`, `tests/fixtures/render/**`. The backend
(`src/app/render/Unity/**`) is out of scope for T-020. It gets its own task
against §15.10, once `app.host`'s Unity project exists (`16` §16.11).

**Dependencies** (for the Planner): the scene layer compiles against
`ISimHost` (T-001), `IFlowSystem` including `SetPromoted`/`AgentsAt` (T-010),
and `IAirsideSystem` including `Layout()` (T-021, as amended). T-020 therefore
depends on T-009, T-010 and T-021, not on T-009 alone.

**Fixture.** `tests/fixtures/render/phase1-layout.json`, in §15.4's file
format (Q-094), binding on the Test Author:

- positions for every taxi node, and geometry for the runway, of
  `tests/fixtures/airside/phase1-single-runway.json`
  (`12-interfaces-airside.md` §12.13);
- a `FlowNodeBox` for every landside node used by the schedule fixture's
  `entry_node`s and by the `sim.flow` fixtures that T-007 and T-023 run, and
  at least one `Queue` node among them;
- a companion max-tier fake-source setup for the budget test (§15.11), which
  needs no real sim.

**Done-condition tests**, phrased per `07-conventions.md`:

- `test_render_layout_rejects_missing_taxi_node_position`
- `test_render_layout_rejects_unknown_runway_geometry`
- `test_scene_stand_colour_follows_occupancy`
- `test_scene_aircraft_on_edge_interpolates_from_entry_node`
- `test_scene_aircraft_off_graph_is_not_drawn`
- `test_scene_queue_fill_scales_with_population_and_clamps`
- `test_scene_agents_capped_per_node_and_inside_box`
- `test_scene_lane_pips_follow_lane_state_and_skip_non_queue_nodes`
- `test_scene_primitive_order_is_stable`
- `test_scene_omits_primitives_of_absent_modules`
- `test_scene_rebuilds_only_when_tick_or_camera_changes`
- `test_scene_calls_only_listed_sim_members` — fakes throw on any member not
  in §15.6
- `test_promotion_first_update_sets_every_layout_node`
- `test_promotion_calls_only_on_change_in_ascending_node_id`
- `test_promotion_zoom_threshold_is_inclusive`
- `test_tick_pacer_steps_ten_ticks_per_real_second`
- `test_tick_pacer_steps_forty_ticks_per_real_second_at_4x`
- `test_tick_pacer_speed_change_keeps_accumulated_time`
- `test_tick_pacer_caps_catch_up_and_drops_backlog`
- `test_tick_pacer_paused_steps_nothing`
- `test_tick_pacer_rejects_negative_elapsed_and_unknown_speed` (Q-098)
- `test_scene_draws_agents_only_where_its_own_camera_promotes` (Q-101): a
  `Build` whose camera makes a box desired-promoted calls `AgentsAt` for
  it, and one whose camera does not makes no `AgentsAt` call for it, with
  no `IPromotionController` involved
- `test_render_loop_is_outcome_neutral_with_scripted_camera` — integration.
  Run one sim-day with the real `sim.schedule`, `sim.flow` and `sim.airside`.
  Run it once through the frame order of `16-interfaces-host.md` §16.6, which
  the test drives itself without depending on `app.host`, with a scripted
  camera that sweeps
  every `FlowNodeBox` in and out of view across the zoom threshold, with
  irregular frame deltas. Run it once headless with plain `Step` calls. The
  checkpoints must be identical at every checkpoint tick. The test also checks
  every `FlowNodeBox` against the node list of the `sim.flow` fixture it runs,
  because `IFlowSystem` offers no enumeration to check against (§15.4).
- `test_scene_assembly_has_no_engine_reference` — static
- `test_scene_build_within_frame_budget_at_max_tier`
- `test_graphics_presets_are_monotone_and_high_matches_phase1_behaviour`
  (D10, §15.14)
- `test_graphics_low_and_medium_match_the_preset_table` (Q-034, §15.14)
- `test_graphics_settings_validate_clamps_every_knob`
- `test_scene_agents_capped_by_graphics_setting`
- `test_scene_rebuilds_when_graphics_settings_change`
- `test_promotion_draw_agents_off_demotes_every_promoted_node`
- `test_render_loop_is_outcome_neutral_across_graphics_changes` —
  integration, as `test_render_loop_is_outcome_neutral_with_scripted_camera`,
  with the graphics settings also switched between every preset and several
  custom values at irregular frames. Checkpoints must be identical.
- `test_scene_gameplay_primitives_identical_at_every_graphics_setting` —
  the §15.14 invariant

The sprite tests, and the T-020 tests that Q-130 changes, are listed in
§15.15.

---

## 15.13 The HUMAN DECISIONS this file left open — all decided 2026-09-23

These were left open by Q-008, and the owner decided all five on 2026-09-23
(D1, D4, D5, D7). None ever blocked T-020's headless scope. What still stands
between them and a playable build is the Phase 1 tasks and the owner's
Phase 1 content values (`04-data-schemas.md`), not anything here.

**(a) Unity 6 against a .NET 8 sim library — DECIDED.** HUMAN DECISION —
owner (delegated), 2026-09-23 (D1). The current Unity 6 LTS runs Mono, which
exposes only .NET Standard 2.1 and C# 9 and cannot load `net8.0` assemblies.
The sim, this scene layer and every headless `app.*` layer Unity consumes
therefore target **`netstandard2.1` only**. Single-targeting is deliberate:
one compiled sim, and no BCL divergence between two builds of it. Tests and
`tools.simharness` target `net8.0`. `01-architecture.md`'s runtime row is
amended accordingly, and §15.3 states the scene layer's target. Revisit when
Unity ships production CoreCLR (.NET 10).

**(b) The engine project shell — DECIDED.** HUMAN DECISION — owner
(delegated), 2026-09-23 (D7). A new module, `app.host`, owns
`unity/AirportSim/`: scenes, settings and the build
(`16-interfaces-host.md` §16.2).

**(c) The composition root — DECIDED.**
HUMAN DECISION — owner (delegated), 2026-09-23 (D7). `app.host`'s headless
part (`src/app/host/`) builds `ISimHost` and the systems from a scenario
bundle and hands read-only views to presentation (`16` §16.3 to §16.5). A
thin Unity bootstrap only calls it (§16.7), and it also runs the frame loop
(§16.6). How each module's system is constructed is published in `08`
§8.11a and each module's Construction section (Q-009).

**(d) Game speeds — DECIDED.** HUMAN DECISION — owner (delegated),
2026-09-23 (D4). Pause, 1x, 2x and 4x. Higher speeds wait for T-011's budget
results (§15.8).

**(e) A player-facing way to open a security lane — DECIDED, with its
command plumbing open.** HUMAN DECISION — owner (delegated), 2026-09-23
(D5). A minimal `app.ui`: clicking a flow-node box requests a lane change
through the command queue, and speed and pause controls drive the pacer. There
is no other UI at Phase 1 (`17-interfaces-ui.md`). The command plumbing and
the lane-state read are answered by Q-010 (`08` §8.7, `09` §9.7b), and this
module draws the lane state as pips (§15.5).

---

## 15.14 Graphics quality — HUMAN DECISION, owner, 2026-09-27 (D10)

> **HUMAN DECISION — owner, 2026-09-27 (D10):** "the final user should be
> able to increase or decrease graphics so the game can also be run on a low
> resource laptop." The Architect specified the mechanism and did not decide
> it. The owner's follow-up decisions (2026-09-27, Q-034) set the low-end
> target, the first-launch default and the pause, and asked the Architect to
> propose the `Low` and `Medium` values (below).

**The rule that makes it safe.** Graphics quality is **presentation only**.
It never changes sim state, the tick rate, a hash, a checkpoint, a command
or the frame loop's `Step` count. It is not in a save and never reaches the
sim. It can change `SetPromoted` calls (`DrawAgents`), and that is safe,
because promotion is outcome-neutral by construction (`09` §9.1, §9.7). It
changes only **render-driven** promotion (§15.7), never promotion for sim
purposes (`01-architecture.md` promotion rule 2, which is not
`app.render`'s). `test_render_loop_is_outcome_neutral_across_graphics_changes`
and the `determinism_promotion` gate prove it.

**Invariant: graphics never affect gameplay or difficulty.** This is binding,
owner, 2026-09-27 (D10 addendum).

1. **Same information at every setting.** For any sim state, camera and
   `GraphicsSettings`, every primitive `Build` produces outside the `Agent`
   layer is **identical** in kind, layer, colour, sprite, geometry, facing
   and source (Q-130), and so is their order. That covers runways and
   their markings, taxiways, junction fills and centrelines, stands,
   landside nodes, queue fill, lane pips and aircraft. Only `Agent`-layer primitives
   (individual passenger dots) may differ. They are decoration, since queue
   length is always shown by the queue fill. A lower preset may simplify
   **how** something is drawn, never **whether** it is shown.
2. **This binds future elements too.** Any later gameplay-relevant element,
   such as alerts, threshold states, flight or delay states, or anything the
   player acts on, is added outside the `Agent` layer, or in `app.ui`, and is
   never gated by a graphics knob. A knob that would hide or thin one is
   rejected in review. It needs its own owner decision.
3. **No effect on time or input.** No knob changes the sim tick, the game
   speed, pause, the pacer, command timing or any click target. Pacing
   depends only on elapsed real time (§15.8), and the `FrameRateCap` floor
   of 15 keeps 4x real-time. Hit-testing uses the layout in world space (`17`
   §17.5), which no knob touches. The backend reports clicks in full-screen
   pixel coordinates whatever `ResolutionScalePercent` is (§15.10). The
   settings panel pauses the sim while it is open (`17` §17.4a, owner,
   Q-034). That pause depends only on whether the panel is open, never on a
   knob's value, and pausing is outcome-neutral (§15.8).
4. **Performance scaling is presentation only.** The sim runs the same fixed
   tick at every setting. A low preset saves only presentation cost: drawing,
   agent-view derivation and backend rendering.

Tests: `test_scene_gameplay_primitives_identical_at_every_graphics_setting`
builds a max-tier fake scene at each preset and at custom extremes, and
asserts that the non-`Agent` primitive lists are equal element by element.
`17` §17.10 adds the UI counterpart.

```
enum GraphicsPreset { Low, Medium, High, Custom }   // saved by name, never by number (17 §17.4a)

readonly struct GraphicsSettings {
  GraphicsPreset Preset
  // scene-layer knobs
  bool   DrawAgents                 // render-driven promotion and agent dots (§15.5, §15.7)
  int32  MaxDrawnAgentsPerNode      // 1 .. MAX_DRAWN_AGENTS_PER_NODE
  // backend knobs (§15.10)
  int32  FrameRateCap               // 0 = uncapped, else 15 .. 240 frames per second
  int32  ResolutionScalePercent     // 50 .. 100
  bool   AntiAliasing
}

RenderFactory.GraphicsForPreset(GraphicsPreset preset) -> GraphicsSettings   // Custom throws ArgumentException
RenderFactory.ValidateGraphics(in GraphicsSettings s) -> GraphicsSettings    // clamps every knob into range
```

Both are on `RenderFactory`, which stays the module's one factory (`08`
§8.11a). Below, they are called `ForPreset` and `Validate`.

- **Knobs.** They are exactly the six fields above. The scene layer reads
  `DrawAgents` and `MaxDrawnAgentsPerNode`, and the backend reads the other
  three (§15.10). `Preset` records the preset that produced the values.
  Any knob changed by hand makes it `Custom`. Visual effects do not exist
  at Phase 1 (§15.5, flat sprites, §15.15), so there is no effects knob,
  and the sprites have no knob of their own: the atlas is the same at
  every setting (Q-130). A new knob
  is added by amendment.
- **Bounds.** They are structural, not player-experience values.
  `FrameRateCap ≥ 15` keeps the pacer's catch-up cap from binding at 4x
  (§15.8: it binds below about 13 fps). `MaxDrawnAgentsPerNode ≥ 1` keeps a
  promoted node visibly promoted.
- **`Validate`** clamps each integer into its range, maps a non-zero
  `FrameRateCap` below 15 to 15, and leaves `Preset` unchanged. Every
  settings value entering the scene layer or the backend has passed through
  it.
- **Monotonicity (binding on the owner's values).** Every knob is `Low ≤
  Medium ≤ High` in cost: `DrawAgents` false ≤ true, a smaller cap ≤ a
  larger one, and `FrameRateCap` read as cost, with 0 counting as the most
  expensive. So a lower preset never costs more.
- **`High`** is the Phase 1 behaviour this file already specifies:
  `DrawAgents = true`, `MaxDrawnAgentsPerNode = MAX_DRAWN_AGENTS_PER_NODE`,
  `FrameRateCap = 0`, `ResolutionScalePercent = 100`, `AntiAliasing = true`.
  That is not a new value.
- **`Low` and `Medium` values.** The owner asked the Architect to propose
  them (Q-034). **LOW CONFIDENCE — owner may revise.**

  | Knob | `Low` | `Medium` | `High` |
  |---|---|---|---|
  | `DrawAgents` | false | true | true |
  | `MaxDrawnAgentsPerNode` | 32 | 64 | 256 (`MAX_DRAWN_AGENTS_PER_NODE`) |
  | `FrameRateCap` | 60 | 60 | 0 (uncapped) |
  | `ResolutionScalePercent` | 75 | 100 | 100 |
  | `AntiAliasing` | false | false | true |

  Why these values:
  - `Low` removes what integrated graphics pays for most: anti-aliasing,
    fill rate (75 % scale is about 56 % of the pixels), and the agent dots.
    Agents are decoration under invariant 1, so no information is lost.
    Its `MaxDrawnAgentsPerNode` applies only if the player turns
    `DrawAgents` back on, and it keeps monotonicity.
  - `Low` caps at 60 rather than lower, because the render target is 60 fps
    (`01`, §15.11). A lower cap would miss it by construction. The cap also
    stops a laptop from rendering frames it cannot show, which saves heat
    and battery.
  - `Medium` keeps full resolution and the agent dots, at a quarter of
    `High`'s dots per node, without anti-aliasing and capped at 60.
  - The table is monotone in every knob.
- **Default on first launch: `Medium`** (HUMAN DECISION — owner,
  2026-09-27, Q-034). It is used when no valid preference is stored
  (`16` §16.6). The spec binds only `Low` to the render target on minimum
  spec (§15.11). `Medium` is not bound there.
- **The low-end target** (HUMAN DECISION — owner, 2026-09-27, Q-034). The
  minimum spec is a 4-core CPU, 8 GB RAM and integrated graphics with no
  dedicated VRAM (`01-architecture.md`). `Low` holds the render target
  there at max tier (§15.11). Shared GPU memory counts against the 8 GB
  (`16` §16.10). CI has no GPU, so this is checked by a manual measurement
  on a minimum-spec machine at 1920 × 1080, in the T-025 playtest or a
  later one. The `Low` row keeps its LOW CONFIDENCE marker until that
  measurement is recorded in `CHANGELOG.md`. If it misses, the `Low` values
  are corrected by amendment, never by a worker. **LOW CONFIDENCE**: the
  1920 × 1080 measurement resolution is the Architect's, so the check has a
  fixed condition. Note also that graphics settings cannot reduce the
  **sim's** cost (6 ms per tick at max tier, `01`), and the 4-core CPU
  minimum is unchanged. On a laptop, CPU and integrated GPU share one power
  and heat budget, so heavy drawing can slow the sim's CPU. That is one
  more reason `Low` caps its frame rate. If real time is still missed, the
  pacer slows gracefully (§15.8), and outcomes stay unchanged.

---

## 15.15 Sprite art — owner decision, 2026-10-07 (Q-130)

> **HUMAN DECISION — owner, 2026-10-07:** the abstract shapes are too
> ugly, so the game gets real top-down art **before** the T-025 playtest.
> The art is agent-made flat vector art: a clean, consistent, flat
> top-down style, made by agents, with no third-party assets. The
> Architect specified the mechanism and the style guide below. The look
> is aesthetic, not balance.

**Where the decisions live.** The scene layer decides everything that can
be tested. It chooses each primitive's `Sprite` and `Facing` (§15.5), and
it builds the atlas pixels (`BuildSpriteAtlas`). The backend only uploads
the atlas and maps each primitive to a tinted, rotated quad (§15.10). The
alternative, a fixed `(DrawLayer, ColourRole, kind)` to sprite map inside
the backend, was rejected: it would put an untested decision in the engine
and could not tell aircraft sizes apart. No older primitive's geometry,
colour, layer or source changes. Aircraft keep diameter `AircraftSize`,
and size categories differ inside that square (the proportion table
below).

### The pipeline: art as code

The art is **C# source in the scene layer**, under
`src/app/render/Scene/Art/`, compiled into `AirportSim.App.Render.dll`.
`BuildSpriteAtlas` rasterises it into plain bytes. The art reaches the
player inside that plugin, through the existing plugin copy (`16` §16.2).
There is no asset file, no `.meta`, no import setting, no build step and
nothing under `unity/`.

Why this option (owner's option (a)):

- **Reviewable as text, and written with Edit/Write.** The art is integer
  coordinates in a declarative table, and nothing is generated by a
  script.
- **Tested in CI.** The rasteriser is headless and deterministic, so
  `dotnet test` checks the art's shape, size and determinism, and CI is
  the only place behaviour is tested (§15.3).
- **No package and no editor step.** Option (b), committed SVG and an
  editor step that rasterises it, needs `com.unity.vectorgraphics`
  (a preview package), or a third-party SVG library, which brings native
  dependencies and an import pipeline that no agent can run or CI test. It
  would also add one more hand-written-`.meta` risk (`16` §16.2). Its
  output would be untested, and SVG is a much larger surface than the
  art needs.
- **Rejected (c), committed PNGs:** agents cannot write binary files.

### The art format (binding)

- `Art/SpriteArt.cs` holds one definition per `SpriteId`, in enum order.
  The rasteriser is in the same directory. Every art type is `internal`.
  The public surface is only `SpriteId`, `AtlasRect`, `SpriteAtlas` and
  `BuildSpriteAtlas` (§15.9).
- **Design space.** Integer design units. A sprite's visible square is
  `0 .. ART_UNITS` on both axes, with the origin at bottom-left and `+Y`
  forward (an aircraft's nose; a threshold's runway side; a segment's
  `B` end). The edge-to-edge sprites (`Solid`, `RunwayEdges` and
  `CentreStripe`) extend their shapes to `−64` and `1088` along every
  axis they span, so the bleed border is filled. Every other sprite keeps
  every shape, with its outline, inside `16 .. 1008`.
- **A definition** has an outline width (design units, 0 for none), an
  outline value, an edge value, a mirror flag and an ordered list of
  shapes.
- **A shape** is a polygon (3 or more integer vertices, simple, with
  either winding) or a circle (integer centre and radius). Each shape
  also has a value (grey, 0 to 255) and an alpha (0 to 255).
- **Mirror.** When set, each shape is drawn twice, itself and then its
  reflection about `x = ART_UNITS / 2`. Aircraft, passengers and the
  threshold use it, so they are symmetric by construction.
- **Literal numbers only**, except that the six aircraft definitions may
  come from one function of their row in the proportion table. Definitions
  are built inside each `BuildSpriteAtlas` call, with no static mutable
  state (§15.3).

### Rasterisation (binding)

- **Cells.** Cell `i = (int)SpriteId` is at column `i % 4` and row
  `i / 4`, with rows counted from the bottom of the atlas. Cell 15 is
  unused and fully transparent (all bytes 0).
- **Rects.** `U0 = (col × 256 + 8) / 1024` and
  `U1 = ((col + 1) × 256 − 8) / 1024`, and the same for `V` with `row`.
  These are `ATLAS_CELL_PIXELS` and `ATLAS_CELL_BORDER_PIXELS` over
  `ATLAS_SIZE`, and are exact in `float`. The rectangle is the visible
  square.
- **Per mip `m`**, with `0 ≤ m < ATLAS_MIP_COUNT`: the cell side is
  `c = 256 >> m` pixels, the border is `b = 8 / 2^m` pixels (0.5 at mip 4),
  the visible side is `v = c − 2b`, and there are `u = ART_UNITS / v`
  design units per pixel. Pixel `(i, j)` of a cell, counted from the
  cell's bottom-left, samples the design point
  `p = ((i + 0.5 − b) × u, (j + 0.5 − b) × u)`. All of this is in `double`.
- **Coverage.** `d_s(p)` is the signed distance in design units, negative
  inside. For a circle it is `|p − centre| − radius`. For a polygon it is
  the distance to the nearest edge, negative when `p` is inside by the
  even-odd rule. The shape's coverage is `clamp(0.5 − d_s / u, 0, 1)`.
  This anti-aliases every edge in the texture itself, so `Low` (no MSAA)
  still draws clean edges.
- **Compositing**, premultiplied, starting from value 0 and alpha 0.
  First, if the outline width `w > 0`, an outline layer with
  `d_o = (min over all shapes of d_s) − w`, the outline value and alpha
  255. Then every shape in order, mirror copies included, "over" the
  result with alpha `alpha × coverage / 255`. Output: alpha
  `A8 = floor(A × 255 + 0.5)`. RGB are all `floor(C / A + 0.5)` when
  `A8 > 0`, else the definition's edge value, so that filtering at an edge
  never pulls in a foreign colour.
- **Every mip is rasterised directly** this way, and none is downsampled.
  So no cell bleeds into another at any level. Every byte of every mip is
  written. A shape may be skipped for a pixel outside its bounding box
  grown by `w + u`, which changes no output.

### Style guide

- **Flat.** No gradients, no noise, no shadows, no text, no logos and no
  real-world liveries (`00-overview.md`: no licensing). Shapes are
  separated by value steps and by outlines.
- **Value, not colour.** Sprites are greyscale. The palette colour tints
  them by multiplication (§15.5), so one sprite serves every state. The
  value steps are: highlight 255, body 230, secondary 200 to 215, detail
  110 to 120, glazing 60, and outline 40.
- **Outline weight** (design units, out of 1024): aircraft 20 (about 2 %
  of the cell), lane pip and passenger 48, and none on markings, surfaces,
  stands and terminals, which use a border band instead.
- **Scale per layer.** World sizes come from the layout: runway `Width`,
  `TaxiwayWidth`, `StandSize`, `AircraftSize` and `AgentSize`. A sprite
  fills its primitive, and only aircraft vary inside their square.

| `SpriteId` | Used by | Art |
|---|---|---|
| `Solid` | runway and taxiway surfaces, queue fill | one square, value 255 |
| `Disc` | taxiway junction fill | circle at `(512,512)`, radius 496, value 255 |
| `RunwayEdges` | runway edge lines | two full-height bars, `x` 16 to 48 and 976 to 1008, value 255 |
| `CentreStripe` | runway dashes, taxiway centrelines | one full-height bar, `x` 480 to 544, value 255 |
| `RunwayThreshold` | runway thresholds | mirrored: four bars 64 wide, `x` from 64 with gaps of 48, `y` 128 to 832, value 255 |
| `Stand` | stands | square 16 to 1008 at value 200, inner square 64 to 960 at value 235 |
| `Terminal` | landside nodes | square 16 to 1008 at value 170, inner roof 56 to 968 at value 235, two roof seams `y` 332 to 348 and 676 to 692 across the roof, value 210 |
| `LanePip` | lane pips | booth square 128 to 896 at value 230, outline 48, officer circle radius 128 at value 120 |
| `Passenger` | agents | mirrored: shoulders polygon about 640 by 380 at value 230, head circle radius 150 at value 255, outline 48 |
| `AircraftA` to `AircraftF` | aircraft | mirrored: fuselage (body, nose circle, tail cone) 255; wings and tailplane 215; engines 110; windscreen 60; outline 20 |

**Aircraft proportions** are fractions of `ART_UNITS`, outline included,
with span across `X`, length along `Y` and the nose at `+Y`, centred on
`(512, 512)`. The fuselage is `length / 10` wide. Sweep is the wingtip's
leading-edge setback as a fraction of the half-span. Size categories F
and A differ by about 2.4 times in span, which reads at a glance and is
compressed from the real ratio (about 5) so that `AircraftA` stays
legible.

| `SpriteId` | Ordinal (`data/`) | Span | Length | Sweep | Engines |
|---|---|---|---|---|---|
| `AircraftA` | 0 (`size_a`) | 0.40 | 0.40 | 0.05 | 2 propellers on a straight wing |
| `AircraftB` | 1 (`size_b`) | 0.50 | 0.50 | 0.05 | 2 propellers on a straight wing |
| `AircraftC` | 2 (`size_c`) | 0.62 | 0.64 | 0.35 | 2 under the wing |
| `AircraftD` | 3 (`size_d`) | 0.76 | 0.76 | 0.38 | 2 under the wing |
| `AircraftE` | 4 (`size_e`) | 0.88 | 0.88 | 0.42 | 2 large, under the wing |
| `AircraftF` | 5 (`size_f`) | 0.96 | 0.94 | 0.45 | 4 under the wing |

**Palette** (`DefaultPalette.asset`, sRGB hex, alpha 255 unless given):

| Entry | Colour | Entry | Colour |
|---|---|---|---|
| Background (grass) | `#6F8F5E` | `Agent` | `#2E5A88` |
| `Runway` | `#3A3E44` | `AircraftMoving` | `#FFFFFF` |
| `RunwayQueued` | `#6A4B2F` | `AircraftHolding` | `#F3B13C` |
| `Taxiway` | `#50565D` | `AircraftOnStand` | `#A9D2EE` |
| `StandFree` | `#9AA0A6` | `LaneOpen` | `#43A047` |
| `StandOccupied` | `#B5A679` | `LaneClosed` | `#C62828` |
| `LandsideNode` | `#D9D5CC` | `RunwayMarking` | `#F4F4EE` |
| `QueueFill` | `#E8A33A`, alpha 140 | `TaxiwayMarking` | `#F2C230` |

> **LOW CONFIDENCE — Unity behaviour no agent can run.** These are the
> Architect's reading: that `Sprites/Default` multiplies `_MainTex` by the
> vertex colour with straight alpha; that a `Texture2D` created with an
> explicit mip count accepts each level through `SetPixelData` and keeps
> them through `Apply(false, true)`; and how the colour space treats the
> tints. The atlas is created as sRGB. The palette is sRGB, and the backend
> converts it with `Color.linear` once at start-up if
> `QualitySettings.activeColorSpace` is `Linear`. `unity-build` checks
> only that it compiles. The look is checked by eye in the T-025
> playtest, and a mismatch is a spec question, filed, not a workaround.
> The atlas build time on Mono is also unmeasured (§15.11).

### Tests (scene-layer task)

New, phrased per `07-conventions.md`:

- `test_scene_sprites_follow_the_draw_table`: every row of §15.5 has its
  sprite, colour, layer and source, and `Facing` is `(0, 0)` on every
  primitive that is not an aircraft or a threshold.
- `test_scene_runway_markings_follow_the_integer_rule`: the fixture
  runway gives `n = 20` and `s_0 = 122`, with exact coordinates. A
  diagonal runway exercises the truncating division. A short runway with
  `R < W` keeps its edges and thresholds and has no dashes. A zero-length
  runway has no markings. Markings keep `RunwayMarking` while the runway
  is `RunwayQueued`.
- `test_scene_taxiway_junction_fill_and_centrelines`: a fill only at nodes
  with two or more incident edges, one centreline per edge on the edge's
  geometry, and within the layer every surface, then every fill, then
  every centreline.
- `test_scene_aircraft_facing_follows_the_five_rules`: each rule,
  including the lowest-id incident edge on a stand node with two edges,
  the runway direction from either end, both destinations, and every
  `(0, 0)` fallback.
- `test_scene_aircraft_sprite_follows_size_category`: ordinals 0 to 5,
  ordinal 6 giving `AircraftF`, and `AircraftC` for an unresolved type, an
  unresolved category, a missing flight, a null `Schedule` and a null
  `Content`. Content is read only at construction. `TryGetFlight` is
  called once per drawn aircraft per rebuild, and never for an undrawn
  one.
- `test_sprite_atlas_shape_and_rects_match_spec`: `Size`, the mip count,
  each mip's byte length, every `Rects` entry exactly, and cell 15 all
  zero.
- `test_sprite_atlas_is_deterministic`: two builds are equal byte for
  byte, and each call returns fresh arrays.
- `test_sprite_atlas_every_sprite_is_drawn_inside_its_cell`: every
  sprite has a texel with alpha ≥ 128 at mips 0 to 2, and alpha > 0 at
  mips 3 and 4. Every sprite except the three edge-to-edge ones has an
  all-transparent border ring at mip 0. `Solid` is `(255, 255, 255, 255)`
  on every texel of its cell at every mip.
- `test_sprite_atlas_aircraft_follow_the_proportion_table`: at mip 0, the
  alpha ≥ 128 bounding box of each aircraft has width `span × 240` and
  height `length × 240`, each ± 3 pixels. Both grow strictly from `A` to
  `F`. Each aircraft is mirror-symmetric, so the texel at cell column `i`
  equals the one at `255 − i` within ± 1 per byte.

Merged T-020 tests that Q-130 changes. The scene-layer task's Test
Author updates each one to this spec, as Q-125 did for the UI surface
test. No worker edits a test.

- `test_scene_runway_colour_follows_queue_length_and_taxiways_follow_edges`
  **breaks**. It asserts 3 primitives in the `Taxiway` layer, and now the
  junction fill and the centrelines are in that layer too. It counts the
  `TaxiEdge`-sourced primitives instead, and asserts each one's `Solid`
  sprite.
- `test_scene_calls_only_listed_sim_members` is **extended**. The
  max-tier fakes gain a schedule and a content index, which are guarded
  like the others, and the test asserts that content is read only at
  construction.
- `test_scene_gameplay_primitives_identical_at_every_graphics_setting`
  and `test_scene_primitive_order_is_stable` are **covered by the kit**.
  The kit's primitive printer (`Prims.Show`) gains `Sprite` and `Facing`,
  so both tests compare the new fields. Their own code is unchanged.
- `test_scene_build_within_frame_budget_at_max_tier` and
  `test_scene_build_and_update_allocate_nothing_after_first_call` are
  **extended**. The max-tier scene gains the schedule and content fakes,
  so the per-rebuild `TryGetFlight` is measured and must not allocate.
- `test_render_constants_match_spec_values_and_types` is **extended**
  with the five rows of §15.2.

Unchanged, and still binding: every other §15.12 test. Their fakes use the
three-argument `RenderSources` constructor, so every aircraft is
`AircraftC`. No `app.ui` or `app.host` test changes, because both
three-argument constructors stay (§15.9, `16` §16.4).

### For the Planner (Q-130)

Three tasks. The second and third start once the first is merged, and
they can run in parallel.

1. **Render scene: sprites, facing, markings and the atlas.** Test Author
   first (the tests above), then a worker. Writable paths:
   `src/app/render/Scene/**` (including the project reference to
   `sim.schedule`) and `tests/app/render/**`. It depends on nothing
   unmerged.
2. **Render backend: the atlas and sprite quads.** A worker only. CI has no
   behaviour test for the backend (§15.3). Writable path:
   `src/app/render/Unity/**`, meaning `RenderBackend.cs` and
   `DefaultPalette.asset`, which keeps its GUID. Done-when:
   `unity-build` green (`16` §16.2), and the Reviewer checks it against
   §15.10 line by line. **Merge it right after task 1.** Until then, the
   merged backend's palette has 13 roles, and indexing it with
   `RunwayMarking` or `TaxiwayMarking` throws in a playable build. The
   checkpoint smoke is unaffected, because it runs with the backends
   deactivated (`16` §16.7).
3. **Host: content and schedule into the render sources.** Test Author
   then a worker. Writable paths: `src/app/host/**` and
   `tests/app/host/**` (`16` §16.4, §16.5, §16.11).

The T-025 playtest waits for all three.
