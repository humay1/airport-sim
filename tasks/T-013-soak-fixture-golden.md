# T-013 — Soak fixture and golden hash, mid-tier, under 0.1 ms/tick

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `tools.simharness` (invoked by `ci/run-checks.sh`) |
| Assigned role | worker |
| Depends on | T-009 |
| Spec source | `spec/00-overview.md`; `spec/02-determinism.md` "Gates" (`soak_500_days`); `spec/03-module-map.md` "The soak fixture" (answers Q-003, D3) |
| Blocked by | — |

## Why this task exists

`02-determinism.md`'s `soak_500_days` gate names no fixture size. D3 fixed
that: the nightly soak runs a **mid-tier** fixture kept under 0.1 ms/tick
mean, not the max-tier fixture the per-module budget tests use — at max-tier
cost 500 days is roughly 12 hours, not "minutes". This task authors that
fixture and its golden hash, once T-009 has landed the harness's own
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

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`

## Interface to implement

No new interface. Drives the existing `ISimHost`/harness surface (T-001,
T-004, T-006, T-009) against a new fixture and a new `soak` subcommand
alongside the existing `determinism`/`saveload`/`promotion`/`budget` ones.

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
tests/sim/core/**
```

Written by the Test Author. Expect a `soak --days 500 --seed <n>` harness
subcommand invocation, asserting: (a) mean tick cost under 0.1 ms on the
reference machine, (b) a golden checkpoint hash that a second run
reproduces exactly (`determinism_same_process`, at this fixture's scale),
and (c) completion well inside CI's nightly window. **Do not edit them.**

## Performance budget

The gate's own ceiling: whole-sim mean **< 0.1 ms/tick** over the fixture
(`spec/03-module-map.md`). This is a nightly gate, not a per-merge one; it
must still not be disabled if it starts failing — shrink the fixture instead
and escalate if that is not enough.

## Done when

- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green (soak run may be nightly-only per existing CI
      wiring; confirm against `ci/run-checks.sh`'s own scheduling, which this
      task does not change)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

Do not author this fixture's golden hash before `SIM_SECONDS_PER_TICK` and
every registered module's own goldens are stable — D2 confirms
`SIM_SECONDS_PER_TICK = 6` and lifts the earlier "do not author goldens"
restriction that older Phase 0 task files (T-001, T-009) carried; those
notes are now stale and are refreshed by this same amendment cycle. If a
module merges after this task and changes its own hash, this fixture's
golden is expected to need a refresh — that is a normal consequence of
`02-determinism.md`'s contract, not a sign this task did something wrong.
