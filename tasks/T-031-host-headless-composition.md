# T-031 — `app.host`: headless composition root, frame loop, checkpoint dump run

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.host` (headless side only) |
| Assigned role | worker |
| Depends on | T-008 (merged #54), T-012 (merged #39), T-020 (not merged), T-021 (not merged), T-022 (not merged), T-023 (merged #71), T-024 (not merged), T-026 (merged #29), T-027 (merged #37), T-029 (not merged), T-030 (not merged), T-048 (not merged; the Phase 1 harness stage) |
| Spec source | `spec/00-overview.md`; `spec/16-interfaces-host.md` §16.1–§16.8, §16.10, §16.11 (new module, D7; graphics preference and frame-loop steps 2/4/5, D10/Q-034) |
| Blocked by | — |

**Amendment (D10/Q-034, this cycle):** the owner's graphics-quality
decision adds an `IPreferenceStore` parameter to `IPresentationComposer.
Compose`, threads `Ui.Graphics` through frame-loop steps 2 and 4, and adds a
step 5 that writes the graphics preference on change, defaulting to
`Medium` when none is stored (`16` §16.6). This is additive inside this
task's existing scope — it does not change this task's dependency list.

## Writable paths

```
src/app/host/**
AirportSim.sln
```

**Correction (Q-021):** `tests/**` is the Test Author's territory
exclusively; the path guard already blocks a worker grant there. The
earlier grant of `tests/app/host/**` is dropped.

**First task of a new module (`07` L8, Q-013):** this task creates
`src/app/host/AirportSim.App.Host.csproj` and
`tests/app/host/AirportSim.App.Host.Tests.csproj` (byte for byte per `07`
L2/L3) and adds both to `AirportSim.sln`. Never release this task
concurrently with any other "first task of a new module" (T-007, T-008,
T-012, T-020, T-021, T-022, T-024, T-029) — concurrent `.sln` edits
conflict (`07` L8).

The Unity project shell (`unity/AirportSim/**`) is a separate task (T-034),
released after this one, the render/UI Unity backends (T-032, T-033), and
after content exists for a real player build.

**Integration test project (Q-077, `07` L1/L3/L8; merged #84).** `test_host_composition_matches_harness_checkpoints` cannot live in
`tests/app/host` (`07` L3: one reference), and the harness may not reference
`app.host` (`19` §19.8). It lives in `tests/integration/`, the one test
project that references both `src/app/host` and `tools/SimHarness` (exactly
two `ProjectReference`s, `AirportSim.Integration.Tests`). Per L8 this task's
**Test Author** writes `tests/integration/AirportSim.Integration.Tests.csproj`
and its parallelisation file; this task's `.sln` edit adds that project
along with its own two projects, in the same change. The Test Author's grant
therefore gains `tests/integration/**` (besides `tests/app/host/**`). Any
other test in that project needs a spec amendment.

**Confirmed (Q-022):** `ComposedSim.World : IWorldSystem?` below now matches
`16-interfaces-host.md` §16.4 exactly — the spec previously omitted it
(the composer already had to construct `sim.world`, but nothing threaded
that through to `16`), and this task file already carried the field. No
change to this task's own interface was needed; the spec caught up to it.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.5, §8.9, §8.11, §8.11a, the Construction
section of every registered module (`09` §9.11, `11` §11.9a, `12` §12.12a,
`13` §13.10a, `14` §14.13a, `18` §18.4), `spec/15-interfaces-render.md`
§15.3, §15.9, §15.10, §15.14, `spec/17-interfaces-ui.md` §17.4, §17.4a,
§17.7, `spec/16-interfaces-host.md` (all sections, including §16.6, §16.10)

## Interface to implement

```
interface IScenarioBundle {
  bool   Has(string fileName)
  byte[] ReadAll(string fileName)
}

readonly struct ComposedSim {
  ISimHost           Host
  IWorldSystem?      World
  IScheduleSystem?   Schedule
  IAirsideSystem?    Airside
  IFlowSystem?       Flow
  ITurnaroundSystem? Turnaround
  IDelaySystem?      Delay
}

interface ISimComposer {
  ComposedSim Compose(IScenarioBundle bundle, ICheckpointSink checkpoints)
}

readonly struct Presentation {
  ISceneBuilder        Scene
  IPromotionController Promotion
  ITickPacer           Pacer
  IUiController        Ui
  IFrameLoop           Frame
}

interface IPresentationComposer {
  Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences)   // D10
}

interface IPreferenceStore {                      // D10; implemented by the bootstrap over the engine's player preferences
  bool TryRead(string key, out string value)
  void Write(string key, string value)
}

readonly struct FrameInput {
  CameraView             camera
  float                  screenWidth
  float                  screenHeight
  IReadOnlyList<UiInput> ui
  int64                  elapsedRealMicroseconds
}

readonly struct FrameOutput { RenderFrame Render; UiFrame Ui }

interface IFrameLoop { FrameOutput RunFrame(in FrameInput input) }

readonly struct CheckpointRunRequest { uint32 Days; string OutputPath }

interface IHostCommandLine {
  bool TryParse(IReadOnlyList<string> args, out CheckpointRunRequest request)
      // recognises exactly: -airportsim-checkpoints <days> <outputPath>
}

interface IHeadlessRun {
  int Run(IScenarioBundle bundle, in CheckpointRunRequest request)   // 0 on success
}
```

Construction (`16` §16.4, §16.5, §16.8, own factories under the `08`
§8.11a rule):

```
HostFactory.CreateSimComposer(IReadOnlyList<IContentDefinition> content) -> ISimComposer
HostFactory.CreatePresentationComposer() -> IPresentationComposer
HostFactory.CreateCommandLine() -> IHostCommandLine
HostFactory.CreateHeadlessRun(ISimComposer composer) -> IHeadlessRun
```

Binding, copied from `spec/16-interfaces-host.md`, not paraphrased:

- **The scenario bundle** (§16.3): read by exact file name only, never by
  directory listing. Files: `bundle.json` (`schema_version`, `seed`,
  `systems`; strict form, `16` §16.3, Q-069: duplicate, unknown or missing key is a load failure), `schedule.csv`, `airside.fixture`, `airside_rules.json`
  (required whenever `sim.airside` is listed; the host parses it into
  `AirsideRules`, "both keys, `boarding_hold_max_minutes` and
  `doors_open_delay_minutes` (Q-047)", and the file is human-authored; the owner landed the schema and the file in `3a00a78`, closing T-046), `turnaround.fixture`,
  `flow.fixture`, `world.fixture` (T-012's `WalkGraph`),
  `render_layout.fixture`. A listed system with a missing file is a hard
  load failure naming the file. A system not listed is skipped, never
  reordered. Content comes from a copy of `data/` through
  `HostFactory.LoadContent`, feeding `ContentIndexFactory.Create` via
  T-027's `IContentLoader`.
- **Composition** (§16.4): parse `bundle.json`, build the
  `ISimHostBuilder` via `SimHostFactory.CreateBuilder`; load each listed
  module's file with that module's loader; construct systems **in
  dependency order** (world; flow(world); schedule(flow); airside(schedule,
  flow, turnaroundRegistered); turnaround(schedule); delay), each with
  `builder.Services`, its data, and its downward interfaces or `null`;
  `Register` them **in registry order** (`08` §8.5: world 1, schedule 2,
  airside 3, flow 4, turnaround 5, delay 7); `Build`. Composition is a
  **pure function of the bundle's bytes and the content data** — no
  machine, runtime, OS, file-system-order or clock dependence. Once per
  session, no recomposition, no hot swap.
- **Presentation assembly** (§16.5, amended by D10/Q-034): build
  `RenderSources { Host, Airside, Flow }` from the `ComposedSim`; load
  `render_layout.*` via `IRenderLayoutLoader`, passing `Airside.Layout()`
  when registered — a layout failure is a hard load failure; construct the
  scene builder, promotion controller and pacer via `RenderFactory`.
  **Graphics preference** (§16.6, D10): read `airportsim.graphics` from the
  given `preferences`; if `TryDecodeGraphicsPreference` succeeds, that value
  is the UI controller's `initialGraphics`, else the default of `15` §15.14
  (`Medium`) is used. The value it started with counts as "last written"
  (see step 5, below). Construct the UI controller plus lane sink via
  `UiFactory.CreateController(layout, sink, initialGraphics)`; build the
  frame loop over those and `sim.Host`. `IHeadlessRun` (§16.8) reads no
  preference — the checkpoint run has no presentation at all.
- **The frame loop** (§16.6, amended by D10/Q-034): `RunFrame`, in order —
  1) `Ui.Update(...)` (may submit commands and change pacing and graphics
  settings); 2) `Promotion.Update(camera, Ui.Graphics)`; 3) `n =
  Pacer.Advance(elapsed, Ui.Pacing.Paused, Ui.Pacing.Speed)`, `if n>0:
  Host.Step(n)`; 4) `render = Scene.Build(camera, Ui.Graphics)`; 5) if
  `Ui.Graphics` differs from the value last written, write
  `UiFactory.EncodeGraphicsPreference(Ui.Graphics)` to the
  `IPreferenceStore` under the key `airportsim.graphics`; 6) `ui =
  Ui.Frame()`; return both. UI first (a pause pressed this frame stops this
  frame's `Step`, and so does the settings panel opening this frame —
  `Ui.Pacing.Paused` already includes it, per `17` §17.4a, with no extra
  rule needed here); promotion before `Step`; build after `Step`. No
  allocation per `RunFrame` after the first. This is the **only** place a
  playable build calls `Step`. The graphics preference is per machine and
  per player: it is never part of the scenario bundle or a session's input,
  and a session's checkpoints are identical whatever it holds.
- **The headless checkpoint run and dump format** (§16.8): `Run` composes
  the bundle with a sink recording every checkpoint, steps to `Days ×
  TICKS_PER_SIM_DAY`, no presentation, writes the dump. Format is fixed,
  identical to T-030's harness-side format (§16.8, reproduced there
  verbatim) — this task writes its **own** implementation of the same
  format, never reuses T-030's code directly, per §16.4's "must not
  construct any system another way".
- **The Unity bootstrap contract** (§16.7) is specified for the Unity shell
  task (T-034) to implement against; this task builds the headless side it
  calls, not the bootstrap script itself.

## Events

Emitted: none. Consumed: none — composition wires the event bus for the
composed systems; `app.host` itself does not subscribe to anything.

## Tests to pass

```
tests/app/host/**
tests/integration/**
```

Written by the Test Author. Expect at least:

- `test_host_assembly_has_no_engine_reference` — static
- `test_frame_loop_runs_ui_then_promotion_then_step_then_build`
- `test_frame_loop_pause_pressed_this_frame_steps_nothing`
- `test_bundle_rejects_listed_system_without_file`
- `test_bundle_unlisted_system_is_not_registered`
- `test_command_line_parses_checkpoint_run_and_rejects_others` -- must also
  cover `TryParse` ignoring the engine's own arguments (`-batchmode`,
  `-nographics`, `-logFile -`), wherever they sit among the three tokens
  (`16` §16.8, Q-114)
- `test_checkpoint_dump_format_is_byte_exact`
- `test_headless_run_result_independent_of_step_batch_size` -- no seam:
  `IHeadlessRun` submits no command (Q-071) and calls
  `Step(TICKS_PER_SIM_DAY)` exactly `Days` times; the test composes the same
  bundle with `ISimComposer.Compose`, steps it in other batches, renders that
  run's dump with its own code and compares it with `Run`'s file (`16` §16.8)
- `test_compose_constructs_in_dependency_order_and_registers_in_registry_order`
- `test_compose_rejects_airside_without_schedule`
- `test_host_composition_matches_harness_checkpoints` -- **in
  `tests/integration/`** (Q-077, merged #84). Runs the harness
  `checkpoints` subcommand (`HarnessCli.Run`, `19` §19.1, in process,
  `net8.0`) and this task's `IHeadlessRun` on the same bundle and content for
  one sim-day, on two Phase 1 test bundles (`16` §16.8; the playtest bundle
  does not exist yet and is compared by `16` §16.9 by hand):
  `tests/fixtures/harness/checkpoints-phase0/` with content
  `tests/fixtures/harness/phase0-content/` (`sim.world`, `sim.schedule`,
  `sim.flow`, no boarding stand-in), and
  `tests/fixtures/harness/checkpoints-phase1/` (T-048) with content `data/`
  (all six systems). Both bundles use the strict `bundle.json` form (`16`
  §16.3). Dumps must be byte-identical.
- `test_frame_loop_passes_ui_graphics_to_promotion_and_scene` (D10)
- `test_frame_loop_writes_graphics_preference_only_on_change`
- `test_presentation_uses_stored_graphics_preference_or_default` — the
  default is `Medium` (Q-034)
- `test_frame_loop_settings_opened_this_frame_steps_nothing` (Q-034)

**Do not edit them.** If a test contradicts `spec/16-interfaces-host.md`,
file an open question and stop.

## Performance budget

`app.host` adds nothing to the sim's 6 ms/tick. `RunFrame` adds only the
calls of §16.6, allocating nothing after the first call; its callees carry
their own budgets (`15` §15.11). Composition and bundle loading happen once
at scene start, off the frame path — no time budget there.

**Memory on minimum spec** (§16.10, HUMAN DECISION — owner, 2026-09-27,
Q-034): the player process — resident memory plus any GPU memory it
allocates, which on integrated graphics is shared system RAM counted
against the 8 GB minimum — stays **≤ 2 GB** at max tier with the `Low` and
`Medium` presets. CI cannot measure this; it is checked by the same manual
measurement on a minimum-spec machine as `15` §15.14's frame-rate check
(T-025 or a later playtest), not by a test this task authors.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

Tests supply content definitions directly to `ISimComposer` (bypassing
T-027's loader) until a real `data/`-backed player build is needed — that
is T-034's problem, not this task's. `sim.save` does not exist; a session
starts from a bundle and nothing else, and this task does not build save
or load.

This task depends on every Phase 1 module's factory existing
(`08` §8.11a, Q-009) — T-008, T-012, T-021, T-022, T-024, T-026 — plus the
render and UI scene layers (T-020, T-029) whose `RenderFactory`/`UiFactory`
the presentation composer calls, plus T-027 (content loader) and T-030 and T-048
(the harness side of the equivalence test, both stages). **Also T-023, named explicitly
now (systematic recheck):** `IFlowSystem.TryGetLaneState`/
`TryGetOutstanding` are called through the same `RenderSources`/lane-sink
wiring T-020/T-029 already require, so this was already transitively
required through those two; it is now listed directly rather than relying
on that transitivity. Do not release this task before all of them merge.

**Graphics preference (D10/Q-034) does not change this task's dependency
list.** `IPreferenceStore` is a plain interface this task declares and
consumes; the bootstrap's real implementation over the engine's player
preferences is T-034's, not this task's. This task's own tests use a fake
store. The preference is per machine and per player — never part of the
scenario bundle, a checkpoint dump or a save, and never read by
`IHeadlessRun` — so `test_host_composition_matches_harness_checkpoints`
needs no change.
