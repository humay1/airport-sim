# T-057 — `app.render` 2D art: elevation (M2)

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-056 merges) |
| Module | `app.render` (Art2D) |
| Assigned role | worker (after the Test Author) |
| Depends on | T-056 |
| Spec source | `spec/15-interfaces-render.md` §15.22 (elevation in `Fill`), §15.23 "For the Planner" task M2 (Q-132) |
| Blocked by | — |

## Writable paths

```
src/app/render/Art2D/**
```

**Test Author's grant (separate, Q-021):** `tests/app/render/**`. The worker
writes nothing under `tests/`. **No `.sln` edit** (T-052 added the project).

**A red path-guard check on the test-only PR is expected, not a defect** (same
pattern as T-049).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md`,
`spec/15-interfaces-render.md` §15.17, §15.22, §15.23

## Interface to implement

Exactly `spec/15-interfaces-render.md` §15.22's elevation rule in the
tessellator: it reads `DrawPrimitive.Elevation` (added by T-056). With `e = 0`
and for every non-aircraft visual, `Fill` is exactly §15.17's, byte for byte.
It adds no quad, no atlas cell, no layer and no `VisualId`. Copy the rule from
the spec; this file does not restate it.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/app/render/**
```

Written by the Test Author (`15` §15.23, 2D art):

- `test_art2d_airborne_aircraft_scaled_with_a_ground_shadow`
- `test_art2d_elevation_zero_or_non_aircraft_is_unchanged`
- extended: `test_art2d_tessellator_fill_within_budget_and_allocates_nothing`
  with 25 airborne aircraft (Q-131's 2.0 ms mean and 4.0 ms p99 stand)

Motion and elevation values compare within 0.01 world units (`15` §15.23).

## Performance budget

2.0 ms mean and 4.0 ms p99 (`15` §15.17), with 25 airborne aircraft added.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

T-053 (Unity backend) does not wait for this task and needs no follow-up,
because M2 adds no quad (`15` §15.23).
