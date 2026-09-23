# T-006 — Determinism gates in CI (same/cross process)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `tools.simharness` (invoked by `ci/run-checks.sh`, which already exists) |
| Assigned role | worker |
| Depends on | T-004 |
| Spec source | `spec/02-determinism.md` "Gates" table; `ci/gates.md`; `ci/run-checks.sh` |
| Blocked by | — |

## Writable paths

```
tools/SimHarness/**, tests/sim/core/**
```

**Note, not a guess:** `ci/gates.md`'s path-guard table reserves `ci/**` for
the human owner only, and `ci/run-checks.sh` already exists and already
invokes `dotnet run --project tools/SimHarness -- determinism ...`,
`... saveload ...`, `... promotion ...` and `... budget ...`. This task's job
is to make `tools/SimHarness` implement those subcommands against the
`ISimHost`/`IStateHasher` surfaces from T-001/T-004, not to touch `ci/**`
itself. If the harness needs a CLI surface not already implied by
`run-checks.sh`'s existing invocations, that is a question for the Architect,
not an invented flag.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/08-interfaces-core.md`,
`ci/gates.md`, `ci/run-checks.sh`

## Interface to implement

No new sim interface. Consumes, unchanged:

```
interface ISimHost {
  Tick   CurrentTick { get }
  void   Step(uint32 ticks)
  uint64 WorldStateHash()
  bool   TrySubmit(in Command cmd, out CommandRejection reason)
}
interface ICheckpointSink { void Record(in Checkpoint cp) }
```

The four subcommands `run-checks.sh` already calls, and their pass
conditions, straight from `spec/02-determinism.md` "Gates":

| Subcommand | Gate | Pass condition |
|---|---|---|
| `determinism --days 10 --seed 12345` | `determinism_same_process` | run twice in one process, identical checkpoint hash each tick |
| `determinism --days 10 --seed 12345 --hash-only` (run twice, diffed by the script) | `determinism_cross_process` | identical final hash across two OS processes |
| `saveload --ticks 1000 --save-at 500` | `determinism_save_load` | identical to an uninterrupted 1000-tick run — save/load itself does not exist before `sim.save`, so this task's fixture snapshots the in-memory command log plus RNG stream state only, per T-005/T-002, and reloads from that; a real file-backed save format is out of scope until `sim.save` is built |
| `promotion --days 1` | `determinism_promotion` | Phase 0 has no promotable system yet (`sim.flow` promotion lands at T-010); this subcommand is a no-op pass-through returning success until T-010 exists — do not fabricate a promotion path here |

**Goldens.** `SIM_SECONDS_PER_TICK = 6` is now a confirmed HUMAN DECISION
(D2, `08` §8.1/§8.2) — golden checkpoint hashes may be authored from this
task onward. Compose the harness's own test systems through `08` §8.11a's
`ISimHostBuilder`/`SystemServices` and each module's `<Module>Factory`
(Q-009) — never by reaching into a module's internals. The separate
`checkpoints` subcommand of `16-interfaces-host.md` §16.8 (byte-exact dump,
used by `app.host`'s harness-equivalence test) is **not** this task's job;
it is T-030, once T-004 and this task exist for it to extend.

## Events

Emitted: none
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect the harness's subcommands to be exercised
via `dotnet test`-driven fixtures, not only shell invocation, so failures
localise. **Do not edit them.**

## Performance budget

Not a per-tick sim module; not counted in the 6 ms budget. `run-checks.sh`
running these gates against a 10-sim-day fixture should complete in low
single-digit minutes, per the reasoning in `spec/01-architecture.md` "Why the
sim is a plain .NET library" point 2 — this is a sanity bound, not a hard
budget test.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green end-to-end (not `--fast`)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

The `promotion` and `saveload` subcommands are necessarily partial until
`sim.flow` (T-010) and `sim.save` exist. Document the partial scope in the
PR description rather than silently expanding this task's writable paths
into `sim.save`, which does not exist yet and is out of Phase 0/1 scope per
the build order.
