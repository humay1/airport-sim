# T-009 — Run 100 sim-days in under 60s, identical across runs

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.core` (harness/integration) |
| Assigned role | worker |
| Depends on | T-006, T-007, T-008 |
| Spec source | `spec/00-overview.md` Phase 0 kill gate |
| Blocked by | Q-004 (transitively, via T-008) |

## Why this task cannot be released

This task depends on T-008, which is `BLOCKED` on Q-004 (`sim.schedule` has
no published interface — see `spec/open-questions.md`). A 100-sim-day run
needs a schedule to drive movements; without T-008 there is nothing to load.
No new open question is filed here since Q-004 already covers the root cause.

## Writable paths (provisional, not yet binding)

```
tools/SimHarness/**, tests/sim/core/**
```

## Done when

- [ ] T-008 unblocked and merged
- [ ] This file is rewritten with concrete fixture size, the exact assertion
      (identical checkpoint hash sequence, wall-clock ceiling measured per
      `spec/03-module-map.md` "How a budget is measured") and re-queued as
      QUEUED
