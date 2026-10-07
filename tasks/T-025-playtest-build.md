# T-025 — Playtest build, 20 external testers

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | — (human decision gate, not a worker task) |
| Assigned role | — |
| Depends on | T-024, T-031, T-032, T-033, T-034, T-050, T-051, T-052, T-053, T-054 |
| Spec source | `spec/00-overview.md` Phase 1 gate; `spec/15-interfaces-render.md` §15.15 (Q-130); `spec/15-interfaces-render.md` §15.11, §15.14; `spec/16-interfaces-host.md` §16.10 (minimum-spec measurement, D10/Q-034) |
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
- [ ] T-050 and the four art tasks, T-051 to T-054, merged (Q-130)
- [ ] The owner's acceptance bar (HUMAN DECISION, owner, 2026-10-07, Q-130,
      `15` §15.15): the playtest is not validated until the graphics
      "actually look like a real airport": grass, aprons, runway and
      taxiway markings, stand lead-ins and numbers, a terminal with piers,
      and dressing such as jet bridges and a control tower. The owner
      judges this, not an agent.
- [ ] The manual minimum-spec measurement of `15` §15.14/§16.10 is taken and
      recorded in `CHANGELOG.md` (HUMAN DECISION — owner, 2026-09-27,
      Q-034): on a minimum-spec machine (4-core CPU, 8 GB RAM, integrated
      graphics with no dedicated VRAM) at 1920 × 1080, the `Low` preset
      holds **60 fps** at max tier, and the player process — resident
      memory plus any GPU memory it allocates, shared system RAM on
      integrated graphics — stays **at most 2 GB**. CI cannot run this; it
      is a manual step of this playtest (or a later one, if the build is
      not ready in time), not a task any worker can complete standalone.
      Both the 60 fps and the 2 GB figures are the Architect's LOW
      CONFIDENCE estimates until this measurement records them, and are
      corrected by amendment if it misses, never by a worker.
- [ ] The human owner runs the playtest and records the verdict — an agent
      does not update this file's status to done under any circumstance
