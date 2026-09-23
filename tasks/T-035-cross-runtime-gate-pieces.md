# T-035 — Cross-runtime determinism gate: buildable pieces (OPTIONAL)

| Field | Value |
|---|---|
| Status | QUEUED — OPTIONAL, does not gate anything |
| Module | `tools.simharness` / `app.host` (wiring only, no new production surface) |
| Assigned role | worker |
| Depends on | T-030, T-031, T-034 |
| Spec source | `spec/16-interfaces-host.md` §16.9 "The cross-runtime determinism gate — PROPOSED (D1)" |
| Blocked by | — |

## Why this task is optional, and why it exists at all

`16` §16.9 proposes `determinism_cross_runtime` (CoreCLR harness dump vs.
the real Unity Mono player's dump, byte-identical), but **adoption is
PENDING HUMAN**: making it a real gate needs a row in
`02-determinism.md`'s locked gate table and a `ci/` step, both human-only
paths, and it needs a Unity licence on a build agent. This task does
**not** add a gate. It wires the pieces the spec already says are
buildable by hand (§16.9: "the harness `checkpoints` subcommand,
`IHeadlessRun`, the dump writer and the bootstrap's batch mode") into one
runnable comparison, so the owner can run it manually and decide adoption
with real evidence instead of a proposal. **Nothing else may depend on
this task**, and it does not appear in any Definition of Done besides its
own.

## Writable paths

```
tools/SimHarness/**, unity/AirportSim/**, tests/sim/core/** (a manual/local
test harness script or task, not a CI-invoked test)
```

Do not add a step to `ci/` — that path is human-only (`ci/gates.md`) and,
per §16.9, adopting the gate is an owner decision this task does not make.

## Readable specs

`CLAUDE.md`, `spec/02-determinism.md` "Gates", `spec/16-interfaces-host.md`
§16.8, §16.9

## Content to build

Binding, copied from `spec/16-interfaces-host.md` §16.9, not paraphrased:

1. **CoreCLR:** `tools.simharness checkpoints --bundle B --days 10 --out a`
   (T-030).
2. **Mono:** the Unity player build of `unity/AirportSim/` (Mono backend,
   `16` §16.2, T-034), with `B` as its scenario, started as
   `-batchmode -nographics -airportsim-checkpoints 10 b` (T-031's
   `IHeadlessRun`/`IHostCommandLine` via T-034's bootstrap).
3. **Pass condition (for a human to read, not a CI assertion this task
   adds):** `a` and `b` byte-identical (`16` §16.8's format).

`B` is the Phase 1 playtest bundle (`16` §16.3), every Phase 1 system
registered. Ten days matches `determinism_cross_process`'s size. The Mono
side must be the **real player** — Unity ships its own Mono fork and class
libraries, so a standalone upstream Mono proves little about the shipped
build.

Deliver this as a short, documented manual procedure (a script that runs
both sides and diffs the two dump files) that the owner or a future CI
step can invoke — not as a new automated gate.

## Tests to pass

None new in the automated suite — this task's own "test" is the manual
comparison procedure it delivers, run by hand against the Phase 1 playtest
bundle at least once before this task is called done, with the diff result
recorded in the PR description.

## Performance budget

Not applicable — this is not a per-tick or per-merge cost.

## Done when

- [ ] The procedure runs both sides against the same bundle and reports a
      byte-diff result
- [ ] It has been run at least once against the Phase 1 playtest bundle,
      with the result recorded
- [ ] No `ci/**` write, no `02-determinism.md` edit
- [ ] Nothing else in the queue has been made to depend on this task
- [ ] Reviewer approved

## Worker notes

If the manual run finds a real Mono/CoreCLR divergence, that is exactly
what `07-conventions.md` "Runtime portability" exists to prevent — file it
as a defect against whichever `src/sim/**` code caused it (a hash-order
iteration, an uncontrolled sort tie, a culture-sensitive parse), not
against this task, and do not attempt to "fix" it by weakening the
comparison. Whether `determinism_cross_runtime` is ever adopted as a real
gate — nightly or otherwise — is `02-determinism.md`/`ci/`'s owner-only
territory (`CLAUDE.md` "What is NOT an agent decision"); this task only
makes the comparison runnable, it does not argue for adoption.
