# T-025 — Playtest build, 20 external testers

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | — (human decision gate, not a worker task) |
| Assigned role | — |
| Depends on | T-024, T-031, T-032, T-033, T-034 |
| Spec source | `spec/00-overview.md` Phase 1 gate |
| Blocked by | — (all Q-002 through Q-012 are answered; T-024 and the full playable-build chain, T-027–T-034, are `QUEUED`, not blocked) |

## Why this task cannot be released

`00-overview.md` is explicit: "T-025 is a human decision, not an agent one
... No agent may mark this task complete." No agent task file is written for
its content beyond this placeholder — there is no interface to implement and
no passing-test done-condition to state; the done-condition is a human
playtest verdict. It also cannot start before T-024 **and** the tasks that
put a build in front of 20 external testers — the playable build needs
`app.host` (T-031, T-034) and both engine backends (T-032, T-033) merged, not
just T-024.

This entry exists only to hold T-025's place in the dependency graph and to
record, per the Planner's brief, that no further work is planned past it.
Every HUMAN DECISION that once blocked a playable build (Q-008 §15.13
(a)–(e)) is now made (D1, D4, D5, D7); what remains before T-025 can even
be attempted is ordinary task completion, not a further open question.

## Done when

- [ ] T-024 merged
- [ ] T-031 and T-034 merged (a running player build)
- [ ] T-032 and T-033 merged (both engine backends)
- [ ] The human owner runs the playtest and records the verdict — an agent
      does not update this file's status to done under any circumstance
