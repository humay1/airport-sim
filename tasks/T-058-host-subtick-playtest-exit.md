# T-058 — `app.host`: the sub-tick into `Build`, and the playtest's exit (M3)

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-055 and T-056 merge; T-054 is merged) |
| Module | `app.host` |
| Assigned role | worker (after the Test Author) |
| Depends on | T-055, T-056, T-054 (merged #146) |
| Spec source | `spec/15-interfaces-render.md` §15.23 "For the Planner" task M3 (Q-132); `spec/16-interfaces-host.md` §16.3, §16.6; `spec/12-interfaces-airside.md` §12.13; `spec/19-interfaces-harness.md` §19.2c |
| Blocked by | — |

## Writable paths

```
src/app/host/**
unity/AirportSim/Assets/Editor/PlaytestBundleBuildStep.cs
```

`PlaytestBundleBuildStep.cs`'s `render_layout.fixture` source becomes
`playtest-layout.json` (`16` §16.3, §16.6).

**Test Author's grant (separate, Q-021):** `tests/app/host/**` and
`tests/fixtures/harness/checkpoints-phase1/airside.fixture` (`19` §19.2c's
exit lines). The worker writes nothing under `tests/`.

**Atomic change.** The airside fixture and the render-layout switch change
**together, in one PR**: either alone makes the playable build fail §15.4
check 4 at presentation assembly. This PR changes the Phase 1 checkpoints dump
(`12` §12.13 determinism note), so **reviewer-core reviews the fixture
change**. It writes the same host paths as T-054, now merged; never release it
beside another `app.host` task.

**A red path-guard check on the test-only PR is expected, not a defect** (same
pattern as T-049).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md`,
`spec/12-interfaces-airside.md` §12.13, `spec/15-interfaces-render.md` §15.23,
`spec/16-interfaces-host.md` §16.3, §16.6, `spec/19-interfaces-harness.md` §19.2c

## Interface to implement

The frame loop builds with the pacer's sub-tick (the three-argument `Build`,
`15` §15.19), and the playtest bundle switches to the exit-node airside
fixture and `playtest-layout.json`. Copy the behaviour from the spec; this
file does not restate it.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/app/host/**
```

Written by the Test Author (`15` §15.23, Host):

- `test_frame_loop_builds_with_the_pacer_sub_tick` (paused frames included)
- `test_playtest_bundle_lands_arrivals_at_the_far_exit` (also `12` §12.13):
  the bundle's `airside.fixture` parses with `ExitNode` 4 and §19.2c's lines,
  and the presentation composes with it and `playtest-layout.json`
- merged `FrameLoopTests` that compare with a two-argument reference `Build`
  are updated to the three-argument one (by the Test Author)

## Performance budget

None new.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green, Phase 1 checkpoints tests green
- [ ] `unity-build` green (`16` §16.2)
- [ ] No writes outside writable paths
- [ ] Reviewer approved; reviewer-core approved the fixture change

## Worker notes

Until this task merges, a playable build shows approaches, takeoffs and
walkers at whole ticks only: the host still calls the two-argument `Build`.
