# 19 — Public interfaces: `tools.simharness` (the CI gates)

Pins the harness subcommands that `ci/run-checks.sh` invokes, their exit
codes and output, and the public surface that the harness's own test project
compiles against. It answers `open-questions.md` Q-025, Q-026 and Q-027,
and, for T-009's Phase 0 composition, Q-041 to Q-043. For T-013's `soak`
subcommand, which the nightly workflow invokes (`.github/workflows/`), it
answers Q-057, and for T-045's budget statistic Q-058. For T-030's
`checkpoints` subcommand it answers Q-066 to Q-076, and for T-014's
promotion in `Promotion` Q-084.
Notation is as in `08-interfaces-core.md`. Where this file appears to
contradict `01-architecture.md` or `02-determinism.md`, those win and it is a
spec bug. `ci/**` is the human owner's. This file describes what the harness
does when it is invoked the way `ci/run-checks.sh` already invokes it, and it
never asks for a change there.

Reading order for a harness worker: `01`, `02`, `07`, `08` §8.5, §8.5a,
§8.7, §8.9 and §8.11a, then this file. For §19.2a, also `09` §9.7 and
§9.11, `11` §11.7 and §11.9, `12` §12.3 and §12.7, and `18`. For the
`checkpoints` subcommand, read `16` §16.3, §16.4 and §16.8, then §19.2c.
`16` §16.8 owns the dump format. §19.2c owns the invocation, the
composition and the failures. For `Promotion`'s second run, read `09`
§9.1, §9.7 (`KindOf` included) and §9.10, `15` §15.6 and `18`, then
§19.2d.

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
  `compose` receives the factory's builder itself in every run except
  `Promotion`'s second run, which hands it the recording builder of
  §19.2d over that run's factory builder (Q-084).
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
- `soak` (§19.2b) and `checkpoints` (§19.2c) have no `HarnessGates`
  member. They are reached only through `HarnessCli.Run`. `checkpoints`
  takes no `SimComposer` and submits no command script (§19.2c), so the
  "One run" bullet above applies to it only as far as §19.2c says.

## 19.2 What each gate does (Q-026, Q-027)

**The command script.** Every run except a `checkpoints` run (§19.2c,
Q-071) submits, before its first `Step`, one
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
| `Promotion` | two runs of `ticks`. The second is the "camera parked" run, which promotes the lowest-id `Gate` node, if any, and draws its agents every tick (§19.2d) | the two runs compare equal |

- **`SaveLoad` before `sim.save` (Q-027).** No save seam exists (`08` §8.8).
  So the save is the seed, the content, the composition and the command log.
  This is the replay form of "the log is the save" (`01-architecture.md`),
  with the snapshot at tick 0. RNG stream state is **not** exported. The
  harness snapshots nothing else, and it invents no seam. When `sim.save`
  specifies a snapshot, `SaveLoad` is amended to reload from it. See the
  HUMAN DECISION (owner, 2026-09-26) in §19.5.
- **`Promotion`'s second run promotes (T-014, Q-033, Q-084).** Before
  its first `Step`, the second run promotes the lowest-id `Gate`-kind
  node through `IFlowSystem.SetPromoted` (`09` §9.7). It then draws that
  node's passengers with `AgentsAt` after every tick, exactly as §19.2d
  pins. That is the owner's literal reading of `02`'s "camera parked on a
  gate" (HUMAN DECISION, 2026-10-02). The comparison is unchanged. A
  composition with nothing to promote, the empty one included, differs
  between the runs in nothing, and the gate still compares for real and
  passes vacuously. A harness that stubs the comparison is wrong.
- **The CLI composition.** Every CLI run, of every subcommand in §19.3
  except `checkpoints`, uses the composer of §19.2a (Q-042, from T-009).
  `soak` runs it over the
  soak fixture set (§19.2b, Q-057), and every other subcommand over the
  Phase 0 fixture set. It replaces
  T-006's empty composition, which no CLI form selects any more. The empty
  composition stays reachable only through `HarnessGates` with a composer
  that registers nothing, as the gate tests already use it. No option,
  flag or environment variable selects a composition or a fixture set. The
  subcommand alone decides the set. `checkpoints` is the one exception
  (Q-069). It composes the bundle that `--bundle` names, over the content
  that `--content` names, by `16` §16.4's rules (§19.2c). It never uses
  the §19.2a composer, either fixture set or the boarding stand-in.
- **Untested by design (Q-029).** `at=world` and `at=count` cannot be
  reached. Runs of one gate step the same ticks at the same checkpoint
  cadence, so their counts match. A world hash is a function of the tick,
  `CoreHash` and `SystemHashes`, so it cannot differ while those agree. Both
  stay in the grammar as defensive reports. Exit codes 1 and 3 cannot be
  reached by a test through `HarnessCli.Run` either (Q-042), except by
  `soak` and, for code 3, `checkpoints` (below). Code 1 needs a
  nondeterministic composition, and the Phase 0 composition is
  deterministic. Code 3 needs a missing or invalid repository fixture, which
  an in-process test cannot arrange without writing repository files. The
  divergence seam (§19.1) proves failure through `HarnessGates` instead, and
  no CLI seam is added. T-006's earlier statement that the first composing
  task makes them reachable is withdrawn. `soak` and `checkpoints` are the
  exceptions, because their paths may be fully qualified paths outside
  the repository (§19.2b, §19.2c). A test reaches `soak`'s code 1 with an
  altered golden and its code 3 with a missing one, both in a temporary
  directory (§19.7). It reaches `checkpoints`'s code 3 with an existing
  or parentless `--out`, a missing `--bundle` or a bundle without a
  listed system's file, also in a temporary directory (§19.8).
  `checkpoints` never returns 1.

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
  `sim.airside` to this composer removes it in the same change. No
  production composition (`16`) registers it, and neither does
  `checkpoints` (§19.2c, Q-070). `08` §8.5's probe-system allowance covers
  it.

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

**Paths.** `P` is not empty. If `Path.IsPathFullyQualified(P)`, it is
used as given. Otherwise it is a `/`-separated repository-relative path,
joined to the root found as in §19.2a. `Path.IsPathRooted` is not the
test. On Windows it also accepts `/x` and `C:x`, which resolve against the
current drive or directory. A `P` that is rooted but not fully qualified
is therefore a usage error. The current working directory is never used.
So the nightly workflow's `--golden tests/golden/soak-500.hashes` names the
committed golden from whatever directory the job runs in.

**The golden.** `tests/golden/soak-500.hashes` is the `--out` dump of
`soak --days 500` (`03`). Until it is committed, the nightly `soak` exits
3. That is correct, because a missing golden is a harness error and never a
pass.

**When the composition changes.** The soak registers what the §19.2a
composer registers, which is every system the harness's CLI composition
composes (`03`). `checkpoints`'s bundle composition (§19.2c) is not part
of that: the harness referencing a module for `checkpoints` does not, by
itself, add it to the §19.2a composer or to the soak.
The amendment that adds a system to that composer, such as `sim.airside`
replacing the stand-in, changes the soak's hashes, and its golden is
re-authored under the README's rules.

## 19.2c `checkpoints`: a bundle's checkpoint dump (Q-066 to Q-076)

Binding on T-030, on its Test Author, and on the harness task that adds
the Phase 1 systems (below). This is the harness side of `16` §16.8 (D7).
`16` §16.8 owns the dump format, and nothing here changes it.

**What it does.** `checkpoints --bundle B --content C --days D --out P`
composes the scenario bundle in directory `B` by `16` §16.4's rules, over
the content in directory `C`. It steps the result for `D` sim-days and
writes the run's checkpoint dump to the new file `P`. It is not a gate. It
compares nothing, and it never exits 1. It does not use the §19.2a
composer, either fixture set, the boarding stand-in or the §19.2 command
script.

**Paths (Q-068).** `B`, `C` and `P` each follow §19.2b "Paths": not empty;
a fully qualified path is used as given; any other path is a
`/`-separated repository-relative path joined to the root, which is found
as in §19.2a; a path that is rooted but not fully qualified is a usage
error. The root is looked for only when at least one of the three is
repository-relative, and not finding it is exit 3. The current working
directory is never used. `B` and `C` are directories. `P` is never
overwritten. The harness writes nothing except `P`.

**The bundle.** The harness reads bundle files by exact name, as `B`
joined with a `16` §16.3 file name. It never lists `B`. It reads
`bundle.json`, and then exactly the files of the listed systems, from the
§16.3 table:

| Listed system | Files read |
|---|---|
| `sim.world` | `world.fixture` |
| `sim.schedule` | `schedule.csv` |
| `sim.airside` | `airside.fixture`, `airside_rules.json` |
| `sim.flow` | `flow.fixture` |
| `sim.turnaround` | `turnaround.fixture` |
| `sim.delay` | none |

It reads no other file in `B`, `render_layout.fixture` included, because
the run has no presentation. `bundle.json` follows `16` §16.3's rules,
including its strict form (Q-069).

**The content (Q-072).** `C` is a content directory laid out like `data/`.
The harness's `IContentSource` returns from `Files()` the path of every
file under `C`, at any depth, relative to `C` and `/`-separated, in any
order. `ReadAll` returns that file's bytes. The content is loaded once,
before the run, with `ContentLoaderFactory.Create().Load(source)` and then
`ContentIndexFactory.Create` of the result (`08` §8.11). The loader reads
in ordinal path order and ignores the directories it does not map, so the
listing decides which files exist and never their order. So
`--content data` loads what the player ships (`16` §16.3), and a fixture
directory such as `tests/fixtures/harness/phase0-content` works the same
way. This is the only directory the harness ever lists. §19.2a's rule
that the harness never reads `data/` binds the §19.2a composition only.

> **LOW CONFIDENCE — a listed content directory.** §19.2a uses a manifest
> so that a stray file cannot change the content. Here the directory is
> the whole content input, as it is for the player, whose content is a
> copy of `data/`. A manifest would need one for `data/`, which no module
> owns. A stray `*.json` in a kind directory changes both sides of a
> comparison alike, or it fails the load.

**The composition (Q-069, Q-070, Q-073).** Exactly `16` §16.4's
`Compose`, steps 1 to 4, written in the harness's own code (§16.4's last
rule):

1. `SimHostFactory.CreateBuilder` with `MasterSeed` = the bundle's seed,
   `Content` = the content index, `Checkpoints` = a harness-internal sink
   that records every checkpoint in order, and `Log` = a harness-internal
   log that discards everything (as in §19.1).
2. Each listed system's file is loaded with its module's loader (the
   Construction sections named in `16`), with `sourceName` exactly the
   bundle file name, for example `schedule.csv` (Q-073). The harness
   parses `airside_rules.json` into `AirsideRules` itself, by
   `04-data-schemas.md`, since no sim module parses it (`12` §12.12a).
3. The listed systems are constructed in §16.4's dependency order, each
   with `builder.Services`, its data, and its downward interfaces or
   `null`. `turnaroundRegistered` is whether `sim.turnaround` is listed.
4. They are registered in registry order (`08` §8.5), and then `Build` is
   called.

Nothing else is registered: no boarding stand-in and no probe (Q-070). So
the harness's dump of a bundle is byte-identical to `IHeadlessRun`'s dump
of the same bundle and content (`16` §16.8, D7).

**Which systems it composes (Q-069). Staged.**

- **T-030** composes `sim.world`, `sim.schedule` and `sim.flow`, whose
  factories the harness already references (T-009). A bundle that lists
  `sim.airside`, `sim.turnaround` or `sim.delay` is exit 3, and stderr
  names the system. It is not a usage error, because the bundle is read
  only after usage has been decided. No test asserts this rejection,
  because the next stage removes it.
- **The Phase 1 stage** is the harness task that adds `sim.airside`,
  `sim.turnaround` and `sim.delay`, together with their `ProjectReference`s
  (`07` L8). It is released after T-021, T-022 and T-024 merge. It removes
  the rejection, so that all six Phase 1 systems are composed. It must
  merge before T-031, whose D7 test needs it (`16` §16.8). If the Planner
  instead orders T-030 itself after T-021, T-022 and T-024, then T-030 is
  both stages, and the first bullet never applies.
- Neither stage changes the §19.2a composer, the soak or the stand-in
  (§19.2b, "When the composition changes").

**The run (Q-071, Q-074).** One run. It submits **no command**, since
§19.2's script does not apply and `IHeadlessRun` submits none either
(`16` §16.8). The queue still enters every hash (`08` §8.7). It is empty on
both sides. The run calls `Step(TICKS_PER_SIM_DAY)` exactly `D` times,
which is the stepping `16` §16.8 pins for both sides. `08` §8.2
("Chunking is invisible") already makes any other batching equal, and the
test of §19.8 checks that against this run. After the last `Step`, `final`
is `WorldStateHash()`.

**The dump.** From the run's recorded checkpoints, the harness builds the
checkpoint dump, version 1, of `16` §16.8, byte for byte, as `soak` does
(§19.2b): `seed` is the bundle's seed, and the `systems` line holds the
registered systems' `Name`s in registry order. Call its bytes `R`.

**When things happen, and failures (Q-067).**

1. A usage error (exit 2) is decided before any file is touched.
2. Then, before the run: the root is found if it is needed. If `P` exists,
   or its parent directory does not, that is exit 3. `bundle.json` is read
   and checked (`16` §16.3), every listed system is checked to be
   composable (above) with its downward interfaces listed (`16` §16.4),
   each listed system's files are read, and the content is loaded.
3. Composition and the run. Loader failures surface here, in step 2 of
   the composition.
4. `P` is created as a new file, and `R` is written to it.

A failure in steps 2 to 4 is exit 3, with stdout empty, and stderr
carries one message naming the file and the failure, or, for an
exception, the exception's `ToString()` (§19.3). These are exit 3: a
missing `B` or `C`; a missing `bundle.json`, or one that breaks `16`
§16.3; a listed system that this stage does not compose; a listed system
whose downward interface is not listed; a listed system whose file is
missing; a content or loader failure; any exception during the run,
`SimInvariantException` included; and an `--out` path that exists, has no
parent directory or cannot be written. `P` is created only in step 4, so
an exit 3 from steps 2 or 3 leaves no file. A write failure in step 4 may
leave a partial file, and no test depends on it. On success, the exit code
is 0 and stdout is the `checkpoints written` line of §19.3.

**The fixture (Q-076).** The Phase 0 checkpoints bundle is the directory
`tests/fixtures/harness/checkpoints-phase0/`, written by T-030's Test
Author (`07` L8). It holds exactly four files:

- `bundle.json`: `schema_version` 1, `seed` `"12345"`, `systems`
  `[ "sim.world", "sim.schedule", "sim.flow" ]`;
- `world.fixture`, `flow.fixture` and `schedule.csv`, which are byte copies
  of the Phase 0 set's walk graph, flow graph and schedule (§19.2a) at the
  time they are written.

They are separate files, as the soak set is (§19.2b), and are not required
to stay equal to the Phase 0 set. Its content is
`--content tests/fixtures/harness/phase0-content`, which has the same
files as its manifest. The Phase 1 checkpoints bundle,
`tests/fixtures/harness/checkpoints-phase1/`, is written by the Phase 1
stage's Test Author. Its `bundle.json` lists all six Phase 1 systems, with
seed `"12345"`. It holds `world.fixture`, `schedule.csv`,
`airside.fixture`, `airside_rules.json`, `turnaround.fixture` and
`flow.fixture`. That is six files for the five
listed systems that need one: `sim.airside` takes two, `airside.fixture`
and `airside_rules.json`, and `sim.delay` takes none. It holds no `render_layout.fixture`, since
`checkpoints` never reads it. Its content is `--content data`.

- `world.fixture`, `schedule.csv`, `airside_rules.json`,
  `turnaround.fixture` and `flow.fixture` are each a byte copy of the
  source that `16` §16.3's playtest-bundle table gives for that name.
- **`airside.fixture` is not a byte copy (Q-113).** It is itself the
  source of §16.3's `airside.fixture` row. It is
  `tests/fixtures/airside/phase1-single-runway.json` (`12` §12.13) with
  exactly three substitutions, applied to every
  `max_aircraft_size_category` value and to nothing else:
  `"medium"` becomes `"size_c"`, `"heavy"` becomes `"size_e"` and
  `"super"` becomes `"size_f"`. Every other byte is the same. The reason:
  §12.13's fixture names the size categories that the airside, turnaround,
  delay, render and UI test kits build in code (`small` 1, `medium` 2,
  `heavy` 3, `super` 4), but `data/` defines only `size_a` to `size_f`,
  so under `--content data` `CreateSystem` throws on it (`12` §12.4). The
  substitution keeps every stand-compatibility outcome for the nine
  aircraft types of `tests/fixtures/schedule/phase0-200.csv` the same as
  under those kits. Under `data/`'s ordinals, the `size_c` stand takes
  `atr72`, `crj900`, `a320`, `a321` and `b738`, the `size_e` stand takes
  every type except `a388`, and the `size_f` stands take all nine.
- **Every content id resolves in `data/` (Q-113).** Checked against
  `data/` at `c48e163`: the schedule's nine `aircraft_type`s (`a320`,
  `a321`, `a359`, `a388`, `atr72`, `b738`, `b744`, `b789`, `crj900`) and
  the size categories they name (`size_b` to `size_f`); its two
  `pax_profile`s (`business`, `leisure`); the flow fixture's one
  `queue_profile` (`security_standard`); and `airside.fixture`'s
  `size_c`, `size_e` and `size_f`. `airline` is not a content id (`11`
  §11.4). The world and turnaround fixtures and `airside_rules.json` name
  no content id. Whoever later changes one of these files, or `data/`,
  keeps this true and checks it in the same change.

## 19.2d `Promotion`: what the second run promotes (Q-084)

Binding on T-014 and its Test Author. It replaces the "vacuous until
T-010" reading of `Promotion` (§19.2).

**Scope.** It applies to **every** composer passed to
`HarnessGates.Promotion`, whether that is the §19.2a composer or a test's.
The gate is a function of its arguments and cannot tell them apart, and
no seam may be added that tells it. Nothing else wraps a builder or
promotes: `SameProcess`, `SaveLoad`, `FinalHash`, `budget`, `soak` and
`checkpoints` are unchanged.

**Run 1** is exactly §19.1's run. The harness calls no member of any
registered system.

**Run 2: the recording builder.** The harness creates the run's factory
builder as §19.1 says. It calls `compose` exactly once, with a fresh
harness-internal `ISimHostBuilder` over the factory builder, called the
**recording builder**:

- `Services` returns the factory builder's `Services`.
- `Register(s)` calls the factory builder's `Register(s)`. If that call
  returns, the recording builder records `s`. If it throws, the exception
  propagates and nothing is recorded.
- `Build()` calls the factory builder's `Build()`. A composer must not
  call it (§19.1). If one does, the harness's own `Build` then throws, as
  `08` §8.11a says.

After `compose` returns, the harness calls `Build()` on the factory
builder. The recording builder adds no behaviour beyond recording, so run
2 registers exactly what run 1 registers.

**Finding the systems.** Among the recorded systems:

- `flow` is the system whose `Id` is `SystemId(4)`, `sim.flow`'s registry
  position (`08` §8.5), if it implements `IFlowSystem`;
- `world` is the system whose `Id` is `SystemId(1)`, if it implements
  `IWorldSystem`.

`Register` takes each position at most once, so each of these is at most
one system. A system at any other position is never `flow` or `world`,
whatever it implements. A system at position 4 or 1 that does not
implement the interface is not `flow` or `world` either. So the gate
tests' non-flow probes, at 4 and elsewhere, cause no promotion.

> **HUMAN DECISION — owner, 2026-10-02 (Q-084): follow `02` literally.**
> "Camera parked on a gate" means that run 2 promotes a real `Gate`-kind
> node and draws its passengers through `AgentsAt` after every tick. Run
> 2 must still match run 1 exactly. The owner's stated purpose was to
> exercise the `flow.presentation` stream (`09` §9.1 rule 2). Merged
> `sim.flow` does not use that stream yet: its `AgentsAt` fills a reused
> buffer with `ProgressAlongEdge = 0` and draws from no RNG. So that
> benefit arrives only when `sim.flow` derives agent detail from the
> stream, and from then on this gate exercises it with no change here.
> The cadence is the owner's. The camera's own `AgentsAt` caller is the
> scene builder, once per rebuild per promoted node (`15` §15.6), not
> once per tick. The Architect recorded this decision and did not make
> it.

**Finding the gate.** After `Build()` returns, and before the command
script is submitted:

1. If `flow` or `world` is missing, the harness calls nothing.
2. Otherwise it calls `world.Nodes()` once. It then calls
   `flow.KindOf(n)` (`09` §9.7, Q-084) for each `n` in that list, in list
   order, which is ascending `NodeId` (`18`). It stops at the first `n`
   whose kind is `NodeKind.Gate`. That node is `gate`, the lowest-id
   `Gate` node.
3. If no node is a `Gate`, which includes an empty `Nodes()`, there is no
   `gate`. The harness calls nothing more, and run 2 submits the script
   and steps exactly as run 1 does.
4. Otherwise it calls `flow.SetPromoted(gate, true)` exactly once.

**Drawing the gate's passengers.** When there is a `gate`, run 2 submits
the script and then, instead of stepping once, repeats this `ticks`
times: call `Step(1)`, then call `flow.AgentsAt(gate)` exactly once.
So the `k`-th `AgentsAt` call comes after `k` ticks, and there are
exactly `ticks` calls. The harness reads only the returned list's
`Count`, keeps no reference to the list, and allocates nothing in the
loop. `08` §8.2 ("Chunking is invisible") makes `ticks` calls of `Step(1)`
equal to run 1's stepping, so the checkpoints are recorded exactly as in
run 1.

The harness calls no other member of `flow` or `world` than those named
above. It never demotes. An exception from `KindOf`, `SetPromoted` or
`AgentsAt`, such as an `ArgumentException` because `flow` was built over
a different world, propagates unchanged from `Promotion`, and the CLI
maps it to exit 3 (§19.3). The comparison, the report and the exit codes
are unchanged (§19.2, §19.3).

- **In the CLI.** Both fixture sets have exactly one `Gate` node,
  `NodeId(8)`, so `promotion --days D` promotes `NodeId(8)` and calls
  `AgentsAt(NodeId(8))` once per tick. `09` §9.11 requires only that
  every `Source` reach a `Gate`. A valid graph with no `Source` can
  therefore have no `Gate`, and the real flow system then reaches step
  3. Neither fixture set is such a graph. If a fixture change ever
  removed the `Gate`, the kit test of §19.9 would fail.
- **Why it is outcome-neutral.** `SetPromoted` writes only the promoted
  flags, and `AgentsAt` writes only its reused view buffer. Both are
  derived and unhashed (`09` §9.10), and neither draws from any RNG
  stream in merged `sim.flow`. If agent detail later comes from
  `flow.presentation`, that stream is excluded from the state hash and
  no other system reads it (`09` §9.1, rules 1 and 2). `KindOf` is a
  query. So the two runs must still compare equal, and `promotion --days
  D` prints the same line as before. A promotion or an agent draw that
  leaked into the hash would fail this gate, and catching that is what
  the gate is for. The calls are therefore observable only through a
  test's own `IFlowSystem` (§19.9).

## 19.3 The command line (Q-026)

Exactly these forms. The first five are the ones `ci/run-checks.sh` uses.
`soak --golden` is the one the nightly workflow uses (Q-057), and
`soak --out` is used by no CI job, and neither is `checkpoints` (Q-066),
which T-031's D7 test and `16` §16.9's procedure use. The flags
after the subcommand may come in any order. Each is given at most once.
Each value is a decimal integer without a sign, except `B`, `C` and `P`,
which are paths (§19.2b, §19.2c).

| Invocation | Does | Seed |
|---|---|---|
| `determinism --days D --seed S` | `SameProcess` with `ticks = D × TICKS_PER_SIM_DAY` | `S` |
| `determinism --days D --seed S --hash-only` | `FinalHash`, same ticks | `S` |
| `saveload --ticks T --save-at K` | `SaveLoad(T, K)` | 12345 |
| `promotion --days D` | `Promotion`, `ticks = D × TICKS_PER_SIM_DAY` | 12345 |
| `budget --tier max` | §19.4 | 12345 |
| `soak --days D --golden P` | §19.2b, the run's dump compared with `P` | 12345 |
| `soak --days D --out P` | §19.2b, the run's dump written to `P` | 12345 |
| `checkpoints --bundle B --content C --days D --out P` | §19.2c, the bundle's dump written to `P` | the bundle's |

**Usage errors.** A missing subcommand or flag, an unknown subcommand or
flag, a repeated flag, a value that does not parse, `D = 0`,
`D × TICKS_PER_SIM_DAY` above `uint32`, `T = 0`, `K` outside `0 < K < T`,
a `--tier` other than `max`, a `soak` with both or neither of `--golden`
and `--out`, an empty `P`, and a `P` for which `Path.IsPathRooted` is
true but `Path.IsPathFullyQualified` is false (§19.2b) are all usage
errors. For `checkpoints`, all four flags are required, and `B` and `C`
are under the same two path rules as `P` (Q-066). It has no `--seed` and
no `--golden`, so either is an unknown flag. The `D` rules are the same
as for every other `--days`: `D = 0` and `D × TICKS_PER_SIM_DAY` above
`uint32` (`D > 298 261`) are usage errors.

**Exit codes.**

| Code | Meaning |
|---|---|
| 0 | the gate passed, or `soak --out` or `checkpoints` wrote its dump |
| 1 | the gate failed: a divergence, a budget exceeded, or a `soak` dump that differs from its golden. `checkpoints` never returns 1 |
| 2 | usage error. Nothing runs, and stdout stays empty |
| 3 | harness error: any exception during the run, `SimInvariantException` included, a §19.2a fixture failure before the first run, a §19.2b path failure (a golden that cannot be read, or an `--out` path that exists, has no parent directory or cannot be written), or any §19.2c failure of steps 2 to 4 (Q-067) |

For codes 2 and 3, stdout is empty, and stderr carries one human-readable
message. For 3 that is the exception's `ToString()`, or, for a pre-run
fixture or path failure that is not an exception, a message naming the file
and the failure. No other exit code is
returned. There is no "not implemented" code: `determinism`, `saveload`,
`promotion` and `budget` are T-006's in full, `soak` is T-013's,
`checkpoints` is T-030's, and a
subcommand that cannot run fails as code 3, never as 0. Until T-013
merges, `soak` is an unknown subcommand, which is code 2, and so is
`checkpoints` until T-030 merges.

**Stdout.** UTF-8 without a BOM, LF, invariant formatting, single spaces.
There is **exactly one line**, followed by a newline:

```
--hash-only:   <hex16>
pass:          PASS <gate> ticks=<n> checkpoints=<k> final=<hex16>
divergence:    FAIL <gate> tick=<t> at=<where>
budget:        <PASS|FAIL> budget ticks=<n> mean_us=<m> p99_us=<p>
soak written:  WROTE soak ticks=<n> checkpoints=<k> final=<hex16>
soak differs:  FAIL soak line=<L>
checkpoints written: WROTE checkpoints ticks=<n> checkpoints=<k> final=<hex16>
```

- In the `checkpoints written` line (Q-067), `<n>` is
  `D × TICKS_PER_SIM_DAY`, `<k>` is the number of checkpoint lines in the
  dump, and `final` is the run's final `WorldStateHash()`.

- In the pass line, `<gate>` is `determinism_same_process`,
  `determinism_save_load`, `determinism_promotion` or `soak`. In the
  divergence line, it is one of the first three only, because `soak`
  reports a difference with its own line.
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
`soak` path in a test is a fully qualified path in a fresh temporary directory
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
  cut to its first `M` lines, with line `M`'s terminating LF kept, exits 1
  and prints `FAIL soak line=<M + 1>`. That is a prefix of `R`, so `o` is
  its length, and `R` has `M` LF bytes before `o`. If line `M`'s LF were
  dropped too, the report would be `line=<M>`. The test uses the LF-kept
  form.
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
`f = 10^7` unless stated otherwise. There are six: the five below, each
with its exact `Report` in full, and one argument test.

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

## 19.8 Tests of `checkpoints` (Q-074 to Q-076)

Binding on T-030's Test Author. Every test is in `tests/tools/simharness/`
(Q-041), calls the harness in process through `HarnessCli.Run` (`07` L3),
and writes no repository file. Each `--out` path is a fully qualified path
in a fresh temporary directory that the test deletes, as in §19.7. Unless
stated otherwise, the invocation is `checkpoints --bundle
tests/fixtures/harness/checkpoints-phase0 --content
tests/fixtures/harness/phase0-content --days 1 --out <tmp>/a`.

**The checkpoints kit.** The test composes the same bundle itself, from
the same files and content, through the published surface only:
`ContentLoaderFactory` over its own `IContentSource` of the content
directory, `SimHostFactory`, and the world, flow and schedule factories
and loaders. It follows §19.2c's composition exactly, with the same
`sourceName`s, registers nothing else, and submits no command. It records
the checkpoints with its own sink and renders the expected dump with its
own code, from `16` §16.8's format and the registered systems' `Name`s.
Unlike §19.6's kit, it has no probe at position 3, because §19.2c
registers no stand-in.

- `test_checkpoint_dump_format_is_byte_exact`. The invocation exits 0 and
  prints `WROTE checkpoints ticks=14400 checkpoints=24 final=<hex16>`.
  `<tmp>/a` is byte-identical to the kit's dump with one
  `Step(TICKS_PER_SIM_DAY)`. Its first three lines are exactly
  `airport-sim-checkpoints 1`, `seed 12345` and
  `systems sim.world sim.schedule sim.flow`. `final` equals the kit's
  final `WorldStateHash()`. Byte identity with a dump the test renders
  itself is what checks the format: no BOM, LF only, a final newline,
  single spaces, decimal ticks and 16 lowercase hexadecimal digits.
- `test_checkpoints_result_independent_of_step_batch_size` (Q-074). The
  kit is run three times: 14 400 calls of `Step(1)`; one call of
  `Step(14 400)`; and `Step(997)` 14 times, then `Step(442)`. The three
  dumps are byte-identical to each other and to the CLI's `<tmp>/a`. The
  CLI's batching is pinned (§19.2c), so this proves that the pinned run
  equals every other batching, with no seam. No CLI flag selects a batch
  size, and none may be added.
- `test_checkpoints_subcommand_composes_through_published_factories_only`
  (Q-075). It has two parts, and both must hold:
  - **Behavioural.** The default invocation exits 0, and `<tmp>/a` is
    byte-identical to the kit's dump, which is built through the
    published surface only. This part fails without a working
    `checkpoints`. It repeats the core of
    `test_checkpoint_dump_format_is_byte_exact` on purpose, so that this
    test cannot pass on its own against a harness that lacks the
    subcommand.
  - **Static,** by reflection over loaded assemblies. The harness
    assembly `AirportSim.Tools.SimHarness` references no
    `AirportSim.App.*` assembly, `AirportSim.App.Host` in particular. So
    the harness cannot reuse `app.host`'s composer, and D7 compares two
    independent compositions (`16` §16.4). No `AirportSim.*` assembly
    that the harness references carries an `InternalsVisibleToAttribute`
    (`07` L5). These checks guard against regression, and today's `main`
    already passes them.

  The Reviewer checks that the harness reaches no non-public member by
  reflection, because a test cannot.
- `test_checkpoints_rejects_usage_errors` (Q-066). First, as its control,
  the default invocation exits 0 and prints the `WROTE checkpoints` line.
  A harness without the subcommand fails here, because an unknown
  subcommand is also exit 2 (§19.3). Then each of these, with the same
  arguments otherwise and a fresh `--out` path, exits 2 with stdout empty
  and creates no file: each of the four flags missing in turn; `--days`
  given twice; `--days 0`; `--days 298262`; `--seed 1` added;
  `--bundle ""`; and, on Windows only, `--out C:a`. The control
  establishes that `checkpoints` is recognised, so each 2 that follows
  comes from the subcommand's own usage rules. stderr is free-form and
  is not asserted.
- `test_checkpoints_harness_failures_exit_3` (Q-067). Each of these exits
  3 with stdout empty: an `--out` path that already exists, which is left
  byte-unchanged; an `--out` path whose parent directory does not exist; a
  `--bundle` directory that does not exist; and a bundle in a temporary
  directory, made of the Phase 0 bundle's `bundle.json` and
  `world.fixture` and `flow.fixture` but no `schedule.csv`. In the last
  three, no file is created at `P`.

The Phase 1 stage's Test Author adds one test,
`test_checkpoints_phase1_bundle_composes_every_phase1_system`: over
`tests/fixtures/harness/checkpoints-phase1` with `--content data` and
`--days 1`, the `systems` line is `systems sim.world sim.schedule
sim.airside sim.flow sim.turnaround sim.delay`, and the file is
byte-identical to a Phase 1 checkpoints kit's dump, built as above with
all six factories. The kit's content is `data/`, loaded through
`ContentLoaderFactory` as above. It builds no content definition in code
(Q-113).

## 19.9 Tests of `Promotion` (Q-084)

Binding on T-014's Test Author. Every test is in `tests/tools/simharness/`
(Q-041).

**The doubles.**

- A **spy flow** is the test's own `IFlowSystem`, by default with
  `Id = SystemId(4)`. It has a chosen kind for each node, which `KindOf`
  returns. `AgentsAt` returns an empty list. It records every `KindOf`,
  `SetPromoted` and `AgentsAt` call, in order, with its arguments and the
  number of its own `Tick` calls so far. `ComputeStateHash()` returns
  `0`. Its `ISimSystem` members work normally and its `Tick` changes
  nothing. Every other `IFlowSystem` member throws
  `InvalidOperationException`, so a harness that calls one fails the test.
- A **test world** is the test's own `IWorldSystem` with
  `Id = SystemId(1)` and a chosen `Nodes()`. It records each `Nodes()`
  call. `ComputeStateHash()` returns `0`, and its `ISimSystem` members work
  as the spy flow's do. Every other `IWorldSystem` member throws
  `InvalidOperationException`.
- Each composition below is built by a fresh `CountingComposer`, which
  constructs fresh doubles on every call, so that run 1's doubles and run
  2's doubles are told apart. Unless stated otherwise, the seed is `12345`
  and `ticks` is `1400`, so a run records 3 checkpoints, at 0, 600 and
  1200 (`08` §8.9).
  "**G**" is the composition of a test world with `Nodes()` `[3, 7]` and a
  spy flow with kinds `3 → Source` and `7 → Gate`.

The tests. The Test Author may add more.

- `test_harness_gates_promotion_promotes_lowest_gate_and_draws_it_every_tick`.
  `Promotion` of G. Run 1's doubles record no call. Run 2's doubles record
  exactly this, in this order: `Nodes()` once; `KindOf(3)`, `KindOf(7)`;
  `SetPromoted(NodeId(7), true)` with 0 ticks seen; and then 1400 calls of
  `AgentsAt(NodeId(7))`, the `k`-th with `k` ticks seen. They record
  nothing else. `Report` is `PASS determinism_promotion ticks=1400
  checkpoints=3 final=<h>`, where `<h>` is `FinalHash` of G with the same
  seed and ticks.
- `test_harness_gates_promotion_promotes_nothing_without_a_gate`. Each
  of these compositions passes `Promotion`, and its doubles record no
  `SetPromoted` and no `AgentsAt` in either run, and no call at all in run
  1:
  - (a) a spy flow alone, with no world. It records no call.
  - (b) a test world with `Nodes()` `[]` and a spy flow. Run 2 records one
    `Nodes()` and no `KindOf`.
  - (c) G, with `7 → Sink` instead of `7 → Gate`. Run 2 records one
    `Nodes()`, then `KindOf(3)` and `KindOf(7)`.
  - (d) G, with the spy flow at `SystemId(5)` instead of 4. Neither the
    test world nor the spy records any call in either run. In particular
    there is no `Nodes()`, because `flow` is missing (§19.2d step 1).
  - (e) G's test world alone, with no flow system. The test world records
    no call in either run.
- `test_harness_gates_promotion_promotes_same_node_on_every_call`. The
  composition is a test world with `Nodes()` `[2, 5, 9]` and a spy flow
  with kinds `2 → Source`, `5 → Gate` and `9 → Gate`. `Promotion` is called
  twice, each time with a fresh `CountingComposer` and the same seed and
  ticks. In each call, run 2's spy flow records `KindOf(2)` and `KindOf(5)`
  only, exactly one `SetPromoted`, which is `SetPromoted(NodeId(5), true)`,
  and 1400 `AgentsAt(NodeId(5))`. Run 1's records nothing. Both calls
  pass with equal `Report`s.
- `test_harness_gates_promotion_still_fails_on_divergence_with_a_gate`.
  Each call registers G's test world (position 1), G's spy flow
  (position 4) and `Probe(5, drifts: call == 2, driftFromTick: 1000)`.
  `Report` is exactly `FAIL determinism_promotion tick=1200 at=system:2`:
  `SystemHashes` index 2 is the probe, since the world and the flow
  hash `0`. Run 2's spy still records exactly one `SetPromoted(NodeId(7),
  true)` and 1400 `AgentsAt` calls, so the failure comes from the drift and
  not from a missing promotion.
- `test_harness_gates_promotion_phase0_kit_promotes_real_gate_and_passes`.
  This is the §19.6 kit composition, with its world and flow systems each
  wrapped in a forwarding spy at the same position. The wrapper forwards
  every member and records `KindOf`, `SetPromoted` and `AgentsAt`. Inside
  each `AgentsAt`, the flow wrapper also reads the wrapped flow's
  `Population` of the same node, which is a query. Seed `12345`, `ticks`
  `14400`. Run 2 records `KindOf` for `NodeId(1)` to `NodeId(8)` in
  ascending order, then exactly one `SetPromoted(NodeId(8), true)`, then
  14 400 `AgentsAt(NodeId(8))` calls. Each returned `Count` equals that
  `Population`, and the sum of the `Count`s is above 0. Run 1 records
  none of these calls. The gate passes, and its `final` equals `FinalHash`
  of the same kit, seed and ticks. Whether the test is Slow follows `07`
  L11a.

Every existing test in `HarnessGatesTests.cs` and `HarnessCliTests.cs` is
unchanged and still passes. Their compositions register no `IFlowSystem`
at position 4, or, for the CLI's `promotion`, an outcome-neutral one. The
recording builder is a fresh object per run, so
`test_harness_gates_compose_called_exactly_once_per_run_with_fresh_builder`
still holds.
