# T-005 — Command queue applied at tick boundaries

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/01-architecture.md` "Command pattern"; `spec/08-interfaces-core.md` §8.7 (as amended by Q-010 and "Queue semantics", Q-020), §8.11a |
| Blocked by | — |

## Writable paths

```
src/sim/core/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory exclusively
(`07-conventions.md` "Solution layout and build"); the path guard already
blocks a worker from writing there, so a worker grant there is a no-op. This
task's earlier grant of `tests/sim/core/**` is dropped.

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

interface ICommandQueue {           // internal (Q-020) — reached only through ISimHost.CommandLogSince
  bool TrySubmit(in Command cmd, out CommandRejection reason)
  void ApplyDue(Tick tick)                        // phase 1 only
  IReadOnlyList<Command> LogSince(Tick tick)      // for save and replay
}

enum CommandRejection { None, TooLate, UnknownKind, MalformedPayload, NotPermitted }
```

**Surface (Q-020):** `ICommandQueue` is the host's internal seam and is
declared `internal`, an exception to `07` L5. Tests, the harness and
`sim.save` reach it only through `ISimHost.CommandLogSince(Tick)` (= this
queue's `LogSince`), which T-001 already declares on `ISimHost` — this task
implements the queue behind it, it does not redeclare `ISimHost` itself.

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

Binding (`08-interfaces-core.md` §8.7, as amended by Q-010 and Q-020's
"Queue semantics" — copied not paraphrased):

- Admission only if `cmd.Tick >= CurrentTick + COMMAND_MIN_LEAD_TICKS`.
  Anything later is rejected `TooLate`, never silently re-dated.
- Total order for due commands: sorted by `(Tick, Issuer, Sequence)`.
  `Sequence` assigned monotonically at admission by the queue.
- **`Command` in C# (Q-020).** `Payload` is `byte[]`. The constructor takes
  `(Tick, Issuer, Kind, Payload)`, throws `ArgumentNullException` for a
  `null` payload (callers use an empty array for none), and sets
  `Sequence = 0` — a caller cannot supply a `Sequence`. Admission **copies**
  the payload, so a caller mutating its array afterwards changes nothing.
- **Admission order, in full (Q-020):** `TooLate` (the tick rule above),
  then `NotPermitted` if `Issuer != PLAYER_LOCAL`, then `UnknownKind` (no
  handler registered in this build), then the kind's check — the handler's
  `Validate`, or, for `NoOp`, "payload length is 0, else `MalformedPayload`".
  `Validate` is a **pure function of the payload and the owner's load-time
  data only** — it never reads runtime sim state, so a `TrySubmit` result is
  reproducible from its arguments alone. It runs **exactly once** for a
  submit that reaches it, and never at application. On success,
  `reason = None`. A wrong payload length is `MalformedPayload`; a
  well-formed target that can never accept the kind is `NotPermitted`.
- **`TrySubmit` during `Step`** (from a handler or a system) throws
  `InvalidOperationException` — commands come from outside the tick.
- **`Sequence` (Q-020)** is one counter for the whole session. The first
  admitted command gets 1, each admission adds 1, and a rejected submit
  consumes nothing. 0 means "not admitted".
- **`LogSince(t)` (Q-020)** returns every admitted command with
  `cmd.Tick >= t`, applied or still pending, in the total order `(Tick,
  Issuer, Sequence)`. It allocates, with fresh payload copies, since it is
  off the hot path. Core keeps every admitted command until `sim.save`
  specifies trimming.
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
- **Registration (Q-020).** The owning system registers its handler
  through `SystemServices.Commands` (this task builds the registry
  `ISimHostBuilder` exposes as `SystemServices.Commands`, `08` §8.11a),
  during construction only. Each kind's owner is the Owner column above,
  mapped to its registry position (`SetServersOpen` → 4, `ReassignStand` →
  3). `Register(owner, handler)` throws `ArgumentException` for an owner
  that does not match `handler.Kind`, for a handler of `NoOp`, or for a
  second handler of a kind; `ArgumentNullException` for `null`;
  `InvalidOperationException` after `Build`. If an owner is not registered
  as a system by `Build`, `Build` throws `InvalidOperationException`.
- **Application.** At phase 1 of `cmd.Tick`, in the `(Tick, Issuer,
  Sequence)` order above, the handler's `Apply` runs with that tick's
  context. An impossibility found at `Apply` (the flight has left, the
  stand is taken) is a **deterministic no-op**: **the handler** logs it
  with its own module's `LogKey` (appended by amendment) at
  `LogLevel.Info` (Q-020) and returns — never thrown
  (`07-conventions.md`: invalid states are data). Core does not catch; an
  exception that does escape `Apply` escapes the tick and is wrapped
  (§8.5a). `Apply` may publish events, dispatched as usual in phase 3.
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
rejection at the boundary), an admission-order test (`NotPermitted` for a
non-`PLAYER_LOCAL` issuer before `UnknownKind`, `UnknownKind` before
`Validate`, `Validate` before acceptance, exactly one `Validate` call per
submit that reaches it), total-order tests with multiple commands sharing a
tick, a `Sequence` test (starts at 1, a rejected submit consumes none, 0
means not admitted), a `TrySubmit`-during-`Step` test
(`InvalidOperationException`), a registration test (duplicate handler for
one kind throws, owner/kind mismatch throws, registration after `Build`
throws, an unregistered owner makes `Build` throw), an
`Apply`-time-impossibility test that the **handler** logs the no-op at
`LogLevel.Info` rather than throwing, a test that `NoOp` commands change
`ComputeStateHash()`/`CoreHash` in a way that proves the queue's state is
part of the hash (via T-001's `CoreHash`, fed by the pending-command count
and the sequence counter), a `null`-payload constructor test
(`ArgumentNullException`), a payload-copy test (mutating the caller's array
after submit changes nothing), and a save/replay test using
`ISimHost.CommandLogSince`. **Do not edit them.**

## Performance budget

Counted within `sim.core`'s `0.25` ms/tick (`spec/03-module-map.md`).
`ApplyDue` must not allocate on the hot path.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] **Green per Q-016 (HUMAN DECISION, owner, 2026-09-24): until T-006
      merges, green = `ci/run-checks.sh`'s `path-guard` and `build-and-test`
      (`--fast`) jobs. The full script becomes mandatory once T-006 merges.**
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
