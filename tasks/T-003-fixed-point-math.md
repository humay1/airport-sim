# T-003 — Fixed-point math type `Fx`

| Field | Value |
|---|---|
| Status | MERGED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | — |
| Spec source | `spec/02-determinism.md` rule 4; `spec/08-interfaces-core.md` §8.3 (as amended, Q-015); `spec/07-conventions.md` "Solution layout and build" L2, L8 (Q-013) |
| Blocked by | — |

**Status note:** released for this cycle; the Test Author is authoring
`tests/sim/core/**` for this task now. Status stays `QUEUED` until those
tests land, then moves to `TESTS_AUTHORED` and the worker may start. Do not
begin implementation before that move.

## Writable paths

```
src/sim/core/**
AirportSim.sln
```

**Correction (Q-021):** `tests/**` is the Test Author's territory
exclusively (`07-conventions.md` "Solution layout and build"); the path
guard already blocks a worker grant there, and the Test Author, not this
task, writes `Fx`'s test file — this task's earlier grant of
`tests/sim/core/**` is dropped. (The byte-for-byte `AirportSim.Sim.Core.Tests.csproj`
this task's tests need is still created by the Test Author per `07` L3/L8,
in the same branch that carries the tests, per `07` L9.)

This task writes only new files (`Fx.cs`) and does not touch
any other file T-001 or T-002 own inside `src/sim/core/**`. **Correction
(Q-013/Q-014, `07` L8):** T-003 now runs first, before T-001 — `T-003 first,
then T-001 (Q-014)` — because `ISimClock.MinutesBetween` returns
`SimMinutes = Fx`, so T-001 cannot compile without this task. T-003 is not
independent of T-001 the way the original text here claimed; it is simply
first, with nothing of its own to depend on. This task **creates
`AirportSim.sln`** (`07` L8: "the first task to merge a production project,
which is T-003. It runs `dotnet new sln --name AirportSim` and adds
`src/sim/core` and `tests/sim/core`") and **creates
`src/sim/core/AirportSim.Sim.Core.csproj`** byte for byte per `07` L2 — the
first `sim.core` task that finds it absent adds it, which in practice is
this task. Do not release this task concurrently with any other task that
also touches `Fx.cs`, `AirportSim.sln`, or the `sim.core`/`sim.core.Tests`
project files.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`

## Interface to implement

```
struct Fx { int64 Raw }             // value = Raw / 2^32, Q31.32

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

**C# shape (Q-015, binding, copied not paraphrased):** `public readonly
struct Fx : IEquatable<Fx>, IComparable<Fx>` with `long Raw { get; }`.
`Zero`/`One`/`MinValue`/`MaxValue` are `public static readonly Fx` fields.
`Fx` has **no public constructor** (an exception to `07` L10) — `FromRaw` is
the only way in from a raw value, and `default(Fx)` is `Zero`. Every
operation is a public **static** method taking its operands, except
`ToDisplayString(int decimals)`, which is an instance method. Operators are
required and match their static method exactly: binary `+ - * /`, unary
`-`, and `== != < <= > >=` comparing `Raw`. `Equals`/`GetHashCode`/
`CompareTo` agree with `Raw`. `ToString()` returns `ToDisplayString(10)`,
diagnostics only. There are no implicit or explicit conversion operators.

Binding rules (`08-interfaces-core.md` §8.3):

- `Mul` and `Div` compute through a 128-bit intermediate, then narrow. No
  64-bit intermediate anywhere.
- **The 128-bit arithmetic, and any leading-zero count, is hand-rolled**
  (D1, `08` §8.3). The sim targets `netstandard2.1`, which has no
  `Int128`/`UInt128`, no `Math.BigMul(long, long, out long)` and no
  `System.Numerics.BitOperations`. Write the 64×64→128-bit multiply behind
  `Mul`, the 128-by-64-bit division behind `Div`, and `Sqrt`'s leading-zero
  count in plain integer code over `uint64` halves inside `Fx`. Do **not**
  use `BigInteger` either — it allocates, and it would be a second
  implementation to keep bit-exact. Check overflow and sign cases
  (including `long.MinValue / -1`) explicitly, before any BCL operator could
  throw; behaviour must never depend on which exception a runtime raises.
  **Test projects target `net8.0` and may use `Int128`/`BigInteger` as a
  reference oracle for `Fx`'s own tests only** — that code never ships in
  `src/sim/**`.
- Rounding is truncation toward negative infinity for every narrowing
  operation (`Mul`, `Div`, all `To*` conversions), with no per-call-site
  exception. `RoundHalfUp` is the only explicit half-up path.
- Division by zero and overflow on narrowing are programmer errors: throw,
  and the exception must carry the tick number per `07-conventions.md` (this
  type has no tick context itself — callers in `TickContext`-bearing code
  are responsible for surfacing it there; this task's own throw need not
  include a tick since `Fx` has none).
- **Exact exception types per operation (Q-015, binding):** `FromInt`
  throws `OverflowException` unless `-2^31 <= v <= 2^31-1`; `FromRaw` never
  throws; `FromRatio` throws `DivideByZeroException` if `d = 0`,
  `OverflowException` if out of range; `Add`/`Sub`/`Mul` throw
  `OverflowException` if out of range; `Div` throws `DivideByZeroException`
  if `b = 0`, `OverflowException` if out of range; `Neg`/`Abs` throw
  `OverflowException` for `MinValue`; `Min`/`Max` never throw; `Clamp`
  throws `ArgumentException` if `lo > hi`; `Floor`/`Ceil`/`RoundHalfUp`
  never throw; `Frac` never throws; `Sqrt` throws
  `ArgumentOutOfRangeException` if `x < 0`. `RoundHalfUp` rounds half toward
  `+∞` (`-0.5 → 0`, `-1.5 → -1`, `2.5 → 3`). `Sqrt`'s result is
  `Raw = floor(sqrt(x.Raw * 2^32))` exactly — the integer square root of the
  96-bit product, which is what "exact-stable" means.
- **`Parse` grammar (Q-015):** the whole string, ASCII only, matches
  `-?(0|[1-9][0-9]*)(\.[0-9]{1,10})?` — no `+`, no whitespace, no leading or
  trailing dot, no exponent, no digit separator, no leading zero, at most 10
  fraction digits. `-0`/`-0.0` parse to `Zero`. The decimal value is taken
  exactly then floored (so `"-0.1"` is not the negation of `Parse("0.1")`).
  `null` throws `ArgumentNullException`; any other mismatch throws
  `FormatException`; an out-of-range value throws `OverflowException`.
- **`ToDisplayString(d)` (Q-015):** `d` from 0 to 10, else
  `ArgumentOutOfRangeException`. Floors to a multiple of `10^-d`, invariant
  ASCII, optional `-`, no leading zero (`"0"` when the integer part is
  zero), then `.` and exactly `d` digits when `d > 0`. Zero has no sign.
- Deliberately absent at Phase 0: trigonometry, exponentials, logarithms,
  `Lerp`, vector types. Do not add them speculatively.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. **Correction (Q-015 A16): this task's earlier
text calling for "a bit-exactness test comparing results across two
independent process runs" is stale and is withdrawn.** There is no
child-process test for `Fx`. Bit-exactness is proven by **golden vectors**:
fixed inputs with their expected `Raw` outputs, committed as literals in the
test and derived from the `Int128`/`BigInteger` oracle — a golden vector
pins the result across processes, machines and time. (Cross-process
determinism of the whole sim is `determinism_cross_process`, T-006's job,
not this task's.) Expect: round-trip precision tests, truncation-direction
tests for negative values, the exact exception-type table of §8.3
(`OverflowException`/`DivideByZeroException`/`ArgumentException`/
`ArgumentOutOfRangeException`, per operation, asserted by type), the golden-vector
bit-exactness tests described above, `Parse` grammar tests (the exact regex
`-?(0|[1-9][0-9]*)(\.[0-9]{1,10})?`, `-0`/`-0.0` to `Zero`, `null` throwing
`ArgumentNullException`), `ToDisplayString` rounding/sign tests, and a test
cross-checking `Mul`/`Div`/`Sqrt` against an `Int128`/`BigInteger` oracle
over a wide random range (`07-conventions.md`: seeded, not unseeded, per
`07` L4's SplitMix64 property-test convention). **Do not edit them.**

## Performance budget

Counted within `sim.core`'s `0.25` ms/tick (`spec/03-module-map.md`). Zero
allocation for all operations — `Fx` is a value type; boxing anywhere is a
rejection.

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

Money is explicitly **not** `Fx` (`08-interfaces-core.md` §8.3) — currency is
`int64` minor units owned by `sim.economy`, which does not exist yet. Do not
add currency helpers here.
