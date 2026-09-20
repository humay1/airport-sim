# 02 — Determinism

> **Locked document.** Agents may not modify this file.

Determinism is not a nicety. It is what makes replays, save integrity, the soak
test and reproducible bug reports possible. Every one of those is load-bearing for
a project built by agents, because they are how you detect drift without reading
every line.

## The contract

Given the same seed, the same content data and the same command log, the
simulation produces bit-identical state at every tick, on every machine, forever.

## Rules

1. **Fixed timestep.** The sim advances in discrete ticks of `TICK_MS`. It never
   reads wall-clock time. Variable frame rate is a presentation concern only.
2. **Single seeded RNG service.** All randomness comes from `IRandomService`,
   seeded per save. No `System.Random`, no `Math.random`, no engine RNG, no
   `Guid.NewGuid()`, no hash-based pseudo-randomness on unordered data.
3. **Per-system RNG streams.** Each system draws from its own named stream so that
   adding a draw in one system does not shift another system's sequence. Streams
   are derived from the master seed by name hash.
4. **No floating point in sim state.** Use fixed-point (`Fx` type) or integers.
   Floats are permitted in presentation only. Rationale: float behaviour varies
   across platforms, compilers and optimisation levels.
5. **Deterministic iteration order.** Never iterate a hash map or dictionary where
   order affects outcomes. Use ordered collections or sort by a stable key first.
   This is the single most common source of drift.
6. **No parallelism without a deterministic merge.** Parallel jobs are allowed only
   where results are combined in a fixed order independent of completion order.
7. **No object identity leaking into behaviour.** Never branch on a pointer,
   reference address or default object hash code.
8. **Promotion neutrality.** Converting passenger cohorts to agents and back must
   not alter outcomes. See `01-architecture.md`.

## Gates

These run in CI on every merge. Failure blocks the merge. No exceptions, no
"temporarily disabled".

| Gate | What it does | Pass condition |
|---|---|---|
| `determinism_same_process` | Run 10 sim-days twice in one process from the same seed | Identical state hash each tick checkpoint |
| `determinism_cross_process` | Same, in two separate processes | Identical final state hash |
| `determinism_save_load` | Run 1000 ticks; save at 500, reload, continue | Identical to uninterrupted run |
| `determinism_promotion` | Same day headless vs with camera parked on a gate | Identical results |
| `soak_500_days` | Nightly. 500 sim-days against a golden hash | Matches `tests/golden/` |

## State hashing

Each system implements `ComputeStateHash()` returning a 64-bit hash of its
serialisable state. The world hash combines system hashes in a fixed, declared
order. Checkpoints are taken every `HASH_CHECKPOINT_TICKS`.

When a determinism gate fails, the checkpoint tick plus the per-system hashes
identify which system drifted and roughly when. Build this reporting early — it is
the difference between a five-minute fix and a two-day hunt.

## When a gate fails

1. The merge is blocked automatically.
2. The Integrator reverts, it does not "fix forward".
3. The task returns to the queue with the failing checkpoint attached.
4. If the same gate fails three times on one task, escalate to the human owner.
   Repeated determinism failure usually means the spec is wrong, not the code.
