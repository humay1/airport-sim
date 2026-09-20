# T-025 — Playtest build, 20 external testers

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | — (human decision gate, not a worker task) |
| Assigned role | — |
| Depends on | T-024 |
| Spec source | `spec/00-overview.md` Phase 1 gate |
| Blocked by | T-024 (transitively Q-007, Q-006, Q-005, Q-004) |

## Why this task cannot be released

`00-overview.md` is explicit: "T-025 is a human decision, not an agent one
... No agent may mark this task complete." No agent task file is written for
its content beyond this placeholder — there is no interface to implement and
no passing-test done-condition to state; the done-condition is a human
playtest verdict. It also cannot start before T-024, which is blocked.

This entry exists only to hold T-025's place in the dependency graph and to
record, per the Planner's brief, that no further work is planned past it.

## Done when

- [ ] T-024 merged
- [ ] The human owner runs the playtest and records the verdict — an agent
      does not update this file's status to done under any circumstance
