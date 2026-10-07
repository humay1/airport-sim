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
- the player-visible text: its keys, the English string table and the
  lookup (§17.4b, Q-125);
- the backend contract for drawing the control strip and reporting input
  (§17.8).

`app.ui` explicitly does **not** own, and must not do, at Phase 1:

- any text, label, panel, tooltip, advisor, delay-tree view or screen, except
  the graphics settings panel of §17.4a (D10) and its text (§17.4b). Each is
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
| Tested in CI | yes, §17.10 | build-checked only, by `unity-build` (`16` §16.2), which is not a required check; no behaviour test |

The scene layer references `app.render`'s scene layer (for `CameraView`,
`WorldPoint`, `GameSpeed`, `RenderLayout`), following `03-module-map.md`
(`app.ui` depends on render). It never references `app.render`'s backend or
`app.host`. It follows the same rules as `15` §15.3: floats permitted, no
wall-clock read, no `System.Random`, no static mutable state, and
`07-conventions.md` "Runtime portability" rules 3, 4 and 7.

**Floats in tests (Q-102).** `tests/app/ui/` may use `float` only for the
values of `float`-typed members and parameters (`ScreenPoint`, the screen
size, `CameraView`, `WorldPoint`). Every such value is dyadic and chosen so
that every intermediate result of §17.3's mapping is exact, and results are
compared exactly (`07` L4). The one exemption: the NaN and ±infinity inputs
that §17.7's argument checks require (a non-finite screen size, a
non-finite click, Q-104) are written as such. They are the only non-dyadic
values a test may write.

---

## 17.3 Input

The backend reports **semantic** inputs in screen coordinates. Mapping them to
the world, and deciding what they mean, happens here.

```
readonly struct ScreenPoint { float X; float Y }   // pixels; origin bottom-left, +Y up

enum UiInputKind {
  TogglePause, SetSpeed, PrimaryClick, SecondaryClick,
  ToggleSettings, SetGraphicsPreset, SetGraphicsSettings        // appended, D10 (§17.4a)
}

readonly struct UiInput {
  UiInputKind      Kind
  GameSpeed        Speed          // SetSpeed only
  ScreenPoint      At             // PrimaryClick / SecondaryClick only
  GraphicsPreset   Preset         // SetGraphicsPreset only (15 §15.14)
  GraphicsSettings Graphics       // SetGraphicsSettings only
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
- **The settings panel pauses (Q-034, §17.4a).** The controller holds the
  player's own pause separately. `IUiController.Pacing` and `UiFrame.Pacing`
  report `Paused = player's pause OR SettingsOpen`, and `Speed` unchanged.
  So closing the panel restores exactly the pause state the player had.
- The frame loop passes `Paused` and `Speed` to `ITickPacer.Advance`
  (`16` §16.6). The pacing state is presentation state, never saved, and it
  cannot change a sim outcome: the sim sees only a sequence of `Step` calls
  (`15` §15.8).

### 17.4a Graphics settings (D10)

HUMAN DECISION — owner, 2026-09-27 (D10). The player changes graphics quality
(`15` §15.14) at runtime from a settings panel. This is presentation state,
exactly like pacing: it is never saved with the game and never reaches the
sim.

- **State.** The controller holds `SettingsOpen`, initially false, and
  `Graphics`, initially the value it was constructed with (§17.7, from the
  stored preference or the default).
- `ToggleSettings` flips `SettingsOpen`. **While the panel is open, the sim
  is paused** (HUMAN DECISION — owner, 2026-09-27, Q-034). The pause is the
  `OR` of §17.4, so it takes effect in the same `RunFrame` as the input
  that opens the panel (`16` §16.6), and it ends in the same `RunFrame` as
  the input that closes it.
- `SetGraphicsPreset(p)` sets `Graphics = ForPreset(p)`, and `p = Custom` is
  ignored. `SetGraphicsSettings(g)` sets `Graphics` to `Validate` of a copy
  of `g` whose `Preset` is `Custom`. Both apply in input order
  (§17.4), whether or not the panel is open.
- While `SettingsOpen`, the panel is modal. Only `ToggleSettings`,
  `SetGraphicsPreset` and `SetGraphicsSettings` apply. `TogglePause`,
  `SetSpeed`, `PrimaryClick` and `SecondaryClick` are ignored, so the
  player's own pause and speed are unchanged when the panel closes. Inputs
  after a `ToggleSettings` in the same frame see the new `SettingsOpen`.
- **Invariant: graphics never affect gameplay (owner, 2026-09-27, D10
  addendum; `15` §15.14).** `Graphics` is read by nothing in this module
  except `UiFrame.Graphics` and the preference codec. Pacing, the pause and
  speed controls, the hit test, the lane request and its command timing,
  and the control strip are identical at every graphics setting. A future
  control or alert the player acts on is never gated by a graphics knob.
  The panel's pause depends on `SettingsOpen` only, never on `Graphics`.
- **Persistence: a player preference, not save state.** The preference
  value is the text
  `graphics 1 <Preset name> <DrawAgents 0|1> <MaxDrawnAgentsPerNode> <FrameRateCap> <ResolutionScalePercent> <AntiAliasing 0|1>`,
  with single spaces, decimal numbers and invariant formatting.
  `UiFactory.EncodeGraphicsPreference(in GraphicsSettings) -> string` and
  `UiFactory.TryDecodeGraphicsPreference(string, out GraphicsSettings) -> bool`
  produce and parse it (on the module's one factory, `08` §8.11a), and the
  decoder runs `Validate`. A missing, unknown
  or malformed value decodes to false, and the caller then uses the default,
  `Medium` (`15` §15.14, owner, Q-034). The host stores and loads it (`16` §16.6). It is never in
  `bundle.json`, a checkpoint dump, a command or a save.
- **Preference grammar (Q-103).** The decoder accepts exactly the text the
  encoder can produce, and nothing looser:
  - The text is exactly eight fields separated by single U+0020 spaces, with
    nothing before the first field or after the last. Any other whitespace,
    including a trailing space, tab, CR or LF, is malformed. A `null` text
    decodes false and does not throw.
  - Field 1 is `graphics` and field 2 is `1`, both exact and case-sensitive.
  - Field 3 is one of `Low`, `Medium`, `High`, `Custom`, exact and
    case-sensitive. A number is malformed.
  - The two boolean fields are exactly `0` or `1`.
  - The three integer fields are one or more ASCII digits `0`–`9`, with no
    sign (`-` or `+`), no leading zero unless the field is exactly `0`, no
    decimal point, no exponent and no group separator, and a value that fits
    in `int32`. Anything else is malformed.
  - Well-formed text decodes true even when a value is out of range or the
    values do not match the named preset. The result is `Validate` of the
    values as written, with `Preset` as written, which `Validate` leaves
    unchanged (`15` §15.14). So `graphics 1 Low 1 0 0 100 0` decodes to
    `Preset = Low` with `MaxDrawnAgentsPerNode = 1`.
  - After a false decode, the `out` value is `default(GraphicsSettings)`.
  - `EncodeGraphicsPreference(in GraphicsSettings settings)` writes the
    values as given, without `Validate`, with booleans as `0`/`1` and
    integers in the grammar above. A `Preset` outside `GraphicsPreset`, or a
    negative integer knob, throws `ArgumentOutOfRangeException` with
    `ParamName` `settings`, because it has no encoding.

### 17.4b Player-visible text (Q-125)

`04-data-schemas.md` says all player-visible text is a localisation key,
never a literal. This section is the whole mechanism at Phase 1: one
language, English, in one file, and one lookup. At Phase 1 the only text
is the settings panel's (§17.8). There is no language choice, no
fallback language, no plural rule and no text template.

- **The key.** `readonly struct LocalisedKey { string Value }`, built with
  `new LocalisedKey(value)`, compared ordinally. A key's `Value` is two or
  more segments separated by `.`, each a lowercase ASCII letter followed by
  lowercase ASCII letters, digits or `_`, that is
  `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`. The first segment names the
  module that draws the text: `ui` for `app.ui`. Every Phase 1 key starts
  `ui.settings.`.
- **The Phase 1 keys.** Exactly these twelve. A key is added, renamed or
  removed only by amendment, together with its text.

  | Key | Where the backend shows it | English text |
  |---|---|---|
  | `ui.settings.title` | the panel's title | `Graphics settings` |
  | `ui.settings.preset.low` | the `Low` preset control | `Low` |
  | `ui.settings.preset.medium` | the `Medium` preset control | `Medium` |
  | `ui.settings.preset.high` | the `High` preset control | `High` |
  | `ui.settings.knob.draw_agents` | the `DrawAgents` label | `Show passengers` |
  | `ui.settings.knob.max_drawn_agents_per_node` | the `MaxDrawnAgentsPerNode` label | `Max passengers drawn per area` |
  | `ui.settings.knob.frame_rate_cap` | the `FrameRateCap` label | `Frame rate cap (fps)` |
  | `ui.settings.knob.resolution_scale_percent` | the `ResolutionScalePercent` label | `Resolution scale (%)` |
  | `ui.settings.knob.anti_aliasing` | the `AntiAliasing` label | `Anti-aliasing` |
  | `ui.settings.value.on` | a boolean knob's value when true | `On` |
  | `ui.settings.value.off` | a boolean knob's value when false | `Off` |
  | `ui.settings.value.uncapped` | `FrameRateCap`'s value when it is 0 | `Uncapped` |

- **Knob values.** Each knob's current value, from `UiFrame.Graphics`, is
  shown beside its label. A boolean value is the text of
  `ui.settings.value.on` or `ui.settings.value.off`. An integer value is
  its invariant decimal digits, which are not text and have no key,
  except that a `FrameRateCap` of 0 is the text of
  `ui.settings.value.uncapped`. `Custom` has no key: no control shows it,
  and which preset is current may be shown by the controls' look.
- **The file.** `data/strings/en.json`, UTF-8 without a BOM:

  ```
  { "schema_version": 1, "id": "en",
    "strings": { "ui.settings.title": "Graphics settings", ... } }
  ```

  It has exactly these three keys. `strings` holds exactly the twelve keys
  above, in any order, each mapped to its English text. Each text is a
  non-empty string of characters U+0020 to U+007E only (Phase 1, so the
  default font draws every character). The English wording is not a
  balance value: a content task may write it, and the Reviewer checks it
  against the table above.
- **CI validation.** `data/schemas/strings.schema.json` validates it,
  through `ci/validate-content.py`'s pairing by name (`04`), with no change
  to `ci/`. The schema requires the three top-level keys with no others,
  `schema_version` the constant 1, `id` the constant `"en"`, and `strings`
  an object that requires exactly the twelve keys with no others, each
  value a string matching `^[ -~]+$`.
- **It is not sim content.** `08` §8.11's loader ignores `strings/`, like
  every directory that is not a kind directory. So the text is not in the
  content index or the content hash, and changing it cannot change a
  checkpoint. The build step already copies every file under `data/` into
  `StreamingAssets/Content/` (`16` §16.3), so it ships with no change
  there.
- **Loading.** `UiFactory.LoadStringTable(IContentSource source) -> IStringTable`,
  on the module's one factory (`08` §8.11a). It calls
  `source.ReadAll("strings/en.json")` once and never calls `Files()`.
  `IContentSource` is `08` §8.11's interface, implemented by the caller,
  so calling it is not a sim member call (§17.6). The file is read with
  `08` §8.11's strict JSON subset, its string rules (Q-033) included, by
  the scene layer's own reader (`07` "Runtime portability" rule 7: no
  package). Each of these is a `FormatException` whose message starts
  with `strings/en.json: ` (`07` "Error handling"): a `null` result from
  `ReadAll`; bytes outside the strict subset; a duplicate, unknown or
  missing key at the top level or in `strings`; `schema_version` other
  than `1`; `id` other than `"en"`; `strings` not an object; and a text
  that is not a string, is empty, or holds a character outside U+0020 to
  U+007E. A `null` `source` throws `ArgumentNullException` (`source`). An
  exception thrown by `ReadAll` passes through unchanged.
- **Resolving.** `IStringTable.Resolve(LocalisedKey key) -> string`
  returns the file's text for `key`. A `key.Value` that is `null` or is
  not one of the twelve keys throws `ArgumentException` (`key`). The
  table never changes after loading.
- **Who uses it.** The bootstrap loads the table at scene start and hands
  it to the UI backend (`16` §16.7). The backend draws every piece of
  text as `Resolve` of its key and never draws a key's `Value`. The
  controller, `UiFrame`, the host's composers and the sim never see the
  table or any text.
- **Scope.** This covers `app.ui`'s own text only. The explanation keys
  of `10` §10.2 and the delay keys `app.ui` derives (`14`) are later
  scope. They use this naming scheme when they are specified, by
  amendment.

---

## 17.5 The lane click

1. **Hit test.** Map the click to world coordinates (§17.3). The hit is the
   `FlowNodeBox` of the validated `RenderLayout` (`15` §15.4) that contains
   the point, with closed intervals. If several boxes contain it, the one
   drawn on top wins, which is the highest `NodeId`, matching `15` §15.5's
   ascending draw order. No box: the click is ignored. A click is never an
   error: one whose world point has a non-finite coordinate hits no box
   (Q-104).
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
  PacingState      Pacing                  // what the backend's control strip shows
  bool             SettingsOpen            // D10, §17.4a
  GraphicsSettings Graphics                // what the settings panel shows
}

interface ILaneCommandSink {
  void Request(NodeId node, int32 delta)   // delta is +1 or −1
}

interface IUiController {
  void        Update(IReadOnlyList<UiInput> inputs, in CameraView camera,
                     float screenWidth, float screenHeight)
  PacingState      Pacing   { get }
  GraphicsSettings Graphics { get }        // D10, §17.4a
  UiFrame          Frame()
}

readonly struct LocalisedKey { string Value }   // §17.4b, Q-125; ordinal

interface IStringTable {                   // §17.4b
  string Resolve(LocalisedKey key)
}
```

Construction (Q-009), following `08` §8.11a's factory rule:

```
UiFactory.CreateController(in RenderLayout layout, ILaneCommandSink sink,
                           in GraphicsSettings initialGraphics) -> IUiController   // D10: initial value is validated
UiFactory.CreateLaneCommandSink(ISimHost host, IFlowSystem flow) -> ILaneCommandSink   // Q-010
UiFactory.LoadStringTable(IContentSource source) -> IStringTable                     // §17.4b, Q-125
```

**Argument checks (Q-104).** A misused call is a programmer error and throws
the `07` "Error handling" type below, with `ParamName` set to the parameter
named here. Checks run in the order listed, before anything else. A throwing
call changes no state, calls no sink and calls no sim member.

- `CreateController`: `layout.FlowNodes` is `null` → `ArgumentException`
  (`layout`); `sink` is `null` → `ArgumentNullException` (`sink`);
  `initialGraphics.Preset` outside `GraphicsPreset` →
  `ArgumentOutOfRangeException` (`initialGraphics`).
- `CreateLaneCommandSink`: `host` is `null` → `ArgumentNullException`
  (`host`); then `flow` is `null` → `ArgumentNullException` (`flow`).
- `IUiController.Update`, every call, even with no inputs:
  1. `inputs` is `null` → `ArgumentNullException` (`inputs`).
  2. `screenWidth` is not finite or is not `> 0` → `ArgumentOutOfRangeException`
     (`screenWidth`). Then the same for `screenHeight`.
  3. Every input, in list order, before any input is applied: a `Kind`
     outside `UiInputKind`, a `SetSpeed` whose `Speed` is outside
     `GameSpeed`, or a `SetGraphicsPreset` whose `Preset` is outside
     `GraphicsPreset` → `ArgumentOutOfRangeException` (`inputs`). Only the
     field the `Kind` uses is checked. The check applies whether or not the
     settings panel is open, although the modal rule of §17.4a would then
     ignore the input. `Custom` is in the enum and is ignored, not an error
     (§17.4a). A click's `At` is never checked (§17.5).
- The production sink's `Request`: `delta` other than `+1` or `−1` →
  `ArgumentOutOfRangeException` (`delta`), before `TryGetLaneState` is
  called. Any `node` value is legal; one that is not a lane is ignored
  (§17.5).
- `LoadStringTable` and `IStringTable.Resolve`: as §17.4b lists.

Controller tests use a fake sink. Sink tests use a fake host and a fake flow.
`app.host` has no sink for a sim without `sim.flow`: its presentation needs
`sim.flow`, and without it the `flow` check above throws (`16` §16.5,
Q-127).

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
- **Settings (D10, §17.4a).** A fifth control, a settings icon, reports
  `ToggleSettings`. While `UiFrame.SettingsOpen`, it draws a panel from
  `UiFrame.Graphics`: one control per preset (`Low`, `Medium`, `High`),
  which reports `SetGraphicsPreset`, and one control per knob of `15`
  §15.14, which reports `SetGraphicsSettings` with that knob changed. Panel
  text is `LocalisedKey`s (`04-data-schemas.md`), and this is the first
  player-visible text. A click on the panel is never also reported as a
  world click. The layout and look of the panel are the backend's.
- **Text (Q-125, §17.4b).** The panel shows the title, the three preset
  controls, each knob's label and each knob's value, with exactly the
  keys of §17.4b's table and its "Knob values" rule. Every piece of text
  it draws is `IStringTable.Resolve` of its key, over the table the
  bootstrap hands it before its first draw (`16` §16.7). How it receives
  the table is the backend's own API. It never draws a key's `Value` and
  never holds an English literal. It may keep its own internal key
  constants, whose values are exactly §17.4b's.
- It collects this frame's inputs, in arrival order, for the bootstrap to put
  in `FrameInput` (`16` §16.6).
- It calls **no** sim member and never branches on sim state. Like
  `15` §15.10, it must stay small enough to review line by line.
- **Packaging (Q-114).** It is the local package
  `com.airportsim.ui.unity` at `src/app/ui/Unity/`, with committed `.meta`
  files, referenced from the Unity project's `Packages/manifest.json`
  (`16` §16.2). Its task adds that one line. CI only compiles it
  (`unity-build`).

---

## 17.9 Budget

- No allocation in `Update` or `Frame` after the first call. This counts
  the controller's own work only; what its sink does is the sink's (Q-105).
- **The production sink (Q-105).** `Request` allocates nothing on a call for
  a node that this sink has handled before (step 4 of §17.5 reached
  `TryGetLaneState` for it). It may allocate on the first call for a node,
  to grow its pending-target store. It may build every payload in one
  reused 8-byte buffer, because admission copies the payload (`08` §8.7).
  Allocations made inside `ISimHost.TrySubmit` are the host's and are not
  counted. A throwing call (§17.7) is not counted either.
- **The string table (Q-125).** `Resolve` allocates nothing: it returns
  the string it loaded. `LoadStringTable` runs once, at scene start, and
  may allocate.
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

**The string table (Q-125)** is a scene-layer task of its own, because
the scene layer has merged: `LocalisedKey`, `IStringTable`,
`UiFactory.LoadStringTable` and its reader in `src/app/ui/Scene/**`, its
tests in `tests/app/ui/**`, and the two content files
`data/strings/en.json` and `data/schemas/strings.schema.json` (§17.4b). It
depends only on merged work (the scene layer and `08` §8.11's
`IContentSource`). The UI backend's text and the bootstrap's call
(`16` §16.7) depend on it.

**Fixture.** None of its own. Tests reuse `tests/fixtures/render/phase1-layout.json`
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
- `test_ui_settings_open_pauses_and_close_restores_player_pause` (Q-034,
  §17.4, §17.4a): opening pauses whether or not the player had paused, and
  closing restores the player's own state, paused or not
- `test_ui_pause_and_speed_ignored_while_settings_open` (Q-034)
- `test_ui_set_graphics_preset_applies_preset_values_and_ignores_custom`
- `test_ui_set_graphics_settings_marks_custom_and_validates`
- `test_ui_world_clicks_ignored_while_settings_open`
- `test_ui_graphics_preference_round_trips_and_rejects_malformed` — covers
  the §17.4a grammar (Q-103), including the `out` value after a false
  decode
- `test_ui_graphics_preference_decode_validates` (Q-103) — out-of-range and
  preset-mismatched values decode true, to `Validate` of the values as
  written
- `test_ui_misused_calls_throw_named_exception_and_change_nothing` (Q-104,
  §17.7)
- `test_ui_update_and_frame_allocate_nothing_after_first_call` (§17.9)
- `test_ui_lane_request_allocates_nothing_after_warm_up` (Q-105, §17.9)
- `test_ui_graphics_changes_do_not_change_outcome` — integration, as
  `test_ui_pause_and_speed_do_not_change_outcome`, with scripted graphics
  inputs.
- `test_ui_controls_and_hits_identical_at_every_graphics_setting` — the same
  inputs, including clicks, give the same lane requests, the same pacing
  state and the same `UiFrame.Pacing` at each preset and at custom extremes
  (the §17.4a invariant).

Done-condition tests for the string table (Q-125, §17.4b):

- `test_ui_string_table_loads_repository_en_file` — loads the
  repository's `data/strings/en.json` through a source over `data/`, and
  every one of the twelve keys resolves to its text in §17.4b's table
- `test_ui_string_table_reads_only_strings_en_json` — a fake source
  records exactly one `ReadAll("strings/en.json")` and no `Files()`
- `test_ui_string_table_rejects_malformed_file` — each `FormatException`
  case of §17.4b, each message starting `strings/en.json: `, and the
  `null` source
- `test_ui_string_table_resolve_rejects_unknown_key` — a `null` `Value`,
  a key not in the table, and a key differing only in case each throw
  `ArgumentException` (`key`)
- `test_ui_string_table_resolve_allocates_nothing` (§17.9)

**A merged test changes (Q-125).** `test_ui_public_surface_matches_spec`
(`tests/app/ui/UiTests.cs`) pins the assembly's exact public types and
`UiFactory`'s exact public methods. Its pinned lists gain `LocalisedKey`
and `IStringTable` among the types and `LoadStringTable` among the
methods, and it asserts that `IStringTable`'s only member is `Resolve`.
The string-table task's Test Author makes that change as part of the
task's tests. It is a spec-driven test change, not a worker edit. No
other merged test pins the `app.ui` surface or the set of files in
`data/`: the tests that read `data/` load it through `08` §8.11's loader,
which ignores `strings/`.

---

## 17.11 Open

- None. Whether the settings panel pauses was decided by the owner on
  2026-09-27 (Q-034): it pauses (§17.4a). More languages, a language
  choice and text for the four pacing icons are later scope (§17.4b).
