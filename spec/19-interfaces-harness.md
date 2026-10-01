# 19 — Public interfaces: `tools.simharness` (the CI gates)

Pins the harness subcommands that `ci/run-checks.sh` invokes, their exit
codes and output, and the public surface that the harness's own test project
compiles against. It answers `open-questions.md` Q-025, Q-026 and Q-027,
and, for T-009's Phase 0 composition, Q-041 to Q-043. For T-013's `soak`
subcommand, which the nightly workflow invokes (`.github/workflows/`), it
answers Q-057, and for T-045's budget statistic Q-058.
Notation is as in `08-interfaces-core.md`. Where this file appears to
contradict `01-architecture.md` or `02-determinism.md`, those win and it is a
spec bug. `ci/**` is the human owner's. This file describes what the harness
does when it is invoked the way `ci/run-checks.sh` already invokes it, and it
never asks for a change there.

Reading order for a harness worker: `01`, `02`, `07`, `08` §8.5, §8.5a,
§8.7, §8.9 and §8.11a, then this file. For §19.2a, also `09` §9.7 and
§9.11, `11` §11.7 and §11.9, `12` §12.3 and §12.7, and `18`. The
`checkpoints` subcommand is
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
HarnessGates.BudgetFromSamples(IReadOnlyList<int64> samples, int64 frequency) -> GateResult   // Q-058, §19.4
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
- `BudgetFromSamples` runs nothing. It is the pure §19.4 statistic, and
  its argument rules are in §19.4. It is the only member that takes
  timings. No CLI form passes samples in, and none may be added.
- `soak` (§19.2b) has no `HarnessGates` member. It is reached only through
  `HarnessCli.Run`.

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
  compares for real and passes vacuously. A **harness task**, not T-010,
  amends the second run to promote, through `IFlowSystem.SetPromoted` (`09`
  §9.7), the nodes the `02` gate calls "a gate". T-010 writes `sim.flow`
  only. The harness task depends on T-010 and on the CLI composition
  including `sim.flow`, and the Planner creates it (Q-033). Until then, a
  harness that stubs the comparison is wrong.
- **The CLI composition.** Every CLI run, of every subcommand in §19.3,
  uses the composer of §19.2a (Q-042, from T-009). `soak` runs it over the
  soak fixture set (§19.2b, Q-057), and every other subcommand over the
  Phase 0 fixture set. It replaces
  T-006's empty composition, which no CLI form selects any more. The empty
  composition stays reachable only through `HarnessGates` with a composer
  that registers nothing, as the gate tests already use it. No option,
  flag or environment variable selects a composition or a fixture set. The
  subcommand alone decides the set.
- **Untested by design (Q-029).** `at=world` and `at=count` cannot be
  reached. Runs of one gate step the same ticks at the same checkpoint
  cadence, so their counts match. A world hash is a function of the tick,
  `CoreHash` and `SystemHashes`, so it cannot differ while those agree. Both
  stay in the grammar as defensive reports. Exit codes 1 and 3 cannot be
  reached by a test through `HarnessCli.Run` either (Q-042), except by
  `soak` (below). Code 1 needs a
  nondeterministic composition, and the Phase 0 composition is
  deterministic. Code 3 needs a missing or invalid repository fixture, which
  an in-process test cannot arrange without writing repository files. The
  divergence seam (§19.1) proves failure through `HarnessGates` instead, and
  no CLI seam is added. T-006's earlier statement that the first composing
  task makes them reachable is withdrawn. `soak` is the exception, because
  its golden and output paths may be rooted paths outside the repository
  (§19.2b). A test reaches code 1 with an altered golden and code 3 with a
  missing one, both in a temporary directory (§19.7).

## 19.2a The Phase 0 CLI composition (Q-042, Q-043)

Binding on T-009 and on every later harness task until an amendment
changes it. It composes `sim.world`, `sim.schedule` and `sim.flow` over
the Phase 0 fixtures, plus a harness-internal **boarding stand-in** in
`sim.airside`'s empty registry slot.

**Inputs.** Four fixtures, all written by the Test Author (`07` L8). The
harness reads nothing else, and never reads `data/`.

| Input | Repository path | Loaded with | `sourceName` |
|---|---|---|---|
| content | manifest `tests/fixtures/harness/phase0-content.files`, files under `tests/fixtures/harness/phase0-content/` | `ContentLoaderFactory.Create().Load(source)`, then `ContentIndexFactory.Create` of the result (`08` §8.11) | none |
| walk graph | `tests/fixtures/world/phase0-landside.json` | `WorldFactory.CreateGraphLoader().Load` (`18`) | `phase0-landside.json` |
| flow graph | `tests/fixtures/flow/phase0-landside.flow.json` | `FlowFactory.CreateGraphLoader().Load(…, world)` (`09` §9.11) | `phase0-landside.flow.json` |
| schedule | `tests/fixtures/schedule/phase0-200.csv` | `ScheduleFactory.CreateLoader().Load` (`11` §11.9a) | `phase0-200.csv` |

These four are the **Phase 0 fixture set**. `soak` uses the **soak fixture
set** of §19.2b instead, which has the same four inputs at other paths.
Every rule in this section applies to either set, with that set's paths
and `sourceName`s.

- **Locating them.** The `07` "Fixture location" rule (Q-031), applied to
  the harness: walk up from `AppContext.BaseDirectory` to the nearest
  ancestor directory that contains a file named `AirportSim.sln`, then join
  the repository-relative path. The current working directory, environment
  variables and flags are never used.
- **When.** A usage error (exit 2) is decided before any file is read.
  Then, before any run, the root is found, every fixture file's bytes are
  read once, and the content is loaded once, into one `IContentIndex`
  that every run of the invocation uses. The walk-graph, flow-graph and
  schedule bytes are parsed inside the composer (below), because the flow
  loader needs the run's `IWorldSystem`. So their failures surface in the
  first run's `compose` call. Every one of these failures is a harness
  error, exit 3 with stdout empty (§19.3): not finding the root, failing
  to read a file, a malformed manifest, and any load, parse or validation
  failure, whether before the first run or inside it.
- **The content manifest.** `phase0-content.files`, like the soak set's
  manifest, is UTF-8 without a BOM.
  Each line is one path relative to its content directory, `/`-separated, and
  ends in LF. It has no empty line and no duplicate. The harness's
  `IContentSource` returns exactly those lines from `Files()`, and those
  files' bytes from `ReadAll`. This is the T-027 `valid.files` convention:
  the content never depends on directory enumeration or on stray files.
- **The content.** It holds only the definitions the other three fixtures
  reference: the schedule's `aircraft_type` and `pax_profile` ids, the
  size categories those aircraft name, and the flow graph's
  `queue_profile` ids. Its values are **fixture sizing** (`09` §9.10, `11`
  §11.10), not balance. The Test Author chooses them so that the security
  queues drain between banks over 100 days, and states the derivation in
  the kill-gate test kit (§19.6). The soak set's sizing is §19.2b.

**The composer.** Every call parses the walk-graph, flow-graph and
schedule bytes afresh and constructs fresh systems, as §19.1 requires of
every run. It constructs in dependency order (`08` §8.11a):

1. `world = WorldFactory.CreateSystem(services, walkGraph)`;
2. `flow = FlowFactory.CreateSystem(services, flowGraph, world)`;
3. `schedule = ScheduleFactory.CreateSystem(services, table, flow)`;
4. the boarding stand-in, over `schedule` and `flow`.

It registers in registry order (`08` §8.5): `world` (1), `schedule` (2),
the stand-in (3), `flow` (4), and nothing else. So every checkpoint's
`SystemHashes` has exactly four entries, in that order.

**The boarding stand-in (Q-043).** A harness-internal `ISimSystem` with
`Id = SystemId(3)`. `Name` is a free diagnostic label. Once the soak
golden exists, though, it is part of that golden's `systems` line (§19.2b),
so whatever value T-009 merges is then kept, and changing it is a golden
change. It stands in for
exactly one `sim.airside` behaviour: the departure's `Absorb` call at the
doors-close point (`12` §12.7), taken at that point's planned tick, `STD`
(`12` §12.3: `DoorsClosed.PlannedTick = STD`). It models no boarding hold,
no stand and no milestone.

- **Its `Tick` at tick `t`:**
  1. if `t % TICKS_PER_SIM_DAY == 0`, it replaces its day list with
     `schedule.MovementsBetween(t, t + TICKS_PER_SIM_DAY,
     MovementKind.Departure)`. Day `t / TICKS_PER_SIM_DAY` is always
     materialised by then (`11` §11.9), and the list is in ascending
     `ScheduledTick`, then `FlightId` (`11` §11.7);
  2. for each flight in the day list whose `ScheduledTick` equals `t`, in
     list order, it calls `flow.Absorb(PHASE0_DEPARTURE_SINK, flight)` and
     discards the return value.
  So every departure whose `STD` falls inside the run is absorbed exactly
  once, at `STD`. `sim.schedule` (2) ticks before it, so a passenger
  injected at `STD` is already in `sim.flow`, and is reported missed.
- **`PHASE0_DEPARTURE_SINK = NodeId(9)`**, the only `Sink` node of
  `tests/fixtures/flow/phase0-landside.flow.json`, and of the soak set's
  flow graph (§19.2b). It is a harness-internal constant, the same for both
  sets. If either fixture's `Sink` changes, this line is amended in the
  same change. A wrong value throws in `Absorb` (`09` §9.7), which is exit
  3.
- **Events.** It publishes nothing itself, and it subscribes to nothing.
  `Absorb` publishes `sim.flow`'s events, with the stand-in as the
  envelope's `Source` (`09` §9.7 "Outside a tick"). It never emits a
  `sim.airside` event (`10` §10.3).
- **State.** `ComputeStateHash()` returns `0`. Its day list is a function of
  the tick and of `sim.schedule`'s hashed state, so, like `09` §9.10's
  derived state, it is not hashed. It consumes no RNG, and it allocates
  only in step 1.
- **For `sim.save`.** That claim holds only for runs that start at tick 0,
  which is every run today, since `SaveLoad` replays from tick 0 (§19.2).
  A run restored from a mid-day snapshot would start with an empty day
  list, and it would diverge. The amendment that makes `SaveLoad` reload a
  real snapshot must therefore either rebuild the day list at restore,
  from `MovementsBetween(t, next day boundary, Departure)` filtered to
  `ScheduledTick ≥ t`, or snapshot and hash it.
- **Scope.** It exists only in this composition. The amendment that adds
  `sim.airside` to the harness removes it in the same change. No
  production composition (`16`) registers it. `08` §8.5's probe-system
  allowance covers it.

**`budget --tier max`** times the first sim-day of this composition, over
the Phase 0 set (§19.4).

## 19.2b `soak`: the soak fixture set and the golden (Q-057)

Binding on T-013 and its Test Author. This is how the harness runs `02`'s
`soak_500_days` gate and `03` "The soak fixture".

**The soak fixture set.** Four fixtures, all written by the Test Author
under `tests/fixtures/soak/` (`03`). They are loaded exactly as the
Phase 0 set is (§19.2a), with the same loaders, rules and failure
handling:

| Input | Repository path | `sourceName` |
|---|---|---|
| content | manifest `tests/fixtures/soak/soak-content.files`, files under `tests/fixtures/soak/soak-content/` | none |
| walk graph | `tests/fixtures/soak/soak-landside.json` | `soak-landside.json` |
| flow graph | `tests/fixtures/soak/soak-landside.flow.json` | `soak-landside.flow.json` |
| schedule | `tests/fixtures/soak/soak.csv` | `soak.csv` |

- The flow graph's only `Sink` is `NodeId(9)`, which is
  `PHASE0_DEPARTURE_SINK` (§19.2a).
- Every schedule row has `repeat_daily = 1`, so that every day carries
  load (`03`).
- **Sizing.** The fixture is sized as `03` requires, with headroom below
  0.1 ms per tick of whole-sim mean cost. The security queues drain between
  banks, and live cohorts stay bounded over 500 days. The values are
  fixture sizing, not balance. The Test Author states the derivation in
  the sizing test (§19.7).
- The set is separate from the Phase 0 set so that editing a Phase 0
  fixture for T-009's tests never moves the soak golden, and editing the
  soak set never moves a T-009 expectation.

**The run.** `soak --days D` makes one run (§19.1) of `ticks = D ×
TICKS_PER_SIM_DAY`, with seed 12345, §19.2's command script for that
`ticks`, and the §19.2a composer over the soak set. It is compared with no
second run. `02`'s pass condition is the golden. `soak` times nothing and
reports no timing. The 0.1 ms bound is checked by the sizing test, not by
the gate.

**The dump.** From the run's recorded checkpoints, the harness builds the
checkpoint dump, version 1, of `16` §16.8, byte for byte: `seed 12345`,
the `systems` line with the four registered systems' `Name`s in registry
order, and then one line per checkpoint. Call its bytes `R`.

**`--golden P`.** Before the run, the harness reads all of `P`'s bytes
once, into `G`. A failure to read is exit 3. After the run, the gate passes
iff `R` and `G` are byte-identical. Otherwise let `o` be the first byte
offset at which they differ, or the shorter length if one is a prefix of
the other. The report is `FAIL soak line=<L>`, where `L` is 1 plus the
number of LF bytes in `R` before `o`, and the exit code is 1. On stderr the
harness should also write line `L` of each. That output is free-form and
is not tested. The harness reads no other golden file, so
`soak-500.meta.json` is never read.

**`--out P`.** This form authors a golden, and CI does not use it. Before
the run, if `P` already exists, or its parent directory does not, that is
exit 3. So the harness never overwrites a file. After the run, it creates
`P` as a new file and writes `R` to it. A failure to write is exit 3.
Writing a file does not make it a golden. Whether a dump is committed as
`tests/golden/soak-500.hashes`, and who confirms it, is governed by
`tests/golden/README.md` (no agent regenerates a golden, and the human
owner confirms every change).

**Paths.** `P` is not empty. If `Path.IsPathRooted(P)`, it is used as
given. Otherwise it is a `/`-separated repository-relative path, joined to
the root found as in §19.2a. The current working directory is never used.
So the nightly workflow's `--golden tests/golden/soak-500.hashes` names the
committed golden from whatever directory the job runs in.

**The golden.** `tests/golden/soak-500.hashes` is the `--out` dump of
`soak --days 500` (`03`). Until it is committed, the nightly `soak` exits
3. That is correct, because a missing golden is a harness error and never a
pass.

**When the composition changes.** The soak registers what the §19.2a
composer registers, which is every system the harness composes (`03`).
The amendment that adds a system to that composer, such as `sim.airside`
replacing the stand-in, changes the soak's hashes, and its golden is
re-authored under the README's rules.

## 19.3 The command line (Q-026)

Exactly these forms. The first five are the ones `ci/run-checks.sh` uses.
`soak --golden` is the one the nightly workflow uses (Q-057), and
`soak --out` is used by no CI job. The flags
after the subcommand may come in any order. Each is given at most once.
Each value is a decimal integer without a sign, except `P`, which is a
path (§19.2b).

| Invocation | Does | Seed |
|---|---|---|
| `determinism --days D --seed S` | `SameProcess` with `ticks = D × TICKS_PER_SIM_DAY` | `S` |
| `determinism --days D --seed S --hash-only` | `FinalHash`, same ticks | `S` |
| `saveload --ticks T --save-at K` | `SaveLoad(T, K)` | 12345 |
| `promotion --days D` | `Promotion`, `ticks = D × TICKS_PER_SIM_DAY` | 12345 |
| `budget --tier max` | §19.4 | 12345 |
| `soak --days D --golden P` | §19.2b, the run's dump compared with `P` | 12345 |
| `soak --days D --out P` | §19.2b, the run's dump written to `P` | 12345 |

**Usage errors.** A missing subcommand or flag, an unknown subcommand or
flag, a repeated flag, a value that does not parse, `D = 0`,
`D × TICKS_PER_SIM_DAY` above `uint32`, `T = 0`, `K` outside `0 < K < T`,
a `--tier` other than `max`, a `soak` with both or neither of `--golden`
and `--out`, and an empty `P` are all usage errors.

**Exit codes.**

| Code | Meaning |
|---|---|
| 0 | the gate passed, or `soak --out` wrote its dump |
| 1 | the gate failed: a divergence, a budget exceeded, or a `soak` dump that differs from its golden |
| 2 | usage error. Nothing runs, and stdout stays empty |
| 3 | harness error: any exception during the run, `SimInvariantException` included, a §19.2a fixture failure before the first run, or a §19.2b path failure (a golden that cannot be read, or an `--out` path that exists, has no parent directory or cannot be written) |

For codes 2 and 3, stdout is empty, and stderr carries one human-readable
message. For 3 that is the exception's `ToString()`, or, for a pre-run
fixture or path failure that is not an exception, a message naming the file
and the failure. No other exit code is
returned. There is no "not implemented" code: `determinism`, `saveload`,
`promotion` and `budget` are T-006's in full, `soak` is T-013's, and a
subcommand that cannot run fails as code 3, never as 0. Until T-013
merges, `soak` is an unknown subcommand, which is code 2.

**Stdout.** UTF-8 without a BOM, LF, invariant formatting, single spaces.
There is **exactly one line**, followed by a newline:

```
--hash-only:   <hex16>
pass:          PASS <gate> ticks=<n> checkpoints=<k> final=<hex16>
divergence:    FAIL <gate> tick=<t> at=<where>
budget:        <PASS|FAIL> budget ticks=<n> mean_us=<m> p99_us=<p>
soak written:  WROTE soak ticks=<n> checkpoints=<k> final=<hex16>
soak differs:  FAIL soak line=<L>
```

- `<gate>` is `determinism_same_process`, `determinism_save_load`,
  `determinism_promotion` or `soak`.
- In the `soak` pass and written lines, `final` is the run's final
  `WorldStateHash()`. `<L>` is defined in §19.2b.
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

One run of `TICKS_PER_SIM_DAY` ticks with the CLI composition over the
Phase 0 set, stepped one
tick per `Step(1)`. Each `Step(1)` is timed with
`Stopwatch.GetTimestamp()` in `long` arithmetic (`07` L11). The samples,
the pass condition and the reported numbers are exactly `03` "Budget tests:
window and arithmetic" (Q-045), with `B = 6000` and `n = TICKS_PER_SIM_DAY`:
each sample is rounded **up** to whole microseconds, `mean_us` is the
rounded-up mean, and `p99_us` is the nearest-rank value. It passes if
`mean_us ≤ 6000` and `p99_us ≤ 12000`, which is exactly `03`'s condition.
This replaces the earlier flooring of the samples and of the mean, which
accepted a mean just over 6000 µs. That is the `03` statistic applied to
`01`'s 6 ms whole-sim total. Per-module budgets
stay in each module's own tests (`03`, "How a budget is measured"). The
harness does not re-assert them.

**The statistic is `HarnessGates.BudgetFromSamples` (Q-058).** Real
timings cannot show rounding up apart from flooring, so the statistic is a
pure public member that a test calls with chosen samples. It runs nothing,
reads no clock, and does not modify `samples`.

- **Result.** `samples` are the raw differences `d`, and `frequency` is
  `f`. With `B = 6000` and `n = samples.Count`, `Passed` is `03`'s verdict
  (steps 2 to 4), and `Report` is the §19.3 budget line, `ticks=<n>`, with
  `03` step 5's `mean_us` and `p99_us`.
- **The CLI uses it.** `budget --tier max` collects its `n` samples in tick
  order and makes exactly one call, with `Stopwatch.Frequency`. It prints
  `Report`, and it exits 0 if `Passed` and 1 otherwise. The CLI computes
  no part of the verdict or the line itself. A Reviewer checks this,
  because a test cannot.
- **Arguments.** `null` throws `ArgumentNullException`. These throw
  `ArgumentOutOfRangeException`: `samples.Count ≠ TICKS_PER_SIM_DAY`
  (`03`'s window), any sample below 0, and a `frequency` that is ≤ 0 or
  above `long.MaxValue / (C + 1)` with `C = 6000 × TICKS_PER_SIM_DAY + 1`
  (`03` step 2's bound, beyond which the rule does not hold). In the CLI,
  such an exception is exit 3.
- **Why a public member.** `07` L5 forbids `InternalsVisibleTo`, so an
  internal function could not be tested. A CLI flag that injects timings
  would put a seam in the gate itself. This member adds neither.

> **LOW CONFIDENCE — the load.** `03` names a max-tier fixture, and the harness
> has none. Until one is specified, `budget --tier max` times the CLI
> composition. From T-009 (Q-042) that is the first sim-day of the Phase 0
> composition (§19.2a): 200 movements and about 20 000 departing passengers,
> well below max tier (`01`). So a PASS here says little about max tier. The
> max-tier load arrives with the max-tier fixture, by amendment. Fixture
> loading is not timed, since only `Step(1)` calls are. The measurement is on
> the CI agent, with no scaling factor applied.

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

## 19.6 Tests of the Phase 0 composition and the kill gate (Q-041 to Q-043)

Binding on T-009's Test Author.

- **Location (Q-041).** Every T-009 test, the kill-gate tests included, is
  in `tests/tools/simharness/`. `07` L3 lets `tests/sim/core` reference
  only `src/sim/core`, so that project cannot see the three factories or
  `HarnessGates`. The harness test project sees `sim.world`, `sim.schedule`
  and `sim.flow` through the harness's own references, which T-009 adds
  (`07` L8).
- **The kit composition.** A test that needs the Phase 0 systems
  themselves builds its own composer from the same four fixtures, exactly
  as §19.2a does. It uses the same construction and registration order. It
  loads the content with `ContentLoaderFactory`, through the test's own
  `IContentSource` over the manifest. At position 3 it registers a probe
  system (`08` §8.5) that behaves as the stand-in and whose
  `ComputeStateHash()` returns `0`. It registers nothing else, so its
  hashes equal the CLI's. The probe may call `sim.flow` and `sim.schedule`
  queries and record what they return, since queries change no hashed
  state.
- **The equivalence test.** `test_harness_cli_hash_only_matches_final_hash_gate`
  compares `determinism --days 1 --seed 99 --hash-only` with
  `HarnessGates.FinalHash` of the kit content and the kit composer, seed
  99, 14 400 ticks, in place of the empty composition. It ties the kit to
  the CLI, so a kill-gate test built on the kit measures the CLI
  composition.
- **T-006's other CLI tests** in `HarnessCliTests.cs`. Every expected hash
  that they compute with `EmptyCompositionFinalHash(n)` becomes
  `HarnessGates.FinalHash(kit content, kit composer, seed, n)`, with the
  invocation's own seed and tick count, where `saveload` and `promotion` use
  seed 12345 (§19.3). A literal golden may be added beside it, but is not
  required. `test_harness_cli_budget_core_only_day_passes` is renamed
  `test_harness_cli_budget_phase0_day_passes`, with its assertions
  unchanged. No other CLI test's assertions change. The Test Author may,
  and should, update the comments in `HarnessCliTests.cs` that describe
  the empty composition, such as the class doc comment and the renamed
  budget test's comment. `HarnessGatesTests.cs` and
  `EmptyCompositionFinalHash` are unchanged, since they test the gates with
  their own composers.
- **The kill-gate tests.**
  - (a) **A whole-run wall-clock gate.** One run of 1 440 000 ticks with
    seed 12345, through `HarnessGates.FinalHash` with the kit, takes under
    60 s **in total**, composition included. The one measurement is the
    elapsed `Stopwatch.GetTimestamp()` difference around the whole call,
    compared in `long` arithmetic (`07` L11). It carries the `Budget`
    trait, but it is not a per-module budget. `03`'s per-tick statistic
    (mean and p99 over one sim-day's ticks) does not apply to it, and it
    takes no per-tick samples.
  - (b) `HarnessGates.SameProcess` with the kit, the same seed and ticks,
    passes, with `checkpoints=2400`.

  Both are Slow by `07` L11a rule (a). After the gate returns, a load check
  replaces the assumption that nothing absorbs. In (a) it checks the one
  run. In (b) it checks the second run, the composer's latest call, whose
  systems the kit keeps. The gate has already shown both runs equal.
  - `PublishedFlights()` holds 101 × 200 flights;
  - the head count is conserved. The passengers injected equal the
    population on all nodes at the end, plus
    `PopulationForFlight(flight, Departing)` summed over the probe's
    `Absorb` calls, each read just before its call;
  - the sum of the `Absorb` return values is above 0;
  - the live cohorts at the end, `CohortsAt(node).Count` summed over all
    nodes, are at most a ceiling that the Test Author sets and derives in
    the test (`09` §9.10). This is the check that catches a `Gate` growing
    without bound.

## 19.7 Tests of `soak` and of the budget statistic (Q-057, Q-058)

Binding on the Test Authors of T-013 and T-045. Every test is in
`tests/tools/simharness/` (Q-041). No test writes a repository file. A
`soak` path in a test is a rooted path in a fresh temporary directory
that the test deletes. No xUnit test runs 500 days, since the nightly gate
does that.

**`soak` (T-013).** The **soak kit** is §19.6's kit composition built over
the soak set instead of the Phase 0 set.

- **Round trip.** `soak --days 1 --out <tmp>/a` exits 0 and prints the
  `WROTE` line. The file starts `airport-sim-checkpoints 1`, then
  `seed 12345`, then a `systems` line with four names, and has 24
  checkpoint lines. `soak --days 1 --golden <tmp>/a` then exits 0 and
  prints the `PASS soak` line, with the same `checkpoints=24` and `final`.
- **Equivalence.** That `final` equals `HarnessGates.FinalHash` of the
  soak kit content and composer, seed 12345, 14 400 ticks. This ties the
  soak kit to the CLI.
- **Reproducible.** Two `--out` runs of `--days 1`, to two files, are
  byte-identical.
- **Differs.** The golden with one hexadecimal digit changed on its
  checkpoint line `L` exits 1 and prints `FAIL soak line=<L>`. The golden
  cut after its line `M` (a prefix) exits 1 and prints
  `FAIL soak line=<M + 1>`.
- **Path failures.** A `--golden` path that does not exist exits 3, and a
  `--out` path that already exists exits 3 and leaves that file unchanged.
  In both, stdout is empty.
- **Usage.** `soak --days 1` alone, `soak` with both `--golden` and
  `--out`, `soak --days 1 --golden ""`, and `soak --days 1 --seed 1
  --golden <tmp>/a` all exit 2.
- **Sizing.** One run of the soak kit through `HarnessGates.FinalHash`,
  of `K` sim-days with `K ≥ 10` and seed 12345, takes in total less than
  `K × 14 400 × 100` µs. That is a mean under 0.1 ms per tick. It is
  measured as §19.6 (a) is: one `long` elapsed difference around the whole
  call, compared in `long` arithmetic. It carries the `Budget` trait, but
  it is a whole-run gate and is not bound by `03`'s per-tick statistic. It
  is Slow by `07` L11a whenever `K > 10`. After it returns, it checks that
  the live cohorts stay at most a ceiling that the Test Author derives in
  the test, as in §19.6. The derivation of the fixture's sizing goes in
  this test.

**The budget statistic (T-045).** These tests call
`HarnessGates.BudgetFromSamples` directly, with `n = 14 400` samples and
`f = 10^7` unless stated otherwise. Each expected `Report` is given in
full.

| Test | Samples | `Report` |
|---|---|---|
| `test_harness_gates_budget_from_samples_at_budget_passes` | all `60 000` | `PASS budget ticks=14400 mean_us=6000 p99_us=6000` |
| `test_harness_gates_budget_from_samples_rounds_each_sample_up` | all `60 001` (6000.1 µs, which flooring would pass) | `FAIL budget ticks=14400 mean_us=6001 p99_us=6001` |
| `test_harness_gates_budget_from_samples_p99_nearest_rank_passes_at_144_slow` | 144 of `120 001`, the rest `0` | `PASS budget ticks=14400 mean_us=121 p99_us=0` |
| `test_harness_gates_budget_from_samples_p99_nearest_rank_fails_at_145_slow` | 145 of `120 001`, the rest `0` | `FAIL budget ticks=14400 mean_us=121 p99_us=12001` |
| `test_harness_gates_budget_from_samples_caps_overflowing_sample` | one `long.MaxValue`, the rest `0` | `FAIL budget ticks=14400 mean_us=6001 p99_us=0` |

In the p99 rows, the position of the slow samples in the list varies and
the result does not. `samples` is unchanged after each call. One more test,
`test_harness_gates_budget_from_samples_rejects_bad_arguments`, checks the
§19.4 argument rules: `null`, 14 399 samples, one sample of `-1`, and
`frequency` 0. The existing `HarnessCliTests` budget tests are unchanged.
