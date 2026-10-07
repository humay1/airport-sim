# T-052 — `app.render` 2D art (Art2D): atlas and tessellator

| Field | Value |
|---|---|
| Status | IN PROGRESS (T-051 merged #141; Test Author's PR #144 is open, being updated to Q-131; worker not released until the tests are approved) |
| Module | `app.render` (new headless assembly `AirportSim.App.Render.Art2D`) |
| Assigned role | worker (after the Test Author) |
| Depends on | T-051 (merged #141) |
| Spec source | `spec/15-interfaces-render.md` §15.15, §15.17, §15.17, §15.18 "For the Planner" task 2 (Q-130, Q-131); `spec/07-conventions.md` "Solution" and L1 |
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

Exactly `spec/15-interfaces-render.md` §15.17 as rewritten by Q-131: the
code-held art, the one rasterised atlas (4096 square, six mips, `Size`,
rects, `LayersOf`, `LogoRect`, `GroundLayer`) and the
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
§15.18, 2D art, as rewritten by Q-131). The Test Author's PR #144 was written
against Q-130 and is **in progress**; its author updates it to Q-131 (the
table of changed tests in `15` §15.18), and no worker edits them.

- `test_art2d_assembly_references` (unchanged)
- `test_art2d_atlas_shape_and_packing_match_spec` (changed: 4096 atlas, 6 mips)
- `test_art2d_atlas_is_deterministic` (changed only by the atlas size)
- `test_art2d_every_cell_is_drawn_inside_its_border` (changed)
- `test_art2d_aircraft_follow_the_proportion_table` (changed)
- `test_art2d_layers_match_the_visual_table` (changed)
- `test_art2d_tessellator_corners_follow_kind_facing_and_slicing` (changed)
- `test_art2d_tessellator_colours_follow_role_region_and_fixed` (changed)
- `test_art2d_tessellator_fill_within_budget_and_allocates_nothing` (changed:
  2.0 ms mean, 4.0 ms p99)
- New (Q-131): `test_art2d_cells_follow_the_value_and_alpha_rules`,
  `test_art2d_tiled_textures_follow_the_style_guide`,
  `test_art2d_tessellator_ground_tiles_follow_the_camera`,
  `test_art2d_tessellator_tiles_boxes_and_segments`

The art tests build the atlas (about 85 MiB) once per test class and share
it, except the determinism test (`15` §15.18). Static array fields are
forbidden in Art2D (`15` §15.3's 2D art rule).

**Do not edit them.** If a test contradicts the spec, file an open question
and stop.

## Performance budget

`test_art2d_tessellator_fill_within_budget_and_allocates_nothing` is the
check; the figures are `15` §15.17's, **raised by Q-131 from 1.5 ms mean
and 3.0 ms p99 to 2.0 ms mean and 4.0 ms p99**.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

**One task, not split (manager decision).** Q-131 roughly doubled the work:
the fills, noise and softness in the rasteriser, 15 more cells, the ground
and tile emission and the shift in the tessellator, and six detailed
aircraft. It stays a single task in a single assembly.

**Project file (`07` L1).** The Art2D csproj row is now in `07` L1:
`src/app/render/Art2D/AirportSim.App.Render.Art2D.csproj`, assembly
`AirportSim.App.Render.Art2D`, `netstandard2.1`, C# 9. The worker creates it
under `src/app/render/Art2D/**` and adds it to `AirportSim.sln`.

**Later task.** Q-132's M2 (T-057) edits `src/app/render/Art2D/**` again,
after T-056; this task does not implement elevation.

Depends on T-051 (merged #141; the `VisualId`, `Facing` and `Paint` types it consumes).
T-051 also writes `tests/app/render/**`, so release this only after T-051
has merged. T-053 depends on this task. T-054 may run in parallel, since it
shares no path (it does not write `AirportSim.sln`).
