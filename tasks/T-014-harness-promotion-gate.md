# T-014 — Harness: make `determinism_promotion` promote a real node

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `tools.simharness` |
| Assigned role | worker |
| Depends on | T-009 (merged #74), T-010 (merged #56) |
| Spec source | `spec/19-interfaces-harness.md` §19.2 ("`Promotion` before `sim.flow` promotion (T-010)"); `spec/09-interfaces-flow.md` §9.1, §9.7 (`SetPromoted`/`AgentsAt`, Q-033), "Promotion rules"; `spec/02-determinism.md` "Gates" (`determinism_promotion`: "same day headless vs with camera parked on a gate") |
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
`spec/08-interfaces-core.md` §8.5, §8.11a, `spec/09-interfaces-flow.md`
§9.1, §9.7, `spec/19-interfaces-harness.md` §19.1, §19.2

## Interface to implement

No new public type. Amends `HarnessGates.Promotion`'s existing signature
(`19` §19.1, unchanged):

```
HarnessGates.Promotion(IContentIndex content, SimComposer compose, uint64 seed, uint32 ticks) -> GateResult
```

**The amendment (`19` §19.2, binding):** `Promotion` runs two builds of
`ticks` from the same `compose`/`content`/`seed` as `SameProcess`. The
**second run only**, after `Build()` and before its first `Step`, calls
`IFlowSystem.SetPromoted(node, true)` on the node(s) the `02` gate calls "a
gate" — a fixed, deterministic choice, not a random or content-driven one.
Do not call it on the first run, and do not call it more than once on any
node (a repeat is a no-op per `09` §9.7 but is still redundant work this
task should not add). The two runs are then compared exactly as `19` §19.2
already specifies (checkpoint-by-checkpoint, then final hash) — **this
task changes no comparison logic**, only what the second run does before
stepping.

**Choosing "a gate" (this task's own decision, deterministic, not a
balance call):** with `sim.flow` in the CLI composition (T-009), pick the
lowest `NodeId` value that both (a) `sim.world`/`sim.flow` register at
Phase 0 scale from the fixed CLI composition and (b) is a valid argument to
`SetPromoted`/`AgentsAt` (`09` §9.7: "every node is promotable", so any
registered node qualifies — no further filtering exists to "find a gate").
If the fixed CLI composition registers no nodes at all, that is a gap in
T-009's or T-007's own scope, not something this task papers over — file
an open question and stop rather than promoting a node this task invents.

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

Written by the Test Author, extending the existing harness test project.
Expect at least: a test that `Promotion` still passes (compares equal) once
a node is promoted in the second run, proving `SetPromoted` is outcome
neutral end to end through the CLI composition; a divergence-seam test
reusing the existing composer-differs pattern (`19` §19.2) to prove the
gate still fails on a real nondeterminism even with promotion wired in; and
a test that the chosen node is deterministic across repeated runs (same
`NodeId` picked every time, not dependent on iteration order of any
collection). **Do not edit them.**

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

Do not build a "gate" concept in `sim.flow`/`sim.world` — none exists at
Phase 0 (`sim.airside`'s boarding gates are T-021's, much later). "A gate"
in `02`'s gate table is the `02` gate's own name for "some promotable
node," not a reference to a spec type. Picking the lowest registered
`NodeId` satisfies that without inventing new sim-layer scope.
