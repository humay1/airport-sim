# T-048 — `tools.simharness`: `checkpoints` composes the Phase 1 systems

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-021, T-022, T-024 and T-030 merge, and its tests are authored) |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-021, T-022, T-024, T-030 |
| Spec source | `spec/19-interfaces-harness.md` §19.2c "Which systems it composes" (the Phase 1 stage) and "The fixture", §19.8 (last paragraph); `spec/16-interfaces-host.md` §16.3, §16.4, §16.8; `spec/07-conventions.md` L8 |
| Blocked by | — |

## Why this task exists

`19` §19.2c stages the `checkpoints` subcommand. T-030 composes `sim.world`,
`sim.schedule` and `sim.flow` and rejects a bundle listing `sim.airside`,
`sim.turnaround` or `sim.delay` with exit 3. This task removes that rejection
so that all six Phase 1 systems are composed, together with their
`ProjectReference`s (`07` L8: later `tools/SimHarness/**` tasks add
`ProjectReference`s only). It must merge before T-031, whose D7 test
(`test_host_composition_matches_harness_checkpoints`, `16` §16.8) runs on the
Phase 1 checkpoints bundle. It changes neither the §19.2a composer, the soak
nor the boarding stand-in (§19.2c, "Neither stage changes ...").

## Writable paths

```
tools/SimHarness/**
```

**Test Author's grant:** `tests/tools/simharness/**` and
`tests/fixtures/harness/checkpoints-phase1/**`. The fixture is
`bundle.json` (all six Phase 1 systems, `schema_version` 1, seed `"12345"`)
plus `world.fixture`, `schedule.csv`, `airside.fixture`,
`airside_rules.json`, `turnaround.fixture` and `flow.fixture`, each a byte
copy of the source that `16` §16.3's playtest-bundle table gives for that
name. It holds no `render_layout.fixture`. Its content is `--content data`.
The worker writes only `tools/SimHarness/**`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/02-determinism.md`,
`spec/07-conventions.md` L8, `spec/08-interfaces-core.md` §8.5, §8.11,
§8.11a, `spec/16-interfaces-host.md` §16.3, §16.4, §16.8,
`spec/19-interfaces-harness.md` §19.2c, §19.3, §19.8, the Construction
section of `12` §12.12a, `13` §13.10a and the delay module's (`14` §14.13a),
`spec/04-data-schemas.md` (`airside_rules.json`)

## Interface to implement

No new command-line surface: the grammar, exit codes and failure order are
T-030's (`19` §19.2c, §19.3). This task extends the composition:

- `sim.airside` (`airside.fixture` through `IAirsideLayoutLoader.Parse`;
  `airside_rules.json` parsed by the harness into `AirsideRules` itself, both
  keys, since no sim module parses it), `sim.turnaround`
  (`turnaround.fixture`) and `sim.delay` (no file) are constructed in `16`
  §16.4's dependency order (world; flow(world); schedule(flow);
  airside(schedule, flow, turnaroundRegistered); turnaround(schedule); delay),
  each with `builder.Services`, its data and its downward interfaces or
  `null`, and registered in registry order (`08` §8.5), with `sourceName`
  exactly the bundle file name (Q-073).
- `turnaroundRegistered` is whether `sim.turnaround` is listed.
- A listed system whose downward interface is not listed stays exit 3 (T-030's
  failure order).
- The `ProjectReference`s for the airside, turnaround and delay production
  projects are added to the harness `.csproj`.

Still no boarding stand-in, no probe and no command (Q-070, Q-071). The
harness's dump of a bundle must stay byte-identical to `IHeadlessRun`'s dump
of the same bundle and content (`16` §16.8).

## Events

Emitted: none new. Consumed: none new.

## Tests to pass

```
tests/tools/simharness/**
```

Written by the Test Author:

- `test_checkpoints_phase1_bundle_composes_every_phase1_system` (`19`
  §19.8): over `tests/fixtures/harness/checkpoints-phase1` with `--content
  data` and `--days 1`, the `systems` line is `systems sim.world
  sim.schedule sim.airside sim.flow sim.turnaround sim.delay`, and the file
  is byte-identical to a Phase 1 checkpoints kit's dump, built as in
  `19` §19.8 with all six factories.

T-030's five tests stay unchanged and must keep passing. **Do not edit
them.** If a test contradicts the spec, file an open question and stop.

## Performance budget

Not a per-tick sim module; not counted in the 6 ms budget.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass, including T-030's five
- [ ] `ci/run-checks.sh` green
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This task writes `tools/SimHarness/**` and `tests/tools/simharness/**`, so it
is one of the harness writers (see `tasks/queue.md` "Harness writers"): never
release it beside T-013, T-014 or T-045. The harness must not reference
`app.host` or any `AirportSim.App.*` assembly (§19.8's static check); D7
compares two independent compositions.
