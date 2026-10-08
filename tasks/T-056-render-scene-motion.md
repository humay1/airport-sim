# T-056 — `app.render` scene layer: motion, approaches and walkers (M1)

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-052 and T-055 merge) |
| Module | `app.render` (scene layer only) |
| Assigned role | worker (after the Test Author) |
| Depends on | T-052, T-055 |
| Spec source | `spec/15-interfaces-render.md` §15.19 to §15.23, "For the Planner" task M1 (Q-132) |
| Blocked by | — |

## Writable paths

```
src/app/render/Scene/**
```

**Test Author's grant (separate, Q-021):** `tests/app/render/**` and
`tests/fixtures/render/**`. The worker writes nothing under `tests/`.

**Ordering.** It depends on T-052 because both write `tests/app/render/**` and
its shared kit, and M1 changes `DrawPrimitive`, which T-052's tests construct.
It depends on T-055 because the arrival rollout reads `RunwayDef.ExitNode`.
Never release it beside T-057 (same test paths; T-057 depends on it).

**A red path-guard check on the test-only PR is expected, not a defect** (same
pattern as T-049).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md`,
`spec/12-interfaces-airside.md` §12.4, §12.13,
`spec/15-interfaces-render.md` §15.19 to §15.23, `spec/19-interfaces-harness.md` §19.2c

## Interface to implement

Exactly `spec/15-interfaces-render.md` §15.19 to §15.22: the tick pacer's
sub-tick, the `Build` overload with the sub-tick, taxi gliding, approach,
hold, landing and takeoff positions, `DrawPrimitive.Elevation`, layout
version 3 (walkways and bridge stands), walkway agents and bridge walkers.
Copy signatures from the spec; this file does not restate them.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/app/render/**
tests/fixtures/render/**
```

Written by the Test Author (`15` §15.23; tolerance 0.01 world units for motion
and `Elevation`, §15.22's corners keep 1e-3):

- `test_tick_pacer_sub_tick_is_the_remainder`
- `test_scene_build_rejects_sub_tick_out_of_range`
- `test_scene_two_argument_build_equals_sub_tick_zero`
- `test_scene_rebuilds_when_sub_tick_changes`
- `test_scene_taxiing_aircraft_glides_with_sub_tick`
- `test_scene_arrival_appears_in_the_approach_window`
- `test_scene_held_arrival_flies_the_square_hold`
- `test_scene_arrival_lands_and_rolls_out_to_the_exit_node`
- `test_render_playtest_layout_extends_the_phase1_layout`
- `test_scene_departure_rolls_lifts_off_and_climbs`
- `test_scene_off_graph_aircraft_without_a_runway_frame_is_not_drawn`
- `test_scene_elevation_is_zero_except_airborne_aircraft`
- `test_render_layout_version_3_loads_walkways_and_bridge_stands`
- `test_scene_walkway_agents_follow_their_cohort_progress`
- `test_scene_bridge_walkers_follow_the_boarding_flight`
- extended: `test_scene_calls_only_listed_sim_members` (`TryGetCohort`,
  `PopulationForFlight`, `TryGetOutstanding`),
  `test_scene_gameplay_primitives_identical_at_every_graphics_setting`
  (several sub-ticks; `Prims.Show` gains `Elevation`),
  `test_render_loop_is_outcome_neutral_with_scripted_camera` and
  `test_render_loop_is_outcome_neutral_across_graphics_changes` (pacer
  sub-tick, version 3 fixture, every walkway node a `corridor`),
  `test_scene_build_within_frame_budget_at_max_tier` and
  `test_scene_build_and_update_allocate_nothing_after_first_call` (max-tier
  scene gains 20 approaching, 5 held and 3 runway aircraft, a walkway on each
  of the 16 promoted nodes, a bridge with boarders on every stand; every
  frame rebuilds)

Merged tests that Q-132 changes, **updated by the Test Author, never by the
worker**: `test_render_layout_version_2_loads_scenery_and_rejects_faults`
(fixture text moves to `"schema_version": 3`),
`test_render_layout_fixture_file_equals_built_layout` (the kit's
`Phase1RenderLayout` gains walkways and stands; the kit's flow fake answers
the three new members), and `test_scene_aircraft_off_graph_is_not_drawn`
(extended with an `AwaitingApproach` arrival outside its window).

**Fixtures (Test Author):** `tests/fixtures/render/phase1-layout.json`
becomes version 3 (bridges 1 to 4 gain `"stand"` 1 to 4; walkways on nodes 4
and 7 as in `15` §15.23; the text `"stand_size": 40` stays), and a new
`tests/fixtures/render/playtest-layout.json` is it plus three `taxi_nodes`
entries (nodes 4, 5, 6 at (-1850, 0), (-1850, -90), (0, -90)). Its test loads
it against `12` §12.13's fixture with `19` §19.2c's exit lines added in code,
since the playtest airside file changes only in T-058.

## Performance budget

Scene build within the existing frame budget at max tier with the extended
scene, and allocation-free after the first call (the two extended tests).

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

The host still calls the two-argument `Build` until T-058 merges, so a build
shows approaches, takeoffs and walkers at whole ticks only. T-057 (Art2D
elevation) and T-058 depend on this task.

**BLOCKED (worker, 2026-10-08), Q-134.** `test_scene_taxiing_aircraft_glides_with_sub_tick`
asserts an exact `(30, 12)` at `α = 0` for `EdgeProgress = FromRatio(6, 20)` (line 246);
§15.20's literal float rule, and the merged code, give `(30.000002, 12)`. Everything else
passes (86 of 87 in `tests/app/render`). Waiting for the Architect's answer; no workaround built.
