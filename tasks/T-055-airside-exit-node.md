# T-055 — `sim.airside`: the runway exit node (A1)

| Field | Value |
|---|---|
| Status | QUEUED (releasable now: Test Author first, then worker) |
| Module | `sim.airside` |
| Assigned role | worker (after the Test Author) |
| Depends on | none unmerged |
| Spec source | `spec/12-interfaces-airside.md` §12.3, §12.4 (`ExitNode`), §12.6, §12.7, §12.8a, §12.9, §12.11, §12.13; `spec/15-interfaces-render.md` §15.23 "For the Planner" task A1 (Q-132) |
| Blocked by | — |

## Writable paths

```
src/sim/airside/**
```

**Test Author's grant (separate, Q-021):** `tests/sim/airside/**`. The worker
writes nothing under `tests/`. No fixture file changes, so every merged run is
byte-identical.

**Reviewed by reviewer-core.** This is determinism-relevant sim code.

**A red path-guard check on the test-only PR is expected, not a defect** (the
same pattern as T-049): the worker's branch carries the Test Author's files
byte-identical and that PR merges.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/07-conventions.md`,
`spec/12-interfaces-airside.md`, `spec/15-interfaces-render.md` §15.23

## Interface to implement

Exactly `spec/12-interfaces-airside.md` §12.4's `ExitNode` (the optional
`exit_node` key, the kept constructor, checks 4, 5 and 7, and routing) and its
use in §12.3, §12.6, §12.7, §12.8a, §12.9 and §12.11. Copy signatures from the
spec; this file does not restate them.

## Events

Emitted: none new. Consumed: none new.

## Tests to pass

```
tests/sim/airside/**
```

Written by the Test Author before release (`12` §12.13, task A1, all except
the playtest-fixture one):

- `test_layout_exit_node_defaults_to_threshold`
- `test_layout_parse_reads_optional_exit_node`
- `test_layout_rejects_bad_exit_node`
- `test_arrival_leaves_runway_at_exit_node_and_taxis_from_it`
- `test_layout_without_exit_node_runs_byte_identical`

`test_playtest_bundle_lands_arrivals_at_the_far_exit` belongs to T-058 (M3),
not here. **Do not edit the tests.**

## Performance budget

None new; the module's existing budget stays met.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] Determinism gate green (`ci/run-checks.sh`)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer (reviewer-core) approved

## Worker notes

The Phase 1 checkpoints fixture does **not** change here: it changes only in
T-058, together with the playtest render layout (`12` §12.13 determinism
note). T-056 (M1) depends on this task, because the arrival rollout reads
`RunwayDef.ExitNode`.

## Blocker (worker, 2026-10-07)

Q-133 filed: two tests compare airside hashes across layouts with different edge counts; the hashed edge list makes that impossible. Implementation otherwise done; 125/127 airside tests pass.
