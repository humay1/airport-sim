# 16 — Public interfaces: `app.host`

Implements the `app.host` module created by owner decision D7: HUMAN
DECISION — owner (delegated), 2026-09-23, reversible. It answers
`15-interfaces-render.md` §15.13(b) and (c). Composition uses the published
construction surface of `08` §8.11a and each module's "Construction" section
(Q-009), with content loaded by `08` §8.11 (Q-011). Notation is as in `08-interfaces-core.md`. Where
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
| Tested in CI | yes, §16.11 | build-checked only: `unity-build` (below), not a required check and not a determinism gate; behaviour is checked by review, and by §16.9 if that gate is adopted |

Binding Unity project settings:

- **Editor version.** One Unity 6 LTS version, pinned in
  `ProjectSettings/ProjectVersion.txt`: **`6000.3.25f1`** (owner, `c48e163`).
  Changing it changes the shipped
  runtime, so it is recorded in `CHANGELOG.md` like a spec change.
- **Scripting backend: Mono**, for every player target (D1: "the shipped
  runtime (Mono)"). IL2CPP is a different runtime with its own class
  libraries. Using it needs its own decision and its own run of §16.9.
- **API compatibility level: .NET Standard 2.1.**
- **Plugins are the tested binaries.** The sim, the `app.render` and `app.ui`
  scene layers and this host are consumed as precompiled plugins. They come
  from the Release `dotnet build` of `AirportSim.sln`, and are copied into
  `unity/AirportSim/Assets/Plugins/AirportSim/` **before** Unity opens the
  project, because the project's scripts compile against them. In CI that
  copy is `unity.yml`'s plugin step (below); a local build runs the same
  two commands by hand. The Unity project holds no plugin-copy code. They
  are build outputs and are never committed. Unity never recompiles them from
  source, so the player runs exactly the assemblies CI tested.
- **Backends by reference: local packages (Q-114).** Each engine backend
  directory is a Unity local package: `src/app/render/Unity/` is
  `com.airportsim.render.unity` and `src/app/ui/Unity/` is
  `com.airportsim.ui.unity`, each with a `package.json` and one `.asmdef`
  at its root. `unity/AirportSim/Packages/manifest.json` references each
  by path, `"file:../../../src/app/render/Unity"` and
  `"file:../../../src/app/ui/Unity"`. The backend task adds its own
  package's one dependency line, and that line is the only write it makes
  under `unity/` (T-032, T-033). Nothing under `src/app/*/Unity/` is in
  `AirportSim.sln`.
- **Committed `.meta` files.** No agent runs the Unity editor, and
  `Main.unity` refers to scripts by GUID. So every asset under
  `unity/AirportSim/Assets/` and under each backend package that is
  committed has its `.meta` file committed beside it, written by the task
  that adds the asset, with a GUID it chooses (32 lowercase hexadecimal
  digits, unique in the repository). A `.meta` that Unity generates in CI
  is never relied on.
- **One scene**, `Assets/Scenes/Main.unity`, holding the bootstrap and the
  backends' components. There is no other scene at Phase 1.
- **Players:** Windows and Linux desktop, with macOS best-effort
  (`01-architecture.md`).

### The skeleton and the Unity build check (owner, `c48e163`)

**The skeleton** is committed: `ProjectSettings/ProjectVersion.txt`
(`6000.3.25f1`), `ProjectSettings/EditorBuildSettings.asset` with
`Assets/Scenes/Main.unity` as its one scene, an empty
`Packages/manifest.json` (`"dependencies": {}`), and `Assets/Scenes.meta`,
`Assets/Scenes/Main.unity` and its `.meta`. The scene holds nothing yet.
The Unity shell task extends this skeleton and does not recreate it.

**Ignored paths.** The root `.gitignore`'s Unity block ignores, under
`unity/AirportSim/`: `Library/`, `Temp/`, `Logs/`, `UserSettings/`,
`Build/`, `obj/`, `*.csproj`, `*.sln`, `Assets/Plugins/AirportSim/` and
`Assets/Plugins/AirportSim.meta`, and `/build/` at the root. The build
step's output (§16.3) is not ignored yet: `Assets/StreamingAssets/` and
`Assets/StreamingAssets.meta`, and Unity's `Packages/packages-lock.json`.
The `.gitignore` is not a module file, so the owner adds them. Until
then, no task commits any of them.

**`unity-build`** is the job of `.github/workflows/unity.yml`. It is
owner-owned, like `ci/`. It runs on pull requests and pushes touching
`unity/**`, `src/app/**` or the workflow, nightly, and by hand, and it is
**not a required status check**. Its steps:

1. It skips cleanly when `ProjectSettings/ProjectVersion.txt` is absent.
2. `dotnet build AirportSim.sln -c Release`, then it copies every
   `src/**/bin/Release/netstandard2.1/*.dll` into
   `Assets/Plugins/AirportSim/` (the plugin copy above).
3. `game-ci/unity-builder@v6` builds the `StandaloneLinux64` player
   (Mono, §16.2), with the editor version read from `ProjectVersion.txt`.
4. **The smoke**, only when
   `unity/AirportSim/Assets/StreamingAssets/Scenario/` holds a file after
   step 3: the player runs as `-batchmode -nographics -logFile -
   -airportsim-checkpoints 1 <path>`, must exit 0, and the dump's first
   line must be `airport-sim-checkpoints 1` (§16.8).

"Unity build green" in a task's Done-when means this job passed on the
PR's head. It checks that the project, the backends and the bootstrap
compile and that a player builds. The smoke checks that batch mode runs a
day. Neither compares a hash: that is §16.9, which stays proposed.

> **LOW CONFIDENCE — Unity mechanics no agent can run.** Local packages
> by `file:` path, hand-written `.meta` GUIDs and the build step's
> pre-build callback (§16.3) are the Architect's reading of how Unity 6
> behaves, with no editor to check them. `unity-build` is the check: a
> backend or shell PR whose job fails on one of these is a spec question,
> filed, not a workaround.

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
| `world.fixture` | `18-interfaces-world.md` §18.2; required whenever `sim.world` is listed, which it must be whenever `sim.flow` is (§16.4) | `IWalkGraphLoader.Load` |
| `schedule.csv` | `11-interfaces-schedule.md` §11.4 | `IScheduleLoader.Load` |
| `airside.fixture` | `12-interfaces-airside.md` §12.4 "File format" (JSON, Q-046) | `IAirsideLayoutLoader.Parse` |
| `airside_rules.json` | `04-data-schemas.md` (`AirsideRules`, `12` §12.4); required whenever `sim.airside` is listed | the host parses it into `AirsideRules`, both keys, `boarding_hold_max_minutes` and `doors_open_delay_minutes` (Q-047) |
| `turnaround.fixture` | `13-interfaces-turnaround.md` §13.4, in §13.10a's file format (Q-086) | `ITurnaroundSetupLoader.Load` |
| `flow.fixture` | `sim.flow`'s opaque `FlowGraph`, in the format of T-007/T-023's fixtures | `IFlowGraphLoader.Load` |
| `render_layout.fixture` | `15-interfaces-render.md` §15.4 | `IRenderLayoutLoader.Load` |

File names are exact. The `.fixture` files keep the byte format their
module's spec pins, or, where no spec pins one, the format its worker
chose. `airside.fixture` is `12` §12.4's JSON, despite its extension. The
name says nothing about the format.

- `systems` lists module names (`ISimSystem.Name`). Each Phase 1
  system's `Name` is exactly its module name from `08` §8.5's registry
  table, for example `sim.world` (Q-069). A listed system whose
  file is missing is a hard load failure naming the file
  (`07-conventions.md`). A system not listed is not registered: it is skipped,
  never reordered (`08` §8.5). `sim.delay` needs no file of its own.
- **Strict form (Q-069).** `bundle.json` is read with `08` §8.11's strict
  JSON subset, so a duplicate, unknown or missing key is a load failure.
  It has exactly the three keys above, and `schema_version` is `1`.
  `seed` is a string of 1 to 20 ASCII digits, with no sign and no leading
  zero except for `"0"` itself, and its value fits in `uint64`. `systems`
  is a non-empty array of distinct strings, each one of the six Phase 1
  module names (§16.4). Their order in the array does not matter, since
  registration follows the registry (§16.4). Anything else is a load
  failure naming `bundle.json`.
- The seed is parsed with the invariant culture (`07-conventions.md`,
  "Runtime portability" rule 4).
- The content is part of the input as well. The composer takes it as
  definitions (`HostFactory.CreateSimComposer(content)`, §16.4). In the
  player those come from `HostFactory.LoadContent(IContentSource source)`,
  which calls `08` §8.11's `IContentLoader` over
  `Assets/StreamingAssets/Content/`, a build-time copy of `data/` made by the
  build step. Tests may supply definitions directly.
- **The Phase 1 playtest bundle** is `unity/AirportSim/Scenario/bundle.json`,
  committed and owned by `app.host`, plus the Phase 1 fixtures named in
  `11` §11.10, `13` §13.11 and `15` §15.12, the `sim.flow` fixture T-023
  runs (`tests/fixtures/flow/phase0-landside.flow.json`), the walk graph
  that fixture is validated against
  (`18` §18.6, `tests/fixtures/world/phase0-landside.json`), the Phase 1
  checkpoints bundle's `airside.fixture` (`19` §19.2c, Q-113), and the
  human-authored `data/balance/airside_rules.json`
  (D6). Its `bundle.json` lists all six Phase 1 systems, so it needs one
  file for each row of the table above. The build step copies each source
  into `Assets/StreamingAssets/Scenario/` under that row's exact name:

  | Bundle file | Source |
  |---|---|
  | `bundle.json` | `unity/AirportSim/Scenario/bundle.json` |
  | `world.fixture` | `18` §18.6, `tests/fixtures/world/phase0-landside.json` |
  | `schedule.csv` | `11` §11.10, `tests/fixtures/schedule/phase0-200.csv` |
  | `airside.fixture` | `19` §19.2c, `tests/fixtures/harness/checkpoints-phase1/airside.fixture` (Q-113) |
  | `airside_rules.json` | `data/balance/airside_rules.json` |
  | `turnaround.fixture` | `13` §13.11, `tests/fixtures/turnaround/phase1-five-vehicles.json` |
  | `flow.fixture` | `tests/fixtures/flow/phase0-landside.flow.json`, the `sim.flow` fixture T-023 runs |
  | `render_layout.fixture` | `15` §15.12, `tests/fixtures/render/phase1-layout.json` |

  (Q-069, review of #83 at `a8e3edb`: the walk graph was missing from this
  list. The bundle lists `sim.world`, which needs `world.fixture`, and it
  lists `sim.flow`, which needs `sim.world`.) The copies are build output: the
  fixtures stay test fixtures, beside their tests (`07-conventions.md`), and
  are not moved into `data/`.
- **The build step (Q-114)** is an editor build callback in the Unity
  project, under `unity/AirportSim/Assets/Editor/`, that runs at the start
  of every player build, before streaming assets are collected. It
  deletes and recreates `Assets/StreamingAssets/Scenario/` and
  `Assets/StreamingAssets/Content/`. It copies each row of the table above
  into the first, by its exact name, and every file under `data/` into the
  second, at the same relative path. Sources are found relative to the
  repository root, two levels above the project directory. A missing
  source fails the build, naming the file. So a plain player build, the
  one `unity-build` runs (§16.2), assembles both directories with no
  workflow step, and the smoke runs whenever the bundle is complete. It is
  the only code that copies them, and it is engine-side code with no
  decision in it, checked by review like the bootstrap (§16.7).
- **The bundle's content ids resolve in `data/` (Q-113).** The player's
  content is a copy of `data/`, so every content id named by a playtest
  bundle file resolves in `data/`. `airside.fixture` is therefore not
  `12` §12.13's fixture, whose size categories (`medium`, `heavy`,
  `super`) exist only in the test kits' in-code content. It is that
  fixture with `data/`'s size ids, which `19` §19.2c pins, and the check
  listed there covers every row of the table above. `render_layout.fixture`
  names no content id.

---

## 16.4 Composition

```
readonly struct ComposedSim {
  ISimHost           Host
  IWorldSystem?      World                    // Q-022; null: not registered
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
(see §16.3).

`Compose` does exactly this, in this order:

1. Parse `bundle.json`, then
   `SimHostFactory.CreateBuilder({ seed, ContentIndexFactory.Create(content), checkpoints, log })`.
   The log sink is the host's (`08` §8.10).
2. Load each listed module's file with that module's loader (§16.3). The
   `sourceName` passed to a loader is exactly the bundle file name, for
   example `schedule.csv` (Q-073).
3. Construct the listed systems **in dependency order**, each with
   `builder.Services`, its data, and its downward interfaces or `null`:
   world; flow(world); schedule(flow); airside(schedule, flow,
   `turnaroundRegistered`); turnaround(schedule); delay.
4. `Register` them **in registry order** (`08` §8.5), then `Build`.

Rules:

- Systems register in the registry order of `08` §8.5. The Phase 1 set is
  `sim.world`, `sim.schedule`, `sim.airside`, `sim.flow`, `sim.turnaround`,
  `sim.delay`. `sim.world` is the fixed-graph subset of `18`.
- **Composition is a pure function of the bundle's bytes and the content
  data.** The same bundle gives the same checkpoint sequence wherever it is
  composed: in the harness, in a test, or in a Unity player on either
  runtime. No composition choice may depend on the machine, the runtime,
  the OS, file-system order or the clock.
- Composition happens once per session. There is no recomposition and no hot
  swap while a sim is running.
- Every checkpoint (`08` §8.9) goes to the given sink.
- A listed system whose required downward interface is not listed (flow
  without world, airside or turnaround without schedule) is a load failure.
- `tools.simharness`'s `checkpoints` subcommand (§16.8, `19` §19.2c) uses
  the **same factories**. It may wire them in its own code, which is what
  the equivalence test compares, but it must not construct any system
  another way. It does not reference `app.host` (a test checks this, `19`
  §19.8), and `app.host` does not reference it.

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
  Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences)   // D10
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

interface IPreferenceStore {                      // D10; implemented by the bootstrap over the engine's player preferences
  bool TryRead(string key, out string value)
  void Write(string key, string value)
}

readonly struct FrameOutput {
  RenderFrame Render                       // valid until the next RunFrame
  UiFrame     Ui
}

interface IFrameLoop { FrameOutput RunFrame(in FrameInput input) }
```

Each `RunFrame`, in this order:

1. `Ui.Update(input.ui, input.camera, input.screenWidth, input.screenHeight)`.
   This may submit commands and change the pacing state and the graphics
   settings (`17` §17.4, §17.4a, §17.5).
2. `Promotion.Update(input.camera, Ui.Graphics)`.
3. `n = Pacer.Advance(input.elapsedRealMicroseconds, Ui.Pacing.Paused,
   Ui.Pacing.Speed)`; if `n > 0`, `Host.Step(n)`.
4. `render = Scene.Build(input.camera, Ui.Graphics)`.
5. If `Ui.Graphics` differs from the value last written, write
   `UiFactory.EncodeGraphicsPreference(Ui.Graphics)` to the
   `IPreferenceStore` under the key `airportsim.graphics` (D10).
6. `ui = Ui.Frame()`. Return both.

**Graphics preference (D10).** At presentation assembly (§16.5), the host
reads `airportsim.graphics` from the store. If `TryDecodeGraphicsPreference`
succeeds, that value is the controller's `initialGraphics`. Otherwise the
default of `15` §15.14 is used. The value it started with counts as "last
written". The preference is per machine and per player. It is not part of the
scenario bundle and not part of a session's input, and a session's
checkpoints are identical whatever it holds. `IHeadlessRun` (§16.8) reads no
preference.

UI goes first, so a pause pressed this frame stops this frame's `Step`, and a
command submitted this frame is already queued before its tick runs. The
same holds for the settings panel, which pauses while open (`17` §17.4a,
owner, Q-034): the frame loop has no rule of its own for it, because
`Ui.Pacing.Paused` already includes it.
Promotion goes before `Step`, so a node that comes into view promotes before
the tick that shows it. Building goes after `Step`, so the frame shows the
state just produced. Nothing touches the sim while `Step` is running
(`15` §15.6). No allocation per `RunFrame` after the first.

---

## 16.7 The Unity bootstrap contract

The bootstrap is engine code. CI only compiles it and runs its batch mode
once (`unity-build`, §16.2), so it holds no decisions. It must stay small
enough for the Reviewer to check line by line against this list:

- **At scene start:** build an `IScenarioBundle` over
  `StreamingAssets/Scenario/`, call `ISimComposer.Compose` and then
  `IPresentationComposer.Compose`, and keep the `IFrameLoop`. Hand the backends
  what they draw. It passes an `IPreferenceStore` over the engine's player
  preferences (D10, §16.6). That adapter holds no logic beyond reading and
  writing one string.
- **Each engine frame:** get this frame's `CameraView` from the render backend
  and this frame's `UiInput`s from the UI backend, read the screen size,
  convert the engine's frame delta to integer microseconds (the float
  conversion is fine here: this is presentation), call `RunFrame`, and pass
  `Render` and `Ui` to their backends to draw.
- **Batch mode:** pass the process arguments to
  `IHostCommandLine.TryParse` (§16.8): the engine's command-line arguments
  with the first one, the executable, removed. If it returns a checkpoint
  run, call `IHeadlessRun.Run`, then quit with its exit code. If it returns
  false, quit with exit code 2 (Q-114). Either way, no frame loop runs and
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

**`TryParse` (Q-114).** The engine adds its own arguments, for example
`-batchmode -nographics -logFile -`, so `args` holds more than the three
tokens above. `TryParse` returns true exactly when the ordinal token
`-airportsim-checkpoints` occurs exactly once in `args`, and the two
arguments after it are `<days>` and `<outputPath>`. `<days>` is ASCII
digits with no sign and no leading zero, parsed with the invariant
culture, from 1 to 298 261 (the bound of `19` §19.3's `--days`).
`<outputPath>` is any non-empty string. Then `Days` and `OutputPath` are
those two values. Every argument that is not the token or one of its two
values is ignored, wherever it is. In every other case, the token absent
included, it returns false and `request` is `default`. It throws nothing.

`Run` composes the bundle (§16.4) with a sink that records every checkpoint.
It submits **no command** (Q-071). It then calls `Step(TICKS_PER_SIM_DAY)`
exactly `Days` times, so that `Days × TICKS_PER_SIM_DAY` ticks run, with no
presentation at all (no promotion, no pacer), and writes the dump. The
harness side steps the same way (`19` §19.2c, Q-074). The step
batch size must not change the result, because the tick is fixed
(`02-determinism.md` rule 1, `08` §8.2). A test asserts that anyway. It
has no seam: `test_headless_run_result_independent_of_step_batch_size`
composes the same bundle with `ISimComposer.Compose`, steps it in other
batches, renders that run's dump with its own code, and compares it with
`Run`'s file, as `19` §19.8 does for the harness.

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
`checkpoints --bundle <dir> --content <dir> --days <n> --out <path>`
(Q-066, Q-072). It composes the bundle in its own code, by §16.4's rules,
over the content in the `--content` directory, and writes the same format.
Its grammar, paths, composition, stages and failures are `19` §19.2c and
§19.3. The existing subcommands that `ci/`
invokes are unchanged. The harness's `soak` subcommand and its golden,
`tests/golden/soak-500.hashes`, use this format too (`19` §19.2b, Q-057).

**Equivalence with the harness (D7).**
`test_host_composition_matches_harness_checkpoints` runs the harness
`checkpoints` subcommand and `IHeadlessRun` (in-process, on `net8.0`) on
the same bundle and content for one sim-day. The dumps must be byte-identical.
It runs on two bundles (Q-069, Q-070, Q-076):

- `tests/fixtures/harness/checkpoints-phase0/`, with content
  `tests/fixtures/harness/phase0-content/`. It lists `sim.world`,
  `sim.schedule` and `sim.flow`. That is T-009's composition **without**
  its boarding stand-in, which only the harness's CLI composition
  registers (`19` §19.2a). A flow without a world is a load failure
  (§16.4), so `sim.world` is listed;
- `tests/fixtures/harness/checkpoints-phase1/`, with content `data/`. It
  lists all six Phase 1 systems over the Phase 1 fixtures (`19` §19.2c,
  "The fixture").

The Phase 1 playtest bundle itself (§16.3) belongs to the Unity shell
task, which follows the headless host, so this test cannot use it. The
playtest bundle is compared by §16.9's procedure.

> **LOW CONFIDENCE — the D7 bundles.** D7's equivalence is tested on a
> Phase 1 test bundle, not on the shipped playtest bundle, because the
> playtest bundle does not exist when the headless host merges. A
> difference that only the playtest bundle's files expose is caught by
> §16.9's procedure, by hand, until that gate is adopted. That procedure
> reads the build step's copy of the playtest bundle (§16.9 step 1), so
> it can run as soon as the Unity shell task has committed the bundle.

**Where the D7 test lives (Q-077).** `tests/integration/`, the one test
project that references both `src/app/host` and `tools/SimHarness` (`07`
L1, L3). It calls the harness through `HarnessCli.Run` (`19` §19.1) and
`app.host` through `HostFactory`, both in process, and it never spawns a
process. It compares the two dumps directly. No golden is committed for
it. It is the only test in that project. Every other `app.host` test stays
in `tests/app/host/`.

---

## 16.9 The cross-runtime determinism gate — PROPOSED (D1)

**Why.** The contract of `02-determinism.md` is "bit-identical ... on every
machine". D1 ships the sim on Unity's Mono and tests it on CoreCLR, and every
existing gate runs CoreCLR only. A violation of `07-conventions.md` "Runtime
portability" can pass every one of those gates, for example a tie in
`List<T>.Sort` that CoreCLR happens to resolve the same way on every run.

**`determinism_cross_runtime`:**

1. **CoreCLR:** `tools.simharness checkpoints --bundle
   unity/AirportSim/Assets/StreamingAssets/Scenario --content
   unity/AirportSim/Assets/StreamingAssets/Content --days 10 --out a`
   (`19` §19.2c). It runs after the player build step (§16.3). That step
   assembles the playtest bundle, meaning every file of §16.3's
   playtest-bundle table, `world.fixture` included, in
   `Assets/StreamingAssets/Scenario/`, and copies `data/`
   into `Assets/StreamingAssets/Content/`. So the harness reads exactly
   the bytes the player reads. Both directories are build output and are
   never committed. `unity/AirportSim/Scenario/` holds only `bundle.json`
   and is not a complete bundle, so it is never passed as `--bundle`.
2. **Mono:** the Unity player build of `unity/AirportSim/` (Mono backend,
   §16.2), built by that same build step, started as
   `-batchmode -nographics -airportsim-checkpoints 10 b`.
3. **Pass:** `a` and `b` are byte-identical (§16.8).

`B`, the scenario that both sides run, is the Phase 1 playtest bundle,
with every Phase 1 system registered.
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

**Since `c48e163` (open for the owner, Q-115).** CI now builds the real
Linux player nightly and on Unity-path changes (`unity-build`, §16.2), and
its smoke already runs `-airportsim-checkpoints 1`. So step 2 exists, and
step 1 could run in the same job, after step 3 of `unity-build`, over the
directories its build step assembles. Adopting that as §16.9 still needs
the owner: the `02-determinism.md` gate-table row, the workflow change,
the day count (1 as the smoke runs, or 10 as above), and whether the job
then becomes required. Until then the smoke compares nothing, and no task
adds a comparison to it.

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
- **Memory on minimum spec (Q-034).** The minimum GPU is integrated
  graphics, whose memory is shared system RAM and counts against the 8 GB
  (HUMAN DECISION — owner, 2026-09-27). The player process, meaning its
  resident memory plus the GPU memory it allocates, stays **≤ 2 GB** at
  max tier with the `Low` and `Medium` presets. The rest of the 8 GB is
  left to the operating system and the integrated GPU's own reservation.
  CI cannot measure it, so it is checked with `15` §15.14's manual
  measurement on a minimum-spec machine. **LOW CONFIDENCE — owner may
  revise**: 2 GB is the Architect's estimate, with no measurement behind
  it.

---

## 16.11 Scope, fixtures and tests (for the Planner)

Two pieces of work. Writable paths are proposed; the Planner confirms them.

- **Headless host:** `src/app/host/**`, `tests/app/host/**`, and
  `tests/integration/**` for the D7 test (§16.8, Q-077). The frame loop,
  the command-line parse and the dump writer can be built now against the
  `app.render` (T-020) and `app.ui` (`17` §17.10) scene-layer interfaces.
  `ISimComposer`, `IPresentationComposer`, `IHeadlessRun` and the
  harness-equivalence test need the module factories to exist (T-007/T-023,
  T-008, T-021, T-022, T-024, and the `sim.world` task). Tests may supply
  content definitions directly.
- **Unity project shell:** `unity/AirportSim/**`, including the bootstrap,
  the build step (§16.3) and the playtest `bundle.json`. It extends the
  committed skeleton (§16.2). It waits for the headless host, for the
  `app.render` and `app.ui` backends, and for the Phase 1 content files in
  `data/` (`04-data-schemas.md`). The pax-profile and queue-profile values
  among them are the owner's. CI checks it only through `unity-build`
  (§16.2). Its build step is what makes the smoke run, so its Done-when
  includes `unity-build` green **with the smoke step run**, not skipped.
- **Engine backends** (`15` §15.10, `17` §17.8): each one's Done-when
  includes `unity-build` green on its PR, with its package referenced
  from `Packages/manifest.json` (§16.2). The smoke is skipped until the
  shell's build step exists.

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
- `test_host_composition_matches_harness_checkpoints`, in
  `tests/integration/` (§16.8, Q-077)
- `test_frame_loop_passes_ui_graphics_to_promotion_and_scene` (D10)
- `test_frame_loop_writes_graphics_preference_only_on_change`
- `test_presentation_uses_stored_graphics_preference_or_default` — the
  default is `Medium` (Q-034)
- `test_frame_loop_settings_opened_this_frame_steps_nothing` (Q-034)

---

## 16.12 Open

- **Gate assignment** (`18` §18.5): deferred to the owner; Phase 0/1 pools
  gates.
- **§16.9 adoption**: an owner decision, because it touches
  `02-determinism.md` and `ci/` (and now `unity.yml`, Q-115).
- **Ignored build output** (§16.2): the owner adds
  `unity/AirportSim/Assets/StreamingAssets/`, its `.meta` and
  `unity/AirportSim/Packages/packages-lock.json` to `.gitignore`.
- **D10 values** (`15` §15.14): decided by the owner on 2026-09-27
  (Q-034): the low-end target (integrated graphics), the first-launch
  default (`Medium`) and the pause. The `Low` and `Medium` values and the
  2 GB memory budget (§16.10) are the Architect's proposals, marked LOW
  CONFIDENCE, for the owner to revise.
