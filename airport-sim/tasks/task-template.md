# T-<nnn> — <title>

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.<name>` |
| Assigned role | worker |
| Depends on | T-<nnn>, T-<nnn> |
| Spec source | `spec/<file>#<section>` |
| Blocked by | — |

## Writable paths

```
src/sim/<name>/**
```

Anything else is read-only. Writing outside these paths is an automatic rejection.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/<module-specific>.md`

## Interface to implement

```
<exact signatures, copied verbatim from the spec — not paraphrased>
```

## Events

Emitted: `<list>`
Consumed: `<list>`

## Tests to pass

```
tests/sim/<name>/**
```

Written by the Test Author. **Do not edit them.** If a test contradicts the spec,
file an open question and stop.

## Performance budget

`<n>` ms/tick at max tier.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

<blockers, decisions deferred to the Architect, anything the reviewer should know>
