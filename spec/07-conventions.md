# 07 — Conventions

Extends `CLAUDE.md`. Where they disagree, `CLAUDE.md` wins.

## Naming

- Modules: `sim.<lowercase>`, `app.<lowercase>`
- Events: past tense, `FlightDeparted`, `QueueThresholdExceeded`
- Commands: imperative, `OpenSecurityLane`, `ReassignStand`
- Tests: `test_<subject>_<condition>_<expectation>`

## Testing

- Unit tests are deterministic and never use wall-clock time or unseeded RNG.
- Every sim module ships a **headless day test**: run one full simulated day
  against a fixture schedule and assert invariants.
- Property-based tests preferred for the flow, baggage and delay modules.
- Test fixtures live beside tests, never in `data/`.
- **Workers never edit tests.** A wrong test is a spec question.

## Comments and documentation

- Comment *why*, never *what*.
- Every public interface carries a doc comment naming the spec section it
  implements. If there is no such section, the interface should not exist.

## Error handling

- Sim code does not throw for gameplay conditions. Invalid states are data.
- Sim code throws on programmer error (broken invariant) — loudly, with the tick
  number and the state hash.
- Content loading fails hard with the file path and schema violation.

## Logging

- Sim layer logs through a deterministic sink; log volume must not affect outcomes.
- Every log line in sim carries the tick number.

## Runtime portability (Mono and CoreCLR)

The sim is compiled **once**, for `netstandard2.1` (`01-architecture.md`, D1).
It is tested on CoreCLR (`net8.0` tests, `tools.simharness`) and shipped on
Unity's Mono. `02-determinism.md`'s contract, "on every machine", covers both.
Any BCL behaviour that is unspecified, differs between the two runtimes, or is
randomised per process must never reach an outcome, an event order, an id or a
hash. The rules below operationalise `02-determinism.md` rules 2, 5 and 7 for
that case. They bind `src/sim/**` and any code that feeds a state hash or a
checkpoint dump.

1. **Hash-collection order.** Enumerating `Dictionary`, `HashSet`, their
   `Keys`/`Values`, or LINQ over any of them never decides an outcome, an
   event order or a hash. The two runtimes' orders are not contract and differ
   after removals. Keyed lookup is fine. Iterate in the declared order of an
   ordered structure, or sort first under rule 3.
2. **No hash code reaches behaviour.** No `GetHashCode()` value is used as a
   seed, an id, a sort key, a bucket choice or a hashed value. This covers
   `string.GetHashCode()` (randomised per process on CoreCLR, not on Mono),
   `System.HashCode` (random per-process seed) and default `object` or
   `ValueType` hash codes (runtime-specific). The only hashes the sim computes
   are FNV-1a (`08-interfaces-core.md` §8.8, §8.9).
3. **Sorts are total.** Every comparison used to sort sim data is a strict
   total order over the items sorted: no two distinct items compare equal.
   Where natural keys can tie, the comparer breaks the tie on a unique id.
   `Array.Sort`, `List<T>.Sort` and span sorts are unstable introsorts, and
   the order in which they leave equal items is not the same across runtimes
   and versions. A total comparer makes the result independent of the
   algorithm.
4. **Strings are ordinal and culture-invariant.** Comparison, equality and
   ordering of strings use `StringComparison.Ordinal`/`StringComparer.Ordinal`.
   Every number parsed from a fixture or content file, and every number
   formatted into a dump, uses `CultureInfo.InvariantCulture`. No
   culture-sensitive `Compare`, `ToUpper` or `ToLower`. Culture data comes
   from ICU or NLS on CoreCLR and from Mono's own tables, and it differs by
   OS as well.
5. **No reflection order.** No behaviour, serialisation or hashing depends on
   the order of `GetFields`, `GetProperties`, `GetMethods` or attributes. That
   order is unspecified and differs between runtimes. Hashing and saving name
   fields explicitly, in their declared order (`08-interfaces-core.md` §8.9).
6. **No memory punning.** Never hash or serialise a struct by reinterpreting
   its bytes (`MemoryMarshal.AsBytes`, `Unsafe.As`, overlapping
   `StructLayout`). Padding bytes and managed layout are not guaranteed to
   match. Feed fields.
7. **API surface.** Only `netstandard2.1` APIs and C# 9. Sim assemblies take
   **no NuGet package references** unless a spec section names the package,
   so the missing .NET 8 APIs cannot be brought back through polyfills. The
   `init` accessors and `record` types of C# 9 need `IsExternalInit`, which
   `netstandard2.1` does not ship. The sim declares no such polyfill and does
   not use them.

The static checks in `ci/` catch only part of this. The cross-runtime gate
(`16-interfaces-host.md` §16.9) is what detects a violation that gets past
review.

## Performance

- Allocation in the per-tick hot path is a rejection criterion.
- Budget assertions live in tests, not in comments.
- Optimise only against a measurement. "This looked slow" is not a reason.
