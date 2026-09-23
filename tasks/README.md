# Tasks

`queue.md` is the dependency-ordered backlog, maintained by the Planner.
Each released task gets a file `T-<nnn>-<slug>.md` from `task-template.md`.

## Lifecycle

```
QUEUED → TESTS_AUTHORED → IN_PROGRESS → IN_REVIEW → VERIFYING → MERGED
                                   └──────── BLOCKED ────────┘
```

A task moves to `BLOCKED` when the worker cannot proceed without guessing. That is
a success, not a failure. Blocked tasks carry an open-question id.

## Numbering

One id block per build-order phase. Phase 0 (the feasibility spike, `00-overview.md`)
uses `T-001`–`T-019`; Phase 1 (the fun prototype) uses `T-020` onward, continuing
past any number already spent. A new task takes the next free number in its own
phase's block — never a number from another phase's block, even if it is free,
and never a number that reuses or reorders an existing task. If a phase's block
is exhausted, that is escalated to the human owner, not solved by borrowing from
the next phase's block.
