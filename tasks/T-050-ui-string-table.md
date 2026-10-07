# T-050 — `app.ui` scene layer: `LocalisedKey` and the English string table

| Field | Value |
|---|---|
| Status | QUEUED (releasable now: Test Author first, then worker) |
| Module | `app.ui` (scene layer only) |
| Assigned role | worker (after the Test Author) |
| Depends on | T-029 (merged #118), T-027 (merged) |
| Spec source | `spec/17-interfaces-ui.md` §17.4b, §17.7, §17.9, §17.10 (Q-125); `spec/04-data-schemas.md` (Files table, Conventions) |
| Blocked by | — |

## Writable paths

```
src/app/ui/Scene/**
data/strings/en.json
data/schemas/strings.schema.json
```

**Test Author's grant (separate, Q-021):** `tests/app/ui/**`. The Test
Author also makes the Q-125 change to the merged
`test_ui_public_surface_matches_spec` (`tests/app/ui/UiTests.cs`); that is a
spec-driven test change (`17` §17.10), not a worker edit. The worker writes
nothing under `tests/`.

No new project and no `.sln` edit: `AirportSim.App.Ui.csproj` and its test
project exist from T-029.

`data/strings/` is ignored by the sim content loader (`08` §8.11 ignores
every directory that is not a kind directory), so the text is not in the
content index or the content hash and cannot change a checkpoint. No change
to `ci/`: `ci/validate-content.py` pairs `data/strings/en.json` with
`data/schemas/strings.schema.json` by name (`04`).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/04-data-schemas.md`, `spec/07-conventions.md` ("Runtime portability"
rule 7, "Error handling"), `spec/08-interfaces-core.md` §8.11, §8.11a,
`spec/17-interfaces-ui.md` §17.4b, §17.6, §17.7, §17.9, §17.10

## Interface to implement

```
readonly struct LocalisedKey { string Value }   // §17.4b; new LocalisedKey(value); ordinal

interface IStringTable {                        // §17.4b
  string Resolve(LocalisedKey key)
}

UiFactory.LoadStringTable(IContentSource source) -> IStringTable   // §17.4b, Q-125
```

Binding, from `spec/17-interfaces-ui.md` §17.4b and §17.7, not paraphrased:

- **The key.** `Value` matches `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`.
  Exactly twelve Phase 1 keys, all starting `ui.settings.`, with the
  English texts of §17.4b's table.
- **The file.** `data/strings/en.json`, UTF-8 without a BOM:
  `{ "schema_version": 1, "id": "en", "strings": { ... } }`. Exactly those
  three keys; `strings` holds exactly the twelve keys, each text non-empty,
  characters U+0020 to U+007E only. `data/schemas/strings.schema.json`
  requires the three top-level keys with no others, `schema_version` the
  constant 1, `id` the constant `"en"`, and `strings` an object requiring
  exactly the twelve keys with no others, each value matching `^[ -~]+$`.
- **Loading.** Calls `source.ReadAll("strings/en.json")` once, never
  `Files()`. Read with `08` §8.11's strict JSON subset, its string rules
  (Q-033) included, by the scene layer's own reader (no package). A
  `FormatException` whose message starts `strings/en.json: ` for: a `null`
  `ReadAll` result; bytes outside the strict subset; a duplicate, unknown or
  missing key at the top level or in `strings`; `schema_version` other than
  1; `id` other than `"en"`; `strings` not an object; a text that is not a
  string, is empty, or holds a character outside U+0020 to U+007E. A `null`
  `source` throws `ArgumentNullException` (`source`). An exception thrown by
  `ReadAll` passes through unchanged.
- **Resolving.** Returns the file's text for `key`. A `key.Value` that is
  `null` or not one of the twelve keys (a key differing only in case
  included) throws `ArgumentException` (`key`). The table never changes
  after loading.
- **Who uses it.** The bootstrap (T-034) loads it and hands it to the UI
  backend (T-033). The controller, `UiFrame`, the host's composers and the
  sim never see the table or any text. Do not add any use of it to the
  controller.

## Events

Emitted: none. Consumed: none.

## Tests to pass

```
tests/app/ui/**
```

Written by the Test Author before this task is released to the worker. The
five tests of `17` §17.10:

- `test_ui_string_table_loads_repository_en_file`
- `test_ui_string_table_reads_only_strings_en_json`
- `test_ui_string_table_rejects_malformed_file`
- `test_ui_string_table_resolve_rejects_unknown_key`
- `test_ui_string_table_resolve_allocates_nothing`

plus the Q-125 update to `test_ui_public_surface_matches_spec`: its pinned
lists gain `LocalisedKey` and `IStringTable` among the types and
`LoadStringTable` among `UiFactory`'s methods, and it asserts that
`IStringTable`'s only member is `Resolve`.

**Do not edit them.** If a test contradicts `spec/17-interfaces-ui.md`, file
an open question and stop.

## Performance budget

`Resolve` allocates nothing (`17` §17.9). `LoadStringTable` runs once at
scene start and may allocate.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass, including the updated public-surface test
- [ ] `data/strings/en.json` validates against `data/schemas/strings.schema.json` through `ci/validate-content.py`; wording checked against §17.4b's table by the Reviewer
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

The scene layer stays engine-free (`test_ui_scene_assembly_has_no_engine_reference`)
and targets `netstandard2.1`/`LangVersion 9`. The English wording is not a
balance value; the table in §17.4b is the text, copy it exactly. T-033 and
T-034 depend on this task and may not start before it merges.
