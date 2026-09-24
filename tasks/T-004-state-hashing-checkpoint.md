# T-004 — State hashing: golden-vector and per-system-discipline proof

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001, T-003 |
| Spec source | `spec/02-determinism.md` "State hashing"; `spec/08-interfaces-core.md` §8.9 (as amended, Q-017) |
| Blocked by | — |

**Scope amendment, second round (Architect batch, Q-017, `db78df0`):**
`IStateHasher`/`StateHasher` is **not this task's to author at all** — it
ships with T-001 in full, "as it exists at T-001 (no pending commands,
sequence 1, and no counters unless `IIdAllocator` is implemented)" (`08`
§8.9). This is a firmer version of the earlier scope note here (which
still described this task as "keeping" `IStateHasher`, hedged on a future
ownership move) — that hedge is now resolved: T-001 owns the type and its
implementation outright, including the golden-vector tests for the hasher
itself. **This task's remaining job is narrower still: it proves the golden
vectors hold (as an independent, second check against T-001's own claim)
and proves the per-system hashing discipline** — that a system feeding its
state in the declared, stable order specified by `08` §8.9 produces a
hash that does not depend on incidental factors (dictionary/hash-set
iteration order, `GetHashCode()`, reflection order — the `07-conventions.md`
"Runtime portability" rules 1, 2 and 5). This task authors **no new
production interface**; its own deliverable is the conformance test suite
(and, if the Test Author's own coverage does not already exercise it, a
small fixture `ISimSystem` double demonstrating correct versus incorrect
feeding order) proving the discipline, not the hasher's code.

## Writable paths

```
src/sim/core/**
```

Expected to be **empty in the ordinary case** — this task has no
production interface of its own to add (see the scope amendment above); it
exists to prove T-001's `StateHasher` and the per-system discipline hold,
which is normally pure test work. **Correction (Q-021):** `tests/**` is the
Test Author's territory exclusively (`07-conventions.md` "Solution layout
and build"); the path guard already blocks a worker grant there, and this
task's earlier grant of `tests/sim/core/**` is dropped. If, once
underway, this task turns out to need no `src/sim/core/**` write at all, do
not invent one — flag to the coordinator whether this task should collapse
into the Test Author's own coverage of T-001, rather than force a
production change that does not exist.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.9

## Interface to implement

None — see the scope amendment above. `IStateHasher`, `StateHasher`,
`Checkpoint` and `ICheckpointSink` are all T-001's, declared and
implemented there. Do not redeclare or re-implement any of them here; a
second definition is a merge conflict for the Integrator to resolve in
T-001's favour, not a reason for this task to carry its own copy.

Binding, restated for this task's own test-writing (`08-interfaces-core.md`
§8.9), copied not paraphrased:

- Algorithm: FNV-1a-64 over the fed byte stream, little-endian, exactly
  T-001's `StateHasher`.
- A system feeds its serialisable state in a **declared, stable order**.
  Ordered collections feed in sequence order; unordered ones are sorted by
  a stable key first (`02-determinism.md` rule 5). Feeding a dictionary
  directly is a review rejection.
- Derived or cached values are never fed.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. **Amended (scope change above): this task no
longer carries its own hasher-authoring tests — those are T-001's,
including the four golden vectors (fresh: `CBF29CE484222325`;
`Feed(0UL)`: `A8C7F832281A39C5`; `Feed(1UL); Feed(-1L); Feed(true)`:
`9185A69DA7E88AC7`; `Feed([1, 2, 3])`: `01EF76D429B11552`).** This task's
own tests independently re-derive and re-assert those same four vectors
against T-001's shipped `StateHasher` (a second, independent check, not a
duplicate of T-001's own suite), plus a per-system-discipline test: a
fixture system that feeds an unordered collection sorted by a stable key
produces the same hash regardless of the collection's insertion or
enumeration order, while a system that (incorrectly) feeds a `Dictionary`
or `HashSet` directly is flagged by review, not by a runtime check — the
test demonstrates the *correct* pattern for later modules to copy. **Do not
edit them.**

## Performance budget

`IStateHasher.Feed` must not allocate — asserted here as an independent
check on T-001's implementation, not a new budget of this task's own.

## Done when

- [ ] All assigned tests pass
- [ ] **Green per Q-016 (HUMAN DECISION, owner, 2026-09-24): until T-006
      merges, green = `ci/run-checks.sh`'s `path-guard` and `build-and-test`
      (`--fast`) jobs. The full script is not required for this task's
      merge and becomes mandatory once T-006 merges.**
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

The per-system hash array (`Checkpoint.SystemHashes`) and the checkpoint/
world-hash fold are T-001's to ship, not this task's — settled, not a
hedge, as of this amendment. This task's job is proof, not authorship:
`StateHasher` must be bit-exact and match the FNV-1a-64 algorithm
precisely, and every system's `ComputeStateHash()` must follow the
declared-order discipline, since every future module's hash depends on
both holding.
