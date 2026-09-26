# T-027 — `sim.core`: strict content loader (Q-011)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001, T-003, T-026 |
| Spec source | `spec/08-interfaces-core.md` §8.11 "The loader" (answers Q-011); `spec/04-data-schemas.md` "Phase 0/1 content fields" |
| Blocked by | — |

## Why this task exists, and why it is separate from T-026

Q-011 needed both the content definition **types** and a **loader** that
turns `data/**/*.json` bytes into them. The Planner folded the types into
T-026 (same "types only, no logic" shape that task already has), but the
loader is real parsing logic — a hand-written, package-free JSON subset
parser plus validation — sized as its own task rather than overloading
T-026's scope.

## Writable paths

```
src/sim/core/**
```

**Correction (Q-021):** `tests/**` (including `tests/fixtures/**`) is the
Test Author's territory exclusively; the path guard already blocks a worker
grant there. The earlier grants of `tests/sim/core/**` and
`tests/fixtures/content/**` are dropped.

Same shared write surface as T-001–T-006/T-026 (`tasks/queue.md`'s Phase 0
release order note) — do not release concurrently with another open
`sim.core` task touching the same files; default to sequential, after T-026.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.11, `spec/04-data-schemas.md`

## Interface to implement

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

Consumes the content definition types T-026 authors
(`ContentId`, `ContentKind`, `IContentDefinition`, `SizeCategoryDefinition`,
`AircraftDefinition`, `PaxProfileDefinition`, `QueueProfileDefinition`,
`ShowUpBucket`) — this task does not redeclare them.

Binding, copied from `spec/08-interfaces-core.md` §8.11, not paraphrased:

- **Directories to kinds:** `size_categories/`, `aircraft/`, `pax_profiles/`
  and `queue_profiles/`, one definition per `*.json` file. Every other
  directory (`schemas/`, `policies/`, `balance/`, ...) is ignored by this
  loader at Phase 0/1.
- **Order:** files are read in ordinal path order, whatever `Files()`
  returns, so the result never depends on file-system enumeration order
  (`07-conventions.md` "Runtime portability" rule 1).
- **Format:** a strict JSON subset, **hand-parsed inside `sim.core`**.
  There is no package (`07` "Runtime portability" rule 7: no NuGet
  polyfills, netstandard2.1/C# 9 surface only), and no floating point
  anywhere. UTF-8 without a BOM; may contain objects, arrays, strings,
  integers, `true`, `false`. A number with a fraction or an exponent is a
  load failure — fixed-point values are **decimal strings**, read with
  `Fx.Parse` (T-003). Duplicate keys, unknown keys, missing keys and
  `schema_version != 1` are load failures.
- **Validation**, each a hard failure naming the path and the field
  (`07-conventions.md`): the field rules of `04-data-schemas.md`; ids
  unique across all files and all kinds; `AircraftDefinition.SizeCategory`
  resolves; size ordinals unique; `ShowUpCurve` per `11` §11.6;
  `WalkSpeedMps > 0`; `ServiceRatePerServerPerMinute >= 0`;
  `CapacityStanding > 0`; `0 <= HysteresisMinutes < ThresholdWaitMinutes`;
  `Category` is `security_queue` or `immigration_queue`.
- The output goes to `ContentIndexFactory.Create` (T-001, `08` §8.11a).
  Tests may skip this loader entirely and build definitions directly — most
  of the sim's own tests do, and should keep doing so; this task's tests are
  the loader's own.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author, against `tests/fixtures/content/**` (a small,
synthetic `IContentSource` fixture covering every load failure and every
success path — not `data/` itself, which stays human/content-task
territory). Expect at least:

- `test_loader_reads_files_in_ordinal_path_order_regardless_of_source_order`
- `test_loader_rejects_number_with_fraction_or_exponent`
- `test_loader_rejects_duplicate_unknown_or_missing_key`
- `test_loader_rejects_schema_version_other_than_one`
- `test_loader_rejects_duplicate_id_across_kinds`
- `test_loader_rejects_aircraft_with_unresolved_size_category`
- `test_loader_rejects_queue_profile_hysteresis_not_less_than_threshold`
- `test_loader_rejects_queue_profile_category_outside_security_or_immigration`
- `test_loader_output_feeds_content_index_factory_without_further_transform`
- `test_loader_no_bom_and_ordinal_string_comparison_throughout`

**Do not edit them.** If a test contradicts `spec/08-interfaces-core.md`
§8.11, file an open question and stop.

## Performance budget

Load-time only, off the tick path — no per-tick budget. It must still not
allocate pathologically (a directory of ~250 files at 1.0 scope,
`00-overview.md`), but there is no ms/tick ceiling to meet.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] **Green per Q-016 (HUMAN DECISION, owner, 2026-09-24): until T-006
      merges, green = `ci/run-checks.sh`'s `path-guard` and `build-and-test`
      (`--fast`) jobs. The full script becomes mandatory once T-006 merges.**
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

Do not reach for a JSON package "just for parsing" — `07-conventions.md`
rule 7 is explicit that the sim's `netstandard2.1` surface takes no NuGet
polyfills, and a JSON library pulled in only for this loader is exactly the
kind of BCL-divergence risk D1's retarget exists to avoid. Hand-write the
subset parser; it is small (objects, arrays, strings, integers, booleans,
no floats).

This task does not write any `data/**/*.json` file — that is a content
task (T-028) and, for `pax_profiles`/`queue_profiles` *values*, the human
owner only (`04-data-schemas.md` "Who writes the Phase 1 files"). This
task's own fixtures live under `tests/fixtures/content/**`, not `data/`.
