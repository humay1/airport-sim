# 08 — Public interfaces: `sim.core`

Implements the `sim.core` row of `03-module-map.md`. Nothing here may contradict
`01-architecture.md` or `02-determinism.md`; where this file appears to, those
files win and the contradiction is a spec bug — file it in `open-questions.md`.

**Notation.** Signatures are written in the language-neutral IDL below. This is a
*specification*, not code: the worker writes the C# that realises it and chooses
access modifiers, namespaces and file layout inside `src/sim/core`. Names given
here are binding. Anything not listed here is internal to the module and may not
be referenced from another module (`03-module-map.md`, "Communication rules").

Reading order for a `sim.core` worker: `01`, `02`, `07`, this file, then `10`.

---

## 8.1 Constants

Compile-time constants in `sim.core`. Content data never overrides them; a value
designers should tune belongs in `data/`, not here.

| Constant | Value | Source |
|---|---|---|
| `TICK_MS` | 100 | `01-architecture.md`, locked |
| `SIM_SECONDS_PER_TICK` | 6 | this file, §8.2 — HUMAN DECISION — owner (delegated), 2026-09-23 (Q-002) |
| `TICKS_PER_SIM_MINUTE` | 10 | derived |
| `TICKS_PER_SIM_HOUR` | 600 | derived |
| `TICKS_PER_SIM_DAY` | 14400 | derived |
| `HASH_CHECKPOINT_TICKS` | 600 (one sim-hour) | this file, §8.9 |
| `COMMAND_MIN_LEAD_TICKS` | 1 | this file, §8.7 |
| `MAX_EVENT_CASCADE_PASSES` | 8 | this file, §8.6 |
| `MAX_EVENTS_PER_TICK` | 4096 | this file, §8.6 |
| `MAX_ATTRIBUTION_DEPTH` | 6 | `06-delay-attribution.md` |
| `FX_FRACTIONAL_BITS` | 32 | this file, §8.3 |

`AGENT_ZOOM_THRESHOLD` is a presentation constant and does **not** live in
`sim.core`; it is defined in `15-interfaces-render.md` §15.2. The sim is told which nodes are promoted; it never asks about cameras.

---

## 8.2 Sim time

The sim has no concept of real time (`02-determinism.md` rule 1). It counts ticks.

- A tick advances sim time by `SIM_SECONDS_PER_TICK` sim-seconds.
- Tick 0 is scenario start. The scenario declares a wall-date for display only.
- Game speed is presentation-side: it changes how many ticks per real second the
  host requests, never the meaning of a tick.

```
type Tick       = uint64            // monotonic
type SimMinutes = Fx                // the unit of delay everywhere in the spec

interface ISimClock {
  Tick       CurrentTick   { get }
  uint32     DayIndex      { get }          // CurrentTick / TICKS_PER_SIM_DAY
  uint32     SecondOfDay   { get }          // 0 .. 86399
  SimMinutes MinutesBetween(Tick a, Tick b)
  Tick       TickOfDayTime(uint32 dayIndex, uint32 secondOfDay)
}
```

`ISimClock` is a pure function of the tick counter. It holds no mutable state and
contributes nothing to the state hash.

> **HUMAN DECISION — owner (delegated), 2026-09-23 (Q-002, D2):
> `SIM_SECONDS_PER_TICK = 6` is confirmed.** It gives (a) delay arithmetic
> sub-minute resolution instead of whole-minute quanta, (b) queue and taxi
> updates ten times per sim-minute, and (c) a sim-day of 14 400 ticks, so the
> nightly 500-day soak is 7.2 M ticks. At 1x a sim-day lasts 24 real minutes.
> A smaller value would make T-009's 100-days-in-60-s gate infeasible (1 s per
> tick is 8.6 M ticks for 100 days); a larger one coarsens delay resolution.
> Golden hashes may now be authored. The decision is reversible, but reversing
> it invalidates every golden hash and every tick-valued fixture. No interface
> in this file changes.

---

## 8.3 Fixed-point: `Fx`

Satisfies `02-determinism.md` rule 4. No `float` or `double` may appear in any sim
assembly — including tests, including log formatting.

```
struct Fx { int64 Raw }             // value = Raw / 2^32
```

- Format Q31.32: range approx ±2.1e9, resolution approx 2.3e-10.
- `Mul` and `Div` compute through a 128-bit intermediate and then narrow. No
  intermediate may be computed in 64 bits.
- Rounding is **truncation toward negative infinity** for every narrowing
  operation, including `Mul`, `Div` and all `To*` conversions. One rule, no
  per-call-site choices. A worker who needs half-up calls `RoundHalfUp`
  explicitly.
- Division by zero and overflow on narrowing are **programmer errors**: throw with
  the tick number (`07-conventions.md`).
- **The 128-bit arithmetic is hand-rolled.** The sim targets `netstandard2.1`
  (`01-architecture.md`, D1), which has no `Int128`/`UInt128`, no
  `Math.BigMul(long, long, out long)` and no `System.Numerics.BitOperations`.
  So the 64×64→128-bit multiply behind `Mul`, the 128-by-64-bit division
  behind `Div`, and any leading-zero count (for example `Sqrt`'s initial
  estimate) are written inside `Fx` in plain integer code over `uint64`
  halves. `BigInteger` is not used either: it allocates, and it would be a
  second implementation to keep bit-exact. Overflow and sign cases, including
  `long.MinValue / -1`, are checked explicitly by `Fx` before any BCL
  operator could throw. Behaviour never relies on which BCL exception a
  runtime raises. Test projects target `net8.0` and **may** use
  `Int128`/`BigInteger` as a reference oracle for `Fx`; sim assemblies may
  not.

```
Fx.FromInt(int64 v)
Fx.FromRatio(int64 numerator, int64 denominator)     // exact, then truncated
Fx.Parse(string decimal)                             // content loading only
Fx.Zero, Fx.One, Fx.MinValue, Fx.MaxValue
Fx.Add, Fx.Sub, Fx.Mul, Fx.Div, Fx.Neg
Fx.Abs, Fx.Min, Fx.Max, Fx.Clamp
Fx.Floor -> int64, Fx.Ceil -> int64, Fx.RoundHalfUp -> int64
Fx.Frac -> Fx
Fx.Sqrt -> Fx                                        // integer Newton, exact-stable
Fx.ToDisplayString(int decimals)                     // presentation and logs only
```

Deliberately **absent** at Phase 0: trigonometry, exponentials, logarithms,
`Lerp`, vector types. Nothing in the Phase 0 build order needs them and each is a
cross-platform bit-exactness liability. A module that needs one files a question.

Money is **not** `Fx`. Currency is `int64` minor units, owned by `sim.economy`,
which does not exist in Phase 0.

---

## 8.4 Identifiers

```
struct SystemId { uint16 Value }                      // fixed registry, §8.5
struct EntityId { uint64 Value }                      // opaque
struct FlightId { uint64 Value }
struct EventId  { uint64 Tick; uint32 Sequence }      // total order, §8.6
```

Ids are values, never references, and carry no meaning in their bit pattern. Never
branch on a reference or a default hash code (`02-determinism.md` rule 7).

```
interface IIdAllocator { EntityId Next(SystemId owner) }
```

Counters are per-owner and part of saved state, so that adding an allocation in
one system cannot shift another system's ids — the same reasoning as per-system
RNG streams.

---

## 8.5 Systems and the tick loop

```
interface ISimSystem {
  SystemId Id   { get }
  string   Name { get }                    // stable, matches the module name
  void     Tick(in TickContext ctx)
  uint64   ComputeStateHash()
}

readonly struct TickContext {
  Tick            Tick
  ISimClock       Clock
  IEventPublisher Events
  IRandomService  Rng
  IContentIndex   Content                  // read-only content data
  ISimLog         Log
}
```

`Tick` must not allocate on the hot path (`07-conventions.md`).

### Fixed phase order

Every tick executes exactly these phases in this order. The order is part of the
determinism contract; changing it changes every golden hash.

1. **Command application** — all commands due at this tick, in the total order of
   §8.7.
2. **System update** — each registered system's `Tick` called once, in registry
   order.
3. **Event dispatch** — the tick's event queue is drained, §8.6.
4. **Checkpoint** — if `Tick % HASH_CHECKPOINT_TICKS == 0`, compute and record the
   world hash, §8.9.

### System registry order

Binding. Also the order in which system hashes are combined
(`02-determinism.md`, "State hashing"). Modules absent from a build are skipped,
never reordered.

| # | System | # | System |
|---|---|---|---|
| 1 | `sim.world` | 8 | *(reserved)* |
| 2 | `sim.schedule` | 9 | `sim.staff` |
| 3 | `sim.airside` | 10 | `sim.economy` |
| 4 | `sim.flow` | 11 | `sim.policy` |
| 5 | `sim.turnaround` | 12 | `sim.reputation` |
| 6 | `sim.baggage` | 13 | `sim.incident` |
| 7 | `sim.delay` | 14 | `sim.progression` |

`sim.delay` updates after every module it observes, so a cause and its attribution
land on the same tick. `sim.core` and `sim.save` are not systems: core owns the
loop, save observes it.

### The host interface

The only entry point the presentation layer has for advancing the sim.

```
interface ISimHost {
  Tick   CurrentTick { get }
  void   Step(uint32 ticks)                // advances exactly this many ticks
  uint64 WorldStateHash()                  // on demand, outside checkpoints
  bool   TrySubmit(in Command cmd, out CommandRejection reason)
}
```

`Step` is synchronous and takes no time argument. A host that wants to run faster
calls it more often. There is no `Update(deltaTime)` and there never will be.

---

## 8.6 Event bus

Events are immutable value types defined in `sim.core` (`03-module-map.md`). The
catalogue is `10-events.md`; this section defines only the transport.

```
interface IEventPublisher {
  EventId Publish<T>(in T evt) where T : struct, ISimEvent
}

interface IEventBus : IEventPublisher {
  void Subscribe<T>(SystemId subscriber, EventHandler<T> handler)
}

interface ISimEvent {
  // marker; every event carries the envelope fields of 10-events.md §10.2
}
```

Ordering rules, all four determinism-critical:

1. `Publish` **queues**; it never calls a handler. Publishing is therefore
   non-reentrant by construction and no module can be re-entered mid-update.
2. The queue is FIFO. `EventId.Sequence` is assigned on publish and restarts at 0
   each tick, giving a total order within the tick and a stable global order
   across ticks.
3. Dispatch drains in FIFO order. Events published *during* dispatch are appended
   and drained in the same tick — this is what lets `sim.delay` answer a cause
   event with a `DelayEvent` on the same tick. A queue still not empty after
   `MAX_EVENT_CASCADE_PASSES` passes is a broken invariant: throw.
4. For one event, handlers run in **registry order of the subscribing system**
   (§8.5) — never subscription order, never dictionary order.

`MAX_EVENTS_PER_TICK` is an invariant, not a rate limiter: exceeding it throws. It
exists to catch a module that starts emitting per-entity-per-tick chatter, which
would silently consume the whole core budget. See the emission discipline in
`10-events.md` §10.3.

Events are **not saved**. They are derived within a tick and must never be the
sole carrier of state.

---

## 8.7 Commands

The `Command` shape is fixed by `01-architecture.md` and reproduced unchanged.
`sim.core` adds ordering and admission only.

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

- **Admission.** Admitted only if
  `cmd.Tick >= CurrentTick + COMMAND_MIN_LEAD_TICKS`. Anything later is rejected
  as `TooLate` and is *never* silently re-dated. Re-dating is the classic way a
  replay diverges from the session it replays.
- **Total order.** Due commands apply sorted by `(Tick, Issuer, Sequence)`.
  `Sequence` is assigned monotonically at admission by the queue, not by the
  caller. With one player this is already deterministic; defining it now keeps the
  loop from baking in the absence of multiplayer.
- **Validation at admission**, not at application. A command in the log is by
  definition well-formed.
- **The log is the save.** `LogSince` plus a snapshot reproduces a session
  (`01-architecture.md`). Core retains the log since the last snapshot; trimming
  is `sim.save`'s call.
- Application is dispatched to the owning system through an interface that system
  publishes. `sim.core` knows command kinds, never their meaning.

`CommandKind` is an enum in `sim.core`, extended only by spec amendment. Phase 0
defines only `NoOp`, used by the determinism harness to prove the queue
participates in the hash.

---

## 8.8 RNG

Satisfies `02-determinism.md` rules 2 and 3.

```
interface IRandomService {
  IRandomStream Stream(RngStreamName name)
  uint64        MasterSeed { get }
}

interface IRandomStream {
  uint64 NextUInt64()
  int32  NextInt(int32 minInclusive, int32 maxExclusive)    // unbiased
  Fx     NextFx01()                                         // [0, 1)
  bool   Chance(Fx probability)
  void   Shuffle<T>(Span<T> items)                          // Fisher-Yates, descending
  uint64 ComputeStateHash()
}
```

Pinned algorithms. These are part of the save format in effect, so changing one is
a migration, not a refactor:

- Generator: **xoshiro256\*\***, 256-bit state.
- Stream seeding: `SplitMix64` seeded with `MasterSeed XOR FNV-1a-64(streamName)`,
  drawn four times to fill the state. An all-zero state is replaced by the next
  SplitMix64 output, since xoshiro's zero state is degenerate.
- `NextInt`: Lemire multiply-shift with rejection, so the bound does not bias the
  stream and equal bounds consume equal draws.
- `NextFx01`: top 32 bits of one draw, uniform over multiples of 2^-32.

Stream names are `"<module>.<purpose>"`, declared as constants by the owning
module and unique across the build (CI asserts uniqueness). A stream is owned by
exactly one system; sharing one across systems reintroduces exactly the coupling
rule 3 exists to prevent.

Stream state is saved and is part of the owning system's hash.

---

## 8.9 State hashing

Satisfies `02-determinism.md`, "State hashing".

```
interface IStateHasher {
  void   Feed(uint64 v)
  void   Feed(int64 v)
  void   Feed(in Fx v)                      // feeds Raw
  void   Feed(bool v)
  void   Feed(ReadOnlySpan<byte> v)
  uint64 Result { get }
}
```

- Algorithm: **FNV-1a-64** over the fed byte stream, little-endian. Chosen for
  being trivially reimplementable by hand when a divergence has to be debugged.
- A system feeds its serialisable state in a **declared, stable order**. Ordered
  collections feed in sequence order; unordered ones are sorted by a stable key
  first (`02-determinism.md` rule 5). Feeding a dictionary directly is a
  code-review rejection.
- Derived or cached values are **not** fed. If a cache can disagree with its
  source that is a bug, and hashing it converts a clean test failure into a
  cross-machine hash mismatch.
- World hash: FNV-1a-64 over the tick followed by each system's
  `ComputeStateHash()` in registry order (§8.5).

```
readonly struct Checkpoint {
  Tick     Tick
  uint64   WorldHash
  uint64[] SystemHashes                     // registry order, §8.5
}

interface ICheckpointSink { void Record(in Checkpoint cp) }
```

The per-system array is the point: on a gate failure, the first disagreeing
checkpoint plus the first disagreeing system index names the culprit in seconds.
`02-determinism.md` asks for this early — it ships with T-004, not later.

---

## 8.10 Logging

```
interface ISimLog {
  void Write(Tick tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
}
```

- Every line carries the tick (`07-conventions.md`).
- `LogKey` is an enum; `LogArgs` holds integers, `Fx` and ids only. No string
  formatting inside the sim: it allocates on the hot path and invites float
  conversion.
- The sink is injected. Logging must not change outcomes: sim code never reads the
  log back and log volume never gates behaviour. Tests run the same scenario with
  a null sink and with a capturing sink and assert identical hashes.

---

## 8.11 Content access

```
interface IContentIndex {
  bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
  IReadOnlyList<ContentId> AllOf(ContentKind kind)      // sorted by id
}
```

- Content is immutable for the lifetime of a session and identical across
  machines, or determinism is void. Hot reload (`01-architecture.md`) is a
  development-build feature that restarts the session; it never mutates a running
  one.
- `AllOf` returns ids sorted by ordinal comparison, so iteration over content is
  stable regardless of file system enumeration order — a real drift source between
  Windows and Linux agents.
- The content hash (FNV-1a-64 over all loaded definitions in id order) goes in the
  save header, so a save opened against edited content fails loudly instead of
  drifting quietly.

---

## 8.12 What `sim.core` does not own

Stated because the temptation is obvious and the cost is a god-module:

- No entity storage, no component system, no scheduler for other modules' work.
- No spatial structures — that is `sim.world`.
- No knowledge of flights, passengers, money, or time-of-day semantics beyond the
  arithmetic in §8.2.
- No threading. `02-determinism.md` rule 6 permits parallelism with a deterministic
  merge; Phase 0 introduces none, and the first module that wants it files a
  question.
