# T-005 — Command queue applied at tick boundaries

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/01-architecture.md` "Command pattern"; `spec/08-interfaces-core.md` §8.7 (as amended by Q-010), §8.11a |
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

This task now also authors the command plumbing Q-010 answers, since it
extends the `Command`/`ICommandQueue` shape this task already owns and no
code exists yet to split from: `PlayerId`, `CommandKind`'s Phase 1 values,
and the `ICommandHandler`/`ICommandHandlerRegistry` dispatch contract.

```
struct PlayerId { uint16 Value }           // PLAYER_LOCAL = 0, the only player at Phase 1

enum CommandKind : uint16 {                // values are saved in command logs: never renumbered
  NoOp           = 0,
  SetServersOpen = 1,
  ReassignStand  = 2
}

interface ICommandHandler {
  CommandKind      Kind { get }
  CommandRejection Validate(ReadOnlySpan<byte> payload)       // at admission
  void             Apply(in Command cmd, in TickContext ctx)   // phase 1, at cmd.Tick
}

interface ICommandHandlerRegistry {
  void Register(SystemId owner, ICommandHandler handler)
}
```

Binding (`08-interfaces-core.md` §8.7, as amended by Q-010):

- Admission only if `cmd.Tick >= CurrentTick + COMMAND_MIN_LEAD_TICKS`.
  Anything later is rejected `TooLate`, never silently re-dated.
- Total order for due commands: sorted by `(Tick, Issuer, Sequence)`.
  `Sequence` assigned monotonically at admission by the queue.
- **Admission order, in full:** `TooLate` (the tick rule above), then
  `UnknownKind` (no handler registered in this build), then the handler's
  `Validate`. `Validate` is a **pure function of the payload and the
  owner's load-time data only** — it never reads runtime sim state, so a
  `TrySubmit` result is reproducible from its arguments alone. A wrong
  payload length is `MalformedPayload`; a well-formed target that can never
  accept the kind is `NotPermitted`.
- **Payload encoding.** Fixed layout, little-endian, fields in the order
  listed, no padding, no length prefix:

  | Kind | Owner | Payload fields | Bytes |
  |---|---|---|---|
  | `NoOp` | `sim.core` | none | 0 |
  | `SetServersOpen` | `sim.flow` | `NodeId.Value : uint32`, `count : int32` | 8 |
  | `ReassignStand` | `sim.airside` | `FlightId.Value : uint64`, `StandId.Value : uint16` | 10 |

  A new kind is appended with the next value, by amendment, together with
  its row here. `sim.core` handles `NoOp` itself; this task does not build
  `SetServersOpen`'s or `ReassignStand`'s own handlers — those are T-023's
  and T-021's, registered through `SystemServices.Commands` at their own
  construction.
- **Registration.** The owning system registers its handler through
  `SystemServices.Commands` (this task builds the registry `ISimHostBuilder`
  exposes as `SystemServices.Commands`, `08` §8.11a), during construction
  only. One handler per kind, only for kinds whose Owner column names it. A
  duplicate registration, or one after `Build`, throws.
- **Application.** At phase 1 of `cmd.Tick`, in the `(Tick, Issuer,
  Sequence)` order above, the handler's `Apply` runs with that tick's
  context. An impossibility found at `Apply` (the flight has left, the
  stand is taken) is a **deterministic no-op**, logged through `ISimLog`
  with the tick, never thrown (`07-conventions.md`: invalid states are
  data). `Apply` may publish events, dispatched as usual in phase 3.
- `LogSince` plus a snapshot reproduces a session; core retains the log since
  the last snapshot (trimming is `sim.save`'s call, out of scope here).
- `CommandKind` is an enum in `sim.core`, extended only by spec amendment.
  `NoOp` is used by the determinism harness to prove the queue participates
  in the hash.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect: admission-window tests (`TooLate`
rejection at the boundary), an admission-order test (`UnknownKind` before
`Validate`, `Validate` before acceptance), total-order tests with multiple
commands sharing a tick, a registration test (duplicate handler for one
kind throws, registration after `Build` throws), an `Apply`-time-impossibility
test that logs a no-op rather than throwing, a test that `NoOp` commands
change `ComputeStateHash()` in a way that proves the queue's state is part
of the hash (per T-004's hasher), and a save/replay test using `LogSince`.
**Do not edit them.**

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

This task's scope grew via Q-010: it now authors `PlayerId`, `CommandKind`'s
Phase 1 values and the `ICommandHandler`/`ICommandHandlerRegistry` contract,
because no other task was scheduled to and T-005 already owns the `Command`
shape this extends. `SetServersOpen`'s and `ReassignStand`'s own handlers
are not built here — only the contract they register against. Do not build
a handler for either kind speculatively; that would pre-empt T-021's and
T-023's own work and their spec sections (`12` §12.10, `09` §9.8).
