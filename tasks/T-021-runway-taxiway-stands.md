# T-021 — One runway, taxiway graph, four contact stands

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.airside` |
| Assigned role | worker |
| Depends on | T-008 |
| Spec source | `spec/00-overview.md` build order #3; `spec/03-module-map.md` (module row only, no interface) |
| Blocked by | Q-005 |

## Why this task cannot be released

`sim.airside` has no published interface (`spec/03-module-map.md`: "not yet
specified — a worker may not start without one"). See `spec/open-questions.md`
Q-005.

Update: Q-004 (`sim.schedule`) is now **ANSWERED**
(`spec/11-interfaces-schedule.md`), so T-008 itself is `QUEUED`, not
`BLOCKED`. T-021's dependency on T-008 is therefore an ordinary merge-order
dependency again, not a compounding block — Q-005 alone is what still stops
this task.

## Writable paths (provisional, not yet binding)

```
src/sim/airside/**, tests/sim/airside/**
```

## Done when

- [ ] Q-005 answered
- [ ] T-008 merged
- [ ] This file is rewritten with the real interface, events, tests and
      budget, then re-queued as QUEUED
