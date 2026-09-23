# T-030 — `tools.simharness`: `checkpoints` subcommand (byte-exact dump)

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-004, T-006 |
| Spec source | `spec/16-interfaces-host.md` §16.8 "The harness side" (D7) |
| Blocked by | — |

## Why this task exists

`app.host`'s D7 equivalence test
(`test_host_composition_matches_harness_checkpoints`, T-031) compares the
harness's checkpoint dump against `app.host`'s `IHeadlessRun`. Both sides
must emit the identical byte format. This task is the harness side, and it
is also one of the reusable, buildable pieces of the proposed
`determinism_cross_runtime` gate (`16` §16.9, adoption pending, T-035).

## Writable paths

```
tools/SimHarness/**, tests/sim/core/**
```

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/02-determinism.md`,
`spec/08-interfaces-core.md` §8.5, §8.9, §8.11a, `spec/16-interfaces-host.md`
§16.8

## Interface to implement

New subcommand: `checkpoints --bundle <dir> --days <n> --out <path>`.
Composes a fixture the same way T-009's harness already does (through
`08` §8.11a's `ISimHostBuilder`/`SystemServices` and each registered
module's `<Module>Factory` — never by reaching into module internals),
steps it for `n` sim-days with no presentation at all, and writes the dump.

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
tests/sim/core/**
```

Written by the Test Author. Expect at least:

- `test_checkpoint_dump_format_is_byte_exact`
- `test_checkpoints_result_independent_of_step_batch_size`
- `test_checkpoints_subcommand_composes_through_published_factories_only`

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
