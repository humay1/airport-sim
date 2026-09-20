# T-022 — Turnaround as job list, 4 vehicles, driver assignment

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.turnaround` |
| Assigned role | worker |
| Depends on | T-021 |
| Spec source | `spec/00-overview.md` build order #5; `spec/03-module-map.md` (module row only, no interface) |
| Blocked by | Q-006 (and transitively Q-005 via T-021) |

## Why this task cannot be released

`sim.turnaround` has no published interface (`spec/03-module-map.md`: "not
yet specified — a worker may not start without one"). See
`spec/open-questions.md` Q-006. It also depends on T-021, itself `BLOCKED`
on Q-005.

## Writable paths (provisional, not yet binding)

```
src/sim/turnaround/**, tests/sim/turnaround/**
```

## Done when

- [ ] Q-006 answered
- [ ] T-021 unblocked and merged
- [ ] This file is rewritten with the real interface, events, tests and
      budget, then re-queued as QUEUED
