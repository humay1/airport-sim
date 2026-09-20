# CLAUDE.md — read this before doing anything

This repository is built by a team of agents. You are one of them. Find your role
brief in `agents/` and read it in full before touching any file.

## The five rules

1. **The spec is the truth.** `spec/` defines the architecture, interfaces and data
   formats. Code disagreeing with the spec is a bug in the code, not the spec.
2. **Only the Architect edits `spec/`.** If you need something the spec doesn't
   cover, append your question to `spec/open-questions.md` and stop. Do not guess,
   do not improvise an interface, do not "just make it work for now".
3. **Stay inside your module.** Your task brief names the directories you may write
   to. Touching another module's internals is an automatic rejection.
4. **Determinism is sacred.** See `spec/02-determinism.md`. Any change that breaks
   the determinism gate is reverted, no discussion.
5. **Done means green.** A task is complete when its tests pass in CI, not when the
   code looks finished.

## Working agreement

- One task, one branch, one worktree. Branch naming: `<role>/<task-id>-<slug>`.
- Never merge your own work. The Integrator merges; the Reviewer approves.
- Never edit tests to make them pass. If a test is wrong, file it in
  `spec/open-questions.md` and stop.
- Commit messages: `<task-id> <imperative summary>`. Body explains *why*.
- If you are blocked for any reason, write the blocker into your task file and
  stop. A stopped agent is cheap. A confidently wrong agent is expensive.

## Code conventions

- Language and engine: see `spec/01-architecture.md`.
- Simulation code must not reference rendering, input, UI or wall-clock time.
- No singletons, no global mutable state, no hidden statics in sim code.
- No `Random` outside the seeded RNG service. No `DateTime.Now` in sim code ever.
- Floating point: the sim layer uses fixed-point or deterministic integer math per
  `spec/02-determinism.md`. Presentation layer may use floats freely.
- Public interfaces are defined in the spec first, implemented second.
- Every module exposes its state as plain serialisable data. If it can't be written
  to a save file, it doesn't belong in the sim.

## Definition of done

- [ ] Implements exactly the interface named in the task brief
- [ ] All tests written by the Test Author pass
- [ ] Determinism gate passes (`ci/run-checks.sh`)
- [ ] Performance budget for the module is met
- [ ] No new writes outside the module's owned directories
- [ ] No new entries needed in `spec/open-questions.md`

## What is NOT an agent decision

These belong to the human owner and must never be decided by an agent:

- Whether the game is fun
- Balance values that shape player experience
- Scope changes, cut decisions, ship dates
- Anything in `spec/01-architecture.md` or `spec/02-determinism.md`

If your task seems to require one of these, stop and escalate.
