# T-005 — Command queue applied at tick boundaries

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/01-architecture.md` "Command pattern"; `spec/08-interfaces-core.md` §8.7 |
| Blocked by | — |

## Writable paths

```
src/sim/core/**, tests/sim/core/**
```

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`

## Interface to implement

```
struct Command {
  Tick        Tick
  PlayerId    Issuer
  CommandKind Kind
  bytes       Payload              // schema per kind
  uint32      Sequence             // assigned on admission
}

interface ICommandQueue {
  bool TrySubmit(in Command cmd, out CommandRejection reason)
  void ApplyDue(Tick tick)                        // phase 1 only
  IReadOnlyList<Command> LogSince(Tick tick)      // for save and replay
}

enum CommandRejection { None, TooLate, UnknownKind, MalformedPayload, NotPermitted }
```

Binding (`08-interfaces-core.md` §8.7):

- Admission only if `cmd.Tick >= CurrentTick + COMMAND_MIN_LEAD_TICKS`.
  Anything later is rejected `TooLate`, never silently re-dated.
- Total order for due commands: sorted by `(Tick, Issuer, Sequence)`.
  `Sequence` assigned monotonically at admission by the queue.
- Validation happens at admission, not at application.
- `LogSince` plus a snapshot reproduces a session; core retains the log since
  the last snapshot (trimming is `sim.save`'s call, out of scope here).
- `CommandKind` is an enum in `sim.core`. Phase 0 defines only `NoOp`, used by
  the determinism harness to prove the queue participates in the hash.
- Application dispatches to the owning system through an interface that
  system publishes; `sim.core` knows kinds, never their meaning. Since no
  system besides the `NoOp` handler exists yet, this task's dispatch target
  is a stub registered by the harness, not a real system.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect: admission-window tests (`TooLate`
rejection at the boundary), total-order tests with multiple commands sharing
a tick, a test that `NoOp` commands change `ComputeStateHash()` in a way that
proves the queue's state is part of the hash (per T-004's hasher), and a
save/replay test using `LogSince`. **Do not edit them.**

## Performance budget

Counted within `sim.core`'s `0.25` ms/tick (`spec/03-module-map.md`).
`ApplyDue` must not allocate on the hot path.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

None.
