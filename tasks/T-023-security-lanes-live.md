# T-023 — Security lanes openable/closable live, visible queues

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-007, T-005 |
| Spec source | `spec/09-interfaces-flow.md` §9.4, §9.8, §9.9 |
| Blocked by | — |

## Writable paths

```
src/sim/flow/**, tests/sim/flow/**
```

Same directory as T-007/T-010/T-011; do not release concurrently with any of
them.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`spec/09-interfaces-flow.md`

## Interface to implement

```
| Command | Payload | Effect |
|---|---|---|
| SetServersOpen | NodeId, int32 count | Clamped to [0, ServerCount]; takes
                                          effect at the next tick boundary |
```

(`09-interfaces-flow.md` §9.8, table row verbatim.) This is a `CommandKind`
value consumed through `ICommandQueue`/`ISimHost.TrySubmit` from T-005 —
admission and total ordering are already handled there; this task adds the
`sim.flow`-side handler that `ApplyDue` dispatches to, and clamps
`ServersOpen` into `[0, ServerCount]` on application.

Also exposes, for visible queues, the existing read path from §9.7:

```
int32  Population(NodeId node)
Fx     PredictedWaitMinutes(NodeId node)
```

`PredictedWaitMinutes` is `population / max(capacityPerMinute, epsilon)`
(§9.4), measured not modelled, never fed back into the sim.

## Events

Emitted: `QueueThresholdExceeded` / `QueueThresholdCleared` (hysteresis per
content-declared threshold, `10-events.md` §10.3 rule 4 — dedup at source)
Consumed: `SetServersOpen` command (via `ICommandQueue`, not the event bus)

## Tests to pass

```
tests/sim/flow/**
```

Written by the Test Author. Expect: a command submitted mid-day changes
`ServersOpen` only at the next tick boundary (never mid-tick), a clamp test
for out-of-range counts, a hysteresis test that an oscillating wait time does
not flood `QueueThresholdExceeded`/`Cleared`, and a live-visibility test that
`Population`/`PredictedWaitMinutes` reflect the change immediately after
application. **Do not edit them.**

## Performance budget

Within `sim.flow`'s existing `2.5` ms/tick (`spec/03-module-map.md`); this
task adds command handling only, no new per-tick O(passengers) work.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`ServiceRatePerServer` and the threshold values are content-driven
(`spec/04-data-schemas.md`); this task must not hardcode a lane throughput or
a threshold number. Phase 1 content fixtures for security lanes do not yet
exist under `data/` — if none are available when this task starts, coordinate
with the content pipeline rather than inventing numbers, since balance values
are human-owned (`spec/04-data-schemas.md` "Balance values are human-owned").
Structural (non-balance) test fixture values may live beside the tests per
`spec/07-conventions.md`.
