# T-029 — `app.ui` scene layer: pacing, lane click, production lane sink

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.ui` (scene layer only) |
| Assigned role | worker |
| Depends on | T-020, T-023, T-005 |
| Spec source | `spec/00-overview.md`; `spec/17-interfaces-ui.md` (new file, D5; command plumbing answered by Q-010) |
| Blocked by | — |

## Writable paths

```
src/app/ui/Scene/**, tests/app/ui/**
```

`src/app/ui/Unity/**` (the backend) is out of scope for this task — it is
T-033, once `app.host`'s Unity project exists.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.5, §8.7, `spec/09-interfaces-flow.md` §9.7b,
§9.8, `spec/15-interfaces-render.md` §15.4, §15.5, §15.8, §15.9,
`spec/16-interfaces-host.md` §16.6, `spec/17-interfaces-ui.md`

## Interface to implement

```
readonly struct ScreenPoint { float X; float Y }

enum UiInputKind { TogglePause, SetSpeed, PrimaryClick, SecondaryClick }

readonly struct UiInput {
  UiInputKind Kind
  GameSpeed   Speed          // SetSpeed only
  ScreenPoint At             // PrimaryClick / SecondaryClick only
}

readonly struct PacingState { bool Paused; GameSpeed Speed }

readonly struct UiFrame { PacingState Pacing }

interface ILaneCommandSink {
  void Request(NodeId node, int32 delta)   // delta is +1 or −1
}

interface IUiController {
  void        Update(IReadOnlyList<UiInput> inputs, in CameraView camera,
                     float screenWidth, float screenHeight)
  PacingState Pacing { get }
  UiFrame     Frame()
}
```

Construction (`17` §17.7, Q-009):

```
UiFactory.CreateController(in RenderLayout layout, ILaneCommandSink sink) -> IUiController
UiFactory.CreateLaneCommandSink(ISimHost host, IFlowSystem flow) -> ILaneCommandSink   // Q-010
```

Binding, copied from `spec/17-interfaces-ui.md`, not paraphrased:

- **Screen to world** (§17.3): `world.X = Centre.X + (At.X/w − 0.5) ×
  ViewHeight × Aspect`; `world.Y = Centre.Y + (At.Y/h − 0.5) × ViewHeight`.
  A click landing on a UI control is the backend's to recognise and report
  as `TogglePause`/`SetSpeed`, never also as a world click.
- **Pacing state** (§17.4): initial `{ Paused: false, Speed: X1 }`.
  `TogglePause` flips `Paused`; `SetSpeed(s)` sets `Speed` and leaves
  `Paused` unchanged. Inputs apply in list order. Never saved; cannot
  change a sim outcome (the sim sees only `Step` calls, `15` §15.8).
- **The lane click** (§17.5): hit-test against the validated `RenderLayout`'s
  `FlowNodeBox`es, closed intervals, highest `NodeId` wins on overlap, no
  box → ignored. `PrimaryClick` = request `+1`; `SecondaryClick` = request
  `−1`. Hand off to `ILaneCommandSink.Request(node, delta)`, once per click,
  in input order.
- **The production sink** (§17.5 step 4, Q-010): if
  `IFlowSystem.TryGetLaneState(node)` is false, ignore. `base` = the node's
  **pending target** if one exists (the `count` of this sink's last admitted
  command for the node, pending while `CurrentTick <= cmd.Tick`), else
  `lanes.ServersOpen`. `target = clamp(base + delta, 0, lanes.ServerCount)`.
  `target == base`: submit nothing. Otherwise submit `Command { Tick =
  CurrentTick + COMMAND_MIN_LEAD_TICKS, Issuer = PLAYER_LOCAL, Kind =
  SetServersOpen, Payload = (node, target) }` (encoding per `08` §8.7)
  through `ISimHost.TrySubmit`; on admission, `target` becomes the pending
  target. A rejected command is dropped, never retried or re-dated. Runs
  only inside `Update`, called by the frame loop between `Step`s (`16`
  §16.6). Pending targets are presentation state, never saved. A click
  while paused still submits and applies at the next tick the game runs.
- **When `sim.flow` is not registered**, `app.host` passes a sink that
  ignores every request — this task's sink does not special-case that
  itself; it is the caller's choice of which sink to construct.

## Events

Emitted: none. Consumed: none (reads `IFlowSystem.TryGetLaneState` and
calls `ISimHost.TrySubmit`; never subscribes to the event bus).

## Tests to pass

```
tests/app/ui/**
```

Written by the Test Author, against `tests/fixtures/render/phase1-layout.*`
(`15` §15.12) plus a synthetic layout with overlapping boxes. Expect at
least:

- `test_ui_scene_assembly_has_no_engine_reference` — static
- `test_ui_starts_unpaused_at_1x`
- `test_ui_toggle_pause_and_set_speed_apply_in_input_order`
- `test_ui_set_speed_does_not_unpause`
- `test_ui_screen_to_world_maps_corners_and_centre`
- `test_ui_click_hits_box_inclusive_of_edges`
- `test_ui_overlapping_boxes_hit_highest_node_id`
- `test_ui_click_outside_every_box_requests_nothing`
- `test_ui_primary_and_secondary_click_request_plus_and_minus_one`
- `test_ui_pause_and_speed_do_not_change_outcome` — integration: one real
  sim-day, once through the `16` §16.6 frame order with scripted
  pause/speed inputs and no clicks, once headless with plain `Step` calls;
  checkpoints identical at every checkpoint tick
- `test_ui_lane_request_submits_set_servers_open_for_next_tick`
- `test_ui_lane_request_encodes_payload_per_core_layout`
- `test_ui_lane_request_ignored_for_non_queue_node`
- `test_ui_lane_request_clamped_and_no_submit_when_unchanged`
- `test_ui_two_clicks_before_tick_runs_build_on_pending_target`
- `test_ui_rejected_command_is_dropped_not_redated`

**Do not edit them.** If a test contradicts `spec/17-interfaces-ui.md`, file
an open question and stop.

## Performance budget

No allocation in `Update`/`Frame` after the first call. `Update` is
O(inputs × flow-node boxes) — a handful of inputs against at most a few
hundred boxes at Phase 1, so no dedicated time budget; `app.ui` adds
nothing to the sim's 6 ms.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

The scene layer references `app.render`'s scene layer for `CameraView`,
`WorldPoint`, `GameSpeed` and `RenderLayout` (`03-module-map.md`: `app.ui`
depends on render) — never its backend, never `app.host`. It targets
`netstandard2.1`/`LangVersion 9` like every other headless layer (D1);
tests target `net8.0`.

The +1/−1 click grammar is the Architect's reading of D5, flagged in
`CHANGELOG.md` as worth the owner's attention, not this task's to
second-guess or change.

Draws nothing itself — `app.render` draws the lane pips in world space
(T-020, `15` §15.5). This task only reads `TryGetLaneState` to compute a
request; do not add any drawing code here.
