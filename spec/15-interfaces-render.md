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
  aircraft's visual and livery (§15.16, Q-130).

Since 2026-10-07 the draw list is drawn as **art**: flat top-down vector
art, made by agents, with no third-party assets, which must look like a
real airport (owner decisions, Q-130, §15.15). The scene layer emits only
semantic visual ids, facings and paint (§15.16). The 2D art and its
atlas belong to the 2D backend (§15.17). "Flat-colour" elsewhere in this
file now means this flat style. **3D is planned after the T-025 gate**
(owner decision, 2026-10-07). It is not designed here: §15.15 only keeps
the 2D design transferable.

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

World units are metres at Phase 1, with +Y pointing up the screen. They mean
nothing to the sim.

**In C# (Q-099).** The table lives in `public static class RenderConstants`
in `AirportSim.App.Render`, as `public const` members with their IDL names
(`07` L10). The types are: `AGENT_ZOOM_THRESHOLD`, `MAX_DRAWN_AGENTS_PER_NODE`
and `MAX_DRAWN_LANES_PER_NODE` are `int`, `MAX_CATCHUP_TICKS_PER_FRAME` is
`uint` (the type `ITickPacer.Advance` returns), and
`REAL_MICROSECONDS_PER_TICK_1X` is `long`. The scene layer has no atlas
constant: those belong to the 2D art (§15.17, Q-130). §15.7 compares `ViewHeight` with
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

**The 2D art (Q-130, §15.17)** is a third part: `src/app/render/Art2D/`,
the assembly `AirportSim.App.Render.Art2D`. It is headless like the scene
layer, under the same rules below, built by `AirportSim.sln` and tested in
`tests/app/render/`. It is the 2D backend's half that can be tested. It
references the scene layer's types, and the scene layer never references
it (asserted by test). The Unity backend references both.

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

enum AreaKind { Apron, Terminal, Pier, ControlTower }                     // Q-130
readonly struct LayoutArea   { uint32 Id; AreaKind Kind; int32 MinX; int32 MinY; int32 MaxX; int32 MaxY }   // Q-130
readonly struct LayoutBridge { uint32 Id; int32 X0; int32 Y0; int32 X1; int32 Y1; int32 Width }             // Q-130; a jet bridge

readonly struct RenderLayout {
  IReadOnlyList<TaxiNodePosition> TaxiNodes
  IReadOnlyList<RunwayGeometry>   Runways
  IReadOnlyList<FlowNodeBox>      FlowNodes
  int32 StandSize                         // side of a stand box
  int32 AircraftSize                      // diameter of an aircraft dot
  int32 AgentSize                         // diameter of an agent dot
  int32 TaxiwayWidth
  IReadOnlyList<LayoutArea>       Areas   // Q-130; scenery: paved aprons and building footprints
  IReadOnlyList<LayoutBridge>     Bridges // Q-130; scenery: jet bridges
}

interface IRenderLayoutLoader {
  RenderLayout Load(ReadOnlySpan<byte> file, string sourceName, in AirsideLayout? airside)
}
```

Coordinates and sizes are integers so that a layout file carries no floats,
following the spirit of `04-data-schemas.md`.

**Scenery (Q-130).** The owner's acceptance bar is that the scene looks
like a real airport. The layout could not say where pavement and
buildings are, so it gains two lists. Both are renderer-agnostic
geometry with semantic kinds, and neither names an atlas or a sprite.
Grass is not data: it is the ground everywhere else, the backend's
background colour. **C# shape:** `RenderLayout` keeps its constructor
with the seven earlier fields, which sets `Areas` and `Bridges` to empty
lists, and gains one that takes all nine fields in declared order.

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

**Version 2 (Q-130)** has `"schema_version": 2` and every key above,
plus exactly these two:

```
  "areas":   [ { "id": <uint32>, "kind": "apron" | "terminal" | "pier" | "control_tower",
                 "min_x": <int32>, "min_y": <int32>, "max_x": <int32>, "max_y": <int32> }, ... ],
  "bridges": [ { "id": <uint32>, "x0": <int32>, "y0": <int32>,
                 "x1": <int32>, "y1": <int32>, "width": <int32> }, ... ]
```

A version 1 file is still valid and loads with empty `Areas` and
`Bridges`. Any other version is a parse failure. A `kind` string other
than the four is a parse failure. `kind` is the only string value in the
file.

- Each object has exactly the keys shown for its version.
  `schema_version` must be `1` or `2`.
- An integer is `0` or `-?[1-9][0-9]*`. One outside its C# type's range is
  a parse failure. Values inside the range parse, and the checks below
  apply to them.
- The arrays may be in any order, and any of them may be empty. `Load`
  returns `TaxiNodes`, `Runways`, `FlowNodes`, `Areas` and `Bridges` in
  ascending id order.
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
   (names the lowest one without);
5. (Q-130) always, and in this order: `LayoutArea.Id` is unique;
   `MinX < MaxX` and `MinY < MaxY`; and a `ControlTower` area is square
   (`MaxX − MinX = MaxY − MinY`). Then `LayoutBridge.Id` is unique,
   `Width > 0`, and `(X0,Y0) ≠ (X1,Y1)`. Each names the failing id.

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

| Source | Primitive | Geometry | `ColourRole` | `DrawLayer` | `Visual` (Q-130) | `SourceRef` |
|---|---|---|---|---|---|---|
| each layout area of kind `Apron` (Q-130) | `Box` | the area | `Apron` | `Ground` | `Apron` | `(Apron, id, 0)` |
| each layout area of kind `Terminal`, `Pier` or `ControlTower` (Q-130) | `Box` | the area | `Building` | `Ground` | `TerminalBuilding`, `Pier` or `ControlTower` | `(Building, id, 0)` |
| each runway | `Segment` | `(X0,Y0)`–`(X1,Y1)`, width `Width` | `RunwayQueued` if `RunwayQueueLength > 0`, else `Runway` | `Runway` | `RunwaySurface` | `(Runway, id, 0)` |
| runway markings (Q-130) | edge lines, thresholds, designators and centreline dashes, §15.16 | §15.16 | `RunwayMarking` | `Runway` | §15.16 | `(RunwayMarking, id, Sub)` |
| each taxi edge | `Segment` | position of `From` to position of `To`, width `TaxiwayWidth` | `Taxiway` | `Taxiway` | `TaxiwaySurface` | `(TaxiEdge, id, 0)` |
| junction fill (Q-130): each taxi node with two or more incident edges in `Layout().Edges` | `Dot` | centred on the node's position, diameter `TaxiwayWidth` | `Taxiway` | `Taxiway` | `TaxiwayJunction` | `(TaxiNode, id, 0)` |
| taxiway centreline (Q-130): each taxi edge | `Segment` | the taxi edge's geometry, width `TaxiwayWidth` | `TaxiwayMarking` | `Taxiway` | `TaxiwayCentreline` | `(TaxiCentreline, id, 0)` |
| each stand | `Box` | centred on the stand's `Node` position, side `StandSize` | `StandOccupied` if `StandState.Occupant` is set, else `StandFree` | `Stand` | `StandPad` | `(Stand, id, 0)` |
| stand lead-in (Q-130): each stand | `Dot` | centred on the stand's `Node` position, diameter `StandSize`, `Facing` the stand's nose-in vector (§15.16) | `TaxiwayMarking` | `Stand` | `StandLeadIn` | `(StandMarking, id, 0)` |
| stand number (Q-130): each digit of each stand's `StandId` | `Dot` | §15.16 | `TaxiwayMarking` | `Stand` | `MarkingDigit0` to `MarkingDigit9` | `(StandNumber, id, digit index)` |
| each layout bridge (Q-130) | `Segment` | `(X0,Y0)`–`(X1,Y1)`, width `Width` | `Building` | `Stand` | `JetBridge` | `(JetBridge, id, 0)` |
| each `FlowNodeBox` | `Box` | the box | `LandsideNode` | `LandsideNode` | `TerminalZone` | `(FlowNode, id, 0)` |
| queue fill, if `Population > 0` | `Box` | same `MinX`, `MinY`, `MaxY`; width = box width × `min(1, Population / FillCapacity)` | `QueueFill` | `QueueFill` | `QueueFill` | `(QueueFill, id, 0)` |
| lane pips of a `FlowNodeBox` whose node `TryGetLaneState` accepts | `Dot` | inside the box, one per server up to `MAX_DRAWN_LANES_PER_NODE`, diameter `AgentSize` | `LaneOpen` for the first `ServersOpen` pips, `LaneClosed` for the rest | `Lane` | `LanePip` | `(Lane, id, index)` |
| agents of a promoted `FlowNodeBox` | `Dot` | inside the box, one per agent, diameter `AgentSize`; `Paint` by passenger (§15.16) | `Agent` | `Agent` | `Passenger` | `(Agent, id, rank)` |
| each tracked aircraft that is on the graph | `Dot` | see below, diameter `AircraftSize`; `Facing` and `Paint` by livery (§15.16) | by phase, below | `Aircraft` | by size category (§15.16) | `(Aircraft, flight, 0)` |

**Visuals, facing, paint and scenery (Q-130)** are §15.16: what every
new field holds, the marking and scenery rules, and the draw order of the
new rows. Layout areas and bridges (§15.4) are scenery and are drawn
whenever the layout has them, whichever sim modules are present. The
stand, marking and aircraft rows follow the airside rule below.

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
terrain beyond the layout's areas, weather, and interpolation between
ticks. Painted stand numbers and runway designators are ground markings
made of digit visuals, not text (§15.16). They are never localised, and
`app.ui` still owns all text.

`ColourRole` names a meaning, not a colour. Mapping roles to RGB is the
backend's palette (§15.10). It is aesthetic, not balance, and no test checks
it. Phase, occupancy, queue and lane states still read by role colour.
Each visual says which of its parts the role colours, and which parts take
the per-instance `Paint` (§15.16, §15.17). An aircraft's role colours its
status outline, and its livery colours the rest. There is one visual per
thing drawn, never one per state or per livery.

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

enum DrawLayer     { Ground, Runway, Taxiway, Stand, LandsideNode, QueueFill, Lane, Agent, Aircraft }   // draw order; Ground inserted first, Q-130
enum PrimitiveKind { Box, Segment, Dot }
enum ColourRole {
  Runway, RunwayQueued, Taxiway, StandFree, StandOccupied,
  LandsideNode, QueueFill, Agent,
  AircraftMoving, AircraftHolding, AircraftOnStand,
  LaneOpen, LaneClosed,                                 // appended, Q-010
  RunwayMarking, TaxiwayMarking, Apron, Building        // appended, Q-130
}
enum SourceKind {
  Runway, TaxiEdge, Stand, FlowNode, QueueFill, Agent, Aircraft, Lane,
  RunwayMarking, TaxiNode, TaxiCentreline,              // appended, Q-130
  Apron, Building, StandMarking, StandNumber, JetBridge // appended, Q-130
}
enum VisualId {                                         // Q-130: WHAT is drawn, never how (§15.16)
  RunwaySurface, RunwayEdgeLines, RunwayThreshold, RunwayCentreDash,
  TaxiwaySurface, TaxiwayJunction, TaxiwayCentreline,
  Apron, TerminalBuilding, Pier, ControlTower, JetBridge,
  StandPad, StandLeadIn,
  MarkingDigit0, MarkingDigit1, MarkingDigit2, MarkingDigit3, MarkingDigit4,
  MarkingDigit5, MarkingDigit6, MarkingDigit7, MarkingDigit8, MarkingDigit9,
  TerminalZone, QueueFill, LanePip, Passenger,
  AircraftA, AircraftB, AircraftC, AircraftD, AircraftE, AircraftF   // by size category
}

readonly struct Rgb   { uint8 R; uint8 G; uint8 B }    // Q-130; sRGB
readonly struct Paint {                                 // Q-130; per-instance region colours (§15.16)
  Rgb   Region0; Rgb Region1; Rgb Region2; Rgb Region3; Rgb Region4
  uint8 Mark                                            // aircraft: (uint8)LogoMark; 0 otherwise
}
enum AircraftRegion  { Fuselage, Tail, Cheatline, Engines, Logo }   // value = Paint region index
enum PassengerRegion { Top, Bottom, Skin, Hair, Bag }                // value = Paint region index
enum LogoMark        { None, Disc, Ring, Chevron, Star, Bars, Diamond, Crescent }

readonly struct SourceRef {
  SourceKind Kind
  uint64     Id                // RunwayId / TaxiEdgeId / TaxiNodeId / StandId / NodeId / FlightId / area or bridge id value
  int32      Sub               // agent rank, lane index, marking or digit index (§15.16); 0 otherwise
}

readonly struct DrawPrimitive {
  PrimitiveKind Kind
  DrawLayer     Layer
  ColourRole    Colour         // the state colour (§15.5)
  VisualId      Visual         // Q-130
  WorldPoint    A              // Box: min corner.  Segment: start.  Dot: centre.
  WorldPoint    B              // Box: max corner.  Segment: end.    Dot: unused.
  float         Size           // Box: unused.      Segment: width.  Dot: diameter.
  WorldPoint    Facing         // Q-130. Dot: the direction the visual's forward points, an exact
                               // integer-valued vector (§15.16); (0,0) = +Y. Box and Segment: (0,0)
                               // (a Segment's forward is A to B).
  Paint         Paint          // Q-130; all zero unless the visual has regions (§15.16)
  SourceRef     Source
}

readonly struct AirlineLivery { AirlineId Airline; Livery Livery }   // Q-130
readonly struct Livery {                                             // Q-130
  Rgb Fuselage; Rgb Tail; Rgb Cheatline; Rgb Engines; Rgb Logo; LogoMark Mark
}
readonly struct RenderLooks {                                        // Q-130, §15.16; renderer-agnostic
  Livery                        DefaultLivery
  IReadOnlyList<AirlineLivery>  Airlines        // ascending AirlineId, unique
  IReadOnlyList<Rgb>            Tops            // each list non-empty
  IReadOnlyList<Rgb>            Bottoms
  IReadOnlyList<Rgb>            Skins
  IReadOnlyList<Rgb>            Hairs
  IReadOnlyList<Rgb>            Bags
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
  IScheduleSystem? Schedule    // Q-130; null: AircraftC and the default livery for every aircraft
  IContentIndex?   Content     // Q-130; null: AircraftC for every aircraft
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
RenderFactory.CreateSceneBuilder(in RenderSources sources, in RenderLayout layout, in RenderLooks looks) -> ISceneBuilder   // Q-130
RenderFactory.LoadLooks(IContentSource source) -> RenderLooks         // Q-130, §15.16
RenderFactory.DefaultLooks() -> RenderLooks                           // Q-130, §15.16; a fresh value per call
```

The two-argument `CreateSceneBuilder` is the three-argument one with
`DefaultLooks()`.

**C# shape (Q-130).** As for every type in this file, the fields above
are get-only properties, and each struct has one constructor taking them
in declared order. The exceptions keep every caller written before Q-130
compiling and behaving as before. `RenderSources` has a second
constructor, `(host, airside, flow)`, which sets `Schedule` and `Content`
to null. `RenderLayout` has its seven-field one (§15.4). `DrawPrimitive`'s
one constructor takes its ten fields in declared order. `Paint`'s default
value is all zero.

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
- **Art (Q-130, §15.17).** All the 2D art logic is in the headless
  `AirportSim.App.Render.Art2D` assembly, where it is tested. The backend
  only moves its output into the engine:
  - **Once, at start-up:** call `Art2DFactory.BuildAtlas()` and upload it
    as **one** texture: `ATLAS_SIZE` square, RGBA32, sRGB,
    `ATLAS_MIP_COUNT` mip levels, each level set from `Mips[m]` as given
    (never generated by the engine), trilinear filtering, clamped wrap,
    and not readable afterwards. It is the only texture it draws with.
    Also create one `ISpriteTessellator`, and convert the palette asset
    to `Rgba` once.
  - **Each `Draw`:** call `Fill(frame, roles, linear)`, with `linear`
    true iff `QualitySettings.activeColorSpace` is `Linear`. Copy its
    `QuadCount` quads' corners, UVs and colours into the reused mesh
    buffers, four vertices per quad in the tessellator's corner order. It
    adds no geometry, UV or colour of its own.
- **Palette (Q-130).** The palette asset has one colour per `ColourRole`,
  including the appended ones, plus the background, which is the grass.
  Its values are §15.17's style guide. Region colours do not come from
  the palette: they arrive in each primitive's `Paint`.
- It turns input (pan, zoom) into a `CameraView` and keeps `ViewHeight` and
  `Aspect` positive.
- It does **not** run the frame order. `app.host`'s frame loop does
  (`16-interfaces-host.md` §16.6), and the Unity bootstrap converts the frame
  delta (§16.7). The backend supplies the `CameraView` and draws the
  `RenderFrame` it is handed.
- It references the scene layer's and the 2D art's types only (Q-130). It calls **no** sim member and
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
  frame after the first. The art keeps this (Q-130). There is one atlas,
  one material (`Sprites/Default`) and one mesh. Its vertex, UV and colour
  buffers are reused and grow only when a frame has more quads than ever
  before, so one draw call still covers every layer, role, visual, region
  and livery, however many quads they make. No texture or material is
  created after start-up, and no material exists per instance. **LOW CONFIDENCE**: this binds the implementation
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
- **Art (Q-130).** The per-rebuild cost adds, inside the same 2 ms:
  one `TryGetFlight` per drawn aircraft, the scenery, the markings and the
  paint of §15.16, with no allocation. The max-tier fakes gain a layout
  with areas and bridges, a schedule and looks. The 2D tessellator has its
  own budget (§15.17). The atlas costs about 21 MB of GPU memory (2048²
  RGBA32 and its mips), which is shared memory on integrated graphics and
  counts against `16` §16.10's budget. `BuildAtlas` runs once at start-up,
  outside every frame. Its time is not budgeted, and the T-025 playtest
  notes it.
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

The Q-130 tests, the T-020 tests that Q-130 changes, and the fixture's
version 2 lists are in §15.18.

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
   layer is **identical** in kind, layer, colour, visual, geometry,
   facing, paint and source (Q-130), and so is their order. That covers
   the scenery, runways and their markings, taxiways, junction fills and
   centrelines, stands with their markings and bridges, landside nodes,
   queue fill, lane pips and aircraft. Only `Agent`-layer primitives
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
  at Phase 1 (§15.5, flat art, §15.15), so there is no effects knob. The
  art has no knob of its own either: the atlas, scenery, markings and paint
  are the same at every setting (Q-130). A new knob
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

## 15.15 Real art — owner decisions, 2026-10-07 (Q-130)

> **HUMAN DECISIONS — owner, 2026-10-07**, all under Q-130:
> 1. The abstract shapes are too ugly, so the game gets real top-down art
>    **before** the T-025 playtest. The art is agent-made flat vector art:
>    a clean, consistent, flat top-down style, made by agents, with no
>    third-party assets.
> 2. Assets are **reusable and parameterised**, not one baked sprite per
>    variant. For example, a passenger's clothes and an aircraft's airline
>    branding change by data.
> 3. The game will ultimately move to **3D, after the T-025 gate**. This
>    design stays 2D, and it must carry over to 3D. 3D is not designed
>    here.
> 4. **Acceptance bar:** T-025 is not validated until the graphics
>    "actually look like a real airport": grass, aprons, runway and
>    taxiway markings, stand lead-ins and numbers, a terminal with piers,
>    and dressing such as jet bridges and a control tower.
>
> The Architect specified the mechanism and the style guide. The look is
> aesthetic, not balance.

**Three parts, and where the decisions live.**

- **The scene layer** (§15.16) decides everything about *what* is
  drawn, and all of it is tested. That covers each primitive's semantic
  `VisualId`, its engine-free `Facing` (an exact integer vector), and its
  `Paint` (named region colours from data). It also generates the
  scenery and markings. It knows nothing about atlases, cells, UVs or
  sprites.
- **The 2D art** (§15.17), a headless assembly, decides *how* 2D draws
  each visual. It holds the art as code, rasterises the one atlas, and
  tessellates a frame into tinted quads. All of it is tested.
- **The Unity backend** (§15.10) uploads the atlas once and copies the
  quads into one mesh.

**What carries over to 3D.** A later 3D backend consumes the same
`RenderFrame`. It maps each `VisualId` to a mesh, each `Paint` region to
a material slot of the same name, `Facing` to a yaw it derives, and `ColourRole` to
its state colour. It replaces only §15.17 and the Unity backend. The
scene layer, the layout's scenery (§15.4), the looks data
(`data/looks/looks.json`, §15.16) and every scene test stay as they are.
Nothing in the scene layer, the layout or `data/` names an atlas, a cell,
a texture coordinate or a sprite. An alternative was rejected: a sprite id
chosen by the scene, or a fixed `(DrawLayer, ColourRole, kind)` map in the
engine. The first would bind the scene to 2D, and the second would put an
untested decision in the engine.

No older primitive's geometry, colour, layer or source changes. Aircraft
keep diameter `AircraftSize`, and size categories differ inside that
square (§15.17).

---

## 15.16 Visuals, facing, paint and scenery (scene layer, Q-130)

**Draw order of the new rows.** `Ground` is the first layer, so aprons
and buildings lie under everything else. The appended `SourceKind`s sort
after the older ones. So in `Ground` every apron is drawn before any
building. In `Runway` every runway surface comes before any marking. In
`Taxiway` the order is every edge surface, then every junction fill, then
every centreline. In `Stand` it is every pad, then every lead-in, then
every number digit, then every bridge. A marking is never hidden by a
surface of its own layer.

### Facing

`Facing` is an exact **integer direction vector**, never an angle. Its
components are differences of the layout's `int32` positions, computed
in `int64` and converted to `float`, so the value is exact. Layout
coordinates stay well inside `float`'s 2^24 exact range. It needs no
trigonometry and no quantisation, and only its direction matters.
`(0, 0)` means unrotated (`+Y`). It is engine-free: the 2D tessellator
normalises it (§15.17), and a 3D backend derives a yaw from it.

**Aircraft facing** is the vector of the first rule that applies:

1. `OnEdge` set: the edge's other endpoint's position minus `AtNode`'s,
   which is the direction of travel. A pushback is therefore drawn nose
   first, a stylisation accepted for Phase 1.
2. Else, `AtNode` set and `Phase` is `OnStand` or
   `AwaitingPushbackClearance`: `AtNode`'s **nose-in vector** (below).
3. Else, `AtNode` set and `Phase` is `OnRunway` or `HeldForRunway`: along
   the runway, away from its threshold. With `Runway` set, `T` the position
   of that runway's `RunwayDef.ThresholdNode`, and `P0 = (X0,Y0)`,
   `P1 = (X1,Y1)` from its geometry: `P1 − P0` if
   `|P0 − T|² ≤ |P1 − T|²` (in `int64`), else `P0 − P1`.
4. Else, `AtNode` set and `Phase` is `HeldOnTaxiway` or `Taxiing`: toward
   the destination, in a straight line. A `Departure`'s destination is the
   `ThresholdNode` of its `Runway`, an `Arrival`'s is the `Node` of its
   `Stand`. The vector is the destination's position minus `AtNode`'s.
5. In every other case, and whenever the rule's id is unset or a position
   or geometry is missing: `(0, 0)`.

A node's **nose-in vector** is its position minus the position of the
other endpoint of the lowest-`TaxiEdgeId` edge incident to it, or `(0, 1)`
if it has no incident edge or the difference is `(0, 0)`.

> **LOW CONFIDENCE — rule 4 points along a straight line, not the
> route.** `IAirsideSystem` exposes no route, so an aircraft held at a
> junction faces its destination, which can be off the next edge by up to
> 90°. It holds only while the aircraft waits, and it is presentation
> only. A route query on `sim.airside` would fix it, and is not worth
> widening `12` for now.

Every other primitive has `Facing = (0, 0)`, except runway thresholds
and designators, stand lead-ins and stand numbers (below).

### Runway markings

The arithmetic is integer, on the layout's `int32` values widened to
`int64`, with C# `long` division (truncating toward zero), and each
coordinate is converted to `float` last. For each runway: `dx = X1 − X0`,
`dy = Y1 − Y0`, `W = Width`, and `L = isqrt(dx² + dy²)`, the exact floor
of the square root. If `L = 0` the runway has no markings. Each end `k`
has a frame. End 0 has origin `P0` and forward `f = (dx, dy)`, and end 1
has origin `P1` and forward `f = (−dx, −dy)`. `Point(k, d, e)` is the point
`d` along the forward and `e` to its left:
`origin + ((f.x × d − f.y × e) / L, (f.y × d + f.x × e) / L)`.

All markings have layer `Runway`, colour `RunwayMarking` and source
`(RunwayMarking, runway id, Sub)`:

| `Sub` | What | Primitive | Geometry | `Visual` | `Facing` |
|---|---|---|---|---|---|
| 0 | edge lines | `Segment` | the runway's own `A`, `B` and `Size` | `RunwayEdgeLines` | `(0, 0)` |
| 1, 2 | threshold at end 0, end 1 | `Dot` | centre `Point(k, W / 2, 0)`, diameter `W` | `RunwayThreshold` | `f` of that end |
| 3, 4 | designator at end 0: tens, units | `Dot` | centre `Point(0, 2W, e_i)`, diameter `G` | `MarkingDigitN` | `f` of end 0 |
| 5, 6 | designator at end 1: tens, units | `Dot` | centre `Point(1, 2W, e_i)`, diameter `G` | `MarkingDigitN` | `f` of end 1 |
| `7 + k` | centreline dash `k`, `0 ≤ k < n` | `Segment` | `Point(0, s_k, 0)` to `Point(0, s_k + W, 0)`, width `W` | `RunwayCentreDash` | `(0, 0)` |

- **Designators** come from the airside layout's integer
  `RunwayDef.ActiveDirectionDeg`, not from an angle computed here. With
  `T` the position of the runway's `ThresholdNode`, the **active end** is
  end 0 if `|P0 − T|² ≤ |P1 − T|²` (in `int64`), else end 1. This matches
  aircraft rule 3. The active end's degrees are
  `D = ActiveDirectionDeg mod 360`, made non-negative, and the other
  end's are `(D + 180) mod 360`. An end's number is
  `((deg + 5) / 10) mod 36`, with `0` written as `36`. It always has two
  digits, so 9 is `09`. If the `RunwayDef` or `T`'s position is missing,
  there are no designator primitives. The digit size is `G = (2 × W) / 5`. Digit `i`
  (0 for tens) is at `e_i = ((c − 1 − 2i) × 3 × G) / 10` with `c = 2`, so
  the tens are on the left of a pilot landing there. It reads upright
  facing the forward. Letters (L/C/R) are not drawn at Phase 1.
- **Dashes.** `M = 3W` and `R = L − 2M`; `n = 0` if `R < W`, else
  `n = (R + W) / (2W)`; and `s_k = M + (R − (2n − 1) × W) / 2 + 2W × k`.
  So `n` dashes of length `W` with gaps of `W` are centred between the
  two threshold-and-designator zones.
- **For the §15.12 fixture's runway** (`(−2000,0)` to `(0,0)`, `W = 45`),
  with threshold node 1 at `(0, 0)` and `active_direction_deg` 270, this
  gives: `L = 2000`; end 1 active; thresholds at `(−1978, 0)` facing
  `(2000, 0)` and `(−22, 0)` facing `(−2000, 0)`; designator `09` (digits `0` at
  `(−1910, 5)` and `9` at `(−1910, −5)`) and `27` (`2` at `(−90, −5)`,
  `7` at `(−90, 5)`), each with `G = 18`; and `n = 19` dashes from
  `s_0 = 167`.

### Stand markings

For each stand, at its node `N` with nose-in vector `v` (above) and
`Ls = isqrt(v.x² + v.y²)`:

- **Lead-in:** the `StandLeadIn` row of §15.5, with `Facing = v`.
- **Number:** the decimal digits of `StandId.Value`, most significant
  first, with no leading zero, `c` of them. `G = StandSize / 6`; if
  `G = 0` there are no digits. Digit `i` is a `Dot` of diameter `G`,
  `Facing = v`, visual `MarkingDigitN`, source
  `(StandNumber, stand id, i)`, centred at
  `(N.x + (v.x × d − v.y × e_i) / Ls, N.y + (v.y × d + v.x × e_i) / Ls)`,
  with `d = (StandSize × 13) / 32` and
  `e_i = ((c − 1 − 2i) × 3 × G) / 10`. The number sits at the nose end of
  the stand and reads upright for a pilot taxiing in. A large aircraft
  may cover part of it, as on a real apron.

### Aircraft visual and livery

- **Visual by size category.** At construction the scene builder reads,
  when `RenderSources.Content` is not null,
  `Content.AllOf(ContentKind.Aircraft)`, and for each id
  `TryGet<AircraftDefinition>` and then `TryGet<SizeCategoryDefinition>`
  of its `SizeCategory`. That gives a fixed map from aircraft type to
  `AircraftA + min(Ordinal, 5)`. So ordinals 0 to 5 (`data/`'s `size_a`
  to `size_f`) are `AircraftA` to `AircraftF`, and a larger ordinal is
  `AircraftF`. A type whose definition or category does not resolve is
  left out. Per rebuild, one `Schedule.TryGetFlight(flight)` per drawn
  aircraft gives `AircraftType` and `Airline`. The visual is the type's
  entry, or `AircraftC` when `Schedule` or `Content` is null, the flight
  is not found, or the type is not in the map.
- **Livery.** From the same `FlightRecord.Airline`: the
  `RenderLooks.Airlines` entry with that `AirlineId`, else
  `DefaultLivery`, which is also used when `Schedule` is null or the
  flight is not found. `Paint` is `Region0..4 = Fuselage, Tail,
  Cheatline, Engines, Logo` (`AircraftRegion` order), and
  `Mark = (uint8)Livery.Mark`.
- Neither the visual nor the paint changes with phase. Phase is the
  `ColourRole`, as before.

### Passenger paint

Each agent's `Paint` is a fixed hash of its `AgentView.Ref`. It never
uses the sim's RNG and never reaches the sim. For region `r` (the
`PassengerRegion` value, 0 to 4), `h_r` is FNV-1a-32 (offset
`0x811C9DC5`, prime `0x01000193`) over 13 bytes: `Ref.Cohort.Value` as 8
bytes little-endian, `Ref.Index` as 4 bytes little-endian two's
complement, then the byte `r`. `Region_r = list_r[h_r mod list_r.Count]`,
where `list_r` is `Tops`, `Bottoms`, `Skins`, `Hairs` or `Bags`, and
`Mark = 0`. The same passenger therefore looks the same in every frame,
in every builder and on every machine. A cohort split may change a
passenger's `Ref` and so its clothes. That is presentation only and is
accepted.

### Looks data (`data/looks/looks.json`)

The livery and clothing data are **not sim content**. `08` §8.11's
loader reads only its kind directories, so it ignores `looks/`, and the
content hash cannot change. The file is validated by
`data/schemas/looks.schema.json` through `ci/validate-content.py`, as
`strings/` is (`04-data-schemas.md`). It reaches the player in the build
step's copy of `data/` (`16` §16.3). It names regions and plain colours
only, never an atlas.

```
{
  "schema_version": 1,
  "id": "looks",
  "default_livery": <livery>,
  "liveries": { "<airline code>": <livery>, ... },
  "passengers": { "tops": [ "<colour>", ... ], "bottoms": [ ... ], "skins": [ ... ],
                  "hairs": [ ... ], "bags": [ ... ] }
}
<livery> = { "fuselage": "<colour>", "tail": "<colour>", "cheatline": "<colour>",
             "engines": "<colour>", "logo": "<colour>",
             "mark": "none" | "disc" | "ring" | "chevron" | "star" | "bars" | "diamond" | "crescent" }
<colour> = "#RRGGBB", upper-case hexadecimal, sRGB
```

- `RenderFactory.LoadLooks(source)` calls `source.ReadAll("looks/looks.json")`
  once and never calls `Files()`. It reads the file with `08` §8.11's
  strict subset and string rules (Q-033), using the scene layer's own
  reader, with no package. Every fault is a `FormatException` whose
  message starts with `looks/looks.json: ` (`07` "Error handling"). The
  faults are: a `null` result; bytes outside the subset; a duplicate,
  unknown or missing key; `schema_version` other than 1 or `id` other
  than `"looks"`; a malformed colour or mark; an airline code that is not
  1 to 8 characters of `A–Z` and `0–9`; two codes with the same
  `AirlineId`; and a passenger list that is empty or longer than 64.
- An airline code maps to `AirlineId` exactly as the schedule does
  (`11` §11.4): FNV-1a-32 over its UTF-8 bytes. `Airlines` is sorted by
  `AirlineId`, and the lists keep file order.
- **Who writes it.** The scene-layer task writes the file and its schema.
  The colours are aesthetic, not balance. It has a livery for each of the
  four Phase 1 fixture airlines (`BRW`, `CTX`, `DLN`, `NVA`), with
  distinct fuselage, tail and mark. It has at least 8 tops, 4 bottoms, 5
  skins (a realistic range of skin tones), 5 hairs and 4 bags. No
  real-world airline's livery, name or logo is used (`00-overview.md`).
- **Defaults when data is missing.** An airline without an entry gets
  `default_livery`. A builder made without looks (the two-argument
  factory, and every merged test) uses `RenderFactory.DefaultLooks()`:
  - livery: fuselage `#F4F5F7`, tail `#2F5D9E`, cheatline `#2F5D9E`,
    engines `#9AA1A9`, logo `#F4F5F7`, mark `none`;
  - no airline entries;
  - one entry per list: top `#3B6EA5`, bottom `#2E3440`, skin `#C68E6B`,
    hair `#3A2A1F`, bag `#5A4A3A`.

  A missing or malformed file in the player is a load failure, never a
  silent default (`04-data-schemas.md`).

### What is recolourable (binding on future art)

| Visual | Recolourable regions (`Paint`) | Coloured by `ColourRole` | Fixed |
|---|---|---|---|
| `AircraftA`–`F` | `Fuselage`, `Tail` (tailplane and fin top), `Cheatline`, `Engines`, `Logo` (+ `Mark`) | the status outline | wings (light grey), glazing |
| `Passenger` | `Top`, `Bottom`, `Skin`, `Hair`, `Bag` | the outline | — |
| a ground vehicle, when one is drawn (not at Phase 1) | `Body` (region 0) | the outline | glazing, tyres |
| every other visual | none | the whole visual | — |

A new visual with variants gets named regions in this scheme, with their
colours in `data/looks/`, and never a baked variant. Adding a region or a
visual is an amendment.

---

## 15.17 The 2D art: atlas and tessellator (Q-130)

The 2D backend's testable half. It lives in `src/app/render/Art2D/` as
the assembly `AirportSim.App.Render.Art2D` (namespace
`AirportSim.App.Render.Art2D`), and it follows §15.3's scene-layer rules.
A 3D backend replaces it (§15.15).

### The pipeline: art as code

The art is **C# source** in this assembly, which reaches the player as a
precompiled plugin through the existing plugin copy (`16` §16.2).
`Art2DFactory.BuildAtlas()` rasterises it into plain bytes at start-up.
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

### Public surface

```
readonly struct Rgba      { uint8 R; uint8 G; uint8 B; uint8 A }
readonly struct AtlasRect { float U0; float V0; float U1; float V1 }      // texture coordinates, 0..1, V up
readonly struct SpriteAtlas {
  int32                 Size                 // ATLAS_SIZE
  IReadOnlyList<byte[]> Mips                 // ATLAS_MIP_COUNT entries; Mips[m] is RGBA32, (Size >> m)² × 4 bytes, rows bottom to top
}
enum LayerColour { Role, Region, Fixed }
readonly struct ArtLayer {
  LayerColour Colour
  int32       Region                         // Paint region index when Colour = Region; else 0
  Rgb         Fixed                          // when Colour = Fixed; else (0,0,0)
  AtlasRect   Rect                           // its cell's visible square; unused when IsLogo
  bool        IsLogo                         // drawn from the Mark's cell, only when Paint.Mark ≠ None
  int32 MinX; int32 MinY; int32 MaxX; int32 MaxY   // the sub-square of the primitive it covers, design units; 0,0,1024,1024 = whole
}
interface ISpriteTessellator {
  int32   Fill(in RenderFrame frame, IReadOnlyList<Rgba> roleColours, bool linear)   // returns QuadCount
  int32   QuadCount
  float[] Corners                            // 8 per quad: x, y of corners 0..3, world units
  float[] Uvs                                // 8 per quad: u, v of corners 0..3
  byte[]  Colours                            // 16 per quad: R, G, B, A of corners 0..3
}
static class Art2DConstants {                // public const int
  ATLAS_SIZE = 2048; LARGE_CELL = 256; SMALL_CELL = 128; ATLAS_MIP_COUNT = 5; ART_UNITS = 1024
}
Art2DFactory.BuildAtlas() -> SpriteAtlas                          // a fresh value per call
Art2DFactory.LayersOf(VisualId visual) -> IReadOnlyList<ArtLayer> // painter order; a fresh list per call
Art2DFactory.LogoRect(LogoMark mark) -> AtlasRect                 // ArgumentOutOfRangeException for None
Art2DFactory.CreateTessellator() -> ISpriteTessellator
```

`Art2DFactory` follows `08` §8.11a's factory rule. Every other art type is
`internal`.

### The art format (binding)

- `Art/Cells.cs` holds one definition per atlas cell, in slot order
  (below). The rasteriser is in the same directory.
- **Design space.** Integer design units. A cell's visible square is
  `0 .. ART_UNITS` on both axes, with the origin at bottom-left and `+Y`
  forward (an aircraft's nose; a threshold's runway side; a digit's top;
  a segment's `B` end). The edge-to-edge cells (`Solid`,
  `RunwayEdgeLines`, `CentreStripe` and `JetBridge`) extend their shapes
  to `−64` and `1088` along every axis they span, so the bleed border is
  filled. Every other cell keeps every shape, grown by the cell's grow,
  inside `16 .. 1008`.
- **A cell definition** has a grow distance (design units, 0 or more),
  an edge value, a mirror flag and an ordered list of shapes.
- **A shape** is a polygon (3 or more integer vertices, simple, with
  either winding) or a circle (integer centre and radius). Each shape
  also has a value (grey, 0 to 255) and an alpha (0 to 255).
- **Grow** dilates every shape of the cell by that distance. An outline
  cell is the part's silhouette with grow > 0, drawn under the part, so
  that only a ring of the grow width shows.
- **Mirror.** When set, each shape is drawn twice, itself and then its
  reflection about `x = ART_UNITS / 2`. So mirrored parts are symmetric by
  construction.
- **Literal numbers only.** The one exception is that the aircraft cells
  may come from one function of their row in the proportion table.
  Definitions are built inside each `BuildAtlas` call, with no static
  mutable state.

### Packing (binding)

- **Large cells** (`LARGE_CELL`) hold the aircraft layers. Size category
  `s` (`A` = 0 to `F` = 5) is row `s` from the bottom, and aircraft layer
  `l` (0 to 6, below) is column `l`. So the cell's lower-left pixel is
  `(256 l, 256 s)`.
- **Small cells** (`SMALL_CELL`) fill the band `y ≥ 1536`. Slot `k` has
  its lower-left pixel at `(128 × (k % 16), 1536 + 128 × (k / 16))`. In
  slot order:

  | Slots | Cells |
  |---|---|
  | 0–4 | `Solid`, `Disc`, `RunwayEdgeLines`, `CentreStripe`, `RunwayThreshold` |
  | 5–9 | `BuildingRoof`, `ControlTower`, `JetBridge`, `StandPad`, `StandLeadIn` |
  | 10–19 | `Digit0` to `Digit9` |
  | 20–21 | `TerminalZone`, `LanePip` |
  | 22–27 | passenger layers: `Outline`, `Bottom`, `Bag`, `Top`, `Skin`, `Hair` |
  | 28–34 | logo marks `Disc`, `Ring`, `Chevron`, `Star`, `Bars`, `Diamond`, `Crescent` |

  Every other pixel of the atlas is transparent, with all bytes 0.
- **Rects.** For a cell of side `c` at lower-left `(px, py)`:
  `U0 = (px + c/32) / 2048`, `U1 = (px + c − c/32) / 2048`, and the same
  for `V` with `py`. These are exact in `float`. The rectangle is the
  visible square, inside a bleed border of `c/32` pixels (8 or 4).

### Rasterisation (binding)

- **Per mip `m`**, with `0 ≤ m < ATLAS_MIP_COUNT`, for a cell of side
  `c`: the cell side is `c_m = c >> m` pixels, the border is
  `b = (c / 32) / 2^m` pixels, the visible side is `v = c_m − 2b`, and
  there are `u = ART_UNITS / v` design units per pixel. Pixel `(i, j)` of
  the cell, counted from its bottom-left, samples the design point
  `p = ((i + 0.5 − b) × u, (j + 0.5 − b) × u)`. All of this is in
  `double`.
- **Coverage.** `d_s(p)` is the signed distance in design units, negative
  inside, minus the cell's grow. For a circle it is
  `|p − centre| − radius`. For a polygon it is the distance to the
  nearest edge, negative when `p` is inside by the even-odd rule. The
  shape's coverage is `clamp(0.5 − d_s / u, 0, 1)`. This anti-aliases
  every edge in the texture itself, so `Low` (no MSAA) still draws clean
  edges.
- **Compositing**, premultiplied, starting from value 0 and alpha 0:
  every shape in order, mirror copies included, "over" the result with
  alpha `alpha × coverage / 255`. Output: alpha `A8 = floor(A × 255 + 0.5)`.
  RGB are all `floor(C / A + 0.5)` when `A8 > 0`, else the cell's edge
  value, so filtering at an edge never pulls in a foreign colour.
- **Every mip is rasterised directly** this way, and none is downsampled.
  So no cell bleeds into another at any level. Every byte of every mip is
  written. A shape may be skipped for a pixel outside its bounding box
  grown by the cell's grow plus `u`, which changes no output.

### Visual layers (binding)

`LayersOf(visual)`, in painter order. "Role" means the primitive's
`ColourRole` palette colour, "region R" means `Paint.Region_R`, and
"fixed" means a constant sRGB colour.

| `VisualId` | Layers (cell: colour) | Box slicing |
|---|---|---|
| `RunwaySurface`, `TaxiwaySurface`, `Apron`, `QueueFill` | `Solid`: role | no |
| `RunwayEdgeLines` | `RunwayEdgeLines`: role | — |
| `RunwayThreshold` | `RunwayThreshold`: role | — |
| `RunwayCentreDash`, `TaxiwayCentreline` | `CentreStripe`: role | — |
| `TaxiwayJunction` | `Disc`: role | — |
| `TerminalBuilding`, `Pier` | `BuildingRoof`: role | 128 units ↔ 3 m |
| `ControlTower` | `ControlTower`: role | no |
| `JetBridge` | `JetBridge`: role | — |
| `StandPad` | `StandPad`: role | 128 units ↔ 2 m |
| `StandLeadIn` | `StandLeadIn`: role | — |
| `MarkingDigitN` | `DigitN`: role | — |
| `TerminalZone` | `TerminalZone`: role | 128 units ↔ 1 m |
| `LanePip` | `LanePip`: role | — |
| `Passenger` | `Outline`: role; `Bottom`: region 1; `Bag`: region 4; `Top`: region 0; `Skin`: region 2; `Hair`: region 3 | — |
| `AircraftA`–`F` | its row's layers 0 to 5, then the logo, then layer 6 (below) | — |

The aircraft layers are: 0 `Status` (the silhouette grown by 32): role;
1 `Wings` (wings only): fixed `#D5D8DC`; 2 `Engines`: region 3;
3 `Fuselage`: region 0; 4 `Cheatline` (a band along each side of the
fuselage): region 2; 5 `Tail` (tailplane and fin top): region 1; then the
logo: region 4, `IsLogo`, on the sub-square centred at
`(512, 512 − 0.30 × length × 1024)` with side `0.2 × length × 1024`,
rounded to integers; then 6 `Glazing` (windscreen): fixed `#2A3138`.
Every region and role layer is drawn at values 225 to 255, so that the
colour reads true. Darker values are used only for shading inside a
part, such as engine intakes at 150.

### Tessellation (binding)

`Fill` emits, for each primitive in list order and for each of its layers
in order, one quad, or nine for a sliced `Box`. A logo layer with
`Mark = None` emits nothing. Corners are numbered 0 to 3 as
`(U0,V0)`, `(U1,V0)`, `(U1,V1)` and `(U0,V1)` of the layer's rect, which
is the backend's triangle order.

- **Box:** corners `(MinX,MinY)`, `(MaxX,MinY)`, `(MaxX,MaxY)` and
  `(MinX,MaxY)`, never rotated. **Slicing:** with design inset `s` and
  world inset `t = min(sliceWorld, (MaxX − MinX) / 2, (MaxY − MinY) / 2)`,
  the box and the rect are each cut into a 3 × 3 grid. The world grid is
  at `t` from each side, and the rect grid at `s / 1024` of the rect from
  each side. Corners keep their size, edges stretch one way, and the
  centre stretches both ways. They are emitted bottom row first, left to
  right.
- **Segment:** with `f = (B − A) / |B − A|` (or `(0, 1)` when `A = B`),
  `r = (f.Y, −f.X)` and `h = Size / 2`, the corners are `A − r h`,
  `A + r h`, `B + r h` and `B − r h`.
- **Dot:** `f` is `Facing` normalised in `double` (`n = sqrt(x² + y²)`,
  `f = (x / n, y / n)`), or `(0, 1)` when `Facing` is `(0, 0)`. Then
  `r = (f.Y, −f.X)` and `h = Size / 2`, and the corners are `centre − r h − f h`, `centre + r h − f h`,
  `centre + r h + f h` and `centre − r h + f h`. A layer's sub-square
  `(MinX..MaxX, MinY..MaxY)` maps design `(x, y)` to
  `centre + r × (x / 1024 − 0.5) × Size + f × (y / 1024 − 0.5) × Size`.
- **Colour:** role gives `roleColours[(int)Colour]`, with its alpha.
  Region gives `Paint.Region_R` with alpha 255. Fixed gives the constant
  with alpha 255. When `linear` is true, R, G and B go through the table
  `L(c) = floor(255 × lin(c / 255) + 0.5)`, where `lin(x)` is `x / 12.92`
  for `x ≤ 0.04045` and `((x + 0.055) / 1.055)^2.4` otherwise, in
  `double`. Alpha is unchanged. All four corners get the same colour.
- **Buffers** are reused and grow only when a frame needs more quads than
  ever before. They are valid until the next `Fill`. `roleColours` must
  have one entry per `ColourRole`, else `ArgumentException`
  (`roleColours`).
- **Budget.** `Fill` over the §15.11 max-tier frame at `High`: mean ≤
  1.5 ms and p99 ≤ 3.0 ms, with §15.11's window and arithmetic, and no
  allocation after the first call. **LOW CONFIDENCE**: unmeasured, and
  the passenger layers dominate (16 × 256 agents × 6 quads).

Tessellator tests may compare corner positions within `1e-3` world
units, because a normalised vector is not exact in floating point. That
is the one tolerance allowed in `tests/app/render/`, and it applies only
to the tessellator's corner positions. The test vectors are chosen far
from that edge: axis-aligned facings (exact), `(3, 4)` (with `n = 5`
exact), and `(1, 1)`, all with `Size ≤ 1000` and centres within ±10 000,
where the float error is below `1e-4`. No angle is computed anywhere in
tested code.

### Style guide

- **The bar is a real airport, seen from above:** grass everywhere that
  is not paved; concrete aprons; dark asphalt runways with white
  markings; taxiways with yellow centrelines; stands with yellow
  lead-ins and painted numbers; light-roofed terminal and piers; jet
  bridges; a control tower; aircraft in airline liveries; and passengers
  in varied clothes.
- **Flat.** No gradients, no noise, no shadows, no text beyond painted
  digits, and no real-world airline livery, name or logo
  (`00-overview.md`: no licensing). Shapes are separated by value steps
  and by outlines.
- **Value, not colour.** Every cell is greyscale. Colour comes only from
  the layer's role, region or fixed colour (above), which multiplies the
  cell. One cell serves every state and every livery. The value steps
  are: true colour 225 to 255, shading 150 to 200, and a dark detail of
  60 or less only in fixed layers.
- **Outline weight**, as grow (design units, out of 1024): aircraft
  status 32 (about 3 % of the cell) and passenger 48. There is none on
  markings, surfaces, stands, buildings and lane pips, which use a border
  band or rim instead.
- **Scale per layer.** World sizes come from the layout: runway `Width`,
  `TaxiwayWidth`, `StandSize`, `AircraftSize`, `AgentSize`, the areas and
  the bridges. A visual fills its primitive. Only aircraft vary inside
  their square, by the proportion table.

| Cell | Art |
|---|---|
| `Solid` | one square, value 255 |
| `Disc` | circle at `(512,512)`, radius 496, value 255 |
| `RunwayEdgeLines` | two full-height bars, `x` 16 to 48 and 976 to 1008, value 255 |
| `CentreStripe` | one full-height bar, `x` 480 to 544, value 255 |
| `RunwayThreshold` | mirrored: four bars 64 wide, `x` from 64 with gaps of 48, `y` 128 to 832, value 255 |
| `BuildingRoof` | parapet square 16 to 1008 at value 170, roof 128 to 896 at value 240 |
| `ControlTower` | base square 64 to 960 at value 180, glazed cab ring radius 360 at value 70, cab roof radius 300 at value 240 |
| `JetBridge` | full-length body `x` 128 to 896 at value 215, edge lines `x` 128 to 192 and 832 to 896 at value 160 |
| `StandPad` | band square 16 to 1008 at value 205, inner 128 to 896 at value 240 |
| `StandLeadIn` | line `x` 488 to 536 from `y` 16 to 848, and stop bar `x` 352 to 672, `y` 800 to 848, value 255 |
| `Digit0`–`Digit9` | block digits with a stroke of 96, inside `x` 256 to 768 and `y` 128 to 896, value 255 |
| `TerminalZone` | border band 16 to 1008 at value 205, floor 128 to 896 at value 245 |
| `LanePip` | rim square 96 to 928 at value 120, booth square 128 to 896 at value 230 over it, and an officer circle of radius 128 at value 150 |
| passenger layers | top-down figure facing `+Y`: `Outline` is the whole silhouette grown by 48; `Bottom` is two small feet polygons ahead of the body; `Bag` is a box at the right hip; `Top` is a shoulders polygon about 640 by 380; `Skin` is two hands at the shoulder ends; `Hair` is a head circle of radius 150. All are mirrored except `Bag` |
| logo marks | each a simple filled mark within the whole visible square, value 255 |

**Aircraft proportions** are fractions of `ART_UNITS`, with the status
outline included, span across `X`, length along `Y` and the nose at `+Y`,
centred on `(512, 512)`. The fuselage is `length / 10` wide. Sweep is
the wingtip's leading-edge setback as a fraction of the half-span. Size
categories F and A differ by about 2.4 times in span, which reads at a
glance and is compressed from the real ratio (about 5) so that
`AircraftA` stays legible.

| Visual | Ordinal (`data/`) | Span | Length | Sweep | Engines |
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
| Background (grass) | `#6F8F5E` | `Agent` (passenger outline) | `#1E2328` |
| `Runway` | `#3A3E44` | `AircraftMoving` (status outline) | `#1F2428` |
| `RunwayQueued` | `#6A4B2F` | `AircraftHolding` | `#F3A21C` |
| `Taxiway` | `#50565D` | `AircraftOnStand` | `#3D8BD4` |
| `StandFree` | `#B4B6B1` | `LaneOpen` | `#43A047` |
| `StandOccupied` | `#C9BE97` | `LaneClosed` | `#C62828` |
| `LandsideNode` (zone floor) | `#E6E1D6` | `RunwayMarking` | `#F4F4EE` |
| `QueueFill` | `#E8A33A`, alpha 140 | `TaxiwayMarking` | `#F2C230` |
| `Apron` | `#A3A49E` | `Building` | `#C9CBCF` |

So an aircraft moving reads with a plain dark outline, holding with an
amber one, and on stand with a blue one. The livery is unchanged in every
case.

> **LOW CONFIDENCE — Unity behaviour no agent can run.** These are the
> Architect's reading: that `Sprites/Default` multiplies `_MainTex` by the
> vertex colour with straight alpha; that a `Texture2D` created with an
> explicit mip count accepts each level through `SetPixelData` and keeps
> them through `Apply(false, true)`; and that vertex colours are not
> converted in a Linear project, which is why `Fill` takes `linear` and
> applies the table itself. The atlas is created as sRGB. `unity-build`
> checks only that it compiles. The look is checked by eye in the T-025
> playtest against the bar above, and a mismatch is a spec question,
> filed, not a workaround. The atlas build time on Mono is also
> unmeasured (§15.11).

---

## 15.18 Q-130: tests and tasks

### Tests

New, phrased per `07-conventions.md`. Scene layer (task 1):

- `test_scene_visuals_follow_the_draw_table`: every row of §15.5 has its
  visual, colour, layer and source. `Facing` is `(0, 0)` and `Paint` is
  zero wherever §15.16 does not set them.
- `test_scene_scenery_follows_the_layout`: areas and bridges are drawn
  with no airside and no flow module present. Within `Ground`, aprons
  come before buildings.
- `test_scene_aircraft_facing_follows_the_five_rules`: each rule's exact
  vector, including the lowest-id incident edge on a stand node with two
  edges, the runway direction from either end, and both destinations,
  plus every `(0, 0)` fallback.
- `test_scene_runway_markings_follow_the_integer_rule`: every value of the
  §15.16 fixture example, exactly. A diagonal runway exercises the
  truncating division and the left offsets. A short runway with `R < W`
  keeps its edges, thresholds and designators and has no dashes. A
  zero-length runway has no markings. Markings keep `RunwayMarking`
  while the runway is `RunwayQueued`. Designators follow
  `ActiveDirectionDeg` from either active end, wrap correctly at
  `0 → 36` (for example 355° and 2°), and are absent without a
  `RunwayDef`.
- `test_scene_taxiway_junction_fill_and_centrelines`: a fill only at nodes
  with two or more incident edges, one centreline per edge on the edge's
  geometry, and the in-layer order.
- `test_scene_stand_lead_in_and_numbers`: the facing as the nose-in
  vector, the digit count and visuals for 1-, 2- and 5-digit stand ids,
  the exact centres, and no digits when `StandSize < 6`.
- `test_scene_aircraft_visual_follows_size_category`: ordinals 0 to 5,
  ordinal 6 giving `AircraftF`, and `AircraftC` for an unresolved type, an
  unresolved category, a missing flight, a null `Schedule` and a null
  `Content`. Content is read only at construction. `TryGetFlight` is
  called once per drawn aircraft per rebuild, and never for an undrawn
  one.
- `test_scene_aircraft_paint_follows_airline_livery`: an airline with an
  entry gets its five colours and mark in region order. An airline
  without one, a missing flight and a null `Schedule` get
  `DefaultLivery`. Phase changes colour but not paint.
- `test_scene_passenger_paint_is_a_fixed_hash_of_the_agent`: exact
  expected regions for given `(Cohort, Index)` values, computed in the
  test from §15.16's FNV-1a-32 definition. They are the same across
  rebuilds, fresh builders and graphics settings. A one-entry list gives
  that entry.
- `test_render_looks_load_the_file_and_reject_each_fault`: the shipped
  `data/looks/looks.json` loads, with the four fixture airlines. Each
  §15.16 fault throws with the `looks/looks.json: ` prefix. A colliding
  pair of codes is rejected.
- `test_render_looks_defaults_match_spec`: `DefaultLooks()` values.
- `test_render_layout_version_2_loads_scenery_and_rejects_faults`: the
  fixture loads with its areas and bridges in id order. A version 1 file
  loads with empty lists. Version 3 and an unknown `kind` are parse
  failures. Each check 5 failure names its id.

2D art (task 2):

- `test_art2d_assembly_references` (static): no engine reference, and the
  scene assembly does not reference `AirportSim.App.Render.Art2D`.
- `test_art2d_atlas_shape_and_packing_match_spec`: `Size`, the mip count,
  each mip's byte length, every rect exactly (through `LayersOf` and
  `LogoRect`), and every pixel outside a cell all zero.
- `test_art2d_atlas_is_deterministic`: two builds are equal byte for
  byte, and each call returns fresh arrays.
- `test_art2d_every_cell_is_drawn_inside_its_border`: every cell has a
  texel with alpha ≥ 128 at mips 0 to 2, and alpha > 0 at mips 3 and 4.
  Every cell except the four edge-to-edge ones has an all-transparent
  border ring at mip 0. `Solid` is `(255, 255, 255, 255)` on every texel
  of its cell at every mip.
- `test_art2d_aircraft_follow_the_proportion_table`: at mip 0, the
  alpha ≥ 128 bounding box of each `Status` cell has width `span × 240`
  and height `length × 240`, each ± 3 pixels. Both grow strictly from `A`
  to `F`. Each aircraft cell is mirror-symmetric within ± 1 per byte.
- `test_art2d_layers_match_the_visual_table`: `LayersOf` for every
  `VisualId` matches §15.17's table, including the logo sub-square.
- `test_art2d_tessellator_corners_follow_kind_facing_and_slicing`: box,
  segment, zero-length segment, and a dot facing `(0, 0)`, `(0, 1)`,
  `(1, 0)`, `(−1, 0)`, `(0, −1)`, `(3, 4)` and `(1, 1)` (§15.17's tolerance
  note); the logo sub-square; the nine quads of a sliced box, with `t`
  clamped for a thin box.
- `test_art2d_tessellator_colours_follow_role_region_and_fixed`: each
  colour source, the logo emitted only with a mark, the role alpha kept,
  `L(0) = 0`, `L(128) = 55` and `L(255) = 255` when `linear`, and a short
  `roleColours` throwing.
- `test_art2d_tessellator_fill_within_budget_and_allocates_nothing`.

Merged tests that Q-130 changes. Each is updated to this spec by the
Test Author of the task named, as Q-125 did for the UI surface test. No
worker edits a test.

- Task 1:
  - `test_scene_runway_colour_follows_queue_length_and_taxiways_follow_edges`
    **breaks**. It asserts 3 primitives in the `Taxiway` layer, and now
    the junction fill and the centrelines are there too. It counts the
    `TaxiEdge`-sourced primitives instead, and asserts their
    `TaxiwaySurface` visual.
  - `test_scene_calls_only_listed_sim_members` is **extended**. The
    max-tier fakes gain a guarded schedule and content index, and the
    test asserts that content is read only at construction.
  - `test_scene_build_within_frame_budget_at_max_tier` and
    `test_scene_build_and_update_allocate_nothing_after_first_call` are
    **extended**. The max-tier scene gains the schedule, content and looks
    fakes and a version 2 layout with scenery.
  - `test_render_layout_fixture_file_equals_built_layout` is
    **extended**: the kit's code-built layout (`Phase1RenderLayout`) gains
    the fixture's areas and bridges.
  - `test_scene_gameplay_primitives_identical_at_every_graphics_setting`
    and `test_scene_primitive_order_is_stable` are **covered by the
    kit**. `Prims.Show` gains `Visual`, `Facing` and `Paint`, so both
    compare the new fields with their own code unchanged.
- No `app.ui` or `app.host` test changes. The old `RenderSources`,
  `RenderLayout`, `ComposedSim`, `CreateSceneBuilder` and `Compose`
  shapes all stay, and a version 2 fixture still keeps the text
  `"stand_size": 40`, which a host test edits.

### Fixture (task 1's Test Author)

`tests/fixtures/render/phase1-layout.json` becomes version 2 with these
lists, and no other value changes. It is also the playtest bundle's
`render_layout.fixture` (`16` §16.3). The terminal encloses the landside
zones, the pier faces the stands, and each bridge reaches its stand's
aircraft door:

```
"areas": [
  { "id": 1, "kind": "apron",         "min_x": 20,  "min_y": -180, "max_x": 900, "max_y": 190 },
  { "id": 2, "kind": "terminal",      "min_x": -10, "min_y": 190,  "max_x": 890, "max_y": 275 },
  { "id": 3, "kind": "pier",          "min_x": 280, "min_y": -205, "max_x": 780, "max_y": -180 },
  { "id": 4, "kind": "control_tower", "min_x": 920, "min_y": 200,  "max_x": 940, "max_y": 220 }
],
"bridges": [
  { "id": 1, "x0": 308, "y0": -180, "x1": 306, "y1": -160, "width": 3 },
  { "id": 2, "x0": 452, "y0": -180, "x1": 446, "y1": -163, "width": 3 },
  { "id": 3, "x0": 608, "y0": -180, "x1": 606, "y1": -160, "width": 3 },
  { "id": 4, "x0": 770, "y0": -180, "x1": 761, "y1": -158, "width": 3 }
]
```

> **LOW CONFIDENCE — the fixture's scenery.** The stands lie south of the
> taxiway and the landside zones north of it, as the merged fixture
> already places them. So the terminal and the pier are separate
> buildings, a satellite pier. Moving merged coordinates would risk
> merged `app.ui` and `app.host` tests. The owner may want a different
> layout for the playtest, and that is a fixture change, not a spec one.

### For the Planner

Four tasks. Each starts only when its dependencies are merged.

1. **Render scene: visuals, facing, paint, markings, scenery and
   looks.** Test Author first, then a worker. Writable paths:
   `src/app/render/Scene/**` (including the project reference to
   `sim.schedule`), `tests/app/render/**`, `tests/fixtures/render/**`,
   `data/looks/**` and `data/schemas/looks.schema.json`. It depends on
   nothing unmerged.
2. **Render 2D art: atlas and tessellator.** Test Author first, then a
   worker. Writable paths: `src/app/render/Art2D/**`,
   `tests/app/render/**` (adding the project reference) and
   `AirportSim.sln`, for the one new project (`07` "Solution"). Do not
   release it alongside another `.sln` task. It depends on task 1.
3. **Render Unity backend: upload and copy.** A worker only, since CI has
   no behaviour test for the backend (§15.3). Writable path:
   `src/app/render/Unity/**`: `RenderBackend.cs`; `DefaultPalette.asset`,
   which keeps its GUID; and the `.asmdef`, which gains
   `AirportSim.App.Render.Art2D.dll`. Done-when: `unity-build` green
   (`16` §16.2), and the Reviewer checks it against §15.10 line by line.
   It depends on task 2. **Merge it as soon as it is green.** From task 1
   merging until then, the merged backend's 13-colour palette is indexed
   by the four new roles, so a playable build throws. The checkpoint
   smoke is unaffected, because its backends are deactivated (`16`
   §16.7).
4. **Host: sources and looks into the scene.** Test Author first, then a
   worker. Writable paths: `src/app/host/**`, `tests/app/host/**` and
   `unity/AirportSim/Assets/Scripts/AirportSimBootstrap.cs` (`16` §16.4,
   §16.5, §16.7, §16.11). Done-when includes `unity-build` green. It
   depends on task 1, and may run in parallel with tasks 2 and 3.

The T-025 playtest waits for all four, and is validated against
§15.15's acceptance bar.
