# T-020 — Minimal top-down renderer, flat colours (headless scene layer)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.render` (scene layer only) |
| Assigned role | worker |
| Depends on | T-009, T-010, T-021, T-023, T-026 |
| Spec source | `spec/00-overview.md` build order; `spec/15-interfaces-render.md` (all §15.13 HUMAN DECISIONS now made: D1, D4, D5, D7; lane pips answer Q-010 item 5) |
| Blocked by | — |

## Scope note

Q-008 is now fully answered: all five §15.13 HUMAN DECISIONS have been made
(D1 netstandard2.1, D4 game speeds, D5 the minimal `app.ui`, D7 `app.host`
and the composition root). This task remains the **headless scene layer
only**; the Unity backend (`src/app/render/Unity/**`) is now taskable as
T-032, once this task and `app.host`'s headless side (T-031) exist.

## Writable paths

```
src/app/render/Scene/**
AirportSim.sln
```

**Correction (Q-021):** `tests/**` (including `tests/fixtures/**`) is the
Test Author's territory exclusively; the path guard already blocks a worker
grant there. The earlier grants of `tests/app/render/**` and
`tests/fixtures/render/**` are dropped.

**First task of a new module (`07` L8, Q-013):** this task creates
`src/app/render/Scene/AirportSim.App.Render.csproj` and
`tests/app/render/AirportSim.App.Render.Tests.csproj` (byte for byte per
`07` L2/L3) and adds both to `AirportSim.sln`. Never release this task
concurrently with any other "first task of a new module" (T-007, T-008,
T-012, T-021, T-022, T-024, T-029, T-031) — concurrent `.sln` edits
conflict (`07` L8).

`src/app/render/Unity/**` is explicitly out of scope for this task — it is
T-032. The scene layer holds **no engine reference**, asserted by
`test_scene_assembly_has_no_engine_reference`, and targets
`netstandard2.1`/`LangVersion 9` (D1, `15` §15.3) — it inherits the sim's
target and makes no framework choice of its own. Its own tests target
`net8.0`, like the sim's.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.5, §8.11a, `spec/09-interfaces-flow.md` §9.1, §9.7, §9.7b,
`spec/12-interfaces-airside.md` §12.4, §12.9, `spec/16-interfaces-host.md`
§16.6, `spec/15-interfaces-render.md`

## Interface to implement

```
readonly struct TaxiNodePosition { TaxiNodeId Node; int32 X; int32 Y }
readonly struct RunwayGeometry   { RunwayId Runway; int32 X0; int32 Y0; int32 X1; int32 Y1; int32 Width }
readonly struct FlowNodeBox      { NodeId Node; int32 MinX; int32 MinY; int32 MaxX; int32 MaxY; int32 FillCapacity }

readonly struct RenderLayout {
  IReadOnlyList<TaxiNodePosition> TaxiNodes
  IReadOnlyList<RunwayGeometry>   Runways
  IReadOnlyList<FlowNodeBox>      FlowNodes
  int32 StandSize
  int32 AircraftSize
  int32 AgentSize
  int32 TaxiwayWidth
}

interface IRenderLayoutLoader {
  RenderLayout Load(ReadOnlySpan<byte> file, string sourceName, in AirsideLayout? airside)
}

readonly struct WorldPoint { float X; float Y }

readonly struct CameraView {
  WorldPoint Centre
  float      ViewHeight        // world units; > 0
  float      Aspect            // width / height; > 0
}

enum DrawLayer     { Runway, Taxiway, Stand, LandsideNode, QueueFill, Lane, Agent, Aircraft }   // draw order
enum PrimitiveKind { Box, Segment, Dot }
enum ColourRole {
  Runway, RunwayQueued, Taxiway, StandFree, StandOccupied,
  LandsideNode, QueueFill, Agent,
  AircraftMoving, AircraftHolding, AircraftOnStand,
  LaneOpen, LaneClosed                                  // appended, Q-010 — no existing ordinal moves
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
  WorldPoint    A
  WorldPoint    B
  float         Size
  SourceRef     Source
}

readonly struct RenderFrame {
  Tick                         Tick
  CameraView                   Camera
  IReadOnlyList<DrawPrimitive> Primitives    // valid until the next Build
}

readonly struct RenderSources {
  ISimHost        Host
  IAirsideSystem? Airside
  IFlowSystem?    Flow
}

interface ISceneBuilder        { RenderFrame Build(in CameraView camera) }
interface IPromotionController { void Update(in CameraView camera) }

enum GameSpeed { X1 = 1, X2 = 2, X4 = 4 }          // the value is the multiplier; D4

interface ITickPacer {
  uint32 Advance(int64 elapsedRealMicroseconds, bool paused, GameSpeed speed)   // ticks to Step this frame
}
```

## Construction (`15` §15.9, Q-009)

```
RenderFactory.CreateLayoutLoader() -> IRenderLayoutLoader
RenderFactory.CreateSceneBuilder(in RenderSources sources, in RenderLayout layout) -> ISceneBuilder
RenderFactory.CreatePromotionController(in RenderSources sources, in RenderLayout layout) -> IPromotionController
RenderFactory.CreatePacer() -> ITickPacer
```

Stateless static methods only, following `08` §8.11a's factory rule. In a
playable build, `app.host`'s presentation composer (T-031) builds
`RenderSources` from the composed sim; this task's own tests build them
from fakes and, for the integration test, from the same composition the
headless harness uses.

Binding, copied from `spec/15-interfaces-render.md`, not paraphrased:

- **Layout and its validation** (§15.4): coordinates and sizes are integers,
  no floats in the layout file. When `airside` is given, every
  `TaxiNodeDef`/`RunwayDef` must have exactly one matching
  `TaxiNodePosition`/`RunwayGeometry`, and no position/geometry may name an
  unknown id — hard failure naming the file and the offending id.
  `FlowNodeBox.Node` unique; `MinX < MaxX`, `MinY < MaxY`, `FillCapacity >
  0`; every size `> 0`. `IFlowSystem` offers no node enumeration, so a
  `FlowNodeBox` naming an unknown landside node is caught only by the
  integration test, not the loader.
- **What is drawn** (§15.5): the full source→primitive table — runways,
  taxi edges, stands, `FlowNodeBox`es, queue fill, agents of promoted boxes,
  tracked aircraft on the graph. Draw order is `DrawLayer` ascending, then
  `SourceRef` ascending, within a layer. Agent position is `AgentsAt(node)`
  sorted `(Cohort, Index)`, truncated to `MAX_DRAWN_AGENTS_PER_NODE = 256`,
  position a pure function of rank and box (exact arrangement is the
  worker's choice; tests assert count and containment only).
  `ProgressAlongEdge` is unused at Phase 1. Aircraft position: interpolate
  `AtNode`→other endpoint by `EdgeProgress` if `OnEdge` set; else the
  `AtNode` position; else (off-graph) not drawn. Aircraft colour by `Phase`
  per the table in §15.5 (`HeldForRunway`/`HeldOnTaxiway` →
  `AircraftHolding`; `OnRunway`/`Taxiing` → `AircraftMoving`;
  `OnStand`/`AwaitingPushbackClearance` → `AircraftOnStand`;
  `AwaitingApproach`/`Departed` → not drawn). If `RenderSources.Airside` is
  null, no airside primitive is produced and the airside half of the layout
  is unvalidated; if `Flow` is null, no landside primitive is produced.
  **Not drawn at Phase 1**: vehicles, turnaround jobs, corridors/flow edges,
  delay state, text/labels, terrain, weather, tick interpolation.
- **Lane pips** (Q-010, the visible half of D5's lane control): for each
  `FlowNodeBox` whose node `IFlowSystem.TryGetLaneState` accepts, one `Dot`
  per server up to `MAX_DRAWN_LANES_PER_NODE = 32`, `LaneOpen` colour for the
  first `ServersOpen` pips, `LaneClosed` for the rest, `DrawLayer.Lane`. The
  *k*-th pip's position is a pure function of *k*, the pip count and the
  box (arrangement is this task's choice; tests assert count, colour split
  and containment only). A node for which `TryGetLaneState` returns false
  gets no pips.
- **Sim queries polled, and cadence** (§15.6): the complete list —
  `ISimHost.CurrentTick` (every `Build`), `IAirsideSystem.Layout` (once, at
  construction), `IAirsideSystem.TrackedFlights`/`TryGetTrack`/`TryGetStand`/`RunwayQueueLength`
  (per rebuild), `IFlowSystem.Population` (per rebuild, per `FlowNodeBox`),
  `IFlowSystem.AgentsAt` (per rebuild, per promoted `FlowNodeBox`),
  `IFlowSystem.TryGetLaneState` (per rebuild, per `FlowNodeBox`),
  `IFlowSystem.SetPromoted` (promotion controller only). Calling any other
  sim member is a review rejection; the fakes in the test suite throw if one
  is called. `Build` is called at most once per rendered frame and rebuilds
  only if `CurrentTick` or the camera differs from the previous call.
  `WorldStateHash`, `TrySubmit`, `Inject`, `Absorb`, and every
  `sim.schedule`/`sim.turnaround`/`sim.delay` query are **never** called.
  `ISimHost.Step` is **not** called by `app.render` at all — `app.host`'s
  frame loop (`16` §16.6) is its only caller.
- **Promotion controller** (§15.7): a `FlowNodeBox` is visible if its box
  intersects the camera's view rectangle (closed intervals — touching
  counts); desired-promoted if visible **and** `camera.ViewHeight <=
  AGENT_ZOOM_THRESHOLD` (inclusive). First `Update`: call `SetPromoted` for
  **every** `FlowNodeBox`, ascending `NodeId`. Every later `Update`: call
  only for nodes whose desired state changed, ascending `NodeId`. A node not
  in the layout is never promoted/demoted. `SetPromoted` is the controller's
  only call that changes anything in the sim, made only between `Step`s,
  and nothing read is fed back — this is what keeps promotion neutrality
  from being broken from outside the sim.
- **Tick pacer** (§15.8, amended by D4 — game speeds pause/1x/2x/4x):
  integer accumulator of **speed-scaled** microseconds, never saved.
  `paused`: returns 0, discards `elapsed`. Otherwise `acc += elapsed ×
  (int)speed; n = acc / REAL_MICROSECONDS_PER_TICK_1X; acc -= n × that`; if
  `n > MAX_CATCHUP_TICKS_PER_FRAME (3)`, clamp `n` to it and zero `acc` —
  the cap is the same at every speed, so it binds only below about 13 fps
  at 4x, limiting one frame's sim work to 3 ticks (18 ms at max tier).
  Changing `speed` between calls keeps the accumulator: no partial tick is
  lost or duplicated. A negative `elapsed`, or a `speed` outside the enum,
  throws. **`app.render` holds neither `paused` nor `speed`** — `app.ui`
  chooses both (T-029, `17` §17.4) and the frame loop passes them in.
  Adding a speed beyond 1x/2x/4x is a spec amendment to `GameSpeed`, never
  a worker's choice.
- **Frame order** (moved to `16-interfaces-host.md` §16.6 by D7 — a frame
  now spans more than one presentation module, and `app.render` may not
  reference `app.ui`). This task's own part of it is unchanged:
  `IPromotionController.Update` runs before `Step`, `ISceneBuilder.Build`
  runs after. This task's integration test drives the full `16` §16.6
  order itself, with no dependency on `app.host` (T-031) existing.
- **No commands.** `app.render` never calls `ISimHost.TrySubmit` at Phase 1.

## Events

Emitted: none.
Consumed: none (reads sim state through the read-only queries of §15.6 only,
never subscribes to the event bus).

## Tests to pass

```
tests/app/render/**
```

Written by the Test Author, against `tests/fixtures/render/phase1-layout.*`
(binding requirements: §15.12 — positions/geometry for every taxi
node/runway of `tests/fixtures/airside/phase1-single-runway.*`, a
`FlowNodeBox` for every landside node used by the schedule/T-007/T-023
fixtures including at least one `Queue` node, and a companion max-tier
fake-source setup for the budget test). Expect at least:

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
- `test_scene_calls_only_listed_sim_members`
- `test_promotion_first_update_sets_every_layout_node`
- `test_promotion_calls_only_on_change_in_ascending_node_id`
- `test_promotion_zoom_threshold_is_inclusive`
- `test_tick_pacer_steps_ten_ticks_per_real_second`
- `test_tick_pacer_steps_forty_ticks_per_real_second_at_4x`
- `test_tick_pacer_speed_change_keeps_accumulated_time`
- `test_tick_pacer_caps_catch_up_and_drops_backlog`
- `test_tick_pacer_paused_steps_nothing`
- `test_render_loop_is_outcome_neutral_with_scripted_camera` — integration:
  one real sim-day, run once through the `16` §16.6 frame order (this test
  drives that order itself, with no dependency on `app.host`) with a
  scripted camera sweeping every `FlowNodeBox` across the zoom threshold
  with irregular frame deltas, once headless with plain `Step` calls;
  checkpoints must match at every checkpoint tick. Also checks every
  `FlowNodeBox` against the running `sim.flow` fixture's node list.
- `test_scene_assembly_has_no_engine_reference` — static
- `test_scene_build_within_frame_budget_at_max_tier`

**Do not edit them.** If a test contradicts `spec/15-interfaces-render.md`,
file an open question and stop.

## Performance budget

`ISceneBuilder.Build` plus `IPromotionController.Update`: **mean ≤ 2.0 ms,
p99 ≤ 4.0 ms per frame** (§15.11), measured per `03-module-map.md`'s
protocol against fakes sized to max tier (3 runways, 60 stands all
occupied, 100 tracked aircraft, 200 `FlowNodeBox`es, 16 promoted with 256
agents each). No allocation in `Build`/`Update` after the first call — the
primitive buffer is reused, which is why `RenderFrame.Primitives` is valid
only until the next `Build`. This budget is separate from, and does not add
to, the sim's own 6 ms/tick budget.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task's dependency list grew from T-009 alone to T-009, T-010, T-021,
**and now T-023**: the scene layer compiles against
`IFlowSystem.SetPromoted`/`AgentsAt` (T-010), `IAirsideSystem.Layout()`
(T-021), and now `IFlowSystem.TryGetLaneState` for lane pips (T-023). Do
not release this task before all four have merged.

The backend (`src/app/render/Unity/**`) is now taskable as **T-032**, since
every `§15.13` HUMAN DECISION is made — do not write anything under
`src/app/render/Unity/**` from this task regardless.

`AGENT_ZOOM_THRESHOLD = 120` and the 2.0/4.0 ms budget are both LOW
CONFIDENCE, **accepted as provisional (HD, D8)** — build to the stated
numbers; retuning them is revisited after T-025's playtest, by Architect
amendment, not a worker judgement call.
