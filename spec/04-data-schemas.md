# 04 — Data schemas

All content is data. Code never hardcodes a content value; a hardcoded aircraft
weight, fee or throughput number is a code-review rejection.

Schemas live in `data/schemas/`. Content files are validated at load in all builds
and at commit time in CI. Invalid content fails the build — it never silently
falls back to a default.

## Files

| Schema | Content file | Count at 1.0 |
|---|---|---|
| `aircraft.schema.json` | `data/aircraft/*.json` | ~40 |
| `airline.schema.json` | `data/airlines/*.json` | ~30 archetypes |
| `object.schema.json` | `data/objects/*.json` | ~150 |
| `incident.schema.json` | `data/incidents/*.json` | 15 at EA |
| `policy.schema.json` | `data/policies/*.json` | ~25 |
| `scenario.schema.json` | `data/scenarios/*.json` | 12 |
| `pax_profiles.schema.json` | `data/pax_profiles/*.json` | — |
| `size_categories.schema.json` | `data/size_categories/*.json` | 6 |
| `queue_profiles.schema.json` | `data/queue_profiles/*.json` | — |
| `balance.schema.json` | `data/balance/*.json` (human-authored, below) | 1 at Phase 1 |

A schema's file name must equal its directory's name, because
`ci/validate-content.py` pairs them by name. That is why the pax profile
schema is `pax_profiles.schema.json` (renamed from `pax_profile`; no schema
file existed yet).

`pax_profile` is referenced by `09-interfaces-flow.md` (`CohortKey.PaxProfile`,
walk speed) and `11-interfaces-schedule.md` §11.6 (show-up curve). Those two
sections are the authority on its simulation-facing fields.

### Phase 0/1 content fields (Q-011)

The definition types are `08-interfaces-core.md` §8.11, and the loader rules
(strict JSON subset, decimals as strings, no unknown keys) are there too.
Every file carries `"schema_version": 1` and a string `"id"`.

| Kind | Fields beyond `schema_version`, `id` |
|---|---|
| size category | `ordinal`: integer, unique |
| aircraft | `size_category`: id of a size category |
| pax profile | `walk_speed_mps`: decimal string > 0; `show_up_curve`: array of `{ "minutes_before_std": uint, "share_permille": uint }` (`11` §11.6) |
| queue profile | `service_rate_per_server_per_minute`: decimal string ≥ 0; `capacity_standing`: integer > 0; `threshold_wait_minutes`: decimal string; `hysteresis_minutes`: decimal string, `0 ≤ h < threshold`; `delay_category`: `"security_queue"` or `"immigration_queue"` |

**Who writes the Phase 1 files.** Size categories and aircraft are ordinary
content and may be written by an agent content task. **Pax profile and queue
profile values are balance values**: walking speed, show-up shares, lane
service rates and queue thresholds all shape the player's experience. They
are authored by the human owner even though they live outside
`data/balance/`. The path guard in `ci/` protects only `data/balance/`, so
until the owner extends it, review enforces this.

## Fixture formats are not content formats

Schedules at Phase 0 are CSV **test fixtures**
(`11-interfaces-schedule.md` §11.4), living beside their tests, never in `data/`
and never validated by a schema here. Nothing in this table commits the shipping
build to CSV for anything.

## Conventions

- All identifiers are stable strings, never array indices. Reordering a file must
  never change behaviour or break a save.
- All numbers that feed the simulation are integers or fixed-point decimal strings.
  No floats in content that reaches sim state. See `02-determinism.md`.
- All player-visible text is a localisation key, never a literal.
- Every file declares `"schema_version"`. Migrations are mandatory, not optional.
- Modders use this exact pipeline. If it is awkward for a designer, it is wrong.

## Balance values are human-owned

Content files under `data/balance/` are **not** agent-editable. Agents may build
the tuning harness and run experiments; deciding which outcome is fun is the
human owner's call. Any agent PR touching `data/balance/` is auto-rejected.

The first balance file is `data/balance/airside_rules.json`:
`{ "schema_version": 1, "boarding_hold_max_minutes": <uint32> }`. The Phase 1
value is 10 (D6, `12-interfaces-airside.md` §12.4 `AirsideRules`), marked for
tuning after the T-025 playtest. It is validated by `balance.schema.json`,
because `ci/validate-content.py` maps one schema to one `data/` directory by
name. The schema may be written by an agent content task. The balance file
itself is written only by the human owner.
