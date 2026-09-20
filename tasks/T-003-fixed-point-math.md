# T-003 — Fixed-point math type `Fx`

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | — |
| Spec source | `spec/02-determinism.md` rule 4; `spec/08-interfaces-core.md` §8.3 |
| Blocked by | — |

## Writable paths

```
src/sim/core/**, tests/sim/core/**
```

This task writes only new files (`Fx.cs` and its tests) and does not touch
files T-001 or T-002 own. It has no declared dependency and may run
concurrently with T-001 for that reason; do not release it concurrently with
any other task that also touches `Fx.cs`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`

## Interface to implement

```
struct Fx { int64 Raw }             // value = Raw / 2^32, Q31.32

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

Binding rules (`08-interfaces-core.md` §8.3):

- `Mul` and `Div` compute through a 128-bit intermediate, then narrow. No
  64-bit intermediate anywhere.
- Rounding is truncation toward negative infinity for every narrowing
  operation (`Mul`, `Div`, all `To*` conversions), with no per-call-site
  exception. `RoundHalfUp` is the only explicit half-up path.
- Division by zero and overflow on narrowing are programmer errors: throw,
  and the exception must carry the tick number per `07-conventions.md` (this
  type has no tick context itself — callers in `TickContext`-bearing code
  are responsible for surfacing it there; this task's own throw need not
  include a tick since `Fx` has none).
- Deliberately absent at Phase 0: trigonometry, exponentials, logarithms,
  `Lerp`, vector types. Do not add them speculatively.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect: round-trip precision tests,
truncation-direction tests for negative values, overflow/div-by-zero throw
tests, and a bit-exactness test comparing results across two independent
process runs (feeding the determinism gate `determinism_same_process`
indirectly). **Do not edit them.**

## Performance budget

Counted within `sim.core`'s `0.25` ms/tick (`spec/03-module-map.md`). Zero
allocation for all operations — `Fx` is a value type; boxing anywhere is a
rejection.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

Money is explicitly **not** `Fx` (`08-interfaces-core.md` §8.3) — currency is
`int64` minor units owned by `sim.economy`, which does not exist yet. Do not
add currency helpers here.
