# T-008 — Schedule loader from CSV fixture, 200 movements

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.schedule` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/00-overview.md` build order #2; `spec/03-module-map.md` (module row only, no interface) |
| Blocked by | Q-004 |

## Why this task cannot be released

`spec/03-module-map.md` states plainly: for every module other than
`sim.core` and `sim.flow`, "not yet specified — a worker may not start
without one." `sim.schedule` has no published `08/09`-style interface file.
Without it there is no `IScheduleSystem` signature to copy verbatim, no
declared shape for the CSV fixture, and no declared contract for how
`sim.schedule` calls `IFlowSystem.Inject` or emits `FlightPlanPublished` /
`FlightMilestoneReached(PlanPublished)`. Filling any of that in here would be
inventing an interface, which `CLAUDE.md` rule 2 and `agents/planner.md`
both forbid.

See `spec/open-questions.md` Q-004 for the full question. This task file will
be completed and re-released once the Architect publishes
`spec/1x-interfaces-schedule.md` (or equivalent) and resolves Q-004.

## Writable paths (provisional, not yet binding)

```
src/sim/schedule/**, tests/sim/schedule/**
```

## Done when

- [ ] Q-004 answered in `spec/open-questions.md`
- [ ] This file is rewritten with the real interface, events, tests and
      budget, then re-queued as QUEUED
