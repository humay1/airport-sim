# T-022 — Turnaround as job list, 4 vehicles, driver assignment

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.turnaround` |
| Assigned role | worker |
| Depends on | T-021 |
| Spec source | `spec/00-overview.md` build order #5; `spec/03-module-map.md` (module row only, no interface) |
| Blocked by | Q-006 |

## Why this task cannot be released

`sim.turnaround` has no published interface (`spec/03-module-map.md`: "not
yet specified — a worker may not start without one"). See
`spec/open-questions.md` Q-006.

Update: Q-005 (`sim.airside`) is now **ANSWERED**
(`spec/12-interfaces-airside.md`), so T-021 itself is `QUEUED`, not
`BLOCKED`. T-022's dependency on T-021 is therefore an ordinary merge-order
dependency again, not a compounding block — Q-006 alone is what still stops
this task.

`spec/12-interfaces-airside.md` §12.8 already fixes one piece of T-022's
eventual interface in advance, binding on whatever the Architect publishes
for Q-006: `sim.turnaround` must emit `FlightMilestoneReached` for
`DeboardComplete`, `ReadyToBoard` and `BoardingComplete` only — **not**
`DoorsOpen`/`DoorsClosed`/`Pushback`, which belong to `sim.airside` — and
`sim.airside` waits on `BoardingComplete` specifically to close the doors.
Whatever Q-006's answer names as `ITurnaroundSystem`'s job-completion event
must be compatible with that handshake, or `sim.airside`'s spec needs a
follow-up amendment.

## Writable paths (provisional, not yet binding)

```
src/sim/turnaround/**, tests/sim/turnaround/**
```

## Done when

- [ ] Q-006 answered
- [ ] T-021 unblocked and merged
- [ ] This file is rewritten with the real interface, events, tests and
      budget, then re-queued as QUEUED
