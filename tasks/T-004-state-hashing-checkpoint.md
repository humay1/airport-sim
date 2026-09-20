# T-004 — State hashing + checkpoint reporting

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001, T-003 |
| Spec source | `spec/02-determinism.md` "State hashing"; `spec/08-interfaces-core.md` §8.9 |
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
interface IStateHasher {
  void   Feed(uint64 v)
  void   Feed(int64 v)
  void   Feed(in Fx v)                      // feeds Raw
  void   Feed(bool v)
  void   Feed(ReadOnlySpan<byte> v)
  uint64 Result { get }
}

readonly struct Checkpoint {
  Tick     Tick
  uint64   WorldHash
  uint64[] SystemHashes                     // registry order, §8.5
}

interface ICheckpointSink { void Record(in Checkpoint cp) }
```

Binding (`08-interfaces-core.md` §8.9):

- Algorithm: FNV-1a-64 over the fed byte stream, little-endian.
- World hash: FNV-1a-64 over the tick followed by each system's
  `ComputeStateHash()` in registry order (§8.5). Phase 0 has no registered
  systems, so this task's own fixture may register only stub/no-op systems —
  proving the checkpoint wiring, not any system's content.
- Checkpoint fires as tick phase 4, when `Tick % HASH_CHECKPOINT_TICKS == 0`
  (§8.5).
- Derived/cached values are never fed.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect: FNV-1a-64 reference-vector tests, a test
that two identical fixtures produce identical `Checkpoint.WorldHash`, a test
that changing tick-phase order would change the hash (regression guard on the
fixed phase order), and the divergence-localisation property — a checkpoint
with one system's hash perturbed identifies that system's index. **Do not
edit them.**

## Performance budget

Checkpoint hashing is off-tick-path budgeted separately: `20` ms per
checkpoint, every `HASH_CHECKPOINT_TICKS` (`spec/03-module-map.md`
"Off-tick budgets" — flagged there as **LOW CONFIDENCE**). `IStateHasher.Feed`
itself must not allocate.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This is explicitly called out in `08-interfaces-core.md` §8.9 as shipping
with T-004, not later — the per-system hash array is what makes a future
determinism-gate failure debuggable in seconds instead of days. Do not
simplify it to a single combined hash.
