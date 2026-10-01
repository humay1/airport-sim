# T-009 — Run 100 sim-days in under 60s, identical across runs

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` (harness/integration, `tools/SimHarness`) |
| Assigned role | worker |
| Depends on | T-006, T-007, T-008, T-012 |
| Spec source | `spec/00-overview.md` Phase 0 kill gate; `spec/02-determinism.md` "Gates"; `spec/11-interfaces-schedule.md` §11.10 (fixture); `spec/19-interfaces-harness.md` §19.2a, §19.6 (PR #66) |
| Blocked by | — |

## Writable paths

```
tools/SimHarness/**
```

Test Author paths (the worker writes none of these):

```
tests/tools/simharness/**
tests/fixtures/harness/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory exclusively
(`07-conventions.md` "Solution layout and build"); the path guard already
blocks a worker from writing there, so a worker grant there is a no-op. This
task's earlier grant of `tests/sim/core/**` is dropped.

Implements the Phase 0 CLI composition of `19` §19.2a, which is "binding on
T-009 and on every later harness task until an amendment changes it": it
"composes `sim.world`, `sim.schedule` and `sim.flow` over the Phase 0
fixtures, plus a harness-internal **boarding stand-in** in `sim.airside`'s
empty registry slot." The composer "registers in registry order (`08`
§8.5): `world` (1), `schedule` (2), the stand-in (3), `flow` (4), and
nothing else. So every checkpoint's `SystemHashes` has exactly four
entries, in that order."

The harness reads four fixtures, all written by the Test Author, "and never
reads `data/`": the content manifest
`tests/fixtures/harness/phase0-content.files` with files under
`tests/fixtures/harness/phase0-content/`, `tests/fixtures/world/phase0-landside.json`,
`tests/fixtures/flow/phase0-landside.flow.json` and
`tests/fixtures/schedule/phase0-200.csv`. Fixture location, load timing and
the exit-3 failure rules are `19` §19.2a "Locating them" and "When".

**The boarding stand-in (Q-043; owner accepted 2026-09-29).** "A
harness-internal `ISimSystem` with `Id = SystemId(3)`." It calls
`flow.Absorb(PHASE0_DEPARTURE_SINK, flight)` for every departure at its
`ScheduledTick` (STD), with `PHASE0_DEPARTURE_SINK = NodeId(9)`, and
`ComputeStateHash()` returns `0`. Follow `19` §19.2a for its `Tick` steps,
events and state. This task does not touch `sim.airside` (T-021).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/02-determinism.md`,
`spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`spec/09-interfaces-flow.md`, `spec/11-interfaces-schedule.md`

## Interface to implement

No new interface. Drives the existing `ISimHost.Step`, registering
`IWorldSystem` (registry position 1, `18` §18.4), `IScheduleSystem`
(registry position 2, §11.7) and `IFlowSystem` (registry position 4,
`08-interfaces-core.md` §8.5) together for the first time. Compose them
through `08` §8.11a's `ISimHostBuilder`/`SystemServices` and each module's
`<Module>Factory` (`WorldFactory`, `ScheduleFactory`, `FlowFactory`,
Q-009) — never by reaching into a module's internals.

## Events

Emitted: none new
Consumed: none new — this task is the first place `FlightPlanPublished` /
`FlightMilestoneReached{PlanPublished}` actually reach a registered
`IFlowSystem.Inject` call end-to-end.

## Tests to pass

```
tests/tools/simharness/**
tests/fixtures/harness/**
```

Written by the Test Author (`19` §19.6 Q-041: "Every T-009 test, the
kill-gate tests included, is in `tests/tools/simharness/`"). The Test Author
also writes the new fixtures under `tests/fixtures/harness/`. **Do not edit
them.** Expect:

- the equivalence test `test_harness_cli_hash_only_matches_final_hash_gate`
  (`determinism --days 1 --seed 99 --hash-only` against
  `HarnessGates.FinalHash` of the kit content and kit composer);
- T-006's `HarnessCliTests.cs` expectations: "Every expected hash that they
  compute with `EmptyCompositionFinalHash(n)` becomes
  `HarnessGates.FinalHash(kit content, kit composer, seed, n)`", and
  `test_harness_cli_budget_core_only_day_passes` is renamed
  `test_harness_cli_budget_phase0_day_passes`;
- (a) "A whole-run wall-clock gate. One run of 1 440 000 ticks with seed
  12345, through `HarnessGates.FinalHash` with the kit, takes under 60 s
  **in total**, composition included", measured as one `Stopwatch` difference
  in `long` arithmetic. It is not a `03` per-tick statistic;
- (b) `HarnessGates.SameProcess` with the kit, same seed and ticks, passes,
  with `checkpoints=2400`;
- the load checks of `19` §19.6 after the gate returns.

(a) and (b) are Slow by `07` L11a rule (a): this task's PR needs the manual
pre-merge `slow-tests` run green on its head.

## Performance budget

Wall-clock ceiling: 60s for 100 sim-days on the reference machine
(`spec/00-overview.md`), measured over the whole run per `19` §19.6(a). This
is the Phase 0 kill-gate measurement, distinct from but consistent with the per-tick budgets already asserted by T-007's and
T-008's own module tests.

## Done when

- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green (not `--fast`)
- [ ] Budget met, or the human owner has been escalated to (this task sits
      directly under the T-011 kill gate in the build order)
- [ ] No writes outside writable paths
- [ ] Pre-merge `slow-tests` run green on the PR head
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

Per `spec/11-interfaces-schedule.md` §11.6 "Running without `sim.flow`",
`sim.schedule`'s own tests (T-008) must already pass without `sim.flow`
registered. This task is the first to exercise both together — if the
combined hash disagrees with the sum of each module's own behaviour, the
bug is in the `Inject` call site, not in either module's isolated tests.

`sim.world` (T-012) has no runtime state and no RNG (`18` §18.4); its
presence in the registered set changes the world hash only through its own
fixture hash, not through any tick behaviour. Its dependency direction is
one-way: `sim.flow` depends on it, it depends on nothing.
