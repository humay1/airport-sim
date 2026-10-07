# T-052 — `app.render` 2D art (Art2D): atlas and tessellator

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-051 merges) |
| Module | `app.render` (new headless assembly `AirportSim.App.Render.Art2D`) |
| Assigned role | worker (after the Test Author) |
| Depends on | T-051 |
| Spec source | `spec/15-interfaces-render.md` §15.15, §15.17, §15.18 "For the Planner" task 2 (Q-130); `spec/07-conventions.md` "Solution" |
| Blocked by | — |

## Writable paths

```
src/app/render/Art2D/**
AirportSim.sln
```

**Test Author's grant (separate, Q-021):** `tests/app/render/**`, which adds
the test project's reference to the Art2D project. The worker writes nothing
under `tests/`.

**Single `.sln` writer.** This task adds the one new project (`07`
"Solution"). Never release it alongside another task that edits
`AirportSim.sln`.

**A red path-guard check on the test-only PR is expected, not a defect.**
`ci/check-paths.sh` checks a `test-author/T-052-*` branch against the first
"Writable paths" block (the worker's), so a test-only PR shows path-guard
red. Such PRs never merge alone (the same pattern as T-049): the worker's
branch carries the Test Author's files byte-identical and that PR merges.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md`,
`spec/15-interfaces-render.md` §15.15 to §15.18

## Interface to implement

Exactly `spec/15-interfaces-render.md` §15.17: the code-held art, the one
rasterised atlas (`Size`, mips, rects, `LayersOf`, `LogoRect`) and the
tessellator that turns a frame into tinted quads. Copy signatures from the
spec; this file does not restate them. The scene assembly must not reference
`AirportSim.App.Render.Art2D`, and Art2D has no engine reference.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/app/render/**
```

Written by the Test Author before this task is released to the worker (`15`
§15.18, 2D art):

- `test_art2d_assembly_references`
- `test_art2d_atlas_shape_and_packing_match_spec`
- `test_art2d_atlas_is_deterministic`
- `test_art2d_every_cell_is_drawn_inside_its_border`
- `test_art2d_aircraft_follow_the_proportion_table`
- `test_art2d_layers_match_the_visual_table`
- `test_art2d_tessellator_corners_follow_kind_facing_and_slicing`
- `test_art2d_tessellator_colours_follow_role_region_and_fixed`
- `test_art2d_tessellator_fill_within_budget_and_allocates_nothing`

**Do not edit them.** If a test contradicts the spec, file an open question
and stop.

## Performance budget

`test_art2d_tessellator_fill_within_budget_and_allocates_nothing` is the
check; the figures are `15` §15.17's.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

Depends on T-051 (the `VisualId`, `Facing` and `Paint` types it consumes).
T-051 also writes `tests/app/render/**`, so release this only after T-051
has merged. T-053 depends on this task. T-054 may run in parallel, since it
shares no path (it does not write `AirportSim.sln`).
