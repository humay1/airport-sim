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
