# Airport Simulation — agent-built project scaffold

A top-down airport management simulation. Two coupled networks — airside and
landside — under the pressure of a published daily flight schedule.

This repository is structured to be built primarily by agents, with a human owner
holding architecture sign-off, playtesting and balance.

## Layout

```
CLAUDE.md              conventions every agent reads first
spec/                  the source of truth — only the Architect writes here
agents/                role briefs, one per agent type
tasks/                 the task queue and per-task briefs
ci/                    merge gates and the check script
tests/golden/          golden simulation outputs for the soak test
data/schemas/          data-driven content schemas (aircraft, airlines, policy...)
src/                   created by workers, one directory per module
```

## The pipeline

```
Architect ──writes──▶ spec/
    │
    ▼
Planner ──writes──▶ tasks/queue.md   (dependency-ordered)
    │
    ├──▶ Test Author  ──writes tests from spec, before implementation
    │
    └──▶ Worker       ──implements the interface, makes tests pass
                │
                ▼
          Reviewer    ──reads diff + spec only, rejects or approves
                │
                ▼
          Verifier    ──determinism, soak, performance budgets
                │
                ▼
          Integrator  ──merges to trunk
```

The Test Author and the Worker must be different agents. The Reviewer must not
have seen the Worker's reasoning — only the diff and the spec.

## Human checkpoints

| When | What the owner does |
|---|---|
| Weekly | Review architect decisions and `spec/CHANGELOG.md` |
| Weekly | Play the current build for 30+ minutes |
| Every merge touching tick loop, save format or RNG | Read the diff personally |
| Phase gates | Decide continue / pivot / kill |
| Always | Balance values, scope, ship decisions |

## Getting started

1. Human: fill in the TODOs in `spec/01-architecture.md` (engine, language, target).
2. Run the Architect against `agents/architect.md` to complete the spec.
3. Human: read and sign off the spec. This is the highest-leverage review in the
   project.
4. Run the Planner to generate `tasks/queue.md`.
5. Start workers on tasks whose dependencies are met.

Do not skip step 3. 
