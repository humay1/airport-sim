# T-023 — Security lanes openable/closable live, visible queues

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.flow` |
| Assigned role | test-author (tests-only; T-007 already implemented the behaviour) |
| Depends on | T-005, T-007, T-026 |
| Spec source | `spec/09-interfaces-flow.md` §9.4, §9.7a, §9.7b, §9.8, §9.9 (§9.7a answers D6, §9.7b answers Q-010 item 5) |
| Blocked by | — |

## Writable paths

```
tests/sim/flow/**
```

Tests-only (team-lead, 2026-10-01): T-007 (PR #48) already implemented the
behaviour below, so this task writes no `src/` path. The earlier grant of
`src/sim/flow/**` is dropped. If a test written here fails against `main`
because the behaviour is missing or wrong, that is a defect in `src/sim/flow`:
the Test Author stops and reports it, and the Planner files a worker task.
Never edit the code and never weaken a test.

Shares a directory with T-039 and T-040 (`tests/sim/flow/**`); do not release
concurrently with either unless they write different files, and check for
overlap before release.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`spec/09-interfaces-flow.md`

## Interface under test (already implemented by T-007)

```
readonly struct OutstandingPassengers { FlightId Flight; int32 Count; NodeId MostHeldAt }
readonly struct LaneState { int32 ServerCount; int32 ServersOpen }
```

Added to `IFlowSystem` by this task (`09` §9.7a, §9.7b):

```
bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)   // false iff none
bool TryGetLaneState(NodeId node, out LaneState lanes)                          // false unless a Queue node
```

- **`TryGetOutstanding`** (§9.7a, D6, LOW CONFIDENCE): counts the flight's
  `Departing` passengers still on any non-`Gate` node. False when that
  count is 0. `MostHeldAt` is the node holding the largest share, ties by
  ascending `NodeId`. O(the flight's cohorts), from the per-flight index
  §9.10 already anticipates; never scans all nodes or cohorts; no
  allocation. Read-only, not itself hashed, consumes no RNG, unaffected by
  promotion. Its caller is `sim.airside`'s boarding hold
  (`12-interfaces-airside.md` §12.8, T-021) — `sim.airside` already calls
  downward into `sim.flow` (`Absorb`), so this adds no new dependency edge.
- **`TryGetLaneState`** (§9.7b, Q-010 item 5, HUMAN DECISION — owner
  (delegated), 2026-09-23, LOW CONFIDENCE): returns the node's current
  `ServerCount`/`ServersOpen`, false for an unknown node or one that is not
  a `Queue`. Read-only, O(1), not hashed (both fields already in node
  runtime state, §9.10), consumes no RNG. Callers are `app.render` (lane
  pips, T-020) and `app.ui`'s lane sink (T-029). It deliberately exposes no
  service rate, capacity or wait figures — `PredictedWaitMinutes` already
  covers the wait, and this is not to be replaced by returning `QueueConfig`
  itself.

```
| Command | Payload | Effect |
|---|---|---|
| SetServersOpen | NodeId, int32 count | Clamped to [0, ServerCount]; takes
                                          effect at the next tick boundary |
```

(`09-interfaces-flow.md` §9.8, table row verbatim.) This is a `CommandKind`
value (byte layout, `PlayerId`, `ICommandHandler` contract all now
published by T-005, Q-010) consumed through `ICommandQueue`/`ISimHost.TrySubmit`.
This task registers `sim.flow`'s `ICommandHandler` for `SetServersOpen`
inside `FlowFactory.CreateSystem` (via `SystemServices.Commands`, `08`
§8.11a), during construction:

- `Validate(payload)`: a length other than 8 bytes is `MalformedPayload`;
  an unknown `NodeId` is `MalformedPayload`; a node that is not a `Queue` is
  `NotPermitted`. Any `count` value is admitted — the clamp happens at
  `Apply`, not at admission, because admission must be a pure function of
  the payload and load-time data only.
- `Apply(cmd, ctx)`: sets `ServersOpen` to `clamp(count, 0, ServerCount)`,
  effective at the tick boundary the command queue already enforces.

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

Written by the Test Author (10 coverage tests are ready). This task's whole deliverable is these tests, which must pass on `main`. Expect: a command submitted mid-day changes
`ServersOpen` only at the next tick boundary (never mid-tick), a validate/apply
split test (a length-9 payload is `MalformedPayload` at admission; an
out-of-range `count` is admitted and clamped at `Apply`), a hysteresis test
that an oscillating wait time does not flood `QueueThresholdExceeded`/`Cleared`,
a live-visibility test that `Population`/`PredictedWaitMinutes` reflect the
change immediately after application, and `TryGetOutstanding`/`TryGetLaneState`
tests (false for a `Gate`-only flight and for a non-`Queue` node
respectively; correct `MostHeldAt` tie-break). Once merged they are not edited to make a build pass.

## Performance budget

Within `sim.flow`'s existing `2.5` ms/tick (`spec/03-module-map.md`); this
task adds command handling only, no new per-tick O(passengers) work.

## Done when

- [ ] All assigned tests pass on `main` (no `src/` change)
- [ ] `ci/run-checks.sh` green
- [ ] Budget unchanged
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task now also carries `TryGetOutstanding` and `TryGetLaneState`
(the Planner's sizing choice, rather than a separate flow task, since both
are small read-only additions to the same `IFlowSystem`/`src/sim/flow/**`
surface this task already owns). `sim.airside`'s boarding hold (T-021),
`app.render`'s lane pips (T-020) and `app.ui`'s lane sink (T-029) all depend
on this task merging first for those two queries; none of them depend on
this task for `SetServersOpen` itself.

`ServiceRatePerServer` and the threshold values are content-driven
(`spec/04-data-schemas.md`); this task must not hardcode a lane throughput or
a threshold number. Phase 1 content fixtures for security lanes do not yet
exist under `data/` — if none are available when this task starts, coordinate
with the content pipeline rather than inventing numbers, since balance values
are human-owned (`spec/04-data-schemas.md` "Balance values are human-owned").
Structural (non-balance) test fixture values may live beside the tests per
`spec/07-conventions.md`.
