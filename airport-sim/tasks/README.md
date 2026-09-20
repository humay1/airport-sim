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
