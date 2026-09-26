# T-001 — Headless harness: fixed timestep, no rendering

| Field | Value |
|---|---|
| Status | IN_PROGRESS |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-003 |
| Spec source | `spec/01-architecture.md` "Platform decisions" (D1), "Layer separation", "Command pattern"; `spec/08-interfaces-core.md` §8.1, §8.2, §8.4, §8.5, §8.5a, §8.6, §8.9 (as amended, Q-017), §8.11a (answers Q-014); `spec/10-events.md` §10.2, §10.9 (`EventEnvelope`/`EventRef`); `spec/07-conventions.md` "Runtime portability", "Solution layout and build" (Q-013) |
| Blocked by | — |

**Status note:** tests have landed (`tests/sim/core/**`); the worker is in
progress.

**Dependency correction (systematic type-dependency recheck):**
`ISimClock.MinutesBetween` returns `SimMinutes`, which is `Fx`
(`08-interfaces-core.md` §8.2) — this task cannot compile without `Fx`
(T-003). `Depends on` is amended to `T-003`. T-003 now merges **before**
this task, with no placeholder `Fx`; see the reordered release sequence in
`tasks/queue.md`.

**Scope amendment, second round (Architect batch 2–5, Q-016–Q-022,
`db78df0` on `architect/Q-013-solution-layout`; coordinator decision on
`IIdAllocator`):** this task's scope grew again. It now ships, in full: the
concrete `StateHasher`, the `CoreHash` section and `Checkpoint.CoreHash`
(Q-017), `EventEnvelope`/`EventRef` (Q-018, `10` §10.2 — "T-001's, because
the bus needs them"), and the **real** `IIdAllocator` allocation rule
(coordinator decision, accepting the Architect's recommendation — no longer
shape-only). The notes below that used to flag `IIdAllocator` and
`IStateHasher`/`ContentIndexFactory` as gaps with no owning task are removed:
`IIdAllocator` is this task's now, `IStateHasher` stays this task's own (it
always shipped here per Q-017: "T-001 ships `StateHasher`... T-004 proves
the golden vectors"), and `ContentIndexFactory` is now T-026's (that task
owns the content definition types it constructs from).

## Writable paths

```
src/sim/core/**
AirportSim.sln
tools/SimHarness/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory
exclusively (`07-conventions.md` "Solution layout and build"); the path
guard already blocks a worker grant there. This task's earlier grant of
`tests/sim/core/**` is dropped.

Anything else is read-only. Writing outside these paths is an automatic
rejection. **`AirportSim.sln` already exists by the time this task starts**
— T-003 creates it (`07` L8: "T-003 before T-001... T-003 creates
`AirportSim.sln`"). This task's own job on the solution is to run
`dotnet new sln`'s add-equivalent for `tools/SimHarness`: add
`tools/SimHarness/AirportSim.Tools.SimHarness.csproj` (created fresh by this
task, byte for byte per `07` L8's table — `Exe`, one `ProjectReference` to
`src/sim/core`) to `AirportSim.sln`. `tools/SimHarness` is the console entry
point CI's `ci/run-checks.sh` already invokes
(`dotnet run --project tools/SimHarness`); create it as a thin host over
`ISimHost`, not as a place to put sim logic. Every `.csproj` this task
writes or edits follows `07-conventions.md` "Solution layout and build" L1–L11
exactly — project paths, names, targets and the byte-for-byte L2/L3 file
contents are fixed there, not this task's choice.

**Targets (D1, `01-architecture.md` "Platform decisions"; `07` L1):**
`src/sim/core/AirportSim.Sim.Core.csproj` targets `netstandard2.1`,
`LangVersion 9`, zero engine references (T-003 already created this file;
this task does not recreate it, only adds to it). `tools/SimHarness/AirportSim.Tools.SimHarness.csproj`
targets `net8.0`, `LangVersion 12`, and consumes the sim unchanged.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/10-events.md` §10.2, §10.9 (envelope
shape and the note that `EventEnvelope`/`EventRef` are this task's; every
other struct in §10.9 is T-026's)

## Interface to implement

Implemented **in full** by this task (Q-014's A3/A4/A6, Q-017, Q-018 —
copied not paraphrased):

```
public static class SimConstants {                  // §8.1, values fixed there
  int   TICK_MS = 100
  int   SIM_SECONDS_PER_TICK = 6
  ulong TICKS_PER_SIM_MINUTE = 10
  ulong TICKS_PER_SIM_HOUR = 600
  ulong TICKS_PER_SIM_DAY = 14400
  ulong HASH_CHECKPOINT_TICKS = 600
  ulong COMMAND_MIN_LEAD_TICKS = 1
  int   MAX_EVENT_CASCADE_PASSES = 8
  int   MAX_EVENTS_PER_TICK = 4096
  int   MAX_ATTRIBUTION_DEPTH = 6
  int   FX_FRACTIONAL_BITS = 32
}

type Tick       = uint64
type SimMinutes = Fx

interface ISimClock {
  Tick       CurrentTick   { get }
  uint32     DayIndex      { get }
  uint32     SecondOfDay   { get }
  SimMinutes MinutesBetween(Tick a, Tick b)
  Tick       TickOfDayTime(uint32 dayIndex, uint32 secondOfDay)
}

struct SystemId { uint16 Value }                 // registry position, 1..14, never 8; 0 = SYSTEM_CORE
struct EntityId { uint64 Value }                 // opaque, §8.4
struct FlightId { uint64 Value }                 // §8.4 — same IDL block as SystemId/EntityId
struct EventId  { uint64 Tick; uint32 Sequence } // total order, §8.6

interface ISimSystem {
  SystemId Id   { get }
  string   Name { get }
  void     Tick(in TickContext ctx)
  uint64   ComputeStateHash()
}

readonly struct TickContext {
  Tick            Tick
  ISimClock       Clock
  IEventPublisher Events
  IRandomService  Rng
  IContentIndex   Content
  ISimLog         Log
}

interface ISimHost {
  Tick   CurrentTick { get }
  void   Step(uint32 ticks)
  uint64 WorldStateHash()
  bool   TrySubmit(in Command cmd, out CommandRejection reason)
}

class SimInvariantException : Exception {     // sealed, §8.5a
  Tick   Tick
  uint64 WorldHash
  bool   HasWorldHash
}

interface IEventPublisher {                                          // §8.6
  EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
}
interface IEventBus : IEventPublisher {
  void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
}
delegate void SimEventHandler<T>(in EventEnvelope envelope, in T evt, in TickContext ctx)
  where T : struct, ISimEvent
interface ISimEvent { }                       // marker only; payload types are T-026's (10-events.md §10.9)

readonly struct EventEnvelope {                                      // 10 §10.2, Q-018 — this task's
  EventId  Id
  Tick     Tick
  SystemId Source
  EventRef Cause
}
readonly struct EventRef { EventId Id; bool HasValue }   // EventRef.None: HasValue = false

interface IIdAllocator { EntityId Next(SystemId owner) }             // §8.4, real behaviour (Q-017)

interface IStateHasher {                                             // §8.9, Q-017 — this task's
  void   Feed(uint64 v)
  void   Feed(int64 v)
  void   Feed(in Fx v)
  void   Feed(bool v)
  void   Feed(ReadOnlySpan<byte> v)
  uint64 Result { get }
}

readonly struct Checkpoint {
  Tick     Tick
  uint64   WorldHash
  uint64   CoreHash                         // Q-017
  uint64[] SystemHashes                     // registry order, §8.5
}
interface ICheckpointSink { void Record(in Checkpoint cp) }
```

Construction (`08` §8.11a, Q-009 — the same surface every later module
factory, `app.host` and `tools.simharness` build against):

```
readonly struct SimHostConfig {
  uint64          MasterSeed
  IContentIndex   Content
  ICheckpointSink Checkpoints
  ISimLog         Log
}

readonly struct SystemServices {
  IEventBus               Events
  IIdAllocator            Ids
  IContentIndex           Content
  ICommandHandlerRegistry Commands
}

interface ISimHostBuilder {
  SystemServices Services { get }
  void     Register(ISimSystem system)     // strictly ascending registry position
  ISimHost Build()                         // once
}

SimHostFactory.CreateBuilder(in SimHostConfig config) -> ISimHostBuilder
```

**Shape only, no behaviour tested by this task's own tests (Q-014 A3).**
Each is declared here because `SystemServices`/`TickContext`/`Command`
already reference it, but this task's tests do not exercise it beyond
existing:

```
struct Command {
  Tick        Tick
  PlayerId    Issuer
  CommandKind Kind
  bytes       Payload
  uint32      Sequence
}
struct PlayerId { uint16 Value }              // PLAYER_LOCAL = 0
enum CommandKind : uint16 { NoOp = 0 }        // T-005 appends SetServersOpen/ReassignStand
enum CommandRejection { None, TooLate, UnknownKind, MalformedPayload, NotPermitted }
interface ICommandHandler {
  CommandKind      Kind { get }
  CommandRejection Validate(ReadOnlySpan<byte> payload)
  void             Apply(in Command cmd, in TickContext ctx)
}
interface ICommandHandlerRegistry { void Register(SystemId owner, ICommandHandler handler) }

interface IRandomService { IRandomStream Stream(RngStreamName name); uint64 MasterSeed { get } }
interface IRandomStream {
  uint64 NextUInt64()
  int32  NextInt(int32 minInclusive, int32 maxExclusive)
  Fx     NextFx01()
  bool   Chance(Fx probability)
  void   Shuffle<T>(Span<T> items)
  uint64 ComputeStateHash()
}
readonly struct RngStreamName { string Value }

interface IContentIndex {
  bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
  IReadOnlyList<ContentId> AllOf(ContentKind kind)
}
readonly struct ContentId { string Value }
enum ContentKind { SizeCategory, Aircraft, PaxProfile, QueueProfile }
interface IContentDefinition { ContentId Id { get }; ContentKind Kind { get } }

interface ISimLog { void Write(Tick tick, LogLevel level, SystemId system, LogKey key, in LogArgs args); }
enum LogLevel : byte   { Debug = 0, Info = 1, Warning = 2, Error = 3 }
enum LogKey   : uint16 { None = 0 }
readonly struct LogArgs { int32 Count; int64 A0; int64 A1; int64 A2; int64 A3 }
```

`Register` out of order, twice for one `SystemId`, or after `Build`, throws;
so does `Subscribe` after `Build`. `Build` creates the RNG service from
`MasterSeed`, wires the sinks, and returns the host at tick 0. A builder
cannot be reused after `Build`.

**What this task implements in full versus what it only shapes (Q-014,
Q-017, Q-018, binding):**

- **In full:** `SimConstants`, `ISimClock`, `SystemId`/`SYSTEM_CORE`,
  `EntityId`, `FlightId`, `EventId` (§8.4's single IDL block), `IIdAllocator`
  with its **real allocation rule** (below — no longer shape-only), the
  builder and factory, the phase loop (below), `SimInvariantException`, the
  full §8.6 event bus/dispatch (queueing, FIFO `Sequence`, cascade passes,
  `MAX_EVENTS_PER_TICK`, registry-order handler dispatch), `EventEnvelope`/
  `EventRef` (Q-018 — declared here because the bus needs them, even though
  every event *payload* struct they wrap is T-026's), `ISimLog`/`LogLevel`/
  `LogKey`/`LogArgs` (the shape is all there is — no sink logic beyond wiring
  the injected one), the concrete `StateHasher` struct and the `CoreHash`
  section (below, Q-017), and `Checkpoint`/`ICheckpointSink` **with the real
  phase-4 cadence and world-hash computation of §8.9 — not a stub (A6).**
- **Shape only, T-005/T-002/T-026/T-027 give behaviour later:** `Command`,
  `PlayerId`, `CommandKind` (only `NoOp = 0` here), `CommandRejection`,
  `ICommandHandler`, `ICommandHandlerRegistry` (T-005 adds `CommandLogSince`
  to `ISimHost`, real admission, ordering and every kind's behaviour,
  `NoOp` included; this task's own `TrySubmit` returns `false` with
  `UnknownKind` for every kind, with no exception for `NoOp`); `IRandomService`/
  `IRandomStream`/`RngStreamName`, with a placeholder whose `MasterSeed` is
  the config's and whose `Stream` throws `InvalidOperationException` (T-002
  replaces it); `IContentIndex`/`IContentDefinition`/`ContentId`/
  `ContentKind` (T-026 adds the definition structs **and now also
  `ContentIndexFactory`** — coordinator decision: T-026 owns it, since it
  owns the definition types and T-027's loader feeds it; this is no longer
  an unowned gap).

**`IIdAllocator`'s allocation rule (Q-017, now real, not shape-only):**
each owner's counter starts at 0. `Next(owner)` increments it and returns
`EntityId((owner.Value << 48) | counter)`, so the first id is counter 1 and
ids never collide across owners. `EntityId(0)` is never allocated. A
counter above `2^48 - 1` throws `SimInvariantException`. An owner of 0, 8,
or above 14 throws `ArgumentException`. Consumers never decode an
`EntityId`'s bit pattern back into an owner — it is opaque past
construction.

**`StateHasher`, encoding and the core section (Q-017, this task's, in
full):**

- **FNV-1a-64**: `h = 0xCBF29CE484222325`, then for each byte
  `h = (h XOR byte) * 0x100000001B3` mod 2^64.
- `public struct StateHasher : IStateHasher`. `new StateHasher()` and
  `default(StateHasher)` are both a fresh hasher, whose `Result` is the
  offset basis; `Result` may be read at any time without changing state.
  Systems use the struct directly — through the interface it would box.
  The struct is mutable, an exception to `07` L10.
- **Encoding.** `Feed(uint64)`/`Feed(int64)` write 8 bytes, little-endian
  (two's complement for `int64`). `Feed(in Fx)` is `Feed(Raw)`. `Feed(bool)`
  writes 1 byte, 0 or 1. `Feed(ReadOnlySpan<byte>)` writes the length as a
  `uint64` first, then the bytes. Every narrower integer, enum or id value
  is widened to 64 bits (sign-extended when signed) and fed as 8 bytes. A
  string (`ContentId`, `RngStreamName`) is fed as the span of its UTF-8
  bytes.
- **Golden vectors (this task's own test oracle):** fresh:
  `CBF29CE484222325`. `Feed(0UL)`: `A8C7F832281A39C5`.
  `Feed(1UL); Feed(-1L); Feed(true)`: `9185A69DA7E88AC7`.
  `Feed([1, 2, 3])`: `01EF76D429B11552`.
- **The core section.** `sim.core` is not a system, but its own state
  changes outcomes. `CoreHash` is a fresh `StateHasher` fed, in order: (1)
  the next command `Sequence` to assign; (2) the number of pending
  (admitted, not yet applied) commands, then each in `(Tick, Issuer,
  Sequence)` order, as `Tick`, `Issuer.Value`, `Kind`, `Sequence`, then
  `Payload` (a span); (3) the number of owners whose id counter is
  non-zero, then, ascending `SystemId`, the owner's `Value` and its
  counter. Applied commands are not fed — they live on in systems' state
  and the sequence counter. RNG streams are not in the core section (each
  is in its owner's hash, §8.8).
- **World hash** = a fresh `StateHasher` fed the ticks-executed count, then
  `CoreHash`, then each registered system's `ComputeStateHash()` in
  registry order, all as `uint64`. `Checkpoint.CoreHash` carries the core
  section on its own, so a divergence there is named as fast as a system's.
- At this task's own scope (no other module registered), the core section
  is: sequence 1, no pending commands, no non-zero counters — this task's
  own fixture proves the wiring, not another system's content.

**Fixed phase order per tick** (`08-interfaces-core.md` §8.5), binding and
not to be reordered: (1) command application, (2) system update in registry
order, (3) event dispatch (full §8.6 semantics, this task's own), (4)
checkpoint if due (§8.9, this task's own, real cadence and hash). Phase 0
has no registered systems and no accepted command kind — this task's own
`TrySubmit` rejects everything with `UnknownKind` (above); it proves the
loop shape, not any command's admission or any system's content.

**Tick numbering (Q-014 A1/A5):** `Step(n)` executes ticks `CurrentTick …
CurrentTick+n−1`; the first `Step(1)` runs tick 0; `Step(0)` does nothing;
chunking is invisible (`Step(a)` then `Step(b)` ≡ `Step(a+b)` in every hash,
checkpoint, event and log line). `MinutesBetween` is signed and never
throws for `b < a`; a tick outside `int64` range throws `OverflowException`.
`TickOfDayTime` floors a non-multiple second to the tick containing it and
throws `ArgumentOutOfRangeException` at `s >= 86400`.

`Step` must be synchronous, take no time argument, and must not read
wall-clock time anywhere in `src/sim`.

## Events

Emitted: none (this task wires the real transport; no event *payload* type
exists yet — those are T-026's, per `10-events.md` §10.9)
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. **Do not edit them.** Expect at minimum: a
headless-day test that builds `ISimHost` with zero systems registered and
steps a full sim-day of ticks with no engine reference anywhere in `src/sim`
(enforced by `ci/run-checks.sh`'s grep gate); a test asserting `Step` never
reads wall-clock time (`DateTime.Now` grep gate); checkpoint-cadence tests
(24 checkpoints per sim-day, none at `Build`, a checkpoint's `WorldHash`
equal to `WorldStateHash()` read immediately after the `Step` that produced
it, `SystemHashes` length/order matching the registered set,
`Checkpoint.CoreHash` present, deterministic and computed at this task's own
(command-free) scope — the `NoOp`-submission hash-change test is T-005's);
`StateHasher` golden-vector tests (the four vectors above, byte-exact);
`IIdAllocator` tests (counters start at 0, first id has counter 1, no
cross-owner collision, invalid owner throws `ArgumentException`, overflow
throws `SimInvariantException`); event-bus tests (FIFO dispatch order,
registry-order handler dispatch, cascade passes up to
`MAX_EVENT_CASCADE_PASSES`, the `MAX_EVENTS_PER_TICK` invariant throwing
`SimInvariantException`); and a `SimInvariantException` wrapping test (the
host wraps any exception escaping a tick, carries the tick and, when
computable, the world hash). Tests may rely on the checkpoint cadence,
`Tick` values, the length/order/values of `SystemHashes`, `CoreHash`, a
checkpoint's `WorldHash` equalling `WorldStateHash()`, and every hash being
deterministic and changing with the tick, the core section, or any system
hash. If a test contradicts this interface, file an open question and stop.

## Performance budget

`0.25` ms/tick at max tier (`sim.core` loop, commands, event dispatch —
`spec/03-module-map.md`). This task's own fixture is empty (no systems), so
the budget test here is a sanity check, not the real measurement — that comes
with later systems.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] **Green per Q-016 (HUMAN DECISION, owner, 2026-09-24): until T-006
      merges, green = `ci/run-checks.sh`'s `path-guard` and `build-and-test`
      (`--fast`) jobs passing, building and testing through `AirportSim.sln`.
      The full `ci/run-checks.sh` (`determinism`/`saveload`/`promotion`/
      `budget`) is not required for this task's merge and is expected to
      fail until T-006 lands; it becomes mandatory the moment T-006 merges.**
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`SIM_SECONDS_PER_TICK = 6` is now a confirmed **HUMAN DECISION** (D2,
`08-interfaces-core.md` §8.1/§8.2), not provisional. Golden hashes may now
be authored (the soak fixture and its golden are T-013's job, not this
one's).

D1 retargets the sim (and every headless `app.*` layer later) to
`netstandard2.1`/`LangVersion 9`; only `tests/**` and `tools/SimHarness/**`
stay on `net8.0`/`LangVersion 12`. Read `spec/07-conventions.md` "Runtime
portability" before writing anything that sorts, hashes, or parses a string
or a number — Mono and CoreCLR must agree, and that section's seven rules
are binding on `src/sim/**` from this task onward.

Both gaps this task's earlier text flagged as unowned are now resolved and
the flag is removed: `IIdAllocator`'s counting behaviour is this task's own
(above), and `ContentIndexFactory` belongs to T-026, not to this task or to
an unnamed future one.
