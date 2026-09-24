# T-031 — `app.host`: headless composition root, frame loop, checkpoint dump run

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.host` (headless side only) |
| Assigned role | worker |
| Depends on | T-008, T-012, T-020, T-021, T-022, T-023, T-024, T-026, T-027, T-029, T-030 |
| Spec source | `spec/00-overview.md`; `spec/16-interfaces-host.md` §16.1–§16.8 (new module, D7) |
| Blocked by | — |

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

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.5, §8.9, §8.11, §8.11a, the Construction
section of every registered module (`09` §9.11, `11` §11.9a, `12` §12.12a,
`13` §13.10a, `14` §14.13a, `18` §18.4), `spec/15-interfaces-render.md`
§15.3, §15.9, §15.10, `spec/17-interfaces-ui.md` §17.4, §17.7,
`spec/16-interfaces-host.md`

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
  Presentation Compose(in ComposedSim sim, IScenarioBundle bundle)
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
  `systems`), `schedule.csv`, `airside.fixture`, `airside_rules.json`
  (required whenever `sim.airside` is listed), `turnaround.fixture`,
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
- **Presentation assembly** (§16.5): build `RenderSources { Host, Airside,
  Flow }` from the `ComposedSim`; load `render_layout.*` via
  `IRenderLayoutLoader`, passing `Airside.Layout()` when registered — a
  layout failure is a hard load failure; construct the scene builder,
  promotion controller and pacer via `RenderFactory`, and the UI controller
  plus lane sink via `UiFactory`; build the frame loop over those and
  `sim.Host`.
- **The frame loop** (§16.6): `RunFrame`, in order — 1) `Ui.Update(...)`;
  2) `Promotion.Update(camera)`; 3) `n = Pacer.Advance(elapsed,
  Ui.Pacing.Paused, Ui.Pacing.Speed)`, `if n>0: Host.Step(n)`; 4) `render =
  Scene.Build(camera)`; 5) `ui = Ui.Frame()`; return both. UI first (a
  pause pressed this frame stops this frame's `Step`); promotion before
  `Step`; build after `Step`. No allocation per `RunFrame` after the first.
  This is the **only** place a playable build calls `Step`.
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
```

Written by the Test Author. Expect at least:

- `test_host_assembly_has_no_engine_reference` — static
- `test_frame_loop_runs_ui_then_promotion_then_step_then_build`
- `test_frame_loop_pause_pressed_this_frame_steps_nothing`
- `test_bundle_rejects_listed_system_without_file`
- `test_bundle_unlisted_system_is_not_registered`
- `test_command_line_parses_checkpoint_run_and_rejects_others`
- `test_checkpoint_dump_format_is_byte_exact`
- `test_headless_run_result_independent_of_step_batch_size`
- `test_compose_constructs_in_dependency_order_and_registers_in_registry_order`
- `test_compose_rejects_airside_without_schedule`
- `test_host_composition_matches_harness_checkpoints` — runs T-030's harness
  `checkpoints` subcommand and this task's `IHeadlessRun` (in-process,
  `net8.0`) on the same bundle for one sim-day, on two bundles: the Phase 1
  playtest bundle and a Phase 0 bundle with only `sim.world`,
  `sim.schedule` and `sim.flow` (T-009's composition). Dumps must be
  byte-identical.

**Do not edit them.** If a test contradicts `spec/16-interfaces-host.md`,
file an open question and stop.

## Performance budget

`app.host` adds nothing to the sim's 6 ms/tick. `RunFrame` adds only the
calls of §16.6, allocating nothing after the first call; its callees carry
their own budgets (`15` §15.11). Composition and bundle loading happen once
at scene start, off the frame path — no time budget there.

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
the presentation composer calls, plus T-027 (content loader) and T-030
(the harness side of the equivalence test). **Also T-023, named explicitly
now (systematic recheck):** `IFlowSystem.TryGetLaneState`/
`TryGetOutstanding` are called through the same `RenderSources`/lane-sink
wiring T-020/T-029 already require, so this was already transitively
required through those two; it is now listed directly rather than relying
on that transitivity. Do not release this task before all of them merge.
