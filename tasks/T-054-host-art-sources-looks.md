# T-054 — `app.host`: sources and looks into the scene

| Field | Value |
|---|---|
| Status | MERGED (#146, tests #143, 2026-10-07) |
| Module | `app.host` |
| Assigned role | worker (after the Test Author) |
| Depends on | T-051 |
| Spec source | `spec/15-interfaces-render.md` §15.16, §15.18 "For the Planner" task 4 (Q-130); `spec/16-interfaces-host.md` §16.4, §16.5, §16.7, §16.11 |
| Blocked by | — |

## Writable paths

```
src/app/host/**
unity/AirportSim/Assets/Scripts/AirportSimBootstrap.cs
```

**Test Author's grant (separate, Q-021):** `tests/app/host/**`. The worker
writes nothing under `tests/`.

**A red path-guard check on the test-only PR is expected, not a defect.**
`ci/check-paths.sh` checks a `test-author/T-054-*` branch against the first
"Writable paths" block (the worker's), so a test-only PR shows path-guard
red. Such PRs never merge alone (the same pattern as T-049): the worker's
branch carries the Test Author's files byte-identical and that PR merges.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md` (L10's kept-constructor
clause), `spec/15-interfaces-render.md` §15.16, `spec/16-interfaces-host.md`
§16.4, §16.5, §16.7, §16.11

## Interface to implement

Exactly `spec/16-interfaces-host.md` §16.4, §16.5, §16.7 and §16.11: the
schedule and content sources and the looks passed into the scene, and the
bootstrap's `LoadLooks` call. Copy signatures from the spec; this file does
not restate them. The kept `RenderSources`, `RenderLayout` and `ComposedSim`
constructors (`07` L10), the two-argument `CreateSceneBuilder` and the
three-argument `Compose` all stay.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/app/host/**
```

Written by the Test Author before this task is released to the worker. The
Test Author updates the merged
`test_host_assembly_public_surface_matches_spec`
(`tests/app/host/HostAssemblyTests.cs`), which **breaks**: it pins
`ComposedSim` to one seven-parameter constructor and the properties `Host`
to `Delay`, and `IPresentationComposer`'s methods to one `Compose`. It is
updated as `16` §16.11 states: two constructors under `07` L10's
kept-constructor clause, `Content` added, and two `Compose` overloads. No
other merged `app.host` test changes (`15` §15.18). A version 2 fixture
still keeps the text `"stand_size": 40`, which a host test edits. New tests
(`16` §16.11):

- `test_compose_exposes_the_content_index`
- `test_presentation_render_sources_carry_schedule_and_content`
- `test_presentation_passes_looks_to_the_scene`

**Do not edit them.** If a test contradicts the spec, file an open question
and stop.

## Performance budget

As `16` §16.10 states; this task adds no per-frame work beyond passing the
sources and looks through.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass, including the updated public-surface test
- [ ] `unity-build` green (`16` §16.2)
- [ ] `ci/run-checks.sh` green
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

Depends on T-051. It may run in parallel with T-052 and T-053: no shared
path (`src/app/host/**` and `tests/app/host/**` against Art2D, `tests/app/render/**`
and `src/app/render/Unity/**`), and it does not write `AirportSim.sln`.
`AirportSimBootstrap.cs` is outside T-053's `src/app/render/Unity/**`.
