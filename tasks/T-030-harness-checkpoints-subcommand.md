# T-030 — `tools.simharness`: `checkpoints` subcommand (byte-exact dump)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-004 (merged #20), T-006 (merged #31), T-009 (merged #74); all merged |
| Spec source | `spec/19-interfaces-harness.md` §19.2c and §19.8 (Q-066 to Q-076); `spec/16-interfaces-host.md` §16.3, §16.4, §16.8 (D7) |
| Blocked by | — |

## Why this task exists

**Dependency correction (systematic type-dependency recheck):** this task's
own text says it "composes a fixture the same way T-009's harness already
does" — that reuses T-009's harness composition of `sim.world`,
`sim.schedule` and `sim.flow` through the harness's own `ISimHostBuilder`
wiring, not just T-004's/T-006's surfaces. `Depends on` is amended to
`T-004, T-006, T-009`.

`app.host`'s D7 equivalence test
(`test_host_composition_matches_harness_checkpoints`, T-031) compares the
harness's checkpoint dump against `app.host`'s `IHeadlessRun`. Both sides
must emit the identical byte format. This task is the harness side, and it
is also one of the reusable, buildable pieces of the proposed
`determinism_cross_runtime` gate (`16` §16.9, adoption pending, T-035).

## Writable paths

```
tools/SimHarness/**
```

**Correction (Q-021):** `tests/**` is the Test Author's territory exclusively; the path guard already blocks a worker grant there. Dropped.

**Test Author's grant (Q-076, `19` §19.2c "The fixture"):**
`tests/tools/simharness/**` and `tests/fixtures/harness/checkpoints-phase0/**`.
The fixture is exactly four files: `bundle.json` (`schema_version` 1, `seed`
`"12345"`, `systems` `[ "sim.world", "sim.schedule", "sim.flow" ]`),
`world.fixture`, `flow.fixture` and `schedule.csv`, byte copies of the Phase 0
set's walk graph, flow graph and schedule (`19` §19.2a). Its content is
`--content tests/fixtures/harness/phase0-content`. The worker still writes
only `tools/SimHarness/**`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/02-determinism.md`,
`spec/08-interfaces-core.md` §8.5, §8.9, §8.11, §8.11a, `spec/16-interfaces-host.md`
§16.3, §16.4, §16.8, `spec/19-interfaces-harness.md` §19.1, §19.2c, §19.3,
§19.8

## Interface to implement

New subcommand (`19` §19.2c, §19.3):
`checkpoints --bundle B --content C --days D --out P`. All four flags are
required, each at most once, in any order; `D` is a decimal integer from 1
to 298261 (`--days 0` and `--days 298262` are usage errors); `--seed` is not
accepted. `B`, `C` and `P` are paths by `19` §19.2b "Paths" (not empty; fully
qualified used as given; otherwise repository-relative `/`-separated, root
found as in §19.2a, and only when at least one of the three needs it; rooted
but not fully qualified, such as `C:a`, is a usage error). `P` is never
overwritten and the harness writes nothing else.

**Exit codes.** 0 success: stdout is exactly `WROTE checkpoints ticks=<n>
checkpoints=<k> final=<hex16>`. 2 usage error, decided before any file is
touched, stdout empty, no file created. 3 any failure in steps 2 to 4 below,
stdout empty, stderr one message naming the file and the failure (or the
exception's `ToString()`). It never exits 1: it is not a gate and compares
nothing.

**Failure order (Q-067).** (1) usage; (2) before the run: find the root if
needed, `P` exists or its parent directory does not (exit 3), read and check
`bundle.json` (`16` §16.3 strict form, Q-069), check every listed system is
composable at this stage and has its downward interfaces listed (`16`
§16.4), read each listed system's files by exact name (never listing `B`;
`16` §16.3 table: world.fixture, schedule.csv, flow.fixture), load the
content; (3) composition and the run, where loader failures surface; (4)
create `P` as a new file and write the dump. `P` exists only after step 4, so
an exit 3 from steps 2 or 3 leaves no file.

**Composition.** `16` §16.4 steps 1 to 4 in the harness's own code: its own
`IContentSource` listing every file under `C` (relative, `/`-separated, any
order), `ContentLoaderFactory.Create().Load` then `ContentIndexFactory.Create`
(Q-072); `SimHostFactory.CreateBuilder` with the bundle's seed, a
harness-internal recording sink and a discarding log; loaders called with
`sourceName` exactly the bundle file name (Q-073); construction in
dependency order, registration in registry order, `Build`. No boarding
stand-in, no probe, no command (Q-070, Q-071). It never uses the §19.2a
composer.

**This task's stage composes only `sim.world`, `sim.schedule` and
`sim.flow`** (`19` §19.2c "Which systems it composes"), the factories the
harness already references (T-009). A bundle listing `sim.airside`,
`sim.turnaround` or `sim.delay` is exit 3 with stderr naming the system.
No test asserts that rejection, because the Phase 1 stage (T-048) removes
it. This task adds no `ProjectReference`.

**The run (Q-074).** `Step(TICKS_PER_SIM_DAY)` exactly `D` times; `final` is
`WorldStateHash()` after the last step. No CLI flag selects a batch size and
none may be added.

**Checkpoint dump, version 1** (`16` §16.8, binding, copied not
paraphrased). UTF-8 without a BOM, LF line endings, a final newline, single
spaces, invariant formatting throughout:

```
airport-sim-checkpoints 1
seed <MasterSeed, decimal>
systems <Name> <Name> ...                  // registered systems, registry order
<tick> <world hash> <system hash> ...      // one line per checkpoint, ascending tick
```

Ticks are decimal. Hashes are exactly 16 lowercase hexadecimal digits.
System hashes follow the `systems` line's order. The step batch size must
not change the result (`02-determinism.md` rule 1 — the tick is fixed); a
test asserts this directly.

The existing subcommands `ci/run-checks.sh` already invokes
(`determinism`, `saveload`, `promotion`, `budget`, and T-013's `soak`) are
unchanged by this task.

## Events

Emitted: none new. Consumed: none new.

## Tests to pass

```
tests/tools/simharness/**
```

Synced 2026-10-01 to `19` §19.6 (Q-041): `07` L3 keeps `tests/sim/core` to `src/sim/core` references, and the harness CLI needs the factories, so these tests live in `tests/tools/simharness/`. The earlier `tests/sim/core/**` grant is dropped.

Written by the Test Author. The five tests of `19` §19.8, each calling
`HarnessCli.Run` in process, with `--out` in a fresh temporary directory
that the test deletes, against `tests/fixtures/harness/checkpoints-phase0`
and `--content tests/fixtures/harness/phase0-content`:

- `test_checkpoint_dump_format_is_byte_exact`
- `test_checkpoints_result_independent_of_step_batch_size` (Q-074)
- `test_checkpoints_subcommand_composes_through_published_factories_only`
  (Q-075; behavioural and static parts)
- `test_checkpoints_rejects_usage_errors` (Q-066; control run first)
- `test_checkpoints_harness_failures_exit_3` (Q-067)

The Phase 1 test, `test_checkpoints_phase1_bundle_composes_every_phase1_system`,
belongs to T-048, not here.

**Do not edit them.** If a test contradicts `spec/16-interfaces-host.md`
§16.8, file an open question and stop.

## Performance budget

Not a per-tick sim module; not counted in the 6 ms budget. A 10-sim-day run
(the size `determinism_cross_process`/`determinism_cross_runtime` use)
should complete in low single-digit minutes, the same sanity bound T-006
already carries.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

The dump format is byte-exact **on purpose** — `app.host`'s equivalence
test and the proposed cross-runtime gate both diff two dumps directly, with
no tolerance for whitespace or formatting differences. Do not "improve"
the format; if it needs a field added later, that is a version bump
(`airport-sim-checkpoints 2`) by spec amendment, not a local choice.

This task does not build `IHeadlessRun`, `IHostCommandLine` or the Unity
bootstrap's batch mode — those are `app.host`'s (T-031), which reuses this
task's dump format but is a separate implementation, per `16` §16.4: "It
may wire them in its own code... but it must not construct any system
another way."
