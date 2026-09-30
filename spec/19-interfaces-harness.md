# 19 — Public interfaces: `tools.simharness` (the CI gates)

Pins the harness subcommands that `ci/run-checks.sh` invokes, their exit
codes and output, and the public surface that the harness's own test project
compiles against. It answers `open-questions.md` Q-025, Q-026 and Q-027,
and, for T-009's Phase 0 composition, Q-041 to Q-043.
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
  compares for real and passes vacuously. A **harness task**, not T-010,
  amends the second run to promote, through `IFlowSystem.SetPromoted` (`09`
  §9.7), the nodes the `02` gate calls "a gate". T-010 writes `sim.flow`
  only. The harness task depends on T-010 and on the CLI composition
  including `sim.flow`, and the Planner creates it (Q-033). Until then, a
  harness that stubs the comparison is wrong.
- **The CLI composition.** Every CLI run, of every subcommand in §19.3,
  uses the Phase 0 composition of §19.2a (Q-042, from T-009). It replaces
  T-006's empty composition, which no CLI form selects any more. The empty
  composition stays reachable only through `HarnessGates` with a composer
  that registers nothing, as the gate tests already use it. No option,
  flag or environment variable selects a composition.
- **Untested by design (Q-029).** `at=world` and `at=count` cannot be
  reached. Runs of one gate step the same ticks at the same checkpoint
  cadence, so their counts match. A world hash is a function of the tick,
  `CoreHash` and `SystemHashes`, so it cannot differ while those agree. Both
  stay in the grammar as defensive reports. Exit codes 1 and 3 cannot be
  reached by a test through `HarnessCli.Run` either (Q-042). Code 1 needs a
  nondeterministic composition, and the Phase 0 composition is
  deterministic. Code 3 needs a missing or invalid repository fixture, which
  an in-process test cannot arrange without writing repository files. The
  divergence seam (§19.1) proves failure through `HarnessGates` instead, and
  no CLI seam is added. T-006's earlier statement that the first composing
  task makes them reachable is withdrawn.

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
- **The content manifest.** `phase0-content.files` is UTF-8 without a BOM.
  Each line is one path relative to `phase0-content/`, `/`-separated, and
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
  the kill-gate test kit (§19.6).

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
`Id = SystemId(3)`. `Name` is a free diagnostic label. It stands in for
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
  `tests/fixtures/flow/phase0-landside.flow.json`. It is a harness-internal
  constant. If that fixture's `Sink` changes, this line is amended in the
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

**`budget --tier max`** times the first sim-day of this composition
(§19.4).

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
| 3 | harness error: any exception during the run, `SimInvariantException` included, or a §19.2a fixture failure before the first run |

For codes 2 and 3, stdout is empty, and stderr carries one human-readable
message. For 3 that is the exception's `ToString()`, or, for a pre-run
fixture failure that is not an exception, a message naming the file and
the failure. No other exit code is
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
