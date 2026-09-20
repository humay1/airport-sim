# T-024 — Delay clock per flight + naive attribution log

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `sim.delay` |
| Assigned role | worker |
| Depends on | T-022, T-023 |
| Spec source | `spec/00-overview.md` build order #6; `spec/06-delay-attribution.md`; `spec/10-events.md` §10.5–§10.7 |
| Blocked by | Q-007 (and transitively Q-006 via T-022) |

## Why this task cannot be released

`06-delay-attribution.md` and `10-events.md` specify the `DelayEvent` payload,
the allocation rules and the emitted-event contract, but neither publishes
the `sim.delay` module's `ISimSystem`-facing interface (query surface for
`app.ui`'s delay tree, hashed-state layout for the attribution tree, ordering
of `DelayEventId` relative to `EventId`) — the level of detail
`08-interfaces-core.md`/`09-interfaces-flow.md` provide for their modules.
See `spec/open-questions.md` Q-007. It also depends on T-022, itself
`BLOCKED` on Q-006.

## Writable paths (provisional, not yet binding)

```
src/sim/delay/**, tests/sim/delay/**
```

## Done when

- [ ] Q-007 answered
- [ ] T-022 unblocked and merged
- [ ] This file is rewritten with the real interface, events, tests
      (`spec/06-delay-attribution.md` already names the required test list:
      `sum_of_leaves_equals_total`, `no_orphan_nodes`, `no_cycles`,
      `depth_capped`, `survives_save_load`, `delay_module_never_writes`) and
      budget, then re-queued as QUEUED
