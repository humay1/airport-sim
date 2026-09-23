# T-028 — Content: Phase 0/1 schemas, size-category and aircraft data

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | content (not `src/sim/**`, not `src/app/**`) |
| Assigned role | worker |
| Depends on | — |
| Spec source | `spec/04-data-schemas.md` "Files", "Phase 0/1 content fields" (Q-011); `spec/12-interfaces-airside.md` §12.4 (`AirsideRules`, D6) |
| Blocked by | — |

## Writable paths

```
data/schemas/size_categories.schema.json
data/schemas/aircraft.schema.json
data/schemas/pax_profiles.schema.json
data/schemas/queue_profiles.schema.json
data/schemas/balance.schema.json
data/size_categories/**
data/aircraft/**
```

**Not writable by this task, or any agent task:** `data/pax_profiles/**` and
`data/queue_profiles/**` (the JSON *content* files, as opposed to their
schemas) and `data/balance/**` (the JSON content files). Walking speed,
show-up shares, lane service rates and queue thresholds are balance values
that shape the player's experience (`04-data-schemas.md` "Who writes the
Phase 1 files"), and the first balance file
(`data/balance/airside_rules.json`) is human-authored per D6. This task
writes their **schemas only**. Any PR from this task touching those four
data directories is out of scope and should be rejected on sight.

## Readable specs

`CLAUDE.md`, `spec/04-data-schemas.md`, `spec/08-interfaces-core.md` §8.11
(the definition types these schemas validate against), `spec/12-interfaces-airside.md`
§12.4 (`AirsideRules`)

## Content to write

Four schemas, one field table each, matching `08-interfaces-core.md` §8.11's
definition types and `04-data-schemas.md`'s field table exactly:

| Schema | Fields beyond `schema_version`, `id` |
|---|---|
| `size_categories.schema.json` | `ordinal`: integer, unique |
| `aircraft.schema.json` | `size_category`: id of a size category |
| `pax_profiles.schema.json` | `walk_speed_mps`: decimal string > 0; `show_up_curve`: array of `{ "minutes_before_std": uint, "share_permille": uint }` |
| `queue_profiles.schema.json` | `service_rate_per_server_per_minute`: decimal string ≥ 0; `capacity_standing`: integer > 0; `threshold_wait_minutes`: decimal string; `hysteresis_minutes`: decimal string, `0 ≤ h < threshold`; `delay_category`: `"security_queue"` or `"immigration_queue"` |

Plus `balance.schema.json`, validating
`{ "schema_version": 1, "boarding_hold_max_minutes": <uint32> }`
(`12-interfaces-airside.md` §12.4, D6) — this task writes the schema only;
the balance file itself (`data/balance/airside_rules.json`, value `10`) is
the human owner's to write, not this task's.

A schema's file name must equal its directory's name
(`04-data-schemas.md`) — `ci/validate-content.py` pairs them by name. This
is why the pax profile schema is `pax_profiles.schema.json`, plural, not
`pax_profile.schema.json`.

**Data files** (ordinary content, not balance): enough `data/size_categories/*.json`
and `data/aircraft/*.json` files to cover the six size categories named in
`04-data-schemas.md`'s "Count at 1.0" column and the aircraft types the
Phase 0/1 schedule and airside fixtures reference by `ContentId`
(`11-interfaces-schedule.md`'s `aircraft_type` column,
`12-interfaces-airside.md`'s `MaxAircraftSizeCategory`). Every id is a
stable string, never an array index (`04-data-schemas.md` "Conventions").

## Tests to pass

```
ci/validate-content.py (existing, unchanged) run against this task's files
```

There is no `tests/sim/**` or `tests/app/**` surface for this task; content
validity is proved by the existing schema validator, not by a new test
suite. If the validator's behaviour needs to change to accept a valid file,
that is a question for whoever owns `ci/validate-content.py`
(human-only path per `ci/gates.md`), not something this task edits around.

## Performance budget

None — content, not code.

## Done when

- [ ] Every schema field matches `08-interfaces-core.md` §8.11 and
      `04-data-schemas.md` exactly, no more, no fewer
- [ ] `ci/validate-content.py` passes against every file this task writes
- [ ] No writes outside the writable paths above, in particular no write to
      `data/pax_profiles/**`, `data/queue_profiles/**` or `data/balance/**`
- [ ] Reviewer approved

## Worker notes

Do not invent a plausible-looking `walk_speed_mps` or
`service_rate_per_server_per_minute` "just to unblock testing" — those
numbers are balance values and belong to the human owner even when this
task is otherwise ready to ship. If T-007's or T-023's own tests need such a
value, they supply it directly to `ContentIndexFactory.Create` as a test
fixture (`08` §8.11a: "Tests may skip the loader and build definitions
directly"), never by writing to `data/`.

Size category and aircraft values are ordinary content and are this task's
to invent sensibly (six size categories, a handful of aircraft types
spanning them) — they are structural, not balance, per
`04-data-schemas.md`.
