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
  nothing from `sim.turnaround`, `sim.delay` or `sim.schedule` at Phase 1.

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
`REAL_MICROSECONDS_PER_TICK_1X` is `long`. §15.7 compares `ViewHeight` with
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

| Source | Primitive | Geometry | `ColourRole` | `DrawLayer` |
|---|---|---|---|---|
| each runway | `Segment` | `(X0,Y0)`–`(X1,Y1)`, width `Width` | `RunwayQueued` if `RunwayQueueLength > 0`, else `Runway` | `Runway` |
| each taxi edge | `Segment` | position of `From` to position of `To`, width `TaxiwayWidth` | `Taxiway` | `Taxiway` |
| each stand | `Box` | centred on the stand's `Node` position, side `StandSize` | `StandOccupied` if `StandState.Occupant` is set, else `StandFree` | `Stand` |
| each `FlowNodeBox` | `Box` | the box | `LandsideNode` | `LandsideNode` |
| queue fill, if `Population > 0` | `Box` | same `MinX`, `MinY`, `MaxY`; width = box width × `min(1, Population / FillCapacity)` | `QueueFill` | `QueueFill` |
| lane pips of a `FlowNodeBox` whose node `TryGetLaneState` accepts | `Dot` | inside the box, one per server up to `MAX_DRAWN_LANES_PER_NODE`, diameter `AgentSize` | `LaneOpen` for the first `ServersOpen` pips, `LaneClosed` for the rest | `Lane` |
| agents of a promoted `FlowNodeBox` | `Dot` | inside the box, one per agent, diameter `AgentSize` | `Agent` | `Agent` |
| each tracked aircraft that is on the graph | `Dot` | see below, diameter `AircraftSize` | by phase, below | `Aircraft` |

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
it.

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

Cadence:

- `Build` is called at most once per rendered frame. It **rebuilds** only if
  `CurrentTick`, the camera or the `GraphicsSettings` (§15.14) differs from
  the previous `Build`; otherwise it returns the previous frame unchanged. At 60 fps and 1x, the sim advances
  every sixth frame, so most frames re-read nothing.
- No query is ever made while `Step` is running. Given §15.8's frame order and
  the fact that `Step` is synchronous (`08` §8.5), this holds by construction.
  It also means a frame never mixes two ticks' state.
- `WorldStateHash`, `TrySubmit`, `Inject`, `Absorb` and every query of
  `sim.schedule`, `sim.turnaround` and `sim.delay` are **not** called.

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
  LaneOpen, LaneClosed                                  // appended, Q-010
}
enum SourceKind    { Runway, TaxiEdge, Stand, FlowNode, QueueFill, Agent, Aircraft, Lane }

readonly struct SourceRef {
  SourceKind Kind
  uint64     Id                // RunwayId / TaxiEdgeId / StandId / NodeId / FlightId value
  int32      Sub               // agent rank or lane index within its node; 0 otherwise
}

readonly struct DrawPrimitive {
  PrimitiveKind Kind
  DrawLayer     Layer
  ColourRole    Colour
  WorldPoint    A              // Box: min corner.  Segment: start.  Dot: centre.
  WorldPoint    B              // Box: max corner.  Segment: end.    Dot: unused.
  float         Size           // Box: unused.      Segment: width.  Dot: diameter.
  SourceRef     Source
}

readonly struct RenderFrame {
  Tick                         Tick
  CameraView                   Camera
  IReadOnlyList<DrawPrimitive> Primitives    // valid until the next Build
  GraphicsSettings             Graphics      // §15.14; what the backend applies (D10)
}

readonly struct RenderSources {
  ISimHost        Host
  IAirsideSystem? Airside
  IFlowSystem?    Flow
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
```

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
  frame after the first. **LOW CONFIDENCE**: this binds the implementation
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

**(c) The composition root — DECIDED, with its construction step open.**
HUMAN DECISION — owner (delegated), 2026-09-23 (D7). `app.host`'s headless
part (`src/app/host/`) builds `ISimHost` and the systems from a scenario
bundle and hands read-only views to presentation (`16` §16.3 to §16.5). A
thin Unity bootstrap only calls it (§16.7), and it also runs the frame loop
(§16.6). How each module's system is constructed is still unpublished; that
is `open-questions.md` Q-009.

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
   layer is **identical** in kind, layer, colour, geometry and source, and
   so is their order. That covers runways, taxiways, stands, landside nodes,
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
  at Phase 1 (§15.5, flat colours), so there is no effects knob. A new knob
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
