# T-046 — Content: `balance.schema.json` requires `doors_open_delay_minutes`

| Field | Value |
|---|---|
| Status | HOLD (needs the owner: see Worker notes; not a spec gap) |
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

**Sequencing problem for the owner.** `data/balance/airside_rules.json` today is
`{ "schema_version": 1, "boarding_hold_max_minutes": 10 }`. With this schema
change it fails validation, and `04` says "Until then, that file fails the
schema, and no build that parses it can register `sim.airside`". Equally, the
owner adding `doors_open_delay_minutes: 2` first fails the current schema
(`additionalProperties: false`). So the schema PR and the owner's balance edit
must land together, or the owner's edit must land first on a branch that also
carries this schema change. Agents may not edit the balance file, so this task is
held until the owner chooses how to land it. Needed before T-031 can load
`airside_rules.json` and before T-021's host wiring is exercised.
