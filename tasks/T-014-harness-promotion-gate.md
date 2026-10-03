# T-014 — Harness: make `determinism_promotion` promote a real node

| Field | Value |
|---|---|
| Status | MERGED (PR #105 with tests #102, 2026-10-03; `19` §19.2d and §19.9, spec PR #95) |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-009 (merged #74), T-010 (merged #56), T-030 (merged #90), T-013 (merged #94), T-049 (merged #99; adds `IFlowSystem.KindOf`) |
| Spec source | `spec/19-interfaces-harness.md` §19.2 ("`Promotion` before `sim.flow` promotion (T-010)"), §19.2d (what the second run promotes, Q-084), §19.9 (the five tests); `spec/09-interfaces-flow.md` §9.1, §9.7 (`SetPromoted`/`AgentsAt`/`KindOf`, Q-033, Q-084), "Promotion rules"; `spec/02-determinism.md` "Gates" (`determinism_promotion`: "same day headless vs with camera parked on a gate"); spec PR #95 (`8f66470`) |
| Blocked by | — |

**Why this task exists (Architect, Q-033 item e, PR #40):** `19` §19.2
names this exact amendment point: "`Promotion` before `sim.flow` promotion
(T-010). Without a promotable system, the second run differs from the first
in nothing... T-010 amends the second run to promote, through
`IFlowSystem.SetPromoted`, the nodes the `02` gate calls 'a gate'." T-006
built `HarnessGates.Promotion` to compare for real but with nothing to
promote (vacuous pass). This task gives it something to promote, once both
`sim.flow` is in the CLI composition (T-009) and promotion is real,
outcome-neutral behaviour (T-010) — until now, no task both existed and was
positioned to make this change.

## Writable paths

```
tools/SimHarness/**
```

**Correction (Q-021, carried forward):** `tests/tools/simharness/**` is the
Test Author's territory exclusively (`07-conventions.md` "Solution layout
and build"); the path guard already blocks a worker grant there, so none is
given. This task extends the existing `tools/SimHarness` project; it does
not need a new `AirportSim.sln` entry (T-006 already created the test
project).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`,
`spec/08-interfaces-core.md` §8.2, §8.5, §8.11a, `spec/09-interfaces-flow.md`
§9.1, §9.7, `spec/18-interfaces-world.md` (`Nodes()`),
`spec/19-interfaces-harness.md` §19.1, §19.2, §19.2d, §19.9

## Interface to implement

No new public type. Amends `HarnessGates.Promotion`'s existing signature
(`19` §19.1, unchanged):

```
HarnessGates.Promotion(IContentIndex content, SimComposer compose, uint64 seed, uint32 ticks) -> GateResult
```

**The amendment (`19` §19.2, binding):** `Promotion` runs two builds of
`ticks` from the same `compose`/`content`/`seed` as `SameProcess`. The
**second run only** promotes and draws the lowest-id `Gate` as the
"Superseded by Q-084" block below says (`19` §19.2d). Nothing is called on
the first run, and `SetPromoted` is called once. The two runs are then
compared exactly as `19` §19.2 already specifies (checkpoint-by-checkpoint,
then final hash) — **this task changes no comparison logic**, only what the
second run does around stepping.

**Superseded by Q-084 (spec PR #95, `8f66470`): `19` §19.2d is binding.**
The earlier text of this section (historical, superseded by Q-084: "pick the
lowest `NodeId` value that `sim.world`/`sim.flow` register... every node is
promotable, so any registered node qualifies") and the single
`SetPromoted` before the first `Step` are replaced by the following, all from
§19.2d:

- **Scope.** It applies to every composer passed to `HarnessGates.Promotion`,
  the §19.2a composer or a test's. No seam may be added that tells them apart.
  `SameProcess`, `SaveLoad`, `FinalHash`, `budget`, `soak` and `checkpoints`
  are unchanged.
- **Run 1** is exactly §19.1's run. The harness calls no member of any
  registered system.
- **Run 2: the recording builder.** Create the factory builder as §19.1 says,
  call `compose` exactly once with a fresh harness-internal `ISimHostBuilder`
  over it (`Services` forwards; `Register(s)` forwards and records `s` only if
  the forwarded call returns; `Build()` forwards, and a composer must not call
  it). After `compose` returns, call `Build()` on the factory builder. Run 2
  registers exactly what run 1 registers.
- **Finding the systems.** `flow` is the recorded system with
  `Id = SystemId(4)` that implements `IFlowSystem`; `world` is the one with
  `Id = SystemId(1)` that implements `IWorldSystem`. A system at another
  position, or one that does not implement the interface, is neither.
- **Finding the gate** (after `Build()`, before the command script). If `flow`
  or `world` is missing, call nothing. Otherwise call `world.Nodes()` once,
  then `flow.KindOf(n)` for each `n` in list order (ascending `NodeId`), and
  stop at the first `NodeKind.Gate`: the lowest-id `Gate`. If there is none
  (including an empty `Nodes()`), call nothing more and run 2 steps exactly as
  run 1 does. Otherwise call `flow.SetPromoted(gate, true)` exactly once.
- **Drawing the gate's passengers (owner decision, 2026-10-02).** When there
  is a `gate`, run 2 submits the script and then repeats `ticks` times:
  `Step(1)`, then `flow.AgentsAt(gate)` exactly once. The harness reads only
  the returned list's `Count`, keeps no reference to it, and allocates nothing
  in the loop. No other member of `flow` or `world` is called, and it never
  demotes. An exception from `KindOf`, `SetPromoted` or `AgentsAt` propagates
  unchanged and the CLI maps it to exit 3.
- **In the CLI.** Both fixture sets have exactly one `Gate`, `NodeId(8)`, so
  `promotion --days D` promotes `NodeId(8)` and calls `AgentsAt(NodeId(8))`
  once per tick. The printed line is unchanged. The comparison, the report and
  the exit codes are unchanged.

`HarnessGates.Promotion` needs `IFlowSystem.KindOf`, which T-049 adds. This
task cannot be released before T-049 merges. If the CLI fixture ever had no
`Gate`, §19.9's kit test would fail; that is a fixture matter, not this
task's to paper over.

**Why this proves the right thing (`09` §9.1, Q-033):** `SetPromoted`
changes no hashed state — promotion is outcome-neutral by construction.
So the two runs must still compare **equal**. `Promotion`'s gate is
therefore a real test of that guarantee, not a vacuous pass: if a future
change to `sim.flow` makes promotion leak into the hash, this gate is the
one that catches it.

## Events

Emitted: none
Consumed: none — `SetPromoted` is a query-adjacent call, not a command, and
publishes nothing (`09` §9.7).

## Tests to pass

```
tests/tools/simharness/**
```

Written by the Test Author, extending the existing harness test project,
all in `tests/tools/simharness/` (Q-041). The five tests are named and
composed exactly in `19` §19.9, which also defines the spy flow, the test
world and `CountingComposer`:

- `test_harness_gates_promotion_promotes_lowest_gate_and_draws_it_every_tick`
- `test_harness_gates_promotion_promotes_nothing_without_a_gate` (cases a to e)
- `test_harness_gates_promotion_promotes_same_node_on_every_call`
- `test_harness_gates_promotion_still_fails_on_divergence_with_a_gate`
- `test_harness_gates_promotion_phase0_kit_promotes_real_gate_and_passes`
  (Slow or not per `07` L11a)

Every existing test in `HarnessGatesTests.cs` and `HarnessCliTests.cs` is
unchanged and still passes. The Test Author may add more. **Do not edit
them.**

**Sequencing (the Test Author's checks).** The spy flow implements
`IFlowSystem`, including `KindOf`, so these tests compile only after T-049
merges. The Test Author is not released before T-049 has merged.

## Performance budget

Not a per-tick sim module (unchanged from T-006). No new budget: `budget
--tier max` is untouched by this task.

## Done when

- [ ] Interface matches spec exactly (no change to `HarnessGates.Promotion`'s
      public signature; only its internal behaviour changes)
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green end to end (Q-016's interim-green rule ended
      with T-006 — full green is mandatory for this task)
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

(historical, superseded by Q-084: "Do not build a 'gate' concept in
`sim.flow`/`sim.world` — none exists at Phase 0... Picking the lowest
registered `NodeId` satisfies that without inventing new sim-layer scope.")
`NodeKind.Gate` is a real kind in `sim.flow`'s `FlowGraph` (`09` §9.11), and
the harness finds the lowest-id `Gate` through `KindOf` (T-049). Add no
other sim-layer member. Writable paths are `tools/SimHarness/**` for the
worker and `tests/tools/simharness/**` for the Test Author.

**Harness writer order.** T-030 (#90) and T-013 (#94) have merged. T-014 is
the next harness writer, after T-049. Do not release it beside T-048 or
T-045.
