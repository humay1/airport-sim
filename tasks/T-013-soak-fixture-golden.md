# T-013 — Soak fixture and golden hash, mid-tier, under 0.1 ms/tick

| Field | Value |
|---|---|
| Status | QUEUED (spec gap closed by Q-057; releasable after T-009 merges and tests are authored) (historical, resolved: T-009 merged #74; now ordered after T-030, then one at a time with T-014) |
| Module | `tools.simharness` (invoked by `ci/run-checks.sh`) |
| Assigned role | worker |
| Depends on | T-009 (merged #74); harness writer order is T-030, then T-013 and T-014 one at a time, then T-045; see `queue.md` |
| Spec source | `spec/19-interfaces-harness.md` §19.2b and §19.3 (Q-057, PR #70, `b00adcb`); `spec/00-overview.md`; `spec/02-determinism.md` "Gates" (`soak_500_days`); `spec/03-module-map.md` "The soak fixture" (answers Q-003, D3) |
| Blocked by | — |

## Why this task exists

`02-determinism.md`'s `soak_500_days` gate names no fixture size. D3 fixed
that: the nightly soak runs a **mid-tier** fixture kept under 0.1 ms/tick
mean, not the max-tier fixture the per-module budget tests use — at max-tier
cost 500 days is roughly 12 hours, not "minutes". This task authors that
fixture and its golden hash, once T-009 (merged #74) has landed the harness's own
100-day/goldens-allowed baseline (`spec/08-interfaces-core.md` §8.1/§8.2,
D2: golden hashes may now be authored).

## Writable paths

```
tools/SimHarness/**
```

**Correction (Q-021):** `tests/**` (including `tests/fixtures/**`) is the
Test Author's territory exclusively; the path guard already blocks a worker
grant there. The earlier grants of `tests/fixtures/soak/**` and
`tests/sim/core/**` are dropped.

This task does not write under `tests/`. Its tests are in `tests/tools/simharness/**` (see "Tests to pass").

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`

## Interface to implement

No new `HarnessGates` member (`19` §19.1: `soak` is reached only through
`HarnessCli.Run`). Adds the `soak` subcommand to `HarnessCli.Run`, alongside
the existing `determinism`/`saveload`/`promotion`/`budget` ones, exactly as
`19` §19.2b and §19.3 (Q-057) say. Binding, from those sections:

- **Grammar.** `soak --days D --golden P` and `soak --days D --out P`, flags
  in any order, each at most once. There is **no `--seed`**: the seed is
  always 12345. `--golden` and `--out` together, or neither, is a usage error
  (exit 2), as are `D = 0`, `D x TICKS_PER_SIM_DAY` above `uint32`, an empty
  `P`, and a `P` that is rooted but not fully qualified.
- **Run.** One run (§19.1) of `D x TICKS_PER_SIM_DAY` ticks, seed 12345,
  the command script of `19` §19.2, and the §19.2a composer over the soak
  fixture set. No second run. `soak` times nothing and reports no timing.
- **Dump.** The checkpoint dump, version 1, of `16` §16.8, byte for byte
  (`seed 12345`, the `systems` line, one line per checkpoint).
- **`--golden P`.** Read all of `P` once, before the run (unreadable: exit 3).
  Pass (exit 0) iff the dump and `P` are byte-identical; otherwise
  `FAIL soak line=<L>` and exit 1.
- **`--out P`.** If `P` exists or its parent directory does not: exit 3,
  before the run. Never overwrites. After the run, create `P` and write the
  dump (write failure: exit 3), exit 0.
- **Paths.** `P` fully qualified is used as given. Otherwise it is a
  `/`-separated path relative to the repository root (the `.sln` root found
  as in §19.2a). The current working directory is never used.
- **Exit codes.** 0 passed or `--out` written; 1 gate failed; 2 usage error
  (stdout empty); 3 harness error (stdout empty). Otherwise stdout is exactly
  one line (`19` §19.3).
- **Fixtures** are loaded exactly as §19.2a loads the Phase 0 set, from
  `tests/fixtures/soak/`: `soak-content.files` plus `soak-content/`,
  `soak-landside.json`, `soak-landside.flow.json`, `soak.csv`. The Test
  Author writes them; this task reads them and writes none.

Binding, from `spec/03-module-map.md` "The soak fixture" (D3):

- Every built system registered (whatever exists in the build at the time
  this task runs — at minimum `sim.world`, `sim.schedule`, `sim.flow`, and
  every Phase 1 module merged so far).
- A `repeat_daily` schedule, sized so the whole-sim mean stays under
  **0.1 ms/tick** — about 12 minutes of wall clock for the full 7.2M-tick,
  500-sim-day run at that rate.
- If a module's cost grows later and the fixture no longer fits under
  0.1 ms/tick, **the fixture is shrunk, not the gate weakened**
  (`02-determinism.md` forbids disabling a gate). That is a future task's
  problem, not this one's, but this task's fixture must be sized with
  headroom rather than exactly at the ceiling.
- Max-tier performance stays with each module's own budget test
  (`03-module-map.md` "How a budget is measured"); this fixture proves
  determinism over volume, not peak throughput.

## Events

Emitted: none new. Consumed: none new.

## Tests to pass

```
tests/tools/simharness/**
tests/fixtures/soak/**
tests/golden/**
```

Synced 2026-10-01 to `19` §19.6 (Q-041): `07` L3 lets `tests/sim/core` reference only `src/sim/core`, so a test that drives the harness CLI over the world, schedule and flow factories lives in `tests/tools/simharness/`. The fixture and golden locations are `03` "The soak fixture" (`tests/fixtures/soak/**`, `tests/golden/`). The earlier `tests/sim/core/**` grant is dropped.

Written by the Test Author (`19` §19.7), including every file under
`tests/fixtures/soak/**`. The tests drive `soak --days D --golden P` and
`soak --days D --out P` through `HarnessCli.Run` and check the exit codes,
the single stdout line and the path rules of `19` §19.2b and §19.3. The
sizing test (§19.7) checks the 0.1 ms bound; `soak` itself times nothing.
**Do not edit them.**

**The golden `tests/golden/soak-500.hashes` is not a test input of this
task.** It is committed only after the implementation has merged, as the
`--out` dump of `soak --days 500`, and only with the owner's confirmation
(`tests/golden/README.md`: no agent regenerates a golden). Until it is
committed, the nightly `soak` exits 3, which `19` §19.2b says is correct.
No agent writes it in this task.

## Performance budget

The gate's own ceiling: whole-sim mean **< 0.1 ms/tick** over the fixture
(`spec/03-module-map.md`). This is a nightly gate, not a per-merge one; it
must still not be disabled if it starts failing — shrink the fixture instead
and escalate if that is not enough.

## Done when

- [ ] All assigned tests pass
- [ ] `tests/golden/soak-500.hashes` not written by this task (committed afterwards, owner-confirmed)
- [ ] `ci/run-checks.sh` green (soak run may be nightly-only per existing CI
      wiring; confirm against `ci/run-checks.sh`'s own scheduling, which this
      task does not change)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

**Spec gap closed (Q-057, spec PR #70, `b00adcb`).** `19` §19.2b and §19.3
now specify `soak` (grammar, exit codes, composition, golden handling). The
hold on this task is lifted: it is releasable once T-009 has merged (done, #74; the harness writer order in `queue.md` still applies) and the
Test Author's tests and `tests/fixtures/soak/**` are authored. The nightly
workflow's existing `soak --days 500 --golden tests/golden/soak-500.hashes`
needs no change.

The golden hash is authored only after this task merges (see "Tests to
pass"), and not before `SIM_SECONDS_PER_TICK` and
every registered module's own goldens are stable — D2 confirms
`SIM_SECONDS_PER_TICK = 6` and lifts the earlier "do not author goldens"
restriction that older Phase 0 task files (T-001, T-009) carried; those
notes are now stale and are refreshed by this same amendment cycle. If a
module merges after this task and changes its own hash, this fixture's
golden is expected to need a refresh — that is a normal consequence of
`02-determinism.md`'s contract, not a sign this task did something wrong.
