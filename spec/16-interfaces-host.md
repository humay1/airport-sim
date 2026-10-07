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
- **the Unity bootstrap**, one thin engine script that calls the headless host,
  plus the one `app.ui` call that loads the player-visible text, and nothing
  else (§16.7, Q-125);
- **the headless checkpoint run** and the checkpoint dump format (§16.8), used
  to prove the host composes the same sim as `tools.simharness` and by the
  non-required cross-runtime check (§16.9).

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
| Tested in CI | yes, §16.11 | build-checked only: `unity-build` (below), not a required check and not a determinism gate; behaviour is checked by review, and by §16.9's separate non-required `cross-runtime` job |

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
5. **The hand-off**, only when the smoke ran: the job sets its output
   `smoke_ran` to `true` and uploads the artifact `cross-runtime-input`
   (§16.9). It compares nothing.

"Unity build green" in a task's Done-when means **this job, `unity-build`,
alone** passed on the PR's head. It checks that the project, the backends
and the bootstrap compile, that a player builds, and that batch mode runs
a day. It never includes the separate `cross-runtime` job (§16.9), whose
result does not decide any task's Done-when.

**Checked against Unity's documentation (2026-10-06, Q-124).** A `file:`
path in `Packages/manifest.json` resolves relative to the `Packages/`
directory, so the two lines above, three levels up, name the backend
directories.

> **LOW CONFIDENCE — Unity mechanics no agent can run.** Hand-written
> `.meta` GUIDs and the build step's pre-build callback (§16.3) are the
> Architect's reading of how Unity 6 behaves, with no editor to check
> them. `unity-build` is the check: a backend or shell PR whose job fails
> on one of these is a spec question, filed, not a workaround.

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
  player those come from
  `HostFactory.LoadContent(IContentSource source) -> IReadOnlyList<IContentDefinition>`
  (Q-117), over `Assets/StreamingAssets/Content/`, a build-time copy of
  `data/` made by the build step. It returns exactly
  `ContentLoaderFactory.Create().Load(source)` (`08` §8.11), and lets that
  loader's `FormatException` through unchanged. It adds no check, no
  filtering and no ordering of its own. Tests may supply definitions
  directly.
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
  source fails the build, naming the file. After the last copy, and
  before it returns, it calls
  `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)`, so
  that the player build collects the files just copied (Q-124, checked
  against Unity's documentation 2026-10-06). So a plain player build, the
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
  IContentIndex?     Content                  // Q-130; the index step 1 built; null only from the 7-argument constructor
}

interface ISimComposer {
  ComposedSim Compose(IScenarioBundle bundle, ICheckpointSink checkpoints)
}
```

`ISimComposer` is created with the content definitions:
`HostFactory.CreateSimComposer(IReadOnlyList<IContentDefinition> content)`
(see §16.3).

**`ComposedSim.Content` (Q-130).** `Compose` returns, as `Content`, the
same `IContentIndex` instance that step 1 passed to `CreateBuilder`.
Under `07` L10's "kept constructor" clause, `ComposedSim` has exactly two
public constructors: one taking all eight fields in declared order, and
the earlier seven-argument one, which sets `Content` to null. Only
presentation reads `Content`, for aircraft visuals (`15` §15.16). A null `Content` draws every aircraft as `AircraftC`.

`Compose` does exactly this, in this order:

1. Parse `bundle.json`, then
   `SimHostFactory.CreateBuilder({ seed, ContentIndexFactory.Create(content), checkpoints, log })`.
   The log sink is the host's (`08` §8.10).
2. Load each listed module's file with that module's loader (§16.3), in
   the row order of §16.3's table, **except `flow.fixture`**, which step 3
   loads. The `sourceName` passed to a loader is exactly the bundle file
   name, for example `schedule.csv` (Q-073).
3. Construct the listed systems **in dependency order**, each with
   `builder.Services`, its data, and its downward interfaces or `null`:
   world; flow(world); schedule(flow); airside(schedule, flow,
   `turnaroundRegistered`); turnaround(schedule); delay. `flow.fixture` is
   loaded after the world system is built and before the flow system,
   because `IFlowGraphLoader.Load` validates the graph against
   `IWorldSystem` (`09` §9.11, Q-126).
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
- **One composer, many calls (Q-123).** An `ISimComposer` may be called
  any number of times. Each `Compose` builds a new `ISimHost` and new
  systems, and shares no mutable object with any other call's result. The
  composer holds only the content it was created with, and no call changes
  it, so the results of two calls on one bundle are the same as from two
  composers. "Once per session" binds a session's sim, not the composer.
  The same holds for `IHeadlessRun.Run` (§16.8) and
  `IPresentationComposer.Compose` (§16.5).
- Every checkpoint (`08` §8.9) goes to the given sink.
- A listed system whose required downward interface is not listed (flow
  without world, airside or turnaround without schedule) is a load failure.
- `tools.simharness`'s `checkpoints` subcommand (§16.8, `19` §19.2c) uses
  the **same factories**. It may wire them in its own code, which is what
  the equivalence test compares, but it must not construct any system
  another way. It does not reference `app.host` (a test checks this, `19`
  §19.8), and `app.host` does not reference it.

**Load failures (Q-118).** Every load failure of `ISimComposer.Compose`
and of `IPresentationComposer.Compose` (§16.5) throws `FormatException`
(`07-conventions.md` "Error handling"). Its message starts with the
name of the bundle file at fault, followed by `": "`:

| Failure | Message starts with |
|---|---|
| `bundle.json` missing, or breaking §16.3's strict form | `bundle.json: ` |
| a listed system whose downward interface is not listed (above); the message names both systems | `bundle.json: ` |
| a file of a listed system missing (§16.3) | that file's name, for example `schedule.csv: ` |
| `airside_rules.json` breaking `04-data-schemas.md`'s `AirsideRules` | `airside_rules.json: ` |
| `render_layout.fixture` missing (§16.5) | `render_layout.fixture: ` |

A loader's or a factory's own `FormatException` passes through
unchanged. Its message already starts with the `sourceName`, which is
the bundle file name (step 2), or with the module name (`07`). The host
never catches, wraps or replaces it. `Compose` checks in this order and
throws at the first failure: `bundle.json`; the downward interfaces; the
presence of every listed system's files, in the row order of §16.3's
table. All three come inside step 1, after the parse and **before**
`ContentIndexFactory.Create` and `CreateBuilder`. Then come step 1's
`ContentIndexFactory.Create` and `CreateBuilder`, whose content failures
start with the module name (`07`); then step 2's loads, in the row order
of §16.3's table without `flow.fixture`; then step 3, in its order, with
the `flow.fixture` load between the world factory and the flow factory
(Q-126). The harness's `checkpoints` composition loads each file just
before its own system's factory instead (`19` §19.2c). So for a bundle
with more than one fault, the two may report different first failures.
Both fail, and no test compares their failures: the D7 test compares
successful dumps only (§16.8). A `null` argument to either composer
throws `ArgumentNullException`, before any check.

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
  Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences,
                       in RenderLooks looks)                                                     // Q-130
}
```

- Builds `RenderSources { Host, Airside, Flow, Schedule, Content }`
  (`15` §15.9, Q-130) from the `ComposedSim`, with its five-argument
  constructor.
- **Looks (Q-130).** The four-argument `Compose` builds the scene with
  `RenderFactory.CreateSceneBuilder(sources, layout, looks)`. The
  three-argument one is the four-argument one with
  `RenderFactory.DefaultLooks()`. The host never reads `data/looks/`
  itself, and `IHeadlessRun` uses no looks.
- Loads `render_layout.fixture` through `IRenderLayoutLoader`, passing
  `Airside.Layout()` when `sim.airside` is registered, and `null`
  otherwise. Its `sourceName` is exactly `render_layout.fixture`, as for
  every bundle file (§16.4 step 2, Q-073, Q-121). The file is required
  whatever `systems` lists. A layout failure is a hard load failure
  (§16.4 "Load failures").
- Constructs the scene builder, the promotion controller and the pacer with
  `RenderFactory` (`15` §15.9), and the UI controller and its lane sink with
  `UiFactory` (`17` §17.7).
- **A presentation needs `sim.flow` (Q-127).** The lane sink is built
  with `sim.Flow`. If `sim.flow` is not registered, that is `null`, and
  `UiFactory.CreateLaneCommandSink` throws its `ArgumentNullException`
  (`flow`, `17` §17.7), which passes through unchanged. It comes after the
  layout load, so a layout failure is reported first. It is not a load
  failure of §16.4's table, and there is no ignore-all sink. The playtest
  bundle lists `sim.flow` (§16.3), and `IHeadlessRun` builds no
  presentation (§16.8).
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
readonly struct FrameInput {                      // members PascalCase (Q-119)
  CameraView             Camera                   // from the render backend
  float                  ScreenWidth              // pixels, > 0
  float                  ScreenHeight             // pixels, > 0
  IReadOnlyList<UiInput> Ui                       // from the UI backend, arrival order (17 §17.3)
  int64                  ElapsedRealMicroseconds  // engine frame delta, converted by the bootstrap
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

1. `Ui.Update(input.Ui, input.Camera, input.ScreenWidth, input.ScreenHeight)`.
   This may submit commands and change the pacing state and the graphics
   settings (`17` §17.4, §17.4a, §17.5).
2. `Promotion.Update(input.Camera, Ui.Graphics)`.
3. `n = Pacer.Advance(input.ElapsedRealMicroseconds, Ui.Pacing.Paused,
   Ui.Pacing.Speed)`; if `n > 0`, `Host.Step(n)`.
4. `render = Scene.Build(input.Camera, Ui.Graphics)`.
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
- **The text (Q-125).** Also at scene start, never in a checkpoint run:
  call `UiFactory.LoadStringTable` (`17` §17.4b) with the same
  `IContentSource` over `StreamingAssets/Content/` that it gives
  `HostFactory.LoadContent` (§16.3), and hand the table to the UI backend
  before the backend's first draw. Apart from handing the backends what
  they draw, this and the looks below are its only calls into a module
  other than `app.host`. The bootstrap does not catch its exception.
- **The looks (Q-130).** Also at scene start, never in a checkpoint run:
  call `RenderFactory.LoadLooks` (`15` §15.16) with that same
  `IContentSource`, and pass the result to the four-argument
  `IPresentationComposer.Compose` (§16.5). It does not catch the
  exception.
- **Each engine frame:** get this frame's `CameraView` from the render backend
  and this frame's `UiInput`s from the UI backend, read the screen size,
  convert the engine's frame delta to integer microseconds (the float
  conversion is fine here: this is presentation), call `RunFrame`, and pass
  `Render` and `Ui` to their backends to draw.
- **Batch mode:** pass the process arguments to
  `IHostCommandLine.TryParse` (§16.8): the engine's command-line arguments
  with the first one, the executable, removed. If it returns a checkpoint
  run: replace `System.Console.Error` with `Console.SetError` by a
  `TextWriter` that forwards each completed line, without its line break,
  to `Debug.LogError`; call `IHeadlessRun.Run`; restore the original
  `Console.Error`; then call `Application.Quit` with `Run`'s exit code
  (Q-120). So `Run`'s failure line (§16.8) lands in the player log, and on
  standard output under `-logFile -`, which is the smoke's log
  (§16.2). A Windows player has no console, so without the forwarding the
  line would be lost. The writer holds no logic beyond splitting lines.
  A line break is LF, or CR followed by LF, which counts as **one** line
  break, so a `WriteLine` gives one line whatever `Environment.NewLine`
  is (Q-129). A CR not followed by LF is part of the line. If
  `TryParse` returns false, quit with exit code 2 (Q-114). Either way, no
  frame loop runs and nothing is drawn.
- It calls no sim member, never branches on sim state, and never reads a
  bundle file itself.

---

## 16.8 The headless checkpoint run and the dump format

```
readonly struct CheckpointRunRequest { uint32 Days; string OutputPath }

interface IHostCommandLine {
  bool TryParse(IReadOnlyList<string> args, out CheckpointRunRequest request)
}   // finds -airportsim-checkpoints <days> <outputPath> among the other arguments (below)

interface IHeadlessRun {
  int Run(IScenarioBundle bundle, in CheckpointRunRequest request)   // 0 on success, 3 on failure (below)
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

So the engine's arguments may come before the three tokens, after them,
or both, but never between the token and its values (Q-122): in
`-airportsim-checkpoints -batchmode 1 p`, `<days>` is `-batchmode`, so
`TryParse` returns false. A `null` `args` returns false. A `null`
element as `<days>` or `<outputPath>` returns false; a `null` element
anywhere else is ignored like any other argument.

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

**`Run`'s stages and failures (Q-120).** The bootstrap quits with `Run`'s
return value (§16.7), and an exception that escapes a script in a
batch-mode player does not end the process, so `Run` reports every
failure as a return value. It mirrors the harness's `checkpoints` stages
(`19` §19.2c):

1. If `OutputPath` exists, as a file or a directory, or its parent
   directory does not exist, `Run` returns 3. `OutputPath` is used as
   given; a relative path is relative to the process's current directory.
2. Composition (§16.4), with §16.4's "Load failures".
3. The run, as above.
4. `OutputPath` is created as a new file, never overwriting one, and the
   dump is written to it.

Any exception in stages 1 to 4, `SimInvariantException` included,
returns 3. A failure in stages 1 to 3 leaves no file. A write failure in
stage 4 may leave a partial file, and no test depends on it. On success
`Run` returns 0 and writes nothing to `System.Console.Error` or
`System.Console.Out`. It returns no other value. The exit code is the
machine contract; the failure line below is the human one.

**The failure line (Q-120, coordinator ruling within the owner's
delegation, 2026-10-06).** On a 3, `Run` makes exactly one call,
`System.Console.Error.WriteLine(line)`, and writes nothing else to the
console. `line` is one of:

| Stage | `line` |
|---|---|
| 1, `OutputPath` exists | `FAIL checkpoints output-exists <OutputPath>` |
| 1, its parent directory does not exist | `FAIL checkpoints output-no-parent <OutputPath>` |
| 2, any exception | `FAIL checkpoints load <type>: <message>` |
| 3, any exception | `FAIL checkpoints run <type>: <message>` |
| 4, any exception | `FAIL checkpoints write <OutputPath> <type>: <message>` |

`<OutputPath>` is the request's value as given. `<type>` is the
exception's `GetType().Name` and `<message>` its `Message`, so a load
failure names its bundle file (§16.4 "Load failures"). There is no stack
trace and no inner exception. The line is plain ASCII: every character
of it outside U+0020 to U+007E, a line break included, is written as
`?`. A character here is one UTF-16 code unit (Q-128), so a character
outside the BMP, which is a surrogate pair, is written as `??`. The harness's `checkpoints` subcommand names no stage words of its
own (`19` §19.3), so these are `Run`'s. In the player, the bootstrap
routes this line to the engine log (§16.7).

The only exceptions `Run` throws are for programmer error, before stage
1 (`07` "Error handling"): `ArgumentNullException` for a `null` bundle,
and `ArgumentOutOfRangeException` for a request `TryParse` cannot return,
that is `Days` outside 1 to 298 261 or `OutputPath` `null` or empty.
They write nothing to the console.

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
> §16.9's `cross-runtime` job, which reads the player's staged copy of
> the playtest bundle (§16.9 step 1) and runs as soon as the Unity shell
> task's build step assembles it.

**Where the D7 test lives (Q-077).** `tests/integration/`, the one test
project that references both `src/app/host` and `tools/SimHarness` (`07`
L1, L3). It calls the harness through `HarnessCli.Run` (`19` §19.1) and
`app.host` through `HostFactory`, both in process, and it never spawns a
process. It compares the two dumps directly. No golden is committed for
it. It is the only test in that project. Every other `app.host` test stays
in `tests/app/host/`.

---

## 16.9 The cross-runtime determinism check — ADOPTED, NOT REQUIRED (D1, Q-115)

**Why.** The contract of `02-determinism.md` is "bit-identical ... on every
machine". D1 ships the sim on Unity's Mono and tests it on CoreCLR, and every
existing gate runs CoreCLR only. A violation of `07-conventions.md` "Runtime
portability" can pass every one of those gates, for example a tie in
`List<T>.Sort` that CoreCLR happens to resolve the same way on every run.

**Status: adopted as a non-required check.** HUMAN DECISION, owner,
2026-10-06 (Q-115). It is its **own job, `cross-runtime`**, in
`.github/workflows/unity.yml`, with `needs: unity-build`, so it runs
wherever that job runs: pull requests and pushes on its paths, nightly,
and by hand. It is **not** a required status check, and it is **not** a
row of `02-determinism.md`'s gate table, which lists required gates
only. After some weeks of clean runs the owner decides whether to make it
required. That decision, and any `02` row it brings, are the owner's. It
starts to run when the smoke does, that is once T-034's build step
assembles `StreamingAssets/Scenario/` (§16.3). Until then it is skipped.
The workflow is owner-owned, so no task writes it.

**Why a separate job (manager decision within Q-115).** A failing step
would turn `unity-build` red, and "Unity build green" is in T-034's and
the backends' Done-when (§16.2, §16.11). A dump difference is not theirs
to fix. As its own job, `cross-runtime` can be red while `unity-build`
is green. It needs no Unity licence, since it never runs the editor or
the player.

**In `unity-build`: the hand-off** (§16.2 step 5). Only when the smoke
ran and passed:

- the job output `smoke_ran` is `true` (absent otherwise);
- `actions/upload-artifact@v4` uploads the artifact named exactly
  **`cross-runtime-input`**, with `include-hidden-files: true` and
  `if-no-files-found: warn`, from the workspace root, of these three
  paths:
  - `checkpoints.txt`, the smoke's dump (§16.2 step 4);
  - `build/StandaloneLinux64/AirportSim_Data/StreamingAssets/Scenario/`;
  - `build/StandaloneLinux64/AirportSim_Data/StreamingAssets/Content/`.

  Those two directories are the player's own staged copies: the Linux
  player's `Application.streamingAssetsPath` points there, so they are
  the bytes the player read. They are not the project's
  `Assets/StreamingAssets/` directories, where Unity may write `.meta`
  files that the harness would list (`19` §19.2c). The artifact keeps
  paths relative to the workspace root, and the zip carries file bytes
  unchanged. A missing path is reported by `cross-runtime` (step 1
  below), not here, so a staging problem never fails `unity-build` in
  this step.

**The `cross-runtime` job, exactly.** It runs on `ubuntu-latest` with
`if: needs.unity-build.outputs.smoke_ran == 'true'`. Its setup:
`actions/checkout@v4` of the same commit, `actions/setup-dotnet@v4` with
`8.0.x`, `dotnet build AirportSim.sln -c Release` (which builds the
harness), and `actions/download-artifact@v4` of `cross-runtime-input`
into `$RUNNER_TEMP/cross-runtime-input`. Let `IN` be that directory and
`S` be `$IN/build/StandaloneLinux64/AirportSim_Data/StreamingAssets`. Then
one step runs these checks **in this order** and stops at the first
failure:

1. **Inputs present.** `$IN/checkpoints.txt` is a file, and `$S/Scenario`
   and `$S/Content` are directories. The first one that is not is
   `FAIL cross_runtime missing <path>`, with the path as written here.
   The harness is not run.
2. **CoreCLR (the reference dump):**
   `dotnet run --project tools/SimHarness -c Release --no-build --
   checkpoints --bundle "$S/Scenario" --content "$S/Content" --days 1
   --out "$RUNNER_TEMP/harness-checkpoints.txt"`. The paths are fully
   qualified, so the harness uses them as given (`19` §19.2c "Paths").
   The `--out` file does not exist beforehand. A non-zero exit is
   `FAIL cross_runtime harness exit=<code>`, followed by its stderr.
3. **Its result line.** Exit 0 with stdout not starting
   `WROTE checkpoints ` (`19` §19.3) is
   `FAIL cross_runtime harness no WROTE line`, followed by its stdout.
4. **The comparison** of the whole of `$RUNNER_TEMP/harness-checkpoints.txt`
   and `$IN/checkpoints.txt`, byte for byte (`cmp`), with no
   normalisation: no line-ending, BOM, whitespace or trailing-newline
   conversion. Both writers emit the §16.8 format, UTF-8 without a BOM,
   LF only, with a final newline, so any such difference is a failure of
   one writer. Pass: the files are identical, and the step prints
   `PASS cross_runtime days=1`. Otherwise
   `FAIL cross_runtime days=1 line=<n>`, where `<n>` is the 1-based
   number of the first differing line (a line present in only one file
   counts as differing), then `harness: <that line of the harness dump>`
   and `player:  <that line of the player dump>`, with `<missing>` for a
   line that does not exist. By §16.8, line 3 names the systems, from
   line 4 on the first field is the tick, and each later column is one
   system's hash in that order.

Every `FAIL` line is the step's first line of output, and the step exits
1, which makes only `cross-runtime` red.

**The two sides.** Mono is the smoke's own run (§16.2 step 4, §16.7):
`build/StandaloneLinux64/AirportSim.x86_64 -batchmode -nographics -logFile -
-airportsim-checkpoints 1 "$PWD/checkpoints.txt"` (the player's file
name is `unity-builder`'s `buildName` plus `.x86_64`, Q-124). No second player run
is made. CoreCLR is step 2's harness run over the downloaded copies of
the directories the player read. **The seed** is not an argument on
either side. Both read it from the staged `bundle.json`, whose source is
the committed `unity/AirportSim/Scenario/bundle.json` (§16.3). The days
are **1** on both sides, as the smoke runs.

Ten days, as `determinism_cross_process` runs, is not used here: one day
keeps a single player run per job, and the owner may lengthen it with
the decision to make the check required. The Mono side must be the
**real player**. Unity ships its own fork of Mono and its own class
libraries, so a pass on a standalone upstream Mono proves little about the
shipped build.

**When it fails.** It is not required and it is a separate job, so it
blocks no merge and no task's Done-when. A `missing` failure or a
harness load failure on the staged files points at the build step
(§16.3), and the task that owns it (T-034) fixes it within the spec. If
the fix needs anything the spec does not say, for example another staged
path, a filter on `.meta` files or a workflow change, that task files a
spec question and stops (Q-124). A dump difference is a
determinism defect. It is reported to the owner and
handled as `02-determinism.md` "When a gate fails" describes for the
soak. It is never fixed by editing either dump, the comparison or the
day count.

**Confirmed (2026-10-06, Q-124).** `unity-builder`'s log of run
37526054729 shows the player `build/StandaloneLinux64/AirportSim.x86_64`
beside its data directory `build/StandaloneLinux64/AirportSim_Data/`, so
the staged streaming assets are under `AirportSim_Data/StreamingAssets/`.

> **LOW CONFIDENCE — Unity behaviour (Q-115).** One thing still depends
> on how Unity's Linux player build behaves, and no agent can check it:
> that the staged files are byte copies of the build step's files, with
> no `.meta` files. The first `cross-runtime` run after T-034 checks it. A
> harness content-load failure naming a `.meta` file is handled as "When
> it fails" says. Nightly, plus PRs on the Unity paths only, still means
> a Mono-only drift from a sim-only PR can live up to a day before it is
> caught.

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
  `app.render` and `app.ui` backends, for the Phase 1 content files in
  `data/` (`04-data-schemas.md`), and for `app.ui`'s string table, whose
  `UiFactory.LoadStringTable` its bootstrap calls (§16.7, `17` §17.10,
  Q-125). The pax-profile and queue-profile values
  among them are the owner's. CI checks it only through `unity-build`
  (§16.2). Its build step is what makes the smoke run, so its Done-when
  includes `unity-build` green **with the smoke step run**, not skipped.
  That PR is also the first run of §16.9's separate `cross-runtime` job,
  so its Done-when includes that job **run, not skipped**, with its
  result line quoted in the PR, whatever that result is. Its colour is
  not part of "`unity-build` green". A `missing` failure or a harness
  load failure on the staged files points at T-034's build step, and
  T-034 fixes it within the spec, or files a spec question and stops if
  the fix needs anything the spec does not say (§16.9 "When it fails",
  Q-124). A difference between two dumps that both loaded and ran
  is a determinism defect reported to the owner, not fixed in T-034.
  T-034 adds nothing for the job: the workflow is the owner's, and the
  bundle and batch mode already exist.
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
- `test_headless_run_failure_returns_3_and_writes_no_file` (Q-120): an
  existing `OutputPath`, left byte-unchanged; a missing parent directory;
  and a bundle with a load failure. Each returns 3, throws nothing,
  leaves no new file, and writes exactly its one §16.8 failure line to a
  captured `Console.Error` (`output-exists`, `output-no-parent`, `load`);
  a successful `Run` writes nothing there
- `test_compose_exposes_the_content_index` (Q-130): `ComposedSim.Content`
  resolves every definition the composer was created with, and nothing
  else
- `test_presentation_render_sources_carry_schedule_and_content` (Q-130):
  with a tracked aircraft on the graph, a frame's `Scene.Build` calls
  `TryGetFlight` on the composed `sim.Schedule`, and the aircraft's visual
  follows its size category in the composed content
- `test_presentation_passes_looks_to_the_scene` (Q-130): with the
  four-argument `Compose`, an aircraft's `Paint` is its airline's livery
  from the given `RenderLooks`. With the three-argument one, it is
  `DefaultLooks()`'s livery

**One merged host test changes (Q-130).** The host task's Test Author
updates `test_host_assembly_public_surface_matches_spec`
(`tests/app/host/HostAssemblyTests.cs`) to this spec. `ComposedSim` now
has exactly the two public constructors of `07` L10's "kept constructor"
clause: the eight-parameter one, `(…, IDelaySystem, IContentIndex)`, and
the earlier seven-parameter one. Its properties are `Host` to `Delay` plus
`Content`. `IPresentationComposer`'s methods are the two `Compose`
overloads (three and four parameters). The test's other pins are
unchanged: exported types, `HostFactory`'s methods, and the other
structs and interfaces. Every other merged host test is unchanged,
because the seven-argument constructor and the three-argument `Compose`
stay.

Host tests may use `float` only as `07` L4's `tests/app/host/` exception
allows (Q-116).

---

## 16.12 Open

- **Gate assignment** (`18` §18.5): deferred to the owner; Phase 0/1 pools
  gates.
- **§16.9 as a required check**: adopted as non-required (owner,
  2026-10-06, Q-115). Making it required, with any `02-determinism.md`
  row, is the owner's later decision.
- **Ignored build output** (§16.2): the owner adds
  `unity/AirportSim/Assets/StreamingAssets/`, its `.meta` and
  `unity/AirportSim/Packages/packages-lock.json` to `.gitignore`.
- **D10 values** (`15` §15.14): decided by the owner on 2026-09-27
  (Q-034): the low-end target (integrated graphics), the first-launch
  default (`Medium`) and the pause. The `Low` and `Medium` values and the
  2 GB memory budget (§16.10) are the Architect's proposals, marked LOW
  CONFIDENCE, for the owner to revise.
