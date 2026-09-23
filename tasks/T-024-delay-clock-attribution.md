# T-024 — Delay clock per flight + naive attribution log

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.delay` |
| Assigned role | worker |
| Depends on | T-022, T-023, T-026 |
| Spec source | `spec/00-overview.md` build order #6; `spec/06-delay-attribution.md`; `spec/10-events.md` §10.5–§10.7; `spec/14-interfaces-delay.md` (answers Q-007) |
| Blocked by | — |

## Writable paths

```
src/sim/delay/**, tests/sim/delay/**
```

`tests/fixtures/delay/**` only if the Test Author finds a dedicated fixture
necessary (`spec/14-interfaces-delay.md` §14.14 — the integrated-day test
reuses the schedule/airside/turnaround fixtures and needs no new one).

`sim.delay` references **no type outside `sim.core`**, holds **no reference
to any other `ISimSystem`**, and makes **no query into another sim module** —
`delay_module_never_writes` (§14.13) is a static check on this. It reads only
the event stream; it does not read `TickContext.Content`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`, `spec/06-delay-attribution.md`,
`spec/10-events.md` (all of it), `spec/14-interfaces-delay.md`. `11`, `12`
and `13` are read **only** for the milestone/event semantics
`14-interfaces-delay.md` cites (`PlannedTick` derivations, the checkpoint
list) — `sim.delay` calls none of those modules' interfaces.

## Interface to implement

```
struct DelayEventId { uint64 Value }         // sim.delay-allocated, §14.7; 0 = none

enum DelayNodeKind { FlightTotal, Allocation }

enum DelaySource {
  FlightTotal,
  InboundAircraft,
  RunwayHold,
  TaxiwayHold,
  StandUnavailable,
  TurnaroundJobWait,
  Unexplained,
  PassengerHold                              // DepartureHeldForPassengers interval (D6); appended last, no ordinal moves
}

readonly struct DelayExplanation {
  DelaySource Source
  uint64      A
  uint64      B
}

readonly struct DelayNode {
  DelayEventId    Id
  DelayNodeKind   Kind
  FlightId        Subject
  DelayEventId    Parent
  DelayCategory?  Category
  uint64          Ticks
  SimMinutes      Minutes                    // derived; never hashed
  bool            RootCause
  FlightId        LinkedFlight
  EventRef        SourceEvent
  DelayExplanation Explanation
  Tick            CreatedAt
}

readonly struct FlightDelay {
  FlightId         Flight
  MovementKind     Kind
  bool             HasRotation
  FlightId         Rotation
  DelayEventId     Root
  uint64           TotalTicks
  SimMinutes       TotalMinutes              // derived
  int32            CheckpointsReached
  Tick             LastCheckpointActual
  bool             Finalised
  Tick             FinalisedAt
  int32            MissedPassengers
  NodeId?          MissedLastBlockedAt
}

interface IDelaySystem : ISimSystem {
  bool TryGetFlightDelay(FlightId flight, out FlightDelay delay)
  IReadOnlyList<FlightId>     RetainedFlights()                 // ascending FlightId
  IReadOnlyList<DelayEventId> LeavesOf(FlightId flight)         // ascending DelayEventId; empty if unknown
  bool TryGetNode(DelayEventId id, out DelayNode node)
}
```

`DelayEventId`, `DelaySource`, `DelayExplanation`, `DelayCategory` are
carried by `DelayEvent`'s payload and therefore declared as `sim.core`
types alongside the other event types (`03-module-map.md`); this task does
not author them (see "Scheduling gap" note below) but does consume them.

Binding, copied from `spec/14-interfaces-delay.md`, not paraphrased:

- **Checkpoints** (§14.4): `Arrival` measures `Landed`, `OnStand` (terminal
  `OnStand`); `Departure` measures `OnStand`, `Pushback`, `Airborne`
  (terminal `Airborne`). Every other `FlightMilestoneReached` is ignored.
  `PlannedTick` is schedule-anchored and cumulative (`10-events.md` §10.4);
  a checkpoint with `PlannedTick == TICK_UNSCHEDULED` is an emitter bug:
  throw. `late(k) = max(0, ActualTick(k) − PlannedTick(k))`.
- **Blocking intervals** (§14.5): four families — Runway
  (`AircraftHeldForRunway`/`Released`, key `(Flight, Runway)`, category
  `runway_congestion`, source `RunwayHold`), Taxiway
  (`AircraftHeldOnTaxiway`/`Released`, key `(Flight, Taxiway)`, category
  `taxi_congestion`, source `TaxiwayHold`), Stand
  (`StandUnavailable`/`StandAssigned`, key `(Flight, Stand)`, category
  `stand_unavailable`, source `StandUnavailable`), Turnaround
  (`TurnaroundJobBlocked`/`Unblocked`, key `(Flight, Turnaround, JobKind)`,
  category from the event's own `category` field, source
  `TurnaroundJobWait`), and Passenger hold (D6:
  `DepartureHeldForPassengers`/`Released`, key `(Flight, PassengerHold)`,
  category `passenger_late`, source `PassengerHold`; explanation `A` =
  `heldAt` `NodeId.Value`, `B` = `outstanding` at the hold's opening — the
  hold opens after the departure's `OnStand` checkpoint and closes before
  `Pushback`, so it is allocated at `Pushback` like any interval in that
  window; no rule in the allocation algorithm below changes for it). Pair by
  key, never by `Cause`. A
  `TurnaroundJobBlocked`/`Unblocked` with `waitingOn ==
  ResourceKind.JobDependency` is ignored entirely, open and close alike. Two
  open intervals with the same key, or a closing event with no open interval
  for a non-finalised flight: throw, with the tick. An interval for an
  unknown flight: throw. Intervals for an already-finalised flight are
  ignored, as is their pair. Closed intervals with `EndTick <= ActualTick`
  are discarded after each checkpoint; all of a flight's intervals are
  discarded at finalisation.
- **Allocation algorithm** (§14.6), run in the checkpoint's event handler, in
  integer ticks only:
  1. `late = late(k)`; `delta = late − F.TotalTicks`. `delta == 0`: no
     change.
  2. **Inbound rule** — a departure's `OnStand`, `delta > 0`: if
     `F.HasRotation`, `inbound = min(delta, R.TotalTicks)` where `R` is the
     rotation arrival's (must-exist, must-be-finalised) record; add to the
     `InboundAircraft` leaf if `> 0` and the depth rule (§14.7) allows the
     link; remainder to `Unexplained`. If `!F.HasRotation`, all of `delta`
     to `Unexplained`.
  3. **General rule** — every other checkpoint, `delta > 0`: clip retained
     intervals to the window `W`; first-blocker-wins ownership by ascending
     `OpenerId`; cap each interval's allocation at `min(owned_i, remaining)`
     visited in ascending `OpenerId`; residue to `Unexplained`.
  4. **Residue**: at most one `Unexplained` leaf per flight, category
     `propagated`, `RootCause = true`.
  5. **Recovery** — `delta < 0`: remove `−delta` ticks from allocation
     leaves in **descending `DelayEventId`**; a leaf reduced to 0 is removed,
     its id never reused.
  6. **Commit**: `F.TotalTicks = late`, `F.LastCheckpointActual =
     ActualTick(k)`, `F.CheckpointsReached += 1`, discard intervals, finalise
     if terminal. A checkpoint out of order, or twice: throw.
  `sum_of_leaves_equals_total` must hold exactly after every handler.
- **Tree shape and ids** (§14.7): one `FlightTotal` root per flight, zero or
  more `Allocation` leaves, `Parent` always the root. `LinkedFlight` (on
  `InboundAircraft` leaves only) is a cross-tree explanation, not a tree
  edge. `RootCause = true` on every leaf except a linked `InboundAircraft`
  leaf; `false` on `FlightTotal`. `DelayEventId` is a single module-owned
  counter starting at 1, never compared with `EventId`, monotone in dispatch
  order, created only inside event handlers in FIFO bus order, and every
  `Parent`/`LinkedFlight` reference points to a strictly lower id
  (`no_cycles` holds by construction). `ChainDepth` is derived, never
  stored: 1 with no leaves, else max over leaves of 2, or `2 +
  ChainDepth(LinkedFlight)` for a linked leaf; a link is only created if `2
  + ChainDepth(R) <= MAX_ATTRIBUTION_DEPTH`, else the ticks go to
  `Unexplained`. `EventEnvelope.Cause` chains are **not** followed.
- **Finalisation and publication** (§14.8): a flight finalises at its
  terminal checkpoint; an unfinalised flight (never reaches terminal) is
  retained indefinitely, across day boundaries. On finalisation, in the same
  handler, publish one `DelayEvent` per tree node — `FlightTotal` first, then
  leaves ascending `DelayEventId` — with `Cause` set to the terminal
  checkpoint's `FlightMilestoneReached`. A zero-delay flight still publishes
  its root with `ticks = 0`. `DelayEvent` is published **only** at
  finalisation, never for a live tree.
- **Retention and pruning** (§14.8): at the first tick of each sim-day
  (`ctx.Tick % TICKS_PER_SIM_DAY == 0`), during `Tick` — the only work `Tick`
  does — prune a rotation pair (or a single rotation-less flight) when every
  member is finalised with `FinalisedAt < (DayIndex − (DELAY_RETENTION_DAYS
  − 1)) × TICKS_PER_SIM_DAY`, i.e. `DELAY_RETENTION_DAYS = 2`. Iterate
  ascending `FlightId` of the pair's lower id. A pair with any unfinalised
  member is never pruned.
- **Missed passengers** (§14.9): `PassengersMissedFlight` adds `count` to
  `MissedPassengers` on the flight's record (not the tree); the first such
  event's `lastBlockedAt` sets `MissedLastBlockedAt`. Accepted whether or not
  finalised; throws only for an unknown flight. Creates no node, contributes
  nothing to `TotalTicks`, is not published as a `DelayEvent`.
- **No mutating entry point** (§14.11). No commands.
- **No RNG** (§14.13). Do not add a stream speculatively.
- **Registry position 7** (§14.10), after `sim.airside` (3), `sim.flow` (4)
  and `sim.turnaround` (5).

## Construction (`14` §14.13a, Q-009)

```
DelayFactory.CreateSystem(in SystemServices services) -> IDelaySystem
```

Takes no construction data and no other module's interface — this is
`delay_module_never_writes` by construction. It subscribes to the events of
the table below through `services.Events` inside `CreateSystem`.

## Events

Emitted: `DelayEvent` only, at finalisation (§14.8).

Consumed (full field lists in `10-events.md` §10.6):

| Event | From | Reaction |
|---|---|---|
| `FlightPlanPublished` | `sim.schedule` | Create the flight's record and `FlightTotal` root (`Ticks = 0`), reading `kind`/`rotation`/`hasRotation`. A second one for the same `FlightId`: throw. |
| `FlightMilestoneReached` | `sim.airside`, `sim.turnaround` | Checkpoints only (§14.4) → §14.6; everything else ignored. |
| `AircraftHeldForRunway`/`Released` | `sim.airside` | §14.5, Runway |
| `AircraftHeldOnTaxiway`/`Released` | `sim.airside` | §14.5, Taxiway |
| `StandUnavailable`/`StandAssigned` | `sim.airside` | §14.5, Stand |
| `TurnaroundJobBlocked`/`Unblocked` | `sim.turnaround` | §14.5, Turnaround; `JobDependency` ignored |
| `DepartureHeldForPassengers`/`Released` | `sim.airside` | §14.5, Passenger-hold family (D6) |
| `PassengersMissedFlight` | `sim.flow` | §14.9 |

Deliberately not consumed at Phase 0/1 (listed in §14.12): `TurnaroundJobStarted`/`Completed`,
`QueueThresholdExceeded`/`Cleared`, `FlowBlocked`/`FlowUnblocked`,
`PassengersArrivedAtGate`, `FlightPlanRevised`/`FlightCancelled`, every Phase
2 event.

## Tests to pass

```
tests/sim/delay/**
```

Written by the Test Author. `sim.delay` is a pure function of its input
events, so most tests feed a seeded, well-formed synthetic event stream
directly with no other module registered, plus one integrated-day test
against the real `sim.schedule`/`sim.flow`/`sim.airside`/`sim.turnaround`
fixtures. Expect at least:

- `test_sum_of_leaves_equals_total`
- `test_no_orphan_nodes`
- `test_no_cycles`
- `test_depth_capped`
- `test_survives_save_load`
- `test_delay_module_never_writes`
- `test_delay_concurrent_intervals_first_blocker_wins`
- `test_delay_blocking_beyond_gap_is_capped_in_opener_order`
- `test_delay_uncovered_gap_is_single_propagated_leaf`
- `test_delay_recovery_trims_latest_leaf_first`
- `test_delay_late_inbound_capped_at_inbound_total`
- `test_delay_job_dependency_wait_is_ignored`
- `test_delay_passenger_hold_is_passenger_late_leaf_naming_held_at_node`
- `test_delay_ids_monotone_and_references_point_backwards`
- `test_delay_events_published_once_per_node_at_finalisation`
- `test_delay_rotation_pair_pruned_together_after_retention`
- `test_delay_unfinalised_flight_survives_day_boundary`
- `test_delay_unmatched_close_event_throws_with_tick`
- `test_delay_tick_consumes_no_rng`

**Do not edit them.** If a test contradicts `spec/14-interfaces-delay.md`,
file an open question and stop.

## Performance budget

`0.40` ms/tick at max tier (`spec/03-module-map.md`, `spec/14-interfaces-delay.md`
§14.13). Per-event O(1) amortised (intervals arrive in `OpenerId` order, no
sort needed); per-checkpoint O(k log k) for the flight's *k* retained
intervals, never a scan over all flights or nodes; pruning is O(nodes
pruned), once per sim-day. No allocation in the update path — records,
intervals and nodes live in pooled, index-stable storage; a removed leaf's
slot is reused, its id is not.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

The `sim.core` payload types this task consumes (`DelayEventId`,
`DelaySource`, `DelayExplanation`, `DelayCategory`, and the amended
`FlightPlanPublished`/`TurnaroundJobBlocked`/`Unblocked` fields) are authored
elsewhere, not by this task — see the Planner's scheduling note in
`tasks/queue.md` ("Scheduling gap: `sim.core` event payload types"). Do not
add these types under `src/sim/delay/**`; if they are missing when this task
starts, that is a release-ordering problem to flag to the Integrator, not
something to work around locally.

Hashed state order is fixed by §14.13: the `DelayEventId` counter; flight
records ascending `FlightId`; retained intervals ascending `OpenerId`; nodes
ascending `DelayEventId`. `Minutes`/`TotalMinutes`, `ChainDepth`, and any
per-flight index are derived and must not be hashed.

Two LOW CONFIDENCE flags carried over from the spec, not this task's to
resolve: (1) the cap/recovery order in the allocation algorithm extends
first-blocker-wins and is flagged for the human owner once a tree is visible
in a build; (2) `DELAY_RETENTION_DAYS = 2` is the narrowest window
satisfying `06-delay-attribution.md` rule 4. Both, plus every other Q-007/
Q-008 LOW CONFIDENCE marker, are accepted as provisional (HD, D8) —
build to the stated values; they are revisited after T-025, not by a
worker.

§14.9's HUMAN DECISION is now **resolved** (D6): a departure *does* wait,
for at most `BoardingHoldMaxMinutes`, for passengers still in the terminal
(`sim.airside`, T-021). The wait is an ordinary blocking interval — the
Passenger-hold family above — so `passenger_late` now reaches the tree as
minutes, not only as a missed-passenger count. Anyone still outstanding
when the hold times out is still recorded as a missed passenger, exactly as
before; a bad security queue now shows up both ways.
