# T-002 — Seeded RNG service with per-system named streams

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/02-determinism.md` rules 2, 3; `spec/08-interfaces-core.md` §8.8, "Exact reference" (Q-019) |
| Blocked by | — |

## Writable paths

```
src/sim/core/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory exclusively
(`07-conventions.md` "Solution layout and build"); the path guard already
blocks a worker from writing there, so a worker grant there is a no-op. This
task's earlier grant of `tests/sim/core/**` is dropped.

Anything else is read-only. Writing outside these paths is an automatic
rejection.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`

## Interface to implement

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

RandomServiceFactory.Create(uint64 masterSeed) -> IRandomService   // Q-019; SimHostFactory.Build uses it too

readonly struct RngStreamName { string Value }   // ordinal equality
```

Pinned algorithms (`08-interfaces-core.md` §8.8 "Exact reference", Q-019),
binding bit for bit, part of the save format — copied not paraphrased:

- Generator: xoshiro256\*\*, 256-bit state.
- Stream seeding: SplitMix64 seeded with `MasterSeed XOR FNV-1a-64(streamName)`,
  drawn four times to fill state. An all-zero state is replaced by the next
  SplitMix64 output.
- `NextInt`: Lemire multiply-shift with rejection.
- `NextFx01`: top 32 bits of one draw, uniform over multiples of 2^-32.
- **`RngStreamName` format rule (Q-019, binding):** the constructor throws
  `ArgumentException` unless `Value` matches `sim\.[a-z]+\.[a-z0-9_]+`,
  where the middle segment is the owning module. Uniqueness across modules
  follows from the prefix, and within a module it is that module's own
  test — this **replaces** the earlier "CI asserts uniqueness" framing, for
  which no mechanism existed; do not add a cross-module CI check yourself.
- **Exact reference (Q-019, this task's test oracle):**
  - Stream seed: `seed = MasterSeed XOR FNV1a64(utf8(name.Value))`, plain
    FNV-1a-64 (`08` §8.9 constants) over the name's UTF-8 bytes, **no**
    length prefix.
  - SplitMix64, the standard one:
    `x += 0x9E3779B97F4A7C15; z = x; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EB; return z ^ (z >> 31)`, mod
    2^64. Seeded with `seed`, its first four outputs are `s[0..3]` in order.
    While all four are 0, `s[0]` is replaced by the next output.
  - xoshiro256** 1.0, the standard one:
    `r = rotl(s[1] * 5, 7) * 9; t = s[1] << 17; s[2] ^= s[0]; s[3] ^= s[1];
    s[1] ^= s[2]; s[0] ^= s[3]; s[2] ^= t; s[3] = rotl(s[3], 45); return r`.
    `NextUInt64` is one step. Check: state `{1, 2, 3, 4}` yields 11520, 0,
    1509978240, 1215971899390074240.
  - `NextInt(min, max)`: `min >= max` throws `ArgumentOutOfRangeException`.
    Else `range = (uint32)(max - min)`; `x = (uint32)(NextUInt64() >> 32)`,
    `m = (uint64)x * range`, `l = (uint32)m`; if `l < range`, set
    `t = (uint32)(0 - range) % range`, and while `l < t` draw a new `x` and
    recompute `m`/`l`; return `min + (int32)(m >> 32)` — Lemire's method on
    the **top 32 bits** of each draw.
  - `NextFx01()` = `Fx.FromRaw((int64)(NextUInt64() >> 32))`.
  - `Chance(p)` = `NextFx01() < p`, **always exactly one draw**; `p <= 0`
    gives false, `p >= 1` gives true, neither is an error.
  - `Shuffle(items)`: for `i` from `n-1` down to 1, `j = NextInt(0, i+1)`,
    swap `items[i]`/`items[j]`. `n <= 1` draws nothing.
  - `ComputeStateHash()` is a fresh `StateHasher` (T-001's, `08` §8.9) fed
    `s[0..3]` as `uint64`. The owning system feeds that value into its own
    hash.
  - `Stream(name)` returns the **same live stream** every call with that
    name in a session — created at the first call, never reset or
    reallocated by later calls.
  - **Golden vectors**, first four `NextUInt64` outputs in hex:

    | MasterSeed | Name | FNV1a64(name) | Outputs |
    |---|---|---|---|
    | 0 | `sim.flow.showup` | `80AA6E48500EF830` | `4D8ADDC1EA523EA8`, `881053D9C83E81EC`, `9F943EEE723DAD43`, `41FB063846C7BC01` |
    | 12345 | `sim.schedule.jitter` | `09F750F58CE1F6D5` | `38C30AB4838B2ECE`, `20E490881273F31A`, `160AF506335E076A`, `3A5487F2EF5EDE58` |
    | 2^64-1 | `sim.airside.taxi` | `C6381A17FBE07F29` | `BAF003FC5983A4B7`, `54E24678B7DA92B8`, `C6C76D95A0A72034`, `F635BA852278C41C` |

    For the first row, a fresh stream's `NextInt(0, 10)` × 4 is 3, 5, 6, 2.
    On another fresh stream, `NextFx01()` × 2 has `Raw` 1300946369,
    2282771417.
  - **No save seam yet.** Exporting/importing stream state belongs to
    `sim.save`, unspecified. Do not invent one.

`NextFx01` and `Chance` depend on `Fx` (T-003, which now merges before this
task per the corrected release order — no stubbing needed).

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect: reproducibility (same seed + stream name
→ identical sequence), independence (drawing from stream A does not shift
stream B's sequence), `NextInt` unbiasedness over a large sample, and a
determinism test comparing same-process vs cross-process draws. **Do not
edit them.**

## Performance budget

Counted within `sim.core`'s `0.25` ms/tick (`spec/03-module-map.md`). No
allocation in `NextUInt64`/`NextInt`/`NextFx01`/`Chance` hot paths
(`spec/07-conventions.md`).

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`RngStreamName` constants beyond `sim.core`'s own needs belong to their owning
modules and do not exist yet in Phase 0; this task defines the type and the
uniqueness check, not a full registry.
