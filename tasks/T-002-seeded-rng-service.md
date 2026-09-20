# T-002 — Seeded RNG service with per-system named streams

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/02-determinism.md` rules 2, 3; `spec/08-interfaces-core.md` §8.8 |
| Blocked by | — |

## Writable paths

```
src/sim/core/**, tests/sim/core/**
```

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
```

Pinned algorithms (`08-interfaces-core.md` §8.8), binding, part of the save
format:

- Generator: xoshiro256\*\*, 256-bit state.
- Stream seeding: SplitMix64 seeded with `MasterSeed XOR FNV-1a-64(streamName)`,
  drawn four times to fill state. An all-zero state is replaced by the next
  SplitMix64 output.
- `NextInt`: Lemire multiply-shift with rejection.
- `NextFx01`: top 32 bits of one draw, uniform over multiples of 2^-32.
- Stream names are `"<module>.<purpose>"`, declared as constants by the owning
  module, unique across the build (this task adds the uniqueness assertion in
  CI-reachable test form, since no other module exists yet to declare a name
  besides `sim.core` itself).

`NextFx01` and `Chance` depend on `Fx` (T-003). If T-003 has not merged when
this task starts, stub the `Fx`-typed members last and land the integer-typed
members first, or coordinate sequencing with the Planner rather than guessing
`Fx`'s shape.

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
