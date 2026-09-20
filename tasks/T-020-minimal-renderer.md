# T-020 — Minimal top-down renderer, flat colours

| Field | Value |
|---|---|
| Status | BLOCKED |
| Module | `app.render` |
| Assigned role | worker |
| Depends on | T-009 |
| Spec source | `spec/03-module-map.md` (module row only, no interface or scope note) |
| Blocked by | Q-008 |

## Why this task cannot be released

`spec/03-module-map.md` lists `app.render`'s directory and dependency
(`sim`, read-only) but no interface spec, no description of scope, and no
statement of what a passing test for a renderer looks like given the sim is
required to build and run fully headless (`spec/01-architecture.md`). See
`spec/open-questions.md` Q-008.

Update: Q-004 (`sim.schedule`) is now **ANSWERED**
(`spec/11-interfaces-schedule.md`), so T-009 is `QUEUED`, not `BLOCKED`.
T-020's dependency on T-009 is therefore an ordinary merge-order dependency —
Q-008 alone is what still stops this task.

## Writable paths (provisional, not yet binding)

```
src/app/render/**, tests/app/render/**
```

## Done when

- [ ] Q-008 answered
- [ ] T-009 merged
- [ ] This file is rewritten with the real interface/scope, tests and budget,
      then re-queued as QUEUED
