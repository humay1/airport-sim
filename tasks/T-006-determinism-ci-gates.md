# T-006 — Determinism gates in CI (`tools.simharness` CLI + gates)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `tools.simharness` (invoked by `ci/run-checks.sh`, which already exists) |
| Assigned role | worker |
| Depends on | T-004 |
| Spec source | `spec/19-interfaces-harness.md` §19.1–§19.5 (Q-025, Q-026, Q-027); `spec/02-determinism.md` "Gates" table; `ci/gates.md`; `ci/run-checks.sh` |
| Blocked by | — |

**Rewrite note (Architect batch, `origin/architect/Q-025-harness-gates`,
`493e28c`, new `spec/19-interfaces-harness.md`):** this task's earlier text
predated §19 and is replaced in full below. Q-025/Q-026 pin the
`HarnessCli`/`HarnessGates` public surface, exit codes and stdout format.
Q-027 is a **HUMAN DECISION (owner-approved): the interim save/load replay**
— `SaveLoad` reconstructs a run from `CommandLogSince(0)` plus the run's
inputs, not from a real snapshot, since `sim.save` does not exist yet.

## Writable paths

```
tools/SimHarness/**
AirportSim.sln
```

This task adds the new `tests/tools/simharness/` **test project** to
`AirportSim.sln` (the project's test files themselves are the Test Author's,
per `07` "Solution layout and build" L8 — the first task to need a module's
test project in the solution wires the `.sln` entry; it does not author the
test files). Do not release this task concurrently with any other task also
listed as growing `AirportSim.sln` (`tasks/queue.md` §"First task of each
new module grants `AirportSim.sln`").

**Correction (Q-021, carried over):** no separate `tests/**` grant is needed
or given beyond the `.sln` wiring above; the path guard blocks a worker from
writing test file contents regardless.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.5, §8.5a, §8.7, §8.9, §8.11a,
`spec/19-interfaces-harness.md`, `ci/gates.md`, `ci/run-checks.sh`

## Interface to implement

Public surface of `AirportSim.Tools.SimHarness` (§19.1 — these are the
**only** public types; `Program` stays `internal`, copied not paraphrased):

```
delegate void SimComposer(ISimHostBuilder builder)

readonly struct GateResult {
  bool   Passed
  string Report                 // exactly the §19.3 stdout line, without its newline
}

HarnessCli.Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr) -> int32

HarnessGates.SameProcess(IContentIndex content, SimComposer compose, uint64 seed, uint32 ticks) -> GateResult
HarnessGates.SaveLoad(IContentIndex content, SimComposer compose, uint64 seed, uint32 ticks, uint32 saveAt) -> GateResult
HarnessGates.Promotion(IContentIndex content, SimComposer compose, uint64 seed, uint32 ticks) -> GateResult
HarnessGates.FinalHash(IContentIndex content, SimComposer compose, uint64 seed, uint32 ticks) -> string
```

- `Program.Main(args)` is exactly `HarnessCli.Run(args, Console.Out,
  Console.Error)`, returned as the process exit code. A call with **no**
  arguments keeps T-001's existing behaviour.
- **One run** builds one host via `SimHostFactory.CreateBuilder` with
  `MasterSeed = seed`, `Content = content`, a harness-internal
  `ICheckpointSink` that records every `Checkpoint` in order, and a
  harness-internal `ISimLog` that discards everything. `compose(builder)` is
  called **exactly once**, then `Build()` (the composer must not call
  `Build`). Every run gets a fresh builder.
- `HarnessGates` members throw `ArgumentNullException` for `null`,
  `ArgumentOutOfRangeException` for `ticks == 0` (and, in `SaveLoad`, unless
  `0 < saveAt < ticks`). A sim exception, `SimInvariantException` included,
  propagates unchanged; `HarnessCli` maps it to exit code 3.
- `FinalHash` returns `WorldStateHash()` after one run, as exactly 16
  lowercase hex digits.

**The command script (§19.2):** every run submits, before its first `Step`,
one `NoOp` command per tick `t` with `1 ≤ t < ticks` and `t % 100 == 0`,
`Issuer = PLAYER_LOCAL`, empty payload, ascending `t`. A rejected submit
throws `InvalidOperationException`. This makes the queue part of every
gated hash.

**Comparing two runs (§19.2):** checkpoint by checkpoint in order, then by
final `WorldStateHash()`. First divergence located by: checkpoint count
(`count`); else `CoreHash` (`core`); else the lowest differing
`SystemHashes` index, a length mismatch counting at the shorter length
(`system:<j>`); else `WorldHash` (`world`); else, if every checkpoint agrees
but finals differ, `final`.

| Gate | Runs | Passes if |
|---|---|---|
| `SameProcess` | two runs of `ticks`, one process | the two runs compare equal |
| `SaveLoad` | **U**: one run of `ticks`. **A**: a run of `saveAt` ticks, then `CommandLogSince(0)` plus the run's inputs as its "save". **B**: a fresh run from those inputs, resubmitting every logged command in log order instead of the script, stepping `saveAt` then `ticks − saveAt` | B's hash at `saveAt` equals A's (else `reload`), and B compares equal to U |
| `Promotion` | two runs of `ticks`; the second is "camera parked" | the two runs compare equal (vacuous pass until T-010 — see below) |

- **`SaveLoad`, interim replay (Q-027, HUMAN DECISION, owner-approved):** no
  save seam exists yet. The save is the seed, content, composition and
  command log — "the log is the save," snapshotted at tick 0. RNG stream
  state is **not** exported; the harness invents no snapshot beyond the log.
  When `sim.save` lands, `SaveLoad` is amended to reload from a real
  snapshot — this task does not build that seam itself.
- **`Promotion`, before `sim.flow` (T-010):** with no promotable system, the
  second run differs from the first in nothing, and the gate compares for
  real and passes vacuously. Do not stub the comparison. T-010 amends the
  second run to promote via `IFlowSystem.SetPromoted`.
- **The CLI composition:** `content = ContentIndexFactory.Create(empty
  list)`, and the composer registers **no systems**. The first task to put
  a module into the harness amends this line (T-009, expected). At T-006
  the gates prove the loop, the queue and the core section only.

**The command line (§19.3), exactly these forms:**

| Invocation | Does | Seed |
|---|---|---|
| `determinism --days D --seed S` | `SameProcess`, `ticks = D × TICKS_PER_SIM_DAY` | `S` |
| `determinism --days D --seed S --hash-only` | `FinalHash`, same ticks | `S` |
| `saveload --ticks T --save-at K` | `SaveLoad(T, K)` | 12345 |
| `promotion --days D` | `Promotion`, `ticks = D × TICKS_PER_SIM_DAY` | 12345 |
| `budget --tier max` | §19.4 below | 12345 |

Flags after the subcommand may come in any order, each given at most once,
decimal without a sign. **Usage errors** (exit 2): missing subcommand or
flag, unknown subcommand or flag, repeated flag, unparsable value, `D = 0`,
`D × TICKS_PER_SIM_DAY` above `uint32`, `T = 0`, `K` outside `0 < K < T`, or
`--tier` other than `max`.

**Exit codes:** `0` gate passed; `1` gate failed (divergence or budget
exceeded); `2` usage error, stdout stays empty; `3` harness error (any
exception, `SimInvariantException` included) — stderr carries the
exception's `ToString()`. No "not implemented" code: every subcommand is
this task's in full.

**Stdout** — UTF-8, no BOM, LF, invariant formatting, single spaces,
**exactly one line** plus a trailing newline:

```
--hash-only:   <hex16>
pass:          PASS <gate> ticks=<n> checkpoints=<k> final=<hex16>
divergence:    FAIL <gate> tick=<t> at=<where>
budget:        <PASS|FAIL> budget ticks=<n> mean_us=<m> p99_us=<p>
```

`<gate>` is `determinism_same_process`, `determinism_save_load` or
`determinism_promotion`. `<hex16>` is 16 lowercase hex digits. `<k>` is the
checkpoint count of one run (for `SaveLoad`, U's). `<where>` is `count`,
`core`, `system:<j>`, `world`, `final` or `reload`. `<t>` is the tick of the
first differing checkpoint (`ticks` for `final`, `saveAt` for `reload`, the
first checkpoint present in only one run for `count`). Everything numeric
is decimal. Diagnostics go to stderr only (free-form, untested); on a
divergence the harness should also log both runs' values at the first
difference to stderr.

**`budget --tier max` (§19.4):** one run of `TICKS_PER_SIM_DAY` ticks with
the CLI composition, one `Step(1)` per tick, each timed with
`Stopwatch.GetTimestamp()` in `long` arithmetic (`07` L11), floored to whole
microseconds. `mean_us` is the floored mean; `p99_us` is the sorted sample
at 0-based index `⌈0.99 × n⌉ − 1`. Passes if `mean_us ≤ 6000` and
`p99_us ≤ 12000`. Per-module budgets stay in each module's own tests; the
harness does not re-assert them. **Low confidence, flagged in the spec, not
this task's problem to resolve:** until modules are composed into the CLI
composition, this times core alone — the task that changes §19.2's CLI
composition line brings the real load with it.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/tools/simharness/**
```

Written by the Test Author, in a new test project this task wires into
`AirportSim.sln` (above). Expect: `HarnessGates` unit tests exercising each
gate's pass/fail logic directly (not only via CLI), a divergence-seam test
proving a gate fails on a composer whose runs differ (no CLI flag), CLI
usage-error tests for every case in §19.3, exit-code tests for all four
codes, stdout-format tests (byte-exact line shape, hex casing, the `<where>`
taxonomy), a `budget` pass/fail test, and end-to-end tests that
`ci/run-checks.sh`'s own invocations succeed. **Do not edit them.**

## Performance budget

Not a per-tick sim module. `budget --tier max`'s own thresholds are above.
`run-checks.sh` running these gates against its fixtures should complete in
low single-digit minutes (`spec/01-architecture.md` "Why the sim is a plain
.NET library" point 2) — a sanity bound, not a hard budget test.

## Done when

- [ ] Interface matches `spec/19-interfaces-harness.md` exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green end-to-end (not `--fast`) — this is the task
      that makes the full script mandatory for everything released before it
      (Q-016)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`SaveLoad` and `Promotion` are real, in full, at this task's own scope —
they are not partial stubs. What is partial is their *load*: `SaveLoad`
replays a log instead of a snapshot until `sim.save` exists (Q-027), and
`Promotion` compares for real but has nothing to promote until T-010. Do
not fabricate a snapshot seam or a promotion path here; both are named
amendment points for later tasks, per §19.2 and §19.5.
