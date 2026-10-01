# T-039 — `sim.flow`: `Inject` rejects non-`Departing` direction

| Field | Value |
|---|---|
| Status | MERGED (PR #63) |
| Module | `sim.flow` |
| Assigned role | worker |
| Depends on | T-010 (PR #56, open) |
| Spec source | `spec/09-interfaces-flow.md` §9.6 "Only `Departing` cohorts exist at Phase 0/1 (Q-040)", §9.7 "Exceptions" check 4 (Q-040, spec PR #55, merged `3a6ba7c`) |
| Blocked by | — |

## Writable paths

```
src/sim/flow/**
```

No other file, and no other module. This is a fix inside `sim.flow`'s
existing `Inject` implementation (T-007), not a new interface.

## Why this task exists

Q-040 (a) answers: at Phase 0/1 there are no arriving or transferring
passengers (`11` §11.1, `12` "Arriving passengers"), and `9.6` defines no
destination for them, so `Inject` must reject a non-`Departing`
`CohortKey.Direction`. `spec/09-interfaces-flow.md` §9.7 "Exceptions"
gives `Inject`'s check order as four checks, in order, throwing at the
first failure and changing nothing on a throw:

1. an unknown `at`: `ArgumentException`;
2. an `at` that is not a `Source`: `ArgumentException`;
3. `count <= 0`: `ArgumentOutOfRangeException`;
4. `key.Direction ≠ Departing`, at Phase 0/1 (Q-040, §9.6):
   `ArgumentException`.

Merged T-007 code (`src/sim/flow/FlowSystem.cs`, `Inject`, around line 483)
implements checks 1–3 only, in that order, and never checks `Direction`.
This task adds check 4, appended after check 3, with no reordering of the
first three.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/09-interfaces-flow.md` §9.6, §9.7

## Interface to implement

No new public signature. `IFlowSystem.Inject` (`09` §9.7) is unchanged:

```
CohortId Inject(in CohortKey key, int32 count, NodeId at)   // sim.schedule, sim.airside
```

Binding, copied from `spec/09-interfaces-flow.md` §9.7, not paraphrased:
`Inject` checks in order, throws at the first failure, and changes nothing
on a throw — unknown `at` (`ArgumentException`), `at` not a `Source`
(`ArgumentException`), `count <= 0` (`ArgumentOutOfRangeException`), then
`key.Direction ≠ Departing` (`ArgumentException`). So
`Inject(Arriving, 0, a valid Source)` throws `ArgumentOutOfRangeException`,
not the direction check — checks 1–3 keep their existing order, check 4 is
appended, never promoted ahead of them.

## Events

Emitted: none (this task changes no event's payload or emission).
Consumed: none.

## Tests to pass

```
tests/sim/flow/**
```

Written by the Test Author: exactly `test_inject_rejects_non_departing_direction`,
per `09` §9.7's "Exact rules (Q-033)" as amended by Q-040:

- **Cases.** One `Inject` with `Direction = Arriving` and one with
  `Direction = Transferring`, each with a valid `Source` and `count > 0`.
  Each throws `ArgumentException`.
- **"Changes nothing" is asserted.** After each throw, and before any
  `Step`, the test checks `Population`, `CohortsAt` and
  `PopulationForFlight` for every node and direction — **note**:
  `PopulationForFlight(FlightId, FlowDirection)` takes no `NodeId`; read
  this as checking the rejected keys' flight in all three `FlowDirection`
  values, not per node. It also checks the `CohortId` returned by the next
  valid `Departing` `Inject`. All must equal those of a control run that
  made the same valid calls without the two rejected ones — so the
  `IIdAllocator` counter was not advanced by a rejected call.

**Do not edit them.** If a test contradicts `spec/09-interfaces-flow.md`,
file an open question and stop.

## Performance budget

No change to `sim.flow`'s existing 2.5 ms/tick budget (`03-module-map.md`,
`09` §9.10) — this task adds one early-exit check to `Inject`, called
outside the tick's update path.

## Done when

- [ ] Interface matches spec exactly (unchanged signature; check order per
      `09` §9.7)
- [ ] `test_inject_rejects_non_departing_direction` passes
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

**Depends on T-010 (PR #56, open as of this task's filing), not merged.**
T-010 also edits `src/sim/flow/FlowSystem.cs` (its own PR touches `Inject`'s
neighbourhood along with the routing cache and promotion work) — releasing
this task before T-010 merges risks a conflicting edit to the same file.
Do not release until T-010 has actually merged.

This is a small, single-check addition: do not widen scope to any other
part of `Inject`, `Absorb` or the routing/promotion code T-010 is landing.
If the fix as specified needs a change to `IFlowSystem`'s public shape,
that is a spec question, not a worker's call — file it and stop.
