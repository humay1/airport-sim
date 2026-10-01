# T-040 — Tag six merged tests `Slow` (L11a rule (b))

| Field | Value |
|---|---|
| Status | QUEUED (released by the owner 2026-09-29, together with T-039 and T-009) |
| Module | `sim.flow` tests, `sim.schedule` tests |
| Assigned role | test-author |
| Depends on | none open (T-007, T-010, T-011, T-008 merged; L11a merged in PR #61, `80f8baa`) |
| Spec source | `spec/07-conventions.md` L11a "Slow tests"; `spec/CHANGELOG.md` 2026-09-29 entry "the Slow test category" (Impact list) |
| Blocked by | — |

## Writable paths

```
tests/sim/flow/**
tests/sim/schedule/**
```

No other file. No `src/`, `spec/`, `ci/` or `.github/` edit. The Test Author
opens its own PR.

## Why this task exists

L11a's CHANGELOG entry names six merged tests that a laptop run put over the
5 s line of rule (b), and says a Test Author task must add the trait because
workers never edit tests. CI's own durations decide afterwards.

## What this task does

Add `[Trait("Category", "Slow")]` to exactly these six test methods, and
nothing else:

| Test method | File |
|---|---|
| `PromotionBudgetTests.test_promotion_budget_one_day_with_every_node_promoted` | `tests/sim/flow/PromotionBudgetTests.cs` |
| `FlowHeadlessDayTests.test_flow_headless_day_keeps_every_invariant` | `tests/sim/flow/FlowHeadlessDayTests.cs` |
| `FlowStressBudgetTests.test_flow_stress_30k_day_update_path_allocates_nothing` | `tests/sim/flow/FlowStressBudgetTests.cs` |
| `FlowStressBudgetTests.test_flow_stress_30k_day_tick_within_budget_and_bounded_cohorts` | `tests/sim/flow/FlowStressBudgetTests.cs` |
| `RoutingCacheTests.test_flow_routing_cache_matches_uncached_reference` | `tests/sim/flow/RoutingCacheTests.cs` |
| `ScheduleHashTests.test_schedule_hash_identical_with_and_without_flow_registered` | `tests/sim/schedule/ScheduleHashTests.cs` |

- No test logic, assertion, name or budget value changes.
- A test that already carries a `Budget` trait keeps it; the two traits
  combine (L11a).
- Tag the method, not the class.

## Tests to pass

This task is the Test Author's own change; there is no separate test grant.

```
tests/sim/flow/**
tests/sim/schedule/**
```

## Performance budget

Not applicable. No timed or budgeted path changes; `ci/run-checks.sh` and
`tools/SimHarness budget` are unchanged by Slow (L11a).

## Done when

- [ ] The six methods above, and only those, carry `[Trait("Category", "Slow")]`
- [ ] Every `Budget` trait that was there is still there
- [ ] Diff contains only added trait lines (plus `using` if absent)
- [ ] PR CI (`SKIP_SLOW=1`) skips the six tests
- [ ] `gh workflow run slow-tests.yml --ref <branch>` on the PR head, which
      contains the current `main`, runs exactly these six and passes
- [ ] No writes outside `tests/sim/flow/**` and `tests/sim/schedule/**`
- [ ] Reviewer approved

## Worker notes

No worker: test-only. Textual conflict risk with T-039's Test Author, who is
adding a new file in `tests/sim/flow/`, is low because this task edits only
existing files. T-009 writes `tests/tools/simharness/**`, `tests/fixtures/harness/**` and
`tools/SimHarness/**` (synced to `19` §19.6): no overlap. If a method name is not found on `main`, stop and report; do not
tag a similar test.
