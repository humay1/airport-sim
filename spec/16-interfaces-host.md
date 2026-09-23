# 16 — Public interfaces: `app.host`

Implements the `app.host` module created by owner decision D7: HUMAN
DECISION — owner (delegated), 2026-09-23, reversible. It answers
`15-interfaces-render.md` §15.13(b) and (c). Composition uses the published
construction surface of `08` §8.11a and each module's "Construction" section
(Q-009). Building the content index from `data/` remains open as
`open-questions.md` Q-011. Notation is as in `08-interfaces-core.md`. Where
this file appears to contradict `01-architecture.md` or `02-determinism.md`,
those win and it is a spec bug.

Reading order for an `app.host` worker: `01`, `02`, `07`, `08` §8.5, §8.9 and §8.11a,
the Construction section of each module (`09` §9.11, `11` §11.9a, `12` §12.12a,
`13` §13.10a, `14` §14.13a),
`15` §15.3, §15.8, §15.9, `17` §17.4, §17.7, this file.

---

## 16.1 What the module is, and what it is not

`app.host` is the top of the dependency graph. It turns fixtures into a
running sim and hands that sim to presentation. It is needed for T-025 (a
playable build), not for T-020.

`app.host` owns:

- **the Unity project** `unity/AirportSim/`: scenes, project settings, the
  package manifest and the player build (§16.2);
- **the headless composition root** `src/app/host/`: it builds `ISimHost` and
  the registered systems from a scenario bundle (§16.3, §16.4), then builds the
  presentation scene-layer objects (§16.5);
- **the frame loop**, the only caller of `ISimHost.Step` in a playable build
  (§16.6). This moved here from `15-interfaces-render.md` §15.8;
- **the Unity bootstrap**, one thin engine script that calls the headless host
  and nothing else (§16.7);
- **the headless checkpoint run** and the checkpoint dump format (§16.8), used
  to prove the host composes the same sim as `tools.simharness` and by the
  proposed cross-runtime gate (§16.9).

`app.host` explicitly does **not** own, and must not do:

- any sim logic, any rendering decision (`app.render`) or any UI decision
  (`app.ui`);
- the engine backends' code. `src/app/render/Unity/**` and
  `src/app/ui/Unity/**` stay owned by their modules, and the project includes
  them by reference (§16.2);
- content, `data/`, `tests/` or `ci/`;
- save and load. That is `sim.save`, which is not specified. A session starts
  from a bundle and nothing else.

---

## 16.2 Layers, directories and targets

| | Headless host | Unity project |
|---|---|---|
| Directory | `src/app/host/` | `unity/AirportSim/` |
| Engine references | **none**, asserted by test | Unity 6 |
| Target | `netstandard2.1`, `LangVersion 9` (`01-architecture.md`, D1) | compiled by Unity |
| Built by | `AirportSim.sln` | the Unity editor / player build |
| Tested in CI | yes, §16.11 | no, except through §16.9 if that gate is adopted |

Binding Unity project settings:

- **Editor version.** One Unity 6 LTS version, pinned in
  `ProjectSettings/ProjectVersion.txt`. Changing it changes the shipped
  runtime, so it is recorded in `CHANGELOG.md` like a spec change.
- **Scripting backend: Mono**, for every player target (D1: "the shipped
  runtime (Mono)"). IL2CPP is a different runtime with its own class
  libraries. Using it needs its own decision and its own run of §16.9.
- **API compatibility level: .NET Standard 2.1.**
- **Plugins are the tested binaries.** The sim, the `app.render` and `app.ui`
  scene layers and this host are consumed as precompiled plugins. They come
  from the Release `dotnet build` of `AirportSim.sln`, and the host's build
  step copies them into `unity/AirportSim/Assets/Plugins/AirportSim/`. They
  are build outputs and are never committed. Unity never recompiles them from
  source, so the player runs exactly the assemblies CI tested.
- **Backends by reference.** The engine backends are included into the
  project by reference, not by copy. The mechanism, for example local
  packages, is the host task's choice.
- **One scene**, `Assets/Scenes/Main.unity`, holding the bootstrap and the
  backends' components. There is no other scene at Phase 1.
- **Players:** Windows and Linux desktop, with macOS best-effort
  (`01-architecture.md`).

Rules binding on the headless host, the same as `15` §15.3: no wall-clock
read (elapsed time is passed in), no `System.Random`, no static mutable state,
and `07-conventions.md` "Runtime portability".

---

## 16.3 The scenario bundle

A bundle is the whole input of a session. It is a set of named files, read
**by exact name only**, never by directory listing: listing order differs
between file systems.

```
interface IScenarioBundle {
  bool   Has(string fileName)
  byte[] ReadAll(string fileName)          // load time only; allocation is fine here
}
```

| File | Format | Consumed by |
|---|---|---|
| `bundle.json` | `{ "schema_version": 1, "seed": "<uint64 decimal>", "systems": [ "<module name>", ... ] }` | the host |
| `schedule.csv` | `11-interfaces-schedule.md` §11.4 | `IScheduleLoader.Load` |
| `airside.fixture` | `12-interfaces-airside.md` §12.4, in the format of T-021's fixture | `IAirsideLayoutLoader.Parse` |
| `airside_rules.json` | `04-data-schemas.md` (`AirsideRules`, `12` §12.4); required whenever `sim.airside` is listed | the host parses it into `AirsideRules` |
| `turnaround.fixture` | `13-interfaces-turnaround.md` §13.4, in the format of T-022's fixture | `ITurnaroundSetupLoader.Load` |
| `flow.fixture` | `sim.flow`'s opaque `FlowGraph`, in the format of T-007/T-023's fixtures | `IFlowGraphLoader.Load` |
| `render_layout.fixture` | `15-interfaces-render.md` §15.4 | `IRenderLayoutLoader.Load` |

File names are exact. The `.fixture` files keep whatever byte format their
module's worker chose; the name says nothing about the format.

- `systems` lists module names (`ISimSystem.Name`). A listed system whose
  file is missing is a hard load failure naming the file
  (`07-conventions.md`). A system not listed is not registered: it is skipped,
  never reordered (`08` §8.5). `sim.delay` needs no file of its own.
- The seed is parsed with the invariant culture (`07-conventions.md`,
  "Runtime portability" rule 4).
- The content index is part of the input as well. It is created with
  `ContentIndexFactory.Create` (`08` §8.11a), but parsing `data/` into
  definitions is not published; that is Q-011. Until Q-011 is answered, the
  host takes the definitions as a constructor argument of its composer, and
  tests supply them.
- **The Phase 1 playtest bundle** is `unity/AirportSim/Scenario/bundle.json`,
  committed and owned by `app.host`, plus the Phase 1 fixtures named in
  `11` §11.10, `12` §12.13, `13` §13.11 and `15` §15.12, the `sim.flow`
  fixture T-023 runs, and the human-authored `data/balance/airside_rules.json`
  (D6). The build step copies them into
  `Assets/StreamingAssets/Scenario/`. The copies are build output: the
  fixtures stay test fixtures, beside their tests (`07-conventions.md`), and
  are not moved into `data/`.

---

## 16.4 Composition

```
readonly struct ComposedSim {
  ISimHost           Host
  IScheduleSystem?   Schedule                 // null: not registered
  IAirsideSystem?    Airside
  IFlowSystem?       Flow
  ITurnaroundSystem? Turnaround
  IDelaySystem?      Delay
}

interface ISimComposer {
  ComposedSim Compose(IScenarioBundle bundle, ICheckpointSink checkpoints)
}
```

`ISimComposer` is created with the content definitions:
`HostFactory.CreateSimComposer(IReadOnlyList<IContentDefinition> content)`
(see §16.3 and Q-011).

`Compose` does exactly this, in this order:

1. Parse `bundle.json`, then
   `SimHostFactory.CreateBuilder({ seed, ContentIndexFactory.Create(content), checkpoints, log })`.
   The log sink is the host's (`08` §8.10).
2. Load each listed module's file with that module's loader (§16.3).
3. Construct the listed systems **in dependency order**, each with
   `builder.Services`, its data, and its downward interfaces or `null`:
   flow; schedule(flow); airside(schedule, flow, `turnaroundRegistered`);
   turnaround(schedule); delay.
4. `Register` them **in registry order** (`08` §8.5), then `Build`.

Rules:

- Systems register in the registry order of `08` §8.5. The Phase 1 set is
  `sim.schedule`, `sim.airside`, `sim.flow`, `sim.turnaround`, `sim.delay`.
  `sim.world` is unspecified and never registered at Phase 1.
- **Composition is a pure function of the bundle's bytes and the content
  data.** The same bundle gives the same checkpoint sequence wherever it is
  composed: in the harness, in a test, or in a Unity player on either
  runtime. No composition choice may depend on the machine, the runtime,
  the OS, file-system order or the clock.
- Composition happens once per session. There is no recomposition and no hot
  swap while a sim is running.
- Every checkpoint (`08` §8.9) goes to the given sink.
- A listed system whose required downward interface is not listed (airside
  or turnaround without schedule) is a load failure.
- `tools.simharness`'s `checkpoints` subcommand (§16.8) uses the **same
  factories**. It may wire them in its own code, which is what the
  equivalence test compares, but it must not construct any system another
  way.

---

## 16.5 Presentation assembly

```
readonly struct Presentation {
  ISceneBuilder        Scene
  IPromotionController Promotion
  ITickPacer           Pacer
  IUiController        Ui                  // 17 §17.7
  IFrameLoop           Frame
}

interface IPresentationComposer {
  Presentation Compose(in ComposedSim sim, IScenarioBundle bundle)
}
```

- Builds `RenderSources { Host, Airside, Flow }` (`15` §15.9) from the
  `ComposedSim`.
- Loads `render_layout.*` through `IRenderLayoutLoader`, passing
  `Airside.Layout()` when `sim.airside` is registered. A layout failure is a
  hard load failure.
- Constructs the scene builder, the promotion controller and the pacer with
  `RenderFactory` (`15` §15.9), and the UI controller and its lane sink with
  `UiFactory` (`17` §17.7).
- It builds the frame loop (§16.6) over those parts and `sim.Host`, and
  returns it as `Presentation.Frame`.
- `HostFactory.CreatePresentationComposer()`, `HostFactory.CreateCommandLine()`
  and `HostFactory.CreateHeadlessRun(ISimComposer composer)` are `app.host`'s
  own factories, under the same rule as `08` §8.11a.
- Presentation receives the sim's read-only interfaces and `ISimHost`. It
  never receives a system's internals.

---

## 16.6 The frame loop

The binding frame order. It was `15-interfaces-render.md` §15.8's "Frame
order" and now lives here, because a frame spans more than one presentation
module and `app.render` may not reference `app.ui`. This is the **only**
place a playable build calls `ISimHost.Step`.

```
readonly struct FrameInput {
  CameraView             camera                   // from the render backend
  float                  screenWidth              // pixels, > 0
  float                  screenHeight             // pixels, > 0
  IReadOnlyList<UiInput> ui                       // from the UI backend, arrival order (17 §17.3)
  int64                  elapsedRealMicroseconds  // engine frame delta, converted by the bootstrap
}

readonly struct FrameOutput {
  RenderFrame Render                       // valid until the next RunFrame
  UiFrame     Ui
}

interface IFrameLoop { FrameOutput RunFrame(in FrameInput input) }
```

Each `RunFrame`, in this order:

1. `Ui.Update(input.ui, input.camera, input.screenWidth, input.screenHeight)`.
   This may submit commands and change the pacing state (`17` §17.4, §17.5).
2. `Promotion.Update(input.camera)`.
3. `n = Pacer.Advance(input.elapsedRealMicroseconds, Ui.Pacing.Paused,
   Ui.Pacing.Speed)`; if `n > 0`, `Host.Step(n)`.
4. `render = Scene.Build(input.camera)`.
5. `ui = Ui.Frame()`. Return both.

UI goes first, so a pause pressed this frame stops this frame's `Step`, and a
command submitted this frame is already queued before its tick runs.
Promotion goes before `Step`, so a node that comes into view promotes before
the tick that shows it. Building goes after `Step`, so the frame shows the
state just produced. Nothing touches the sim while `Step` is running
(`15` §15.6). No allocation per `RunFrame` after the first.

---

## 16.7 The Unity bootstrap contract

The bootstrap is engine code and cannot be tested in CI, so it holds no
decisions. It must stay small enough for the Reviewer to check line by line
against this list:

- **At scene start:** build an `IScenarioBundle` over
  `StreamingAssets/Scenario/`, call `ISimComposer.Compose` and then
  `IPresentationComposer.Compose`, and keep the `IFrameLoop`. Hand the backends
  what they draw.
- **Each engine frame:** get this frame's `CameraView` from the render backend
  and this frame's `UiInput`s from the UI backend, read the screen size,
  convert the engine's frame delta to integer microseconds (the float
  conversion is fine here: this is presentation), call `RunFrame`, and pass
  `Render` and `Ui` to their backends to draw.
- **Batch mode:** pass the process arguments to
  `IHostCommandLine.TryParse` (§16.8). If it returns a checkpoint run, call
  `IHeadlessRun.Run`, then quit with its exit code. No frame loop runs and
  nothing is drawn.
- It calls no sim member, never branches on sim state, and never reads a
  bundle file itself.

---

## 16.8 The headless checkpoint run and the dump format

```
readonly struct CheckpointRunRequest { uint32 Days; string OutputPath }

interface IHostCommandLine {
  bool TryParse(IReadOnlyList<string> args, out CheckpointRunRequest request)
}   // recognises exactly: -airportsim-checkpoints <days> <outputPath>

interface IHeadlessRun {
  int Run(IScenarioBundle bundle, in CheckpointRunRequest request)   // 0 on success
}
```

`Run` composes the bundle (§16.4) with a sink that records every checkpoint.
It then calls `Step` until `Days × TICKS_PER_SIM_DAY` ticks have run, with no
presentation at all (no promotion, no pacer), and writes the dump. The step
batch size must not change the result, because the tick is fixed
(`02-determinism.md` rule 1). A test asserts that anyway.

**Checkpoint dump, version 1.** UTF-8 without a BOM, LF line endings, a
final newline, single spaces, and invariant formatting throughout:

```
airport-sim-checkpoints 1
seed <MasterSeed, decimal>
systems <Name> <Name> ...                  // registered systems, registry order
<tick> <world hash> <system hash> ...      // one line per checkpoint, ascending tick
```

Ticks are decimal. Hashes are exactly 16 lowercase hexadecimal digits. System
hashes follow the order of the `systems` line. Two runs agree if and only if
their dumps are **byte-identical**. When they are not, the first differing
line names the checkpoint tick and the column names the system
(`02-determinism.md`, "State hashing").

**The harness side.** `tools.simharness` gains one subcommand,
`checkpoints --bundle <dir> --days <n> --out <path>`. It composes the bundle
its own way and writes the same format. The existing subcommands that `ci/`
invokes are unchanged.

**Equivalence with the harness (D7).**
`test_host_composition_matches_harness_checkpoints` runs the harness
`checkpoints` subcommand and `IHeadlessRun` (in-process, on `net8.0`) on the
same bundle for one sim-day. The dumps must be byte-identical. It runs on two
bundles: the Phase 1 playtest bundle (§16.3), and a Phase 0 bundle with only
`sim.schedule` and `sim.flow`, the composition T-009 runs.

---

## 16.9 The cross-runtime determinism gate — PROPOSED (D1)

**Why.** The contract of `02-determinism.md` is "bit-identical ... on every
machine". D1 ships the sim on Unity's Mono and tests it on CoreCLR, and every
existing gate runs CoreCLR only. A violation of `07-conventions.md` "Runtime
portability" can pass every one of those gates, for example a tie in
`List<T>.Sort` that CoreCLR happens to resolve the same way on every run.

**`determinism_cross_runtime`:**

1. **CoreCLR:** `tools.simharness checkpoints --bundle B --days 10 --out a`.
2. **Mono:** the Unity player build of `unity/AirportSim/` (Mono backend,
   §16.2), with `B` as its scenario, started as
   `-batchmode -nographics -airportsim-checkpoints 10 b`.
3. **Pass:** `a` and `b` are byte-identical (§16.8).

`B` is the Phase 1 playtest bundle, with every Phase 1 system registered.
Ten days matches `determinism_cross_process`. The Mono side must be the
**real player**. Unity ships its own fork of Mono and its own class
libraries, so a pass on a standalone upstream Mono proves little about the
shipped build.

**Status: proposed, not a gate.** The player build needs the Unity editor and
a licence on the build agent, which is what `01-architecture.md` keeps out of
the per-merge gates. Making this a gate means adding a row to
`02-determinism.md`'s gate table (locked) and a step to `ci/` (human-only).
Both are **escalated to the owner**. The proposed cadence is nightly, beside
`soak_500_days`, and additionally on any change to the Unity version, the
target framework or the plugin set. Until the owner adopts it, the Planner
can task its pieces so it can be run by hand: the harness `checkpoints`
subcommand, `IHeadlessRun`, the dump writer and the bootstrap's batch mode.

> **LOW CONFIDENCE — nightly, and the real player.** Nightly means a
> Mono-only drift can merge and live for up to a day before it is caught. The
> nightly failure then follows `02-determinism.md` "When a gate fails", as
> the soak does. Per-merge would catch it at once but puts Unity in every
> merge's path. Using the real player is slower and needs a licence, but a
> standalone Mono run does not test the class libraries that ship.

---

## 16.10 Budget

- `app.host` adds nothing to the sim's 6 ms per tick.
- `RunFrame` adds only the calls of §16.6. It allocates nothing after the
  first call, and its callees carry their own budgets (`15` §15.11).
- Composition and bundle loading happen once, at scene start, off the frame
  path. No time budget.

---

## 16.11 Scope, fixtures and tests (for the Planner)

Two pieces of work. Writable paths are proposed; the Planner confirms them.

- **Headless host:** `src/app/host/**`, `tests/app/host/**`. The frame loop,
  the command-line parse and the dump writer can be built now against the
  `app.render` (T-020) and `app.ui` (`17` §17.10) scene-layer interfaces.
  `ISimComposer`, `IPresentationComposer`, `IHeadlessRun` and the
  harness-equivalence test need the module factories to exist (T-007/T-023,
  T-008, T-021, T-022, T-024). Tests supply content definitions directly
  until Q-011 is answered.
- **Unity project shell:** `unity/AirportSim/**`, including the bootstrap and
  the playtest `bundle.json`. It waits for the headless host, for the
  `app.render` and `app.ui` backends, and for Q-011: a player build has no
  other source of content definitions. It is not testable in CI (§16.2).

Done-condition tests for the headless host, phrased per `07-conventions.md`:

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
- `test_host_composition_matches_harness_checkpoints`

---

## 16.12 Open

- **Q-011**: content definitions and the `data/` loader (§16.3).
- **§16.9 adoption**: an owner decision, because it touches
  `02-determinism.md` and `ci/`.
