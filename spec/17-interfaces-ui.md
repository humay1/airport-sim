# 17 — Public interfaces: `app.ui` (Phase 1)

Implements the `app.ui` row of `03-module-map.md` for Phase 1 only, as owner
decision D5 scopes it: HUMAN DECISION — owner (delegated), 2026-09-23,
reversible. Clicking a flow-node box requests a lane change through the
command queue. Speed and pause controls drive the pacer. There is no other UI
at Phase 1. The split into a headless layer and a thin backend follows
`15-interfaces-render.md`. The command plumbing and the lane-state read it
relies on were answered by `open-questions.md` Q-010 (`08` §8.7, `09` §9.7b).
Notation is as in `08-interfaces-core.md`. Where
this file appears to contradict `01-architecture.md` or `02-determinism.md`,
those win and it is a spec bug.

Reading order for an `app.ui` worker: `01`, `02`, `07`, `08` §8.5 and §8.7,
`09` §9.7b and §9.8, `15` §15.4, §15.5, §15.8, §15.9, `16` §16.6, this file.

---

## 17.1 What the module is, and what it is not

At Phase 1, `app.ui` owns:

- the **pacing state**, paused and speed, which the frame loop hands to the
  pacer (§17.4);
- the **lane click**: hit-testing a click against the flow-node boxes and
  turning a hit into a lane request (§17.5);
- screen-to-world mapping for clicks (§17.3);
- the backend contract for drawing the control strip and reporting input
  (§17.8).

`app.ui` explicitly does **not** own, and must not do, at Phase 1:

- any text, label, panel, tooltip, advisor, delay-tree view or screen. Each is
  later scope, by amendment;
- the camera (`app.render`'s backend) or the frame loop (`app.host`,
  `16` §16.6);
- any sim query beyond those listed in §17.6, and any command other than the
  lane request's `SetServersOpen`;
- drawing the lane count. `app.render` draws it as lane pips in world space
  (`15` §15.5); `app.ui` only reads it to compute a request.

---

## 17.2 The two layers

| | UI scene layer | UI backend |
|---|---|---|
| Directory | `src/app/ui/Scene/` | `src/app/ui/Unity/` |
| Engine references | **none**, asserted by test | Unity 6 |
| Target | `netstandard2.1`, `LangVersion 9` (D1) | compiled by Unity, in `app.host`'s project (`16` §16.2) |
| Tested in CI | yes, §17.10 | no |

The scene layer references `app.render`'s scene layer (for `CameraView`,
`WorldPoint`, `GameSpeed`, `RenderLayout`), following `03-module-map.md`
(`app.ui` depends on render). It never references `app.render`'s backend or
`app.host`. It follows the same rules as `15` §15.3: floats permitted, no
wall-clock read, no `System.Random`, no static mutable state, and
`07-conventions.md` "Runtime portability" rules 3, 4 and 7.

---

## 17.3 Input

The backend reports **semantic** inputs in screen coordinates. Mapping them to
the world, and deciding what they mean, happens here.

```
readonly struct ScreenPoint { float X; float Y }   // pixels; origin bottom-left, +Y up

enum UiInputKind { TogglePause, SetSpeed, PrimaryClick, SecondaryClick }

readonly struct UiInput {
  UiInputKind Kind
  GameSpeed   Speed          // SetSpeed only
  ScreenPoint At             // PrimaryClick / SecondaryClick only
}
```

Screen to world, for a screen of `w × h` pixels (`w, h > 0`) and the frame's
`CameraView`:

```
world.X = Centre.X + (At.X / w − 0.5) × ViewHeight × Aspect
world.Y = Centre.Y + (At.Y / h − 0.5) × ViewHeight
```

A click that lands on a UI control is the backend's to recognise. It reports
that click as `TogglePause`/`SetSpeed`, never also as a world click.

---

## 17.4 Pacing state

```
readonly struct PacingState { bool Paused; GameSpeed Speed }
```

- Initial state: not paused, `X1`.
- `TogglePause` flips `Paused`. `SetSpeed(s)` sets `Speed = s` and leaves
  `Paused` unchanged.
- Inputs are applied in list order. A frame may carry several.
- The frame loop passes `Paused` and `Speed` to `ITickPacer.Advance`
  (`16` §16.6). The pacing state is presentation state, never saved, and it
  cannot change a sim outcome: the sim sees only a sequence of `Step` calls
  (`15` §15.8).

---

## 17.5 The lane click

1. **Hit test.** Map the click to world coordinates (§17.3). The hit is the
   `FlowNodeBox` of the validated `RenderLayout` (`15` §15.4) that contains
   the point, with closed intervals. If several boxes contain it, the one
   drawn on top wins, which is the highest `NodeId`, matching `15` §15.5's
   ascending draw order. No box: the click is ignored.
2. **Lane request.** `PrimaryClick` on a hit requests **one more** open
   server at that node; `SecondaryClick` requests **one fewer**. At Phase 1 a
   security checkpoint is drawn as its node's box, so clicking anywhere in
   the box is clicking "the lanes".
3. **Hand-off.** The controller calls its `ILaneCommandSink` (§17.7) with
   `(node, +1)` or `(node, −1)`, once per click, in input order.
4. **Command (Q-010).** The production sink handles `Request(node, delta)`
   like this:
   - If `IFlowSystem.TryGetLaneState(node)` returns false, the node is not a
     lane, and the request is ignored.
   - `base` is the node's **pending target** if there is one, otherwise
     `lanes.ServersOpen`. A pending target is the `count` of this sink's last
     admitted command for the node, and it stays pending while
     `CurrentTick <= cmd.Tick`, i.e. until the sim has certainly run that
     tick. This is what stops two quick clicks before a tick runs from
     collapsing into one.
   - `target = clamp(base + delta, 0, lanes.ServerCount)`. If
     `target == base`, nothing is submitted.
   - Otherwise it submits one `Command { Tick = CurrentTick +
     COMMAND_MIN_LEAD_TICKS, Issuer = PLAYER_LOCAL, Kind = SetServersOpen,
     Payload = (node, target) }`, encoded per `08` §8.7, through
     `ISimHost.TrySubmit`. If the command is admitted, `target` becomes the
     node's pending target.
   - A rejected command is dropped: it is never retried and never re-dated
     (`08` §8.7).
   - It runs only inside `Update`, which the frame loop runs between
     `Step`s. The pending targets are presentation state and are never saved.

While paused, a click is still submitted. It applies at the next tick the
game runs.

---

## 17.6 Sim members used

The complete list. Calling any other sim member from `app.ui` is a review
rejection.

| Member | Spec | Used by | When |
|---|---|---|---|
| `ISimHost.CurrentTick` | `08` §8.5 | the lane sink | per request |
| `ISimHost.TrySubmit` | `08` §8.5, §8.7 | the lane sink | per request, inside `Update` |
| `IFlowSystem.TryGetLaneState` | `09` §9.7b | the lane sink | per request |

`Step`, `WorldStateHash` and every other query are not called.

---

## 17.7 Types and interfaces (scene layer)

```
readonly struct UiFrame {
  PacingState Pacing                       // what the backend's control strip shows
}

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

Construction (Q-009), following `08` §8.11a's factory rule:

```
UiFactory.CreateController(in RenderLayout layout, ILaneCommandSink sink) -> IUiController
UiFactory.CreateLaneCommandSink(ISimHost host, IFlowSystem flow) -> ILaneCommandSink   // Q-010
```

Controller tests use a fake sink. Sink tests use a fake host and a fake flow.
When `sim.flow` is not registered, `app.host` passes a sink that ignores every
request.

---

## 17.8 The backend contract

Specified so that its task cannot drift.

- It draws a control strip with four controls: pause, 1x, 2x and 4x. They are
  drawn as **icons**, so no player-visible text exists yet. If text is ever
  added, it is a `LocalisedKey` (`04-data-schemas.md`). The strip's state
  comes from `UiFrame` only.
- It reports presses on those controls as `TogglePause`/`SetSpeed`, and any
  other primary or secondary click inside the game view as
  `PrimaryClick`/`SecondaryClick` at its screen position. Optional keyboard
  shortcuts map to the same inputs.
- It collects this frame's inputs, in arrival order, for the bootstrap to put
  in `FrameInput` (`16` §16.6).
- It calls **no** sim member and never branches on sim state. Like
  `15` §15.10, it must stay small enough to review line by line.

---

## 17.9 Budget

- No allocation in `Update` or `Frame` after the first call.
- `Update` is O(inputs × flow-node boxes). At Phase 1 that is a handful of
  inputs against at most a few hundred boxes, so it carries no time budget of
  its own. `app.ui` adds nothing to the sim's 6 ms.

---

## 17.10 Scope, fixtures and tests (for the Planner)

**Scope.** The UI scene layer: `src/app/ui/Scene/**`, `tests/app/ui/**`. It
depends on T-020 (it compiles against `app.render`'s scene-layer types). The
production `ILaneCommandSink` compiles against `sim.core`'s command types
(`08` §8.7) and `IFlowSystem.TryGetLaneState` (`09` §9.7b), so it also depends
on the tasks that deliver those. The UI backend
(`src/app/ui/Unity/**`) is a separate task, after `app.host`'s Unity project
exists.

**Fixture.** None of its own. Tests reuse `tests/fixtures/render/phase1-layout.*`
(`15` §15.12), plus a synthetic layout with overlapping boxes.

Done-condition tests, phrased per `07-conventions.md`:

- `test_ui_scene_assembly_has_no_engine_reference` — static
- `test_ui_starts_unpaused_at_1x`
- `test_ui_toggle_pause_and_set_speed_apply_in_input_order`
- `test_ui_set_speed_does_not_unpause`
- `test_ui_screen_to_world_maps_corners_and_centre`
- `test_ui_click_hits_box_inclusive_of_edges`
- `test_ui_overlapping_boxes_hit_highest_node_id`
- `test_ui_click_outside_every_box_requests_nothing`
- `test_ui_primary_and_secondary_click_request_plus_and_minus_one`
- `test_ui_pause_and_speed_do_not_change_outcome` — integration. Run one
  sim-day with the real `sim.schedule` and `sim.flow`, once through the
  `16` §16.6 frame order with scripted pause and speed inputs and no clicks,
  and once headless with plain `Step` calls. Checkpoints must be identical at
  every checkpoint tick.
- `test_ui_lane_request_submits_set_servers_open_for_next_tick`
- `test_ui_lane_request_encodes_payload_per_core_layout`
- `test_ui_lane_request_ignored_for_non_queue_node`
- `test_ui_lane_request_clamped_and_no_submit_when_unchanged`
- `test_ui_two_clicks_before_tick_runs_build_on_pending_target`
- `test_ui_rejected_command_is_dropped_not_redated`

---

## 17.11 Open

None at Phase 1.
