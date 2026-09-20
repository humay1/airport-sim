# T-021 — One runway, taxiway graph, four contact stands

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.airside` |
| Assigned role | worker |
| Depends on | T-008 |
| Spec source | `spec/00-overview.md` build order #3; `spec/03-module-map.md` (module row only, no interface) |
| Blocked by | Q-005 (and transitively Q-004 via T-008) |

## Why this task cannot be released

`sim.airside` has no published interface (`spec/03-module-map.md`: "not yet
specified — a worker may not start without one"). See `spec/open-questions.md`
Q-005. It also depends on T-008, itself `BLOCKED` on Q-004.

## Writable paths (provisional, not yet binding)

```
src/sim/airside/**, tests/sim/airside/**
```

## Done when

- [ ] Q-005 answered
- [ ] T-008 unblocked and merged
- [ ] This file is rewritten with the real interface, events, tests and
      budget, then re-queued as QUEUED
