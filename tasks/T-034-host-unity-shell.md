# T-034 — `app.host`: Unity project shell, bootstrap, playtest bundle

| Field | Value |
|---|---|
| Status | QUEUED (extends committed skeleton `unity/AirportSim/` from c48e163, does not create it; rescoped 2026-10-06 per Q-114, `16` §16.2/§16.3) |
| Module | `app.host` (Unity project shell) |
| Assigned role | worker |
| Depends on | T-027, T-028, T-031, T-032, T-033 |
| Spec source | `spec/16-interfaces-host.md` §16.1, §16.2, §16.3 (playtest-bundle table, incl. `world.fixture`), §16.7 |
| Blocked by | — |

## Writable paths

```
unity/AirportSim/**
```

Including the bootstrap script, the playtest `bundle.json`
(`unity/AirportSim/Scenario/bundle.json`) and the editor build step under
`unity/AirportSim/Assets/Editor/` (Q-114, `16` §16.3). Not behaviour-tested in
CI (`16` §16.2); `unity-build` compiles it.

**Scope (Q-114).** Extend the committed skeleton, add the bootstrap, and add
the editor build step. The build step is an editor build callback that runs at
the start of every player build, before streaming assets are collected: it
deletes and recreates `Assets/StreamingAssets/Scenario/` and
`Assets/StreamingAssets/Content/`, copies each row of `16` §16.3's table into
the first by its exact name and every file under `data/` into the second, finds
sources relative to the repository root, and fails the build naming any missing
source. After the last copy, before it returns, it calls
`AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)` so the
player build collects the files just copied (`16` §16.3, Q-124). It is the
only code that copies them.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/16-interfaces-host.md`, `spec/11-interfaces-schedule.md` §11.10,
`spec/12-interfaces-airside.md` §12.13, `spec/13-interfaces-turnaround.md`
§13.11, `spec/15-interfaces-render.md` §15.12, `spec/18-interfaces-world.md`
§18.6, `spec/04-data-schemas.md`

## Content to build

Binding, copied from `spec/16-interfaces-host.md` §16.2, §16.3, §16.7, not
paraphrased:

- **One Unity 6 LTS version**, pinned in `ProjectSettings/ProjectVersion.txt`.
  Changing it later is recorded in `CHANGELOG.md` like a spec change.
- **Scripting backend: Mono**, for every player target (D1's "the shipped
  runtime"). **API compatibility level: .NET Standard 2.1.**
- **Plugins are the tested binaries.** The sim, the `app.render` and
  `app.ui` scene layers and the headless host (T-031) are consumed as
  precompiled plugins from the Release `dotnet build` of `AirportSim.sln`,
  copied by this task's build step into
  `Assets/Plugins/AirportSim/`. Build outputs, never committed. Unity never
  recompiles them from source.
- **Backends by reference** (T-032, T-033), not by copy — mechanism (e.g.
  local packages) is this task's choice.
- **One scene**, `Assets/Scenes/Main.unity`, holding the bootstrap and the
  backends' components.
- **Players:** Windows and Linux desktop, macOS best-effort.
- **The Unity bootstrap** (§16.7): at scene start, build an
  `IScenarioBundle` over `StreamingAssets/Scenario/`, call
  `ISimComposer.Compose` then `IPresentationComposer.Compose`, keep the
  `IFrameLoop`. Each engine frame: get this frame's `CameraView` and
  `UiInput`s from the backends, read the screen size, convert the frame
  delta to integer microseconds, call `RunFrame`, pass `Render`/`Ui` to the
  backends to draw. Batch mode: pass process args to
  `IHostCommandLine.TryParse` (the engine's arguments with the first, the
  executable, removed); if a checkpoint run, replace `System.Console.Error`
  via `Console.SetError` with a `TextWriter` that forwards each completed
  line, without its line break, to `Debug.LogError`, call
  `IHeadlessRun.Run`, restore the original `Console.Error`, then call
  `Application.Quit` with `Run`'s exit code (`16` §16.7, Q-120); the writer
  holds no logic beyond splitting lines. If `TryParse` returns false, quit
  with exit code 2 (Q-114). Either way no frame loop, nothing drawn. The bootstrap
  calls no sim member itself, never branches on sim state, never reads a
  bundle file directly.
- **The Phase 1 playtest bundle** (§16.3): `unity/AirportSim/Scenario/`
  holds only the committed `bundle.json` and is not a complete bundle. The
  build step assembles `Assets/StreamingAssets/Scenario/` with one file for
  each row of §16.3's playtest-bundle table, under its exact name:
  `bundle.json`; `world.fixture` (`18` §18.6,
  `tests/fixtures/world/phase0-landside.json`); `schedule.csv` (`11`
  §11.10, `tests/fixtures/schedule/phase0-200.csv`); `airside.fixture` (`19`
  §19.2c, `tests/fixtures/harness/checkpoints-phase1/airside.fixture`, Q-113);
  `airside_rules.json` (the human-authored `data/balance/airside_rules.json`,
  D6, **not** written by this task); `turnaround.fixture` (`13` §13.11,
  `tests/fixtures/turnaround/phase1-five-vehicles.json`); `flow.fixture` (the
  `sim.flow` fixture T-023 runs); and `render_layout.fixture` (`15` §15.12,
  under `tests/fixtures/render/`). It also copies `data/` into
  `Assets/StreamingAssets/Content/`. Both directories are build output — the fixtures stay test fixtures beside their tests, never
  moved into `data/`.
- **Content for the player**: a copy of `data/` in
  `Assets/StreamingAssets/Content/` through
  `HostFactory.LoadContent`, feeding T-027's loader — this is why this task
  depends on T-027 (the loader) and T-028 (schemas plus size-category and
  aircraft data). `pax_profiles`/`queue_profiles` data and
  `data/balance/**` are human-authored and are assumed present, not
  written by this task.

- **The player and its staged files (`16` §16.9, Q-124).** The Linux player
  is `build/StandaloneLinux64/AirportSim.x86_64` (`unity-builder`'s
  `buildName` plus `.x86_64`), with its data in `AirportSim_Data/`, so the
  staged streaming assets are under
  `build/StandaloneLinux64/AirportSim_Data/StreamingAssets/`
  (`Scenario/` and `Content/`). Those staged copies are what the
  `cross-runtime` job reads. A `missing` failure or a harness load failure
  on the staged files points at this task's build step, which this task
  fixes within the spec; if the fix needs anything the spec does not say
  (another staged path, a filter on `.meta` files, a workflow change), file
  a spec question in `spec/open-questions.md` and stop (§16.9 "When it
  fails", §16.11, Q-124). The workflow `.github/workflows/unity.yml` is
  owner-owned; this task writes nothing in it.

## Tests to pass

None in CI (`16` §16.2: "Tested in CI: no, except through §16.9 if that
gate is adopted"). Reviewer checks the bootstrap against §16.7's contract
line by line, the same posture as T-032/T-033.

## Performance budget

Not budgeted here beyond `app.host`'s own (§16.10: nothing added to the
sim's 6 ms; `RunFrame`'s cost is T-031's).

## Done when

- [ ] Project settings match §16.2 exactly (Unity version pinned, Mono,
      .NET Standard 2.1, plugins never recompiled)
- [ ] Bootstrap matches §16.7's contract line by line
- [ ] Playtest bundle names every file `bundle.json` lists and none it
      does not (all eight files of the §16.3 table, `world.fixture` included)
- [ ] No write to `data/pax_profiles/**`, `data/queue_profiles/**` or
      `data/balance/**`
- [ ] Editor build step under `Assets/Editor/` assembles both StreamingAssets directories (`16` §16.3)
- [ ] `unity-build` green with the smoke step run, not skipped (`16` §16.11)
- [ ] Build step calls `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)` after its last copy (`16` §16.3, Q-124)
- [ ] Batch mode forwards `Console.Error` to `Debug.LogError` around `Run`, restores it, then `Application.Quit(code)` (`16` §16.7, Q-120)
- [ ] The non-required `cross-runtime` job (`16` §16.9, Q-115; already in `.github/workflows/unity.yml` on main) is **run, not skipped**, on the PR, with its result line quoted in the PR. It compares the player's checkpoint dump with the harness dump byte for byte, on PRs and nightly. It is NOT a required check and is not part of "`unity-build` green": T-034 must make a `missing` or staged-load failure pass within the spec, but a red result does not block merging, and a dump difference between two dumps that both loaded and ran is a determinism defect reported to the owner, not fixed here (`16` §16.11)
- [ ] Reviewer approved

## Worker notes

This is the last task before a playable build exists, and it is the one
place Unity itself is touched. Everything with a decision in it lives
upstream (T-031's headless host, T-032/T-033's backend contracts) — if
writing this task tempts a design choice that is not already in `16` §16.2/
§16.7, stop and file a question rather than deciding it inside the Unity
project, where it cannot be reviewed as code.
