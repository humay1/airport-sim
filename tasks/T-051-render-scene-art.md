# T-051 — `app.render` scene layer: visuals, facing, paint, markings, scenery and looks

| Field | Value |
|---|---|
| Status | QUEUED (releasable now: Test Author first, then worker) |
| Module | `app.render` (scene layer only) |
| Assigned role | worker (after the Test Author) |
| Depends on | none unmerged (T-020 merged #111, T-029 merged #118, T-031 merged #130) |
| Spec source | `spec/15-interfaces-render.md` §15.15, §15.16, §15.18 "For the Planner" task 1 (Q-130); `spec/16-interfaces-host.md` §16.4, §16.5 |
| Blocked by | — |

## Writable paths

```
src/app/render/Scene/**
data/looks/**
data/schemas/looks.schema.json
```

**Test Author's grant (separate, Q-021):** `tests/app/render/**` and
`tests/fixtures/render/**`. The worker writes nothing under `tests/`.

`src/app/render/Scene/**` includes the scene project's reference to
`sim.schedule` (`15` §15.18 task 1). No new project and no `.sln` edit.

**A red path-guard check on the test-only PR is expected, not a defect.**
`ci/check-paths.sh` checks a `test-author/T-051-*` branch against the first
"Writable paths" block (the worker's), so a test-only PR shows path-guard
red. Such PRs never merge alone (the same pattern as T-049): the worker's
branch carries the Test Author's files byte-identical and that PR merges.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/04-data-schemas.md`, `spec/07-conventions.md`
(including L10's kept-constructor clause), `spec/15-interfaces-render.md`
§15.4, §15.5, §15.15 to §15.18, `spec/16-interfaces-host.md` §16.4, §16.5

## Interface to implement

Exactly the scene-layer surface of `spec/15-interfaces-render.md` §15.16 and
the layout scenery of §15.4: the semantic `VisualId` of each primitive, its
engine-free integer `Facing`, its `Paint` regions, the generated scenery and
markings (runway, taxiway, stand lead-ins and numbers, areas and bridges),
`data/looks/looks.json` with its loader and `DefaultLooks()`, and the
version 2 layout. Copy the signatures from the spec; this file does not
restate them. Nothing in the scene layer, the layout or `data/` names an
atlas, a cell, a texture coordinate or a sprite (§15.15).

## Events

Emitted: none. Consumed: none (as the merged scene layer).

## Tests to pass

```
tests/app/render/**
tests/fixtures/render/**
```

Written by the Test Author before this task is released to the worker. New
tests (`15` §15.18):

- `test_scene_visuals_follow_the_draw_table`
- `test_scene_scenery_follows_the_layout`
- `test_scene_aircraft_facing_follows_the_five_rules`
- `test_scene_runway_markings_follow_the_integer_rule`
- `test_scene_taxiway_junction_fill_and_centrelines`
- `test_scene_stand_lead_in_and_numbers`
- `test_scene_aircraft_visual_follows_size_category`
- `test_scene_aircraft_paint_follows_airline_livery`
- `test_scene_passenger_paint_is_a_fixed_hash_of_the_agent`
- `test_render_looks_load_the_file_and_reject_each_fault`
- `test_render_looks_defaults_match_spec`
- `test_render_layout_version_2_loads_scenery_and_rejects_faults`

Merged tests that Q-130 changes, updated by this task's Test Author (no
worker edits a test):

- **Break:**
  - `test_scene_runway_colour_follows_queue_length_and_taxiways_follow_edges`
    counts the `TaxiEdge`-sourced primitives instead of the `Taxiway`
    layer, and asserts their `TaxiwaySurface` visual.
  - `test_scene_stand_colour_follows_occupancy` (`SceneTests.cs:29`) counts
    the `SourceKind.Stand`-sourced primitives (2) instead of the `Stand`
    layer, and asserts their `StandPad` visual.
- **Extended:**
  - `test_scene_calls_only_listed_sim_members`: max-tier fakes gain a
    guarded schedule and content index; content is read only at
    construction.
  - `test_scene_build_within_frame_budget_at_max_tier` and
    `test_scene_build_and_update_allocate_nothing_after_first_call`: the
    max-tier scene gains the schedule, content and looks fakes and a
    version 2 layout with scenery.
  - `test_render_layout_fixture_file_equals_built_layout`: the kit's
    code-built layout (`Phase1RenderLayout`) gains the fixture's areas and
    bridges.
- **Covered by the kit** (no test code change):
  `test_scene_gameplay_primitives_identical_at_every_graphics_setting` and
  `test_scene_primitive_order_is_stable`, because `Prims.Show` gains
  `Visual`, `Facing` and `Paint`.

The Test Author also makes `tests/fixtures/render/phase1-layout.json`
version 2 with the areas and bridges lists of `15` §15.18 "Fixture", and no
other value changes. It is also the playtest bundle's
`render_layout.fixture` (`16` §16.3).

**Do not edit the tests.** If a test contradicts the spec, file an open
question and stop.

## Performance budget

As the merged scene layer: `15` §15.14 and the `03` budget rule. The
frame-budget and zero-allocation tests above are the check.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass, including the broken and extended merged tests
- [ ] `data/looks/looks.json` validates against `data/schemas/looks.schema.json` through `ci/validate-content.py`
- [ ] Determinism gate passes (`ci/run-checks.sh` green)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

**Merge-order hazard, binding.** Once this merges, the merged Unity backend
(`RoleCount = (int)ColourRole.LaneClosed + 1` at `RenderBackend.cs:14`)
throws on every frame of a playable build. T-053 closes it and must merge as
soon as it is green (`15` §15.18). The checkpoint smoke is unaffected (`16`
§16.7).

No older primitive's geometry, colour, layer or source changes; aircraft keep
diameter `AircraftSize` (`15` §15.15). The scene layer stays engine-free.
T-052 and T-054 depend on this task and may not start before it merges. T-052
also writes `tests/app/render/**`, so the two never run together.
