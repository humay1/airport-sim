# T-049 — `sim.flow`: `KindOf` query (Q-084)

| Field | Value |
|---|---|
| Status | MERGED (PR #99 with tests #98, 2026-10-03) |
| Module | `sim.flow` |
| Assigned role | worker (tests by the Test Author, first) |
| Depends on | T-021 (merged #91). T-007 (merged #48) and T-023 (merged #71) are the existing `IFlowSystem` surface |
| Spec source | `spec/09-interfaces-flow.md` §9.7 (`NodeKind KindOf(NodeId node)` and the `KindOf(node)` paragraph, Q-084, spec PR #95, merged `8f66470`); `spec/19-interfaces-harness.md` §19.2d (its first caller) |
| Blocked by | — |

## Why this task exists

`19` §19.2d has `HarnessGates.Promotion` find the lowest-id `Gate` node by
calling `flow.KindOf(n)` over `world.Nodes()`. `IFlowSystem` has no such
member today. Q-084 adds it to `09` §9.7. T-014 (the harness) depends on this
task, because the real `FlowSystem` must answer `KindOf` for the CLI's
`promotion` to find `NodeId(8)`.

## Writable paths

Worker:

```
src/sim/flow/**
```

Test Author (separate branch, first; the path guard checks a
`test-author/T-049-*` branch against the first block above only, see "Worker
notes"):

```
tests/sim/flow/**
tests/sim/schedule/ScheduleTestKit.cs
tests/sim/airside/AirsideRigs.cs
```

Two test doubles implement `IFlowSystem` and break when `KindOf` joins it:
`RecordingFlow` in `tests/sim/schedule/ScheduleTestKit.cs`, and
`FakeFlowBase : IFlowSystem` in `tests/sim/airside/AirsideRigs.cs` (added by
T-021's branches, so it exists on `main` only after T-021 merges). The Test
Author adds `KindOf` to both in the same change. The production implementer is
`FlowSystem` (`src/sim/flow/FlowSystem.cs`). Re-checked on `main`,
`worker/T-021-airside` and `test-author/T-021-airside-tests` (2026-10-02):
those three are the only `IFlowSystem` implementers. The Test Author re-greps
after T-021 lands. No other file under `tests/sim/schedule/**` or
`tests/sim/airside/**` changes. The worker writes nothing under `tests/`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/09-interfaces-flow.md` §9.7, §9.10, §9.11,
`spec/19-interfaces-harness.md` §19.2d

## Interface to implement

Copied from `09` §9.7:

```
NodeKind KindOf(NodeId node)     // Q-084; the node's FlowGraph kind
```

Binding, from the `KindOf(node)` paragraph of §9.7: it returns the
`NodeKind` that the system's `FlowGraph` gives `node` (§9.11). An unknown
node throws `ArgumentException`, as `SetPromoted` does. It is a query:
read-only, O(1), allocation-free, not hashed (the kinds are construction
data), and it consumes no RNG. It may be called at any time, inside a `Tick`
included. The worker adds the member to `IFlowSystem` and implements it in
`FlowSystem`.

## Events

Emitted: none. Consumed: none. `KindOf` is a query.

## Tests to pass

```
tests/sim/flow/**
```

Written by the Test Author, first: exactly
`test_kind_of_returns_graph_kinds_and_throws_on_unknown_node` (`09` §9.7).
Over a fixture with one node of each `NodeKind`, it returns each node's kind,
and an unknown `NodeId` throws `ArgumentException`. The Test Author also adds
`KindOf` to `RecordingFlow` in `tests/sim/schedule/ScheduleTestKit.cs` and to
`FakeFlowBase` in `tests/sim/airside/AirsideRigs.cs`.
**Do not edit them.** If a test contradicts `09` §9.7, file an open question
and stop.

## Performance budget

No change to `sim.flow`'s 2.5 ms/tick budget (`03-module-map.md`, `09`
§9.10). `KindOf` is O(1) and allocation-free.

## Done when

- [ ] Interface matches spec exactly (`NodeKind KindOf(NodeId node)`)
- [ ] `test_kind_of_returns_graph_kinds_and_throws_on_unknown_node` passes
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

**Serialisation: after T-021.** This task writes `src/sim/flow/**`, and its
Test Author writes `tests/sim/flow/**`, `tests/sim/schedule/ScheduleTestKit.cs`
and `tests/sim/airside/AirsideRigs.cs`. T-021's branches add
`FakeFlowBase : IFlowSystem` in `AirsideRigs.cs`, which stops compiling once
`KindOf` joins `IFlowSystem`. So T-049 is not released until T-021 (#91) has
merged. Do not release it beside any other task that writes these paths. No
other `sim.flow` writer is open (T-039, T-023, T-043 are merged).

**A red path-guard check on the test-only PR is expected, not a defect.**
`ci/check-paths.sh` checks a `test-author/T-049-*` branch against the first
"Writable paths" block (the worker's `src/sim/flow/**`), so a test-only PR
shows path-guard red. Such PRs never merge alone (the same pattern as #78,
#88 and #92): the worker's branch carries the Test Author's files
byte-identical and that PR merges.

**Order.** The Test Author authors first, once T-021 has merged. The new flow test calls `IFlowSystem.KindOf`
and cannot compile until the worker adds it, so the test and the
implementation merge together: the worker's branch carries the Test Author's
files byte-identical (the owner-approved path-guard pairing rule, see
`queue.md`). Only after that merge may T-014 be released.

Do not widen scope: no other `IFlowSystem` member changes. A different change
to the interface's shape is a spec question, not a worker's call.
