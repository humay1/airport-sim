# 19 — Public interfaces: `tools.simharness` (the CI gates)

Pins the harness subcommands that `ci/run-checks.sh` invokes, their exit
codes and output, and the public surface that the harness's own test project
compiles against. It answers `open-questions.md` Q-025, Q-026 and Q-027.
Notation is as in `08-interfaces-core.md`. Where this file appears to
contradict `01-architecture.md` or `02-determinism.md`, those win and it is a
spec bug. `ci/**` is the human owner's. This file describes what the harness
does when it is invoked the way `ci/run-checks.sh` already invokes it, and it
never asks for a change there.

Reading order for a harness worker: `01`, `02`, `07`, `08` §8.5, §8.5a,
§8.7, §8.9 and §8.11a, then this file. The `checkpoints` subcommand is
`16` §16.8, and nothing here changes it.

---

## 19.1 Public surface (Q-026)

`07` L5 applies. These are the **only** public types of
`AirportSim.Tools.SimHarness`. Everything else, `Program` included, stays
`internal`.

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
  Console.Error)`, and it returns that value as the process exit code. A
  call with **no** arguments keeps T-001's behaviour. It is not a gate, and
  nothing here specifies it.
- **One run.** A run builds one host with `SimHostFactory.CreateBuilder`.
  `MasterSeed = seed` and `Content = content`. `Checkpoints` is a
  harness-internal sink that records every `Checkpoint` in order, and `Log`
  is a harness-internal log that discards everything. The harness then calls
  `compose(builder)` **exactly once** and calls `Build()` itself. It submits
  the command script (§19.2) and steps. A composer registers systems and
  must not call `Build`. Every run gets a fresh builder and a fresh call.
- **The divergence seam.** A test proves that a gate *fails* on
  nondeterminism by passing a composer whose runs differ, for example one
  that counts its own calls in a captured local and registers a probe system
  whose state depends on that count. No CLI flag exists for this, and none
  may be added.
- `HarnessGates` members throw `ArgumentNullException` for a `null`
  argument. They throw `ArgumentOutOfRangeException` for `ticks == 0`, and in
  `SaveLoad` unless `0 < saveAt < ticks`. An exception from the sim,
  `SimInvariantException` included, propagates unchanged. `HarnessCli` maps
  exceptions to exit codes (§19.3).
- `FinalHash` runs once and returns the final `WorldStateHash()` as exactly
  16 lowercase hexadecimal digits.

## 19.2 What each gate does (Q-026, Q-027)

**The command script.** Every run submits, before its first `Step`, one
`NoOp` command for each tick `t` with `1 ≤ t < ticks` and `t % 100 == 0`.
Here `ticks` is always the **gate's** `ticks` argument, for every run of
the gate, including a run that steps fewer ticks (A in `SaveLoad`). So A's
log also holds its commands that are still pending after `saveAt` (Q-029).
Each has `Issuer = PLAYER_LOCAL`, an empty payload, and is submitted in
ascending `t`. This makes the queue part of every gated hash (`08` §8.7).
A rejected submit throws `InvalidOperationException`.

**Comparing two runs.** Two runs are compared checkpoint by checkpoint, in
order, and then by their final `WorldStateHash()`. The first difference is
located as follows. The checkpoint counts differ: `count`. Otherwise the
first differing checkpoint is located by `CoreHash` first (`core`), then by
the lowest index `j` at which `SystemHashes` differ, where a length mismatch
counts at the shorter length (`system:<j>`), then by `WorldHash` (`world`).
If every checkpoint agrees but the final hashes differ: `final`.

| Gate method | Runs | Passes if |
|---|---|---|
| `SameProcess` | two runs of `ticks`, in one process | the two runs compare equal |
| `SaveLoad` | In this order (Q-029). **U**: one run of `ticks`, run to the end. **A**: a run of `saveAt` ticks, then its "save", which is `CommandLogSince(0)` plus the run's inputs (`content`, `compose`, `seed`). **B**: a fresh run from those inputs, in which every logged command is resubmitted in log order in place of the script. B steps `saveAt`, then `ticks − saveAt` | First, after B's first `saveAt` ticks, B's `WorldStateHash()` equals A's. Otherwise the gate fails at once with `tick=saveAt at=reload`, and B is not stepped further. Then B compares equal to U |
| `Promotion` | two runs of `ticks`. The second is the "camera parked" run | the two runs compare equal |

- **`SaveLoad` before `sim.save` (Q-027).** No save seam exists (`08` §8.8).
  So the save is the seed, the content, the composition and the command log.
  This is the replay form of "the log is the save" (`01-architecture.md`),
  with the snapshot at tick 0. RNG stream state is **not** exported. The
  harness snapshots nothing else, and it invents no seam. When `sim.save`
  specifies a snapshot, `SaveLoad` is amended to reload from it. See the
  HUMAN DECISION (owner, 2026-09-26) in §19.5.
- **`Promotion` before `sim.flow` promotion (T-010).** Without a promotable
  system, the second run differs from the first in nothing. The gate
  compares for real and passes vacuously. T-010 amends the second run to
  promote, through `IFlowSystem.SetPromoted` (`09` §9.7), the nodes the
  `02` gate calls "a gate". Until then, a harness that stubs the comparison
  is wrong.
- **The CLI composition.** For CLI runs, `content` is
  `ContentIndexFactory.Create` of an empty list, and the composer registers
  **no systems**. The task that first puts a module into the harness
  amends this line with that module's composition (T-009 is expected to be
  first). At T-006, the gates therefore prove the loop, the queue and the
  core section only.
- **Untested by design (Q-029).** `at=world` and `at=count` cannot be
  reached. Runs of one gate step the same ticks at the same checkpoint
  cadence, so their counts match. A world hash is a function of the tick,
  `CoreHash` and `SystemHashes`, so it cannot differ while those agree. Both
  stay in the grammar as defensive reports. With the empty CLI composition,
  exit codes 1 and 3 cannot be reached through `HarnessCli.Run` either. The
  divergence seam (§19.1) proves failure through `HarnessGates` instead, and
  no CLI seam is added. The task that first amends the CLI composition makes
  them reachable, and its tests cover them.

## 19.3 The command line (Q-026)

Exactly these forms, which are the ones `ci/run-checks.sh` uses. The flags
after the subcommand may come in any order. Each is given at most once, and
each value is a decimal integer without a sign.

| Invocation | Does | Seed |
|---|---|---|
| `determinism --days D --seed S` | `SameProcess` with `ticks = D × TICKS_PER_SIM_DAY` | `S` |
| `determinism --days D --seed S --hash-only` | `FinalHash`, same ticks | `S` |
| `saveload --ticks T --save-at K` | `SaveLoad(T, K)` | 12345 |
| `promotion --days D` | `Promotion`, `ticks = D × TICKS_PER_SIM_DAY` | 12345 |
| `budget --tier max` | §19.4 | 12345 |

**Usage errors.** A missing subcommand or flag, an unknown subcommand or
flag, a repeated flag, a value that does not parse, `D = 0`,
`D × TICKS_PER_SIM_DAY` above `uint32`, `T = 0`, `K` outside `0 < K < T`,
and a `--tier` other than `max` are all usage errors.

**Exit codes.**

| Code | Meaning |
|---|---|
| 0 | the gate passed |
| 1 | the gate failed: a divergence, or a budget exceeded |
| 2 | usage error. Nothing runs, and stdout stays empty |
| 3 | harness error: any exception during the run, `SimInvariantException` included |

For codes 2 and 3, stdout is empty, and stderr carries one human-readable
message (for 3, the exception's `ToString()`). No other exit code is
returned. There is no "not implemented" code: all four subcommands are
T-006's in full, and a subcommand that cannot run fails as code 3, never as
0.

**Stdout.** UTF-8 without a BOM, LF, invariant formatting, single spaces.
There is **exactly one line**, followed by a newline:

```
--hash-only:   <hex16>
pass:          PASS <gate> ticks=<n> checkpoints=<k> final=<hex16>
divergence:    FAIL <gate> tick=<t> at=<where>
budget:        <PASS|FAIL> budget ticks=<n> mean_us=<m> p99_us=<p>
```

- `<gate>` is `determinism_same_process`, `determinism_save_load` or
  `determinism_promotion`.
- `<hex16>` is 16 lowercase hexadecimal digits.
- `<k>` is the number of checkpoints in one run (for `SaveLoad`, in U).
- `<where>` is `count`, `core`, `system:<j>`, `world`, `final` or `reload`
  (§19.2).
- `<t>` is the tick of the first differing checkpoint. For `final`, it is
  `ticks`. For `reload`, it is `saveAt`. For `count`, it is the tick of the
  first checkpoint present in only one run.
- Numbers are decimal.

Anything diagnostic goes to stderr. On a divergence the harness should also
write to stderr both runs' values at the first difference. That output is
free-form and is not tested.

## 19.4 `budget --tier max` (Q-026)

One run of `TICKS_PER_SIM_DAY` ticks with the CLI composition, stepped one
tick per `Step(1)`. Each `Step(1)` is timed with
`Stopwatch.GetTimestamp()` in `long` arithmetic (`07` L11), and the time is
converted to whole microseconds by flooring. `mean_us` is the floored mean,
and `p99_us` is the value at 0-based index `⌈0.99 × n⌉ − 1` of the sorted
samples. It passes if `mean_us ≤ 6000` and `p99_us ≤ 12000`. That is the
`03` statistic applied to `01`'s 6 ms whole-sim total. Per-module budgets
stay in each module's own tests (`03`, "How a budget is measured"). The
harness does not re-assert them.

> **LOW CONFIDENCE — the load.** `03` names a max-tier fixture, and the harness
> has none until modules are composed into it. Until then, `budget --tier max`
> times the CLI composition, which at T-006 is core alone, and the task that
> amends the CLI composition (§19.2) brings the load with it. The measurement
> is on the CI agent, with no scaling factor applied.

## 19.5 Save/load before `sim.save` — HUMAN DECISION (Q-027)

`02-determinism.md`'s `determinism_save_load` row says "save at 500, reload,
continue". It does not say what a save contains, and `sim.save` is
unspecified. The harness implements §19.2's replay form. `02` is **not**
edited.

> **HUMAN DECISION — owner, 2026-09-26: approved.** Until `sim.save` exists,
> `determinism_save_load` is satisfied by the replay check in §19.2. The real
> snapshot round-trip replaces it when `sim.save` is specified. The
> Architect recorded this and did not decide it.

What the owner weighed:

- **Adopted: the replay form as the gate's interim meaning.** It
  exercises `01`'s "the log is the save" end to end, and it catches a
  command log that fails to reproduce the session. It does **not** prove
  that mutable state, such as RNG stream state or system state, survives a
  snapshot, because no snapshot exists yet. When `sim.save` is specified,
  the gate is amended to reload from a real snapshot.
- Rejected: defer `determinism_save_load` until `sim.save`. That
  contradicts `02` "no temporarily disabled" and would need an edit to `02`
  and to `ci/`, both human-only.
