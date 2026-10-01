# T-046 — Content: `balance.schema.json` requires `doors_open_delay_minutes`

| Field | Value |
|---|---|
| Status | DONE by the owner (commit `3a00a78`, 2026-10-01); no worker release |
| Module | content (not `src/sim/**`, not `src/app/**`) |
| Assigned role | worker |
| Depends on | T-028 (merged) |
| Spec source | `spec/04-data-schemas.md` "Who writes the Phase 1 files" (first balance file); `spec/12-interfaces-airside.md` §12.4 `AirsideRules` (Q-047, PR #67, `8cf445c`) |
| Blocked by | — |

## Writable paths

```
data/schemas/balance.schema.json
```

**Not writable by this task, or any agent task:** `data/balance/**`. The balance
file is human-only: "Any agent PR touching `data/balance/` is auto-rejected."

## What this task does

`04` now says of `data/balance/airside_rules.json`: "`{ "schema_version": 1,
"boarding_hold_max_minutes": <uint32>, "doors_open_delay_minutes": <uint32> }`.
Both keys are required." and "`schema_version` stays 1, because no build has
shipped the one-key form." "The schema may be written by an agent content
task."

Change `data/schemas/balance.schema.json` (merged by T-028 with one key):

- `required`: `schema_version`, `boarding_hold_max_minutes`,
  `doors_open_delay_minutes`;
- add `doors_open_delay_minutes`: integer, minimum 0, maximum 4294967295 (a
  `uint32`; 0 means `DoorsOpen` fires the same tick as `OnStand`, `12` §12.4);
- `additionalProperties: false` and `schema_version` const 1 unchanged.

## Readable specs

`CLAUDE.md`, `spec/04-data-schemas.md`, `spec/12-interfaces-airside.md` §12.4

## Tests to pass

`ci/validate-content.py` over `data/` (the schema is paired with
`data/balance/*.json` by name). No xUnit test.

## Done when

- [ ] The schema requires both keys and no others
- [ ] `ci/validate-content.py` passes on the PR head
- [ ] No writes outside `data/schemas/balance.schema.json`
- [ ] Reviewer approved

## Worker notes

**Done by the owner, not by an agent.** Commit `3a00a78` ("balance change")
landed the schema change and the balance edit together, which is the
sequencing the earlier note asked for. `data/schemas/balance.schema.json`
now requires `schema_version`, `boarding_hold_max_minutes` and
`doors_open_delay_minutes`, and `data/balance/airside_rules.json` is
`{ "schema_version": 1, "boarding_hold_max_minutes": 10,
"doors_open_delay_minutes": 2 }`. Do not release this task. T-031 and
T-021's host wiring no longer wait on it.
