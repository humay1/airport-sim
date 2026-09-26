# 08 — Public interfaces: `sim.core`

Implements the `sim.core` row of `03-module-map.md`. Nothing here may contradict
`01-architecture.md` or `02-determinism.md`; where this file appears to, those
files win and the contradiction is a spec bug — file it in `open-questions.md`.

**Notation.** Signatures are written in the language-neutral IDL below. This is a
*specification*, not code: the worker writes the C# that realises it. Access
modifiers, namespaces, file names and the IDL-to-C# mapping are fixed by
`07-conventions.md`, "Solution layout and build" (Q-013). Names given here are
binding. Anything not listed here is internal to the module and may not
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

In C# these are `public const` members of `public static class SimConstants`
(`07` L10), with the names above. `TICKS_PER_SIM_MINUTE`, `TICKS_PER_SIM_HOUR`,
`TICKS_PER_SIM_DAY`, `HASH_CHECKPOINT_TICKS` and `COMMAND_MIN_LEAD_TICKS` are
`ulong`, because they are tick arithmetic. The rest are `int` (Q-014).

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

### Tick numbering and clock arithmetic (Q-014)

- `Step(n)` executes ticks `CurrentTick`, `CurrentTick + 1`, …,
  `CurrentTick + n − 1`, in that order, and leaves `CurrentTick` larger by
  `n`. `CurrentTick` is therefore the number of ticks executed so far, which
  is also the number of the next tick to run. It is 0 after `Build`. The
  first `Step(1)` executes tick 0, with `ctx.Tick == 0`. `Step(0)` does
  nothing.
- During tick `t`, `ctx.Tick == ctx.Clock.CurrentTick == t`. Sim-day `d` is
  ticks `d · TICKS_PER_SIM_DAY` to `(d + 1) · TICKS_PER_SIM_DAY − 1`.
  `Step(TICKS_PER_SIM_DAY)` from a fresh host executes exactly day 0.
- **Chunking is invisible.** `Step(a)` followed by `Step(b)` is
  indistinguishable from `Step(a + b)` in every hash, checkpoint, event and
  log line.
- `DayIndex = CurrentTick / TICKS_PER_SIM_DAY` and
  `SecondOfDay = (CurrentTick % TICKS_PER_SIM_DAY) · SIM_SECONDS_PER_TICK`.
  A `DayIndex` above `uint32` range throws `OverflowException`.
- `MinutesBetween(a, b)` is `b − a` in sim-minutes, **signed**. It is
  negative when `b < a`, and order is never a reason to throw. It equals
  `Fx.FromRatio((int64)b − (int64)a, TICKS_PER_SIM_MINUTE)`, floored like
  every narrowing (§8.3). A tick above `int64` range throws
  `OverflowException`.
- `TickOfDayTime(d, s) = d · TICKS_PER_SIM_DAY + s / SIM_SECONDS_PER_TICK`,
  in integer division. A second that is not a multiple of
  `SIM_SECONDS_PER_TICK` floors to the tick that contains it.
  `s >= 86400` throws `ArgumentOutOfRangeException`.

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
Fx.FromRaw(int64 raw)                                // exact; saved state, hashing, tests (Q-015)
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

### C# shape and edge cases (Q-015)

- `public readonly struct Fx : IEquatable<Fx>, IComparable<Fx>` with
  `long Raw { get; }`. `Zero`, `One`, `MinValue` (`Raw = int64.MinValue`) and
  `MaxValue` (`Raw = int64.MaxValue`) are `public static readonly Fx` fields.
  `Fx` has **no public constructor**, an exception to `07` L10. `FromRaw` is
  the only way in from a raw value, and `default(Fx)` is `Zero`.
- Every operation in the list is a public **static** method taking its
  operands (`Fx.Add(a, b)`, `Fx.Clamp(x, lo, hi)`, `Fx.Floor(x)`), except
  `ToDisplayString(int decimals)`, which is an instance method.
- **Operators are required**, and each is exactly its static method: binary
  `+ - * /`, unary `-`, and `== != < <= > >=` comparing `Raw`. `Equals(Fx)`,
  `Equals(object)`, `GetHashCode()` and `CompareTo(Fx)` agree with `Raw`.
  `07` "Runtime portability" rule 2 still forbids using the hash code for
  anything. `ToString()` returns `ToDisplayString(10)`, for diagnostics only.
  There are **no** implicit or explicit conversion operators.
- **Semantics.** Below, `x` means the exact rational `Raw / 2^32`. Every
  result is floored toward −∞ to a multiple of 2^−32, and "out of range"
  means the floored result is outside `[MinValue, MaxValue]`. `Fx` never
  wraps and never saturates.

| Operation | Result | Throws |
|---|---|---|
| `FromInt(v)` | `v` | `OverflowException` unless −2^31 ≤ `v` ≤ 2^31 − 1 |
| `FromRaw(r)` | `Raw = r` | never |
| `FromRatio(n, d)` | `n / d` | `DivideByZeroException` if `d = 0`; `OverflowException` if out of range |
| `Add`, `Sub` | exact | `OverflowException` if out of range |
| `Mul` | `a · b` | `OverflowException` if out of range |
| `Div` | `a / b` | `DivideByZeroException` if `b = 0`; `OverflowException` if out of range |
| `Neg`, `Abs` | exact | `OverflowException` for `MinValue` |
| `Min`, `Max` | as named | never |
| `Clamp(x, lo, hi)` | `Max(lo, Min(x, hi))` | `ArgumentException` if `lo > hi` |
| `Floor` | ⌊x⌋ as `int64` | never |
| `Ceil` | ⌈x⌉ as `int64` | never |
| `RoundHalfUp` | ⌊x + ½⌋ as `int64`. Half goes toward +∞: −0.5 → 0, −1.5 → −1, 2.5 → 3 | never, `MaxValue` included |
| `Frac` | `x − Floor(x)`, in `[0, 1)` | never |
| `Sqrt` | `Raw = ⌊√(x.Raw · 2^32)⌋` exactly, the integer square root of the 96-bit product. This is what "exact-stable" means | `ArgumentOutOfRangeException` if `x < 0` |

- **`Parse` grammar.** The whole string, ASCII only, must match
  `-?(0|[1-9][0-9]*)(\.[0-9]{1,10})?`. There is no `+`, no whitespace, no
  leading or trailing dot, no exponent, no digit separator and no leading
  zero, and at most 10 fraction digits. `-0` and `-0.0` parse to `Zero`. The
  decimal value is taken exactly and then floored, so `"-0.1"` is
  `⌊−0.1 · 2^32⌋`, which is not the negation of `Parse("0.1")`. `null`
  throws `ArgumentNullException`. Any other mismatch throws
  `FormatException`, and an out-of-range value throws `OverflowException`.
- **`ToDisplayString(d)`**, with `d` from 0 to 10 (otherwise
  `ArgumentOutOfRangeException`). The value is floored to a multiple of
  10^−d (it is a `To*` conversion) and written in invariant ASCII: an
  optional `-`, then the integer digits with no leading zero (`0` when the
  integer part is zero), then, when `d > 0`, a `.` and exactly `d` digits. A
  result equal to zero has no sign. For example, `-0.25` gives `"-0.3"` at
  `d = 1`, and `0.25` gives `"0.2"`.
- **Test oracle.** Bit-exactness is proven by **golden vectors**: fixed
  inputs with their expected `Raw` outputs, committed as literals in the test
  and derived from the `Int128`/`BigInteger` oracle. A golden vector pins the
  result across processes, machines and time, so no child-process test
  exists for `Fx`. Cross-process determinism of the whole sim is
  `determinism_cross_process` (`ci/run-checks.sh`).

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

`SystemId.Value` is the registry position of §8.5, from 1 to 14. Position 8
is reserved. Value 0 is `SYSTEM_CORE = SystemId(0)`, the id `sim.core` uses
as an event `Source` and a log `system` for its own work. Neither 0 nor 8
can be registered or subscribe (Q-014).

Ids are values, never references, and carry no meaning in their bit pattern. Never
branch on a reference or a default hash code (`02-determinism.md` rule 7).

```
interface IIdAllocator { EntityId Next(SystemId owner) }
```

Counters are per-owner and part of saved state, so that adding an allocation in
one system cannot shift another system's ids — the same reasoning as per-system
RNG streams.

**Allocation rule (Q-017).** Each owner's counter starts at 0. `Next(owner)`
increments it and returns `EntityId((owner.Value << 48) | counter)`, so the
first id is counter 1 and ids never collide across owners. `EntityId(0)` is
never allocated. A counter above 2^48 − 1 throws `SimInvariantException`.
That overflow is untested by design, since no public API can set a counter
and 2^48 calls are infeasible in a test (Q-024). An
owner of 0, 8 or above 14 throws `ArgumentException`. Consumers never decode
the bit pattern, following the rule above. `Next` is callable during
construction and during phases 1–3.

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

**Registration rules (Q-014).** `Register` accepts a system whose `Id.Value` is
in 1–14, is not 8, and is strictly greater than every earlier registration.
Otherwise it throws `ArgumentException`, and a `null` system throws
`ArgumentNullException`. After `Build`, it throws `InvalidOperationException`.
`Name` is a diagnostic label and is not checked. Tests may register probe
systems at any legal position whose module is not in that build.

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

## 8.5a Broken invariants (Q-014)

```
class SimInvariantException : Exception {     // sealed
  Tick   Tick
  uint64 WorldHash                            // valid only if HasWorldHash
  bool   HasWorldHash
}
```

- A module that detects a broken invariant during a tick throws
  `new SimInvariantException(string message, Tick tick)`, which leaves
  `HasWorldHash` false. This is the only public constructor.
- **The host wraps.** Any exception that escapes phases 1–4 of tick `t` leaves
  `Step` as a new `SimInvariantException` with `Tick = t`, with
  `InnerException` set to the escaping exception, and with `WorldHash` computed
  at that moment (§8.9) and `HasWorldHash` true. The tick count fed into that
  hash is `t`, the number of ticks **completed**, because tick `t` did not
  complete. `CurrentTick` also stays `t`. The rest of the hash is the
  partial state as it stands, with nothing rolled back (Q-024). If computing the hash throws
  too, `HasWorldHash` is false. It wraps exactly once, even when the escaping
  exception is itself a `SimInvariantException`. `sim.core`'s own limits
  (§8.6) are thrown and wrapped the same way.
- After that, the host is unusable. `Step`, `TrySubmit` and
  `WorldStateHash` throw `InvalidOperationException`.
- Exceptions from calls made outside a tick (construction, `Register`,
  `Subscribe`, argument checks) are not wrapped.

---

## 8.6 Event bus

Events are immutable value types defined in `sim.core` (`03-module-map.md`). The
catalogue is `10-events.md`; this section defines only the transport.

```
interface IEventPublisher {
  EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
}

interface IEventBus : IEventPublisher {
  void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
}

delegate void SimEventHandler<T>(in EventEnvelope envelope, in T evt, in TickContext ctx)
  where T : struct, ISimEvent

interface ISimEvent {
  // marker; the struct holds payload fields only, 10-events.md §10.2
}
```

**Envelope and publication (Q-014).** An event struct holds only its payload
fields (`10-events.md` §10.6). The bus carries the envelope (`10` §10.2)
beside the struct and hands it to every handler. `Publish` copies the
payload and fills the envelope:

- `Id = (current tick, next Sequence)` and `Tick = current tick`.
- `Source` is the system whose code is running. In phase 1 that is the owner
  the command handler was registered with. In phase 2 it is the system whose
  `Tick` is running, and in phase 3 the subscriber whose handler is running.
  `sim.core`'s own events use `SYSTEM_CORE`.
- `Cause` is the argument. A root event passes `EventRef.None`, a
  `public static readonly` value with `HasValue` false.

`Publish` outside phases 1–3 of a tick throws `InvalidOperationException`.
`Subscribe` takes one handler per `(subscriber, T)`. A second one, a
subscriber of 0, 8 or above 14, or a `null` handler throws
`ArgumentException` (`ArgumentNullException` for `null`). A subscriber that is
not registered by the time of `Build` makes `Build` throw
`InvalidOperationException`. The name `EventHandler` is not used, because it
collides with `System.EventHandler<T>`.

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
   **Passes (Q-014):** pass 1 dispatches, in `Sequence` order, every event
   published in phases 1 and 2. The events published during pass `k` form
   pass `k + 1`. If any event is published during pass
   `MAX_EVENT_CASCADE_PASSES`, core throws `SimInvariantException` (§8.5a).
4. For one event, handlers run in **registry order of the subscribing system**
   (§8.5) — never subscription order, never dictionary order.

`MAX_EVENTS_PER_TICK` is an invariant, not a rate limiter: exceeding it throws
(the publish that would be number 4097 in the tick throws `SimInvariantException`). It
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

`CommandKind` is an enum in `sim.core`, extended only by spec amendment. `NoOp`
is used by the determinism harness to prove the queue participates in the
hash.

### Queue semantics (Q-020)

- **Surface.** `ICommandQueue` is the host's internal seam and is declared
  `internal`, an exception to `07` L5. Tests, the harness and `sim.save`
  reach it only through `ISimHost`:

```
interface ISimHost {                                   // additions to §8.5
  IReadOnlyList<Command> CommandLogSince(Tick tick)    // = ICommandQueue.LogSince
}
```

- **`Command` in C#.** `Payload` is `byte[]`. The constructor takes
  `(Tick, Issuer, Kind, Payload)`, throws `ArgumentNullException` for a
  `null` payload (use an empty array), and sets `Sequence = 0`. A caller
  cannot supply a `Sequence`. Admission **copies** the payload, so a caller
  mutating its array afterwards changes nothing.
- **Admission order** (`TrySubmit`): `TooLate`, then `NotPermitted` if
  `Issuer != PLAYER_LOCAL`, then `UnknownKind`, then the kind's check, which
  is the handler's `Validate` or, for `NoOp`, "payload length is 0, else
  `MalformedPayload`". `Validate` runs **exactly once** for a submit that
  reaches it, and never at application. On success, `reason = None`.
- `TrySubmit` during `Step` (from a handler or a system) throws
  `InvalidOperationException`. Commands come from outside the tick.
- **`Sequence`** is one counter for the whole session. The first admitted
  command gets 1, each admission adds 1, and a rejected submit consumes
  nothing. 0 means "not admitted".
- **`LogSince(t)`** returns every admitted command with `cmd.Tick >= t`,
  applied or still pending, in the total order `(Tick, Issuer, Sequence)`.
  It allocates, with fresh payload copies, since it is off the hot path.
  Core keeps every admitted command until `sim.save` specifies trimming.
- **Handler registration.** Each kind's owner is the Owner column of the
  payload table, mapped to its registry position (`SetServersOpen` → 4,
  `ReassignStand` → 3). `Register(owner, handler)` throws `ArgumentException`
  for an owner that does not match `handler.Kind`, for a handler of `NoOp`,
  or for a second handler of a kind. `ArgumentNullException` is thrown for
  `null`, and `InvalidOperationException` after `Build`. If an owner is not
  registered as a system by `Build`, `Build` throws
  `InvalidOperationException`.
- **Impossible at `Apply`.** The **handler** logs the no-op with its own
  module's `LogKey` (appended by amendment) at `LogLevel.Info`, and returns.
  Core does not catch. An exception out of `Apply` escapes the tick and is
  wrapped (§8.5a).

### Issuer, kinds and payloads (Q-010)

```
struct PlayerId { uint16 Value }           // PLAYER_LOCAL = 0, the only player at Phase 1
```

`PLAYER_LOCAL` is `public static readonly PlayerId PLAYER_LOCAL = new
PlayerId(0)` in `SimConstants`, and `SYSTEM_CORE` is likewise
`public static readonly SystemId SYSTEM_CORE` in `SimConstants`. A struct
cannot be `const` (`07` L10), and L10 keeps IDL names, so they are not
aliased as `PlayerId.Local` or `SystemId.Core`. Neither type gains a static
member (Q-023).

```

enum CommandKind : uint16 {                // values are saved in command logs: never renumbered
  NoOp           = 0,
  SetServersOpen = 1,
  ReassignStand  = 2
}
```

**Payload encoding.** Fixed layout, little-endian, fields in the order
listed, no padding, no length prefix. An id encodes its `Value` at its
declared width.

| Kind | Owner | Payload fields | Bytes | Semantics |
|---|---|---|---|---|
| `NoOp` | `sim.core` | none | 0 | none |
| `SetServersOpen` | `sim.flow` | `NodeId.Value : uint32`, `count : int32` | 8 | `09` §9.8 |
| `ReassignStand` | `sim.airside` | `FlightId.Value : uint64`, `StandId.Value : uint16` | 10 | `12` §12.10 |

A new kind is appended with the next value, together with its row here, by
amendment.

### Dispatch (Q-010)

```
interface ICommandHandler {
  CommandKind      Kind { get }
  CommandRejection Validate(ReadOnlySpan<byte> payload)       // at admission
  void             Apply(in Command cmd, in TickContext ctx)   // phase 1, at cmd.Tick
}

interface ICommandHandlerRegistry {
  void Register(SystemId owner, ICommandHandler handler)
}
```

- **Registration.** The owning system registers its handler through
  `SystemServices.Commands` (§8.11a), during construction only. It registers
  one handler per kind, and only for kinds whose Owner column names it. A
  duplicate, or a registration after `Build`, throws. `sim.core` handles
  `NoOp` itself.
- **Admission** (`TrySubmit`), in this order: `TooLate` (the tick rule
  above), then `UnknownKind` (no handler registered in this build), then the
  handler's `Validate`. `Validate` is a **pure function of the payload and
  the owner's load-time data**, for example "is this a known `Queue` node".
  It never reads runtime sim state. State can change between admission and
  application, and a pure check makes a `TrySubmit` result reproducible from
  its arguments. A wrong payload length is `MalformedPayload`. A well-formed
  target that can never accept the kind is `NotPermitted`.
- **Application.** At phase 1 of `cmd.Tick`, in the total order above, the
  handler's `Apply` runs with that tick's context. A command whose effect is
  impossible in the *current* state (the flight has left, the stand is
  taken) is a **deterministic no-op**, recorded through `ISimLog` with the
  tick. It is never thrown, following `07-conventions.md` "invalid states
  are data". `Apply` may publish events, which are dispatched in phase 3 as
  usual.

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

```
RandomServiceFactory.Create(uint64 masterSeed) -> IRandomService   // Q-019; Build uses it too
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

```
readonly struct RngStreamName { string Value }   // ordinal equality; Q-014
```

Stream names are `"sim.<module>.<purpose>"`, declared as constants by the owning
module and unique across the build by construction ("Names" below). A stream is owned by
exactly one system; sharing one across systems reintroduces exactly the coupling
rule 3 exists to prevent.

Stream state is saved and is part of the owning system's hash.

### Exact reference (Q-019)

Binding bit for bit. The golden vectors below are the test oracle.

- **Stream seed.** `seed = MasterSeed XOR FNV1a64(utf8(name.Value))`. This is
  plain FNV-1a-64 (§8.9 constants) over the name's UTF-8 bytes, with **no**
  length prefix.
- **SplitMix64**, the standard one:
  `x += 0x9E3779B97F4A7C15; z = x; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
  z = (z ^ (z >> 27)) * 0x94D049BB133111EB; return z ^ (z >> 31)`, all mod
  2^64. Seeded with `seed`, its first four outputs are `s[0]`, `s[1]`,
  `s[2]`, `s[3]`, in that order. While all four are 0, `s[0]` is replaced by
  the next output.
- **xoshiro256\*\* 1.0**, the standard one:
  `r = rotl(s[1] * 5, 7) * 9; t = s[1] << 17; s[2] ^= s[0]; s[3] ^= s[1];
  s[1] ^= s[2]; s[0] ^= s[3]; s[2] ^= t; s[3] = rotl(s[3], 45); return r`.
  `NextUInt64` is one step. Check: state `{1, 2, 3, 4}` yields 11520, 0,
  1509978240, 1215971899390074240. This check is an aid for the implementer,
  not a test obligation (Q-023).
- **`NextInt(min, max)`**: `min >= max` throws `ArgumentOutOfRangeException`.
  Otherwise let `range = (uint32)(max − min)`. Take
  `x = (uint32)(NextUInt64() >> 32)`, `m = (uint64)x * range`,
  `l = (uint32)m`. If `l < range`, set `t = (uint32)(0 − range) % range`,
  and while `l < t` draw a new `x` and recompute `m` and `l`. Return
  `min + (int32)(m >> 32)`. This is Lemire's method on the **top 32 bits**
  of each draw.
- **`NextFx01()`** = `Fx.FromRaw((int64)(NextUInt64() >> 32))`.
- **`Chance(p)`** = `NextFx01() < p`. It is **always exactly one draw**. A
  `p <= 0` gives false and `p >= 1` gives true, and neither is an error.
- **`Shuffle(items)`**: for `i` from `n − 1` down to 1, `j = NextInt(0, i + 1)`,
  then swap `items[i]` and `items[j]`. `n <= 1` draws nothing.
- **Stream hash.** `ComputeStateHash()` is a fresh `StateHasher` (§8.9) fed
  `s[0]`, `s[1]`, `s[2]`, `s[3]` as `uint64`. The owning system feeds that
  value into its own hash.
- **`Stream(name)`** returns the **same live stream** every time it is called
  with that name in a session. It is created at the first call, and later
  calls neither allocate nor reset it.
- **Names.** The `RngStreamName` constructor throws `ArgumentNullException`
  for a `null` value, and `ArgumentException` unless the **whole** value
  matches `sim\.[a-z]+\.[a-z0-9_]+`, where the middle segment is the owning
  module. "Whole" means anchored at both ends with nothing after the last
  character, as `\A…\z`, so a trailing newline is malformed (Q-023).
  `Stream(default(RngStreamName))` throws `ArgumentException`. Uniqueness across
  modules follows from the prefix, and within a module it is that module's
  own test. This replaces "CI asserts uniqueness", for which no mechanism
  existed. There is no build-wide uniqueness check.
- **What is tested (Q-023).** The golden vectors and the `NextInt`/`NextFx01`
  checks below are the **sole required proof** of seeding and generation,
  and they are reached only through `RandomServiceFactory.Create` and
  `Stream`. There is no test seam that sets a raw state, and no task adds
  one. The `{1, 2, 3, 4}` check cannot be reached through the public API. The
  all-zero replacement cannot be reached at all: SplitMix64's output
  function is a bijection of its counter, and the counter never repeats
  within 2^64 steps, so four consecutive outputs are never all 0. The
  replacement stays in the reference so that the pinned algorithm is the
  standard one. Neither is tested, by design. Bit-exactness across processes
  and machines is proven by the committed vectors, not by a child process
  (as for `Fx`, Q-015 A16).
- **Cost (Q-023).** There is no per-call time budget. After the first
  `Stream(name)` call for a name, `Stream` and every `IRandomStream` member
  allocate nothing, `Shuffle` included. The time a draw takes is charged to
  the budget of the system that makes it (`03`, "Performance"), since draws
  happen inside that system's `Tick`. It is not charged to `sim.core`'s
  0.25 ms.
- **Save seam.** None yet. Exporting and importing stream state belongs to
  `sim.save`, which is unspecified. No task invents one.

Golden vectors, the first four `NextUInt64` outputs in hex:

| MasterSeed | Name | FNV1a64(name) | Outputs |
|---|---|---|---|
| 0 | `sim.flow.showup` | `80AA6E48500EF830` | `4D8ADDC1EA523EA8`, `881053D9C83E81EC`, `9F943EEE723DAD43`, `41FB063846C7BC01` |
| 12345 | `sim.schedule.jitter` | `09F750F58CE1F6D5` | `38C30AB4838B2ECE`, `20E490881273F31A`, `160AF506335E076A`, `3A5487F2EF5EDE58` |
| 2^64 − 1 | `sim.airside.taxi` | `C6381A17FBE07F29` | `BAF003FC5983A4B7`, `54E24678B7DA92B8`, `C6C76D95A0A72034`, `F635BA852278C41C` |

For the first row, a fresh stream's `NextInt(0, 10)` × 4 is 3, 5, 6, 2. On
another fresh stream, `NextFx01()` × 2 has `Raw` 1300946369, 2282771417.

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
- World hash: FNV-1a-64 over the tick, the core section and each system's
  `ComputeStateHash()` in registry order (§8.5). The exact layout is below
  (Q-017).

```
readonly struct Checkpoint {
  Tick     Tick
  uint64   WorldHash
  uint64   CoreHash                         // Q-017
  uint64[] SystemHashes                     // registry order, §8.5
}

interface ICheckpointSink { void Record(in Checkpoint cp) }
```

### Encoding, the concrete hasher and the core section (Q-017)

- **FNV-1a-64**: `h = 0xCBF29CE484222325`, then for each byte
  `h = (h XOR byte) * 0x100000001B3` mod 2^64.
- **`StateHasher`** is the one implementation, a `public struct StateHasher :
  IStateHasher`. `new StateHasher()` and `default(StateHasher)` are both a
  fresh hasher, whose `Result` is the offset basis. Systems use the struct
  directly. Through the interface it would box. `Result` may be read at any
  time without changing the state. The struct is mutable, an exception to
  `07` L10.
- **Encoding.** `Feed(uint64)` and `Feed(int64)` write 8 bytes,
  little-endian (two's complement for `int64`). `Feed(in Fx)` is
  `Feed(Raw)`. `Feed(bool)` writes 1 byte, 0 or 1.
  `Feed(ReadOnlySpan<byte>)` writes the length as a `uint64` first, then the
  bytes, so splitting a span differently changes the hash. Every narrower
  integer, enum and id value is widened to 64 bits (sign-extended when
  signed) and fed as 8 bytes. A string (`ContentId`, `RngStreamName`) is fed
  as the span of its UTF-8 bytes.
- **Golden vectors.** Fresh: `CBF29CE484222325`. `Feed(0UL)`:
  `A8C7F832281A39C5`. `Feed(1UL); Feed(-1L); Feed(true)`: `9185A69DA7E88AC7`.
  `Feed([1, 2, 3])`: `01EF76D429B11552`.
- **The core section.** `sim.core` is not a system, but it holds state that
  changes outcomes. `CoreHash` is a fresh `StateHasher` fed, in this order:
  1. the next command `Sequence` to assign;
  2. the number of **pending** commands (admitted and not yet applied),
     then each pending command in `(Tick, Issuer, Sequence)` order, as
     `Tick`, `Issuer.Value`, `Kind`, `Sequence` and then `Payload` (a span);
  3. the number of owners whose id counter is non-zero, then, for each in
     ascending `SystemId`, the owner's `Value` and its counter.

  Applied commands are not fed: they live on in the systems' state and in
  the sequence counter. That counter is what makes a `NoOp` visible in the
  hash. RNG streams are not in the core section. Each is in its owner's
  hash (§8.8).
- **World hash** = a fresh `StateHasher` fed the ticks-executed count, then
  `CoreHash`, then each registered system's `ComputeStateHash()` in registry
  order, all as `uint64`. `Checkpoint.CoreHash` carries the core section so
  that a divergence in it is named as quickly as a system's.
- `SystemHashes[i]` belongs to the i-th registered system. There is no
  separate id array, because the harness knows what it registered.
- **Ownership.** T-001 ships `StateHasher` and the world hash with the core
  section as it exists at T-001 (no pending commands, sequence 1, and no
  counters unless `IIdAllocator` is implemented). T-004 proves the
  golden vectors and the per-system hashing discipline.

**Tick fed, cadence and contents (Q-014).** The tick fed into the world hash
is the number of ticks executed at that moment (`ISimHost.CurrentTick`), as a
`uint64`. In phase 4 of tick `t`, if `t % HASH_CHECKPOINT_TICKS == 0`, the
host calls `Record` once with `Tick = t`. `WorldHash` is the world hash with
`t + 1` fed, so it equals `WorldStateHash()` read as soon as the `Step` that
ran tick `t` returns. `SystemHashes` has one entry per **registered** system,
in registry order, and the host allocates a fresh array for each checkpoint,
which the sink may keep. A sim-day therefore records 24 checkpoints, at
`t = 0, 600, …, 13 800`. There is no checkpoint at `Build`.

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

```
enum LogLevel : byte   { Debug = 0, Info = 1, Warning = 2, Error = 3 }
enum LogKey   : uint16 { None = 0 }             // appended by amendment; never renumbered
readonly struct LogArgs { int32 Count; int64 A0; int64 A1; int64 A2; int64 A3 }
```

(Q-014) `LogArgs` has public constructors taking 1, 2, 3 or 4 `int64` values,
which set `Count` to the number passed, and `default(LogArgs)` has
`Count = 0`. This is an exception to `07` L10's single constructor. An `Fx`
is passed as its `Raw`. An id is passed as its `Value`, reinterpreted as
`int64` with `unchecked`. The key's amendment defines what each slot means.
A new `LogKey` is appended with the module that writes it.

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
- `TryGet<T>(id, out d)` returns true, with `d` the definition, if and only
  if a definition with that id exists and is a `T`. Otherwise it returns
  false with `d = default`, and a definition of another type is not an
  error. `T = IContentDefinition` is legal and matches any definition. An
  `id` whose `Value` is `null` throws `ArgumentException` (Q-028).
- `AllOf` returns ids sorted by ordinal comparison, so iteration over content is
  stable regardless of file system enumeration order — a real drift source between
  Windows and Linux agents.
- The content hash (FNV-1a-64 over all loaded definitions in id order) goes in the
  save header, so a save opened against edited content fails loudly instead of
  drifting quietly.

### Definition types (Q-011)

The Phase 0/1 set. These are `sim.core` types for the same reason event
payloads are: several modules read each one. A new kind is appended by
amendment, together with its row in `04-data-schemas.md`.

```
readonly struct ContentId { string Value }     // ordinal equality and order; hashed as its UTF-8 bytes

enum ContentKind { SizeCategory, Aircraft, PaxProfile, QueueProfile }

interface IContentDefinition { ContentId Id { get }; ContentKind Kind { get } }

readonly struct SizeCategoryDefinition : IContentDefinition { ContentId Id; int32 Ordinal }
readonly struct AircraftDefinition     : IContentDefinition { ContentId Id; ContentId SizeCategory }

readonly struct ShowUpBucket { uint32 MinutesBeforeStd; uint32 SharePermille }
readonly struct PaxProfileDefinition   : IContentDefinition {
  ContentId Id
  Fx        WalkSpeedMps                       // 09 §9.6
  IReadOnlyList<ShowUpBucket> ShowUpCurve      // 11 §11.6
}

readonly struct QueueProfileDefinition : IContentDefinition {
  ContentId     Id
  Fx            ServiceRatePerServerPerMinute  // 09 §9.4
  int32         CapacityStanding               // 09 §9.5
  Fx            ThresholdWaitMinutes           // QueueThresholdExceeded, 10 §10.6
  Fx            HysteresisMinutes              // cleared below threshold − hysteresis, 10 §10.3 rule 4
  DelayCategory Category                       // security_queue | immigration_queue
}
```

Ids are unique across **all** kinds. `ContentKind` is fixed by the definition
type.

### The loader (Q-011)

```
interface IContentSource {
  IReadOnlyList<string> Files()            // paths relative to data/, '/'-separated, any order
  byte[]                ReadAll(string path)
}

interface IContentLoader {
  IReadOnlyList<IContentDefinition> Load(IContentSource source)
}

ContentLoaderFactory.Create() -> IContentLoader
```

- **Directories to kinds:** `size_categories/`, `aircraft/`, `pax_profiles/`
  and `queue_profiles/`, one definition per `*.json` file. Every other
  directory (`schemas/`, `policies/`, `balance/`, ...) is ignored by this
  loader at Phase 0/1.
- **Order:** files are read in ordinal path order, whatever `Files()`
  returns, so the result never depends on file-system enumeration.
- **Format:** a strict JSON subset, hand-parsed inside `sim.core`. There is
  no package (`07` "Runtime portability" rule 7), and no floating point
  anywhere. The file is UTF-8 without a BOM and may contain objects, arrays,
  strings, integers, `true` and `false`. A number with a fraction or an
  exponent is a load failure: fixed-point values are **decimal strings**,
  read with `Fx.Parse` (`04-data-schemas.md`). Duplicate keys, unknown keys,
  missing keys and `schema_version != 1` are load failures.
- **Validation**, each a hard failure naming the path and the field
  (`07-conventions.md`): the field rules of `04-data-schemas.md`; ids unique
  across all files; `AircraftDefinition.SizeCategory` resolves; size
  ordinals unique; `ShowUpCurve` per `11` §11.6; `WalkSpeedMps > 0`;
  `ServiceRatePerServerPerMinute >= 0`; `CapacityStanding > 0`;
  `0 <= HysteresisMinutes < ThresholdWaitMinutes`; `Category` is
  `security_queue` or `immigration_queue`.
- The output goes to `ContentIndexFactory.Create` (§8.11a). Tests may skip the
  loader and build definitions directly.

---

## 8.11a Construction (Q-009)

How a sim is assembled. The same surface serves `app.host` (`16` §16.4),
`tools.simharness` and every integration test. There is no other way to
construct a system.

```
readonly struct SimHostConfig {
  uint64          MasterSeed
  IContentIndex   Content
  ICheckpointSink Checkpoints
  ISimLog         Log
}

readonly struct SystemServices {           // what a module factory may receive from core
  IEventBus               Events           // Subscribe during construction only
  IIdAllocator            Ids
  IContentIndex           Content          // read-only; load-time validation
  ICommandHandlerRegistry Commands         // §8.7; register during construction only
}

interface ISimHostBuilder {
  SystemServices Services { get }
  void     Register(ISimSystem system)     // strictly ascending registry position (§8.5)
  ISimHost Build()                         // once
}

SimHostFactory.CreateBuilder(in SimHostConfig config) -> ISimHostBuilder
ContentIndexFactory.Create(IReadOnlyList<IContentDefinition> definitions) -> IContentIndex
```

- **Factories.** Every module publishes exactly one `<Module>Factory` of
  stateless static methods, named in its interface file's "Construction"
  section. These are the only static members a module publishes. They hold no
  state, cache nothing, and read nothing but their arguments. This is the one
  permitted exception to `CLAUDE.md`'s "no hidden statics", and it is not
  hidden.
- **Inputs.** A factory takes `SystemServices`, the module's validated
  construction data, and the downward interfaces the module calls. Nothing
  else: no service locator, no registry lookup, no file path.
- **Order.** Construct in dependency order (a module's downward interfaces
  must exist first), then `Register` in registry order. `Register` out of
  order, twice for one `SystemId`, or after `Build` throws. So does
  `Subscribe` after `Build`.
- **No nulls (Q-014).** Every reference member of `SimHostConfig` is
  non-null, and `CreateBuilder` throws `ArgumentNullException` otherwise.
  `sim.core` publishes no null or empty implementation of `IContentIndex`,
  `ICheckpointSink` or `ISimLog`. Tests implement these public interfaces
  themselves, or use `ContentIndexFactory.Create` with an empty list.
  `TickContext.Content` and `TickContext.Log` are the config's instances.
- `Build` creates the RNG service from `MasterSeed` (§8.8), wires the
  checkpoint and log sinks, and returns the host at tick 0. A builder cannot
  be reused after `Build`.
- `ContentIndexFactory.Create` sorts definitions by ordinal id and throws on
  a duplicate id. A `null` list throws `ArgumentNullException`. A `null`
  element, an element whose `Id.Value` is `null`, or a duplicate id throws
  `ArgumentException`. The input list is copied, so later changes to it
  change nothing (Q-028). Parsing `data/` files into definitions is §8.11's
  `IContentLoader` (Q-011).
- **A module that is not registered is also not constructed.** Callers pass
  `null` for an optional downward interface. The module's own spec says what
  it does then, for example `11` §11.6 and `12` §12.8.

## 8.12 What `sim.core` does not own

Stated because the temptation is obvious and the cost is a god-module:

- No entity storage, no component system, no scheduler for other modules' work.
- No spatial structures — that is `sim.world`.
- No knowledge of flights, passengers, money, or time-of-day semantics beyond the
  arithmetic in §8.2.
- No threading. `02-determinism.md` rule 6 permits parallelism with a deterministic
  merge; Phase 0 introduces none, and the first module that wants it files a
  question.
