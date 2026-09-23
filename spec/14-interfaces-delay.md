# 14 — Public interfaces: `sim.delay`

Implements the `sim.delay` row of `03-module-map.md`: the delay attribution
tree. Answers `open-questions.md` Q-007. Notation and binding rules are as in
`08-interfaces-core.md`; where this file appears to contradict
`01-architecture.md` or `02-determinism.md`, those win and it is a spec bug.

`06-delay-attribution.md` states the *principles* (one parent per minute, live
recording, saved tree, depth cap) and `10-events.md` §10.5 the allocation
*rules*. This file is the **operational definition**: exactly which events are
read, when a gap is measured, how it is split, how ids are allocated, what is
hashed. Where `06`/`10` §10.5 and this file differ in detail, this file is the
more specific and wins; a real contradiction is a spec bug.

Reading order for a `sim.delay` worker: `01`, `02`, `07`, `08`, `06`, `10`
(all of it), this file. `11`, `12` and `13` are read only for the milestone and
event semantics this file cites — `sim.delay` calls none of them.

---

## 14.1 What the module is, and what it is not

`sim.delay` turns milestones and blocking intervals, emitted by the modules
that knew the reason at the time (`10-events.md` §10.1), into a per-flight tree
of attributed delay ticks. It performs no inference and no reconstruction
(`06-delay-attribution.md` rule 3).

`sim.delay` owns:

- one **delay record** per published flight (§14.3), including its delay clock,
- the **attribution tree** per flight: one flight-total node and its allocation
  leaves (§14.7),
- the `DelayEventId` counter (§14.7),
- publication of `DelayEvent` (`10-events.md` §10.7), at finalisation (§14.8),
- the retention and pruning rule for yesterday's trees (§14.8).

`sim.delay` explicitly does **not** own, and must not do:

- any query into another sim module. It references `sim.core` types only
  (every event type and every type an event field carries is a `sim.core` type,
  `03-module-map.md` "Events are immutable value types, defined in `sim.core`").
  This is what `delay_module_never_writes` asserts statically (§14.13);
- content access. It does not read `TickContext.Content` at Phase 0/1: every
  category it needs arrives on an event (§14.5);
- passenger-subject or vehicle-subject delay trees. At Phase 0/1 every tree's
  subject is a `FlightId`. `PassengerCohortId`/`VehicleId` subjects in
  `06-delay-attribution.md`'s `DelayEvent` are reserved for a later amendment;
- aggregate views (daily OTP, season ranking, advisor top three,
  `06-delay-attribution.md` "Aggregate views"). They are later consumers of
  this module's queries, not part of it at Phase 0/1;
- following `EventRef Cause` chains across events (§14.7, "Cause chains").

---

## 14.2 Constants

| Constant | Value | Meaning |
|---|---|---|
| `DELAY_RETENTION_DAYS` | 2 | §14.8: trees finalised on the current or the previous sim-day are retained — **LOW CONFIDENCE** |
| `DELAY_EVENT_ID_NONE` | 0 | Never allocated; the counter starts at 1 |
| `FLIGHT_ID_NONE` | 0 | "no flight" in explanation parameters; `11-interfaces-schedule.md` §11.3 derivation never yields 0 |
| `MAX_ATTRIBUTION_DEPTH` | 6 | `08-interfaces-core.md` §8.1, reproduced unchanged |

> **LOW CONFIDENCE — `DELAY_RETENTION_DAYS = 2`.** `06-delay-attribution.md`
> rule 4 requires that "a player must be able to open yesterday's disaster".
> Two days is the narrowest window that satisfies it. Keeping every tree forever
> is not free: at max tier it is on the order of a million nodes after a season,
> all of it hashed at every checkpoint and written to every save. Longer history
> (the season-long ranking in "Aggregate views") is expected to be served by
> compact per-day aggregates, which are later scope. How far back a player may
> look is player-facing; flagged for the human owner.

---

## 14.3 Types

All types below appear in event payloads or are returned by queries; the ones
carried by `DelayEvent` (`DelayEventId`, `DelaySource`, `DelayExplanation`,
`DelayCategory`) are declared in `sim.core` with the other event types.

```
struct DelayEventId { uint64 Value }         // sim.delay-allocated, §14.7; 0 = none

enum DelayNodeKind { FlightTotal, Allocation }

enum DelaySource {                           // what produced a node; UI maps it to a LocalisedKey
  FlightTotal,                               // the root of a flight's tree
  InboundAircraft,                           // late_inbound, §14.6 step 3
  RunwayHold,                                // AircraftHeldForRunway interval
  TaxiwayHold,                               // AircraftHeldOnTaxiway interval
  StandUnavailable,                          // StandUnavailable interval
  TurnaroundJobWait,                         // TurnaroundJobBlocked interval
  Unexplained                                // residue, category propagated
}

readonly struct DelayExplanation {           // the "explanation" of 06; ids and integers only
  DelaySource Source
  uint64      A                              // meaning per Source, table below
  uint64      B
}

readonly struct DelayNode {
  DelayEventId    Id
  DelayNodeKind   Kind
  FlightId        Subject
  DelayEventId    Parent                     // FlightTotal: DELAY_EVENT_ID_NONE; Allocation: its flight's root
  DelayCategory?  Category                   // null only on FlightTotal
  uint64          Ticks                      // authoritative, §14.6
  SimMinutes      Minutes                    // derived: Fx.FromRatio(Ticks, TICKS_PER_SIM_MINUTE); never hashed
  bool            RootCause                  // 06 rule 1's root_cause marker, §14.7
  FlightId        LinkedFlight               // InboundAircraft only: the inbound's tree; else FLIGHT_ID_NONE
  EventRef        SourceEvent                // the interval's opening event; None for FlightTotal/InboundAircraft/Unexplained
  DelayExplanation Explanation
  Tick            CreatedAt
}

readonly struct FlightDelay {                // the per-flight record, as queried
  FlightId         Flight
  MovementKind     Kind
  bool             HasRotation
  FlightId         Rotation                  // FlightId itself if !HasRotation, as in FlightRecord
  DelayEventId     Root
  uint64           TotalTicks                // the delay clock: lateness at the last checkpoint
  SimMinutes       TotalMinutes              // derived, as DelayNode.Minutes
  int32            CheckpointsReached        // 0 .. count for Kind, §14.4
  Tick             LastCheckpointActual      // PublishTick until the first checkpoint
  bool             Finalised
  Tick             FinalisedAt               // TICK_UNSCHEDULED until Finalised
  int32            MissedPassengers          // §14.9; not delay minutes
  NodeId?          MissedLastBlockedAt       // from the first PassengersMissedFlight
}
```

### `DelayExplanation` parameters

| `Source` | `A` | `B` |
|---|---|---|
| `FlightTotal` | 0 | 0 |
| `InboundAircraft` | inbound `FlightId.Value` | 0 |
| `RunwayHold` | `RunwayId.Value` | `queuePosition` at the hold's opening |
| `TaxiwayHold` | `TaxiEdgeId.Value` | blocking `FlightId.Value`, or `FLIGHT_ID_NONE` |
| `StandUnavailable` | `StandId.Value + 1`, or 0 when the event's stand is null (always, at Phase 1: `12-interfaces-airside.md` §12.7) | occupying `FlightId.Value`, or `FLIGHT_ID_NONE` |
| `TurnaroundJobWait` | `JobKind` ordinal | `ResourceKind` ordinal |
| `Unexplained` | 0 | 0 |

`StandId` is offset by one only because a `StandId` of 0 is legal and the
field must also say "no stand"; every other id in this table is always present.

No strings and no `LocalisedKey` live in `sim.delay`'s state. `app.ui` derives
the localisation key from `(Source, Category)` and formats `A`/`B`; this keeps
the tree a pure function of events and keeps text out of the hash.

---

## 14.4 The delay clock: checkpoints

A flight's delay is measured only at a fixed, short list of **checkpoint
milestones**. Every other `FlightMilestoneReached` is ignored by `sim.delay`.
Because lateness is cumulative (below), measuring at fewer milestones loses no
minutes — it only coarsens *when* the clock updates.

| `MovementKind` | Checkpoints, in order | Terminal |
|---|---|---|
| `Arrival` | `Landed`, `OnStand` | `OnStand` |
| `Departure` | `OnStand`, `Pushback`, `Airborne` | `Airborne` |

- **Arrival delay is on-block delay** (`OnStand` against its plan), the
  industry convention. Nothing at Phase 0/1 can block an arrival between
  `OnStand` and `DeboardComplete` (`Deboard` needs no vehicle,
  `13-interfaces-turnaround.md` §13.4), so nothing is lost by stopping there.
- **Departure delay runs to `Airborne`**, so that runway and taxi holds after
  pushback are attributed to the departure rather than silently dropped.

### What `PlannedTick` means here

`sim.delay` relies on the rule `10-events.md` §10.4 now states explicitly:
`PlannedTick` is **schedule-anchored and cumulative** — derived from the
published schedule plus nominal durations, never shifted by the actual lateness
of an earlier milestone. The per-milestone derivations are
`12-interfaces-airside.md` §12.3 and `13-interfaces-turnaround.md` §13.6.
Therefore, at checkpoint *k*:

```
late(k) = ActualTick(k) > PlannedTick(k) ? ActualTick(k) − PlannedTick(k) : 0     // ticks
```

is the flight's **whole** lateness at that point, and the gap still to explain
is `late(k)` minus what the tree already holds (§14.6). This is exactly
`10-events.md` §10.5's "minus any gap already attributed at milestone n−1".

A checkpoint with `PlannedTick == TICK_UNSCHEDULED` is an emitter bug: throw.
Finishing early (`ActualTick < PlannedTick`) is lateness 0, never negative.

> **LOW CONFIDENCE — the checkpoint set.** It is the smallest set that captures
> every blocking family Phase 1 emits (§14.5) and matches the usual on-block /
> off-block conventions. It is not a player-facing number yet: which lateness
> the UI *headlines* ("dep 07:35 — 34 min late", `06-delay-attribution.md`) is
> `app.ui`'s choice among the queries of §14.10. Adding a checkpoint later is
> additive; removing one changes tree contents.

---

## 14.5 Blocking intervals

An interval is opened by one event and closed by its pair (`10-events.md`
§10.3 rule 2). `sim.delay` keys intervals as below and never pairs by `Cause`
(the emitters' `Cause` conventions differ between families, e.g.
`StandAssigned`'s `Cause` is the freeing `Pushback`, not the
`StandUnavailable`).

| Family | Opens | Closes | Key | Category | `DelaySource` |
|---|---|---|---|---|---|
| Runway | `AircraftHeldForRunway` | `AircraftHeldForRunwayReleased` | `(Flight, Runway)` | `runway_congestion` | `RunwayHold` |
| Taxiway | `AircraftHeldOnTaxiway` | `AircraftHeldOnTaxiwayReleased` | `(Flight, Taxiway)` | `taxi_congestion` | `TaxiwayHold` |
| Stand | `StandUnavailable` | `StandAssigned` | `(Flight, Stand)` | `stand_unavailable` | `StandUnavailable` |
| Turnaround | `TurnaroundJobBlocked` | `TurnaroundJobUnblocked` | `(Flight, Turnaround, JobKind)` | the event's `category` field (`10-events.md` §10.6) | `TurnaroundJobWait` |

An interval covers ticks `[StartTick, EndTick)`, where `StartTick` is the
opening event's tick and `EndTick` the closing event's tick. While open,
`EndTick = TICK_UNSCHEDULED`. Each interval records its **opening event's
`EventId`** (`OpenerId`), which is its identity and its priority (§14.6).

Binding details:

- **Job-dependency waits are not blocking intervals.** A `TurnaroundJobBlocked`
  or `TurnaroundJobUnblocked` with `waitingOn == ResourceKind.JobDependency`
  (`Boarding` waiting on its five prerequisites,
  `13-interfaces-turnaround.md` §13.6) is **ignored**, open and close alike. A
  job-dependency wait is not a cause; it is the sum of other jobs' durations
  and waits, which are already intervals of their own. Counting it would let
  `Boarding` claim minutes that belong to `Fuel`.
- Two open intervals with the same key: throw (emitter bug, `10-events.md`
  §10.3 rule 2). A closing event with no open interval of its key, for a flight
  that is not finalised: throw. Both carry the tick, per `07-conventions.md`.
- An interval opened or closed for a flight that is already **finalised**
  (§14.8) is ignored, as is its pair. This is the normal case for an arrival's
  `BaggageUnload` waits, which happen after the arrival's terminal `OnStand`.
- An interval for a flight `sim.delay` has no record of (no
  `FlightPlanPublished` seen, or already pruned): throw.
- Intervals are retained until they can no longer overlap a future window:
  after each checkpoint, closed intervals with `EndTick <= ActualTick` are
  discarded; at finalisation all of the flight's intervals are discarded, open
  or not.

---

## 14.6 Allocation at a checkpoint

Run in the event handler for the checkpoint's `FlightMilestoneReached`, for
flight *F*, in this order. All arithmetic is in **integer ticks**; `SimMinutes`
appear only as derived display values.

1. **Measure.** `late = late(k)` per §14.4. `delta = late − F.TotalTicks`
   (signed). The window is `W = [F.LastCheckpointActual, ActualTick(k))`. For
   the first checkpoint, `F.LastCheckpointActual` is the tick
   `FlightPlanPublished` was handled.
2. **`delta == 0`**: no tree change.
3. **Inbound rule — a Departure's `OnStand`, `delta > 0`.** A departure has no
   track and therefore no intervals before its `OnStand`
   (`12-interfaces-airside.md` §12.3), so this window's gap is the aircraft
   arriving late from its previous leg:
   - if `F.HasRotation`: let *R* be the rotation arrival's record, which must
     exist and be finalised (throw otherwise — its terminal `OnStand` always
     precedes this departure's `OnStand`). `inbound = min(delta,
     R.TotalTicks)`. If `inbound > 0` and the depth rule of §14.7 allows the
     link, add `inbound` to F's `InboundAircraft` leaf (`late_inbound`,
     `LinkedFlight = R`, `RootCause = false`). Any remainder `delta − inbound`
     goes to F's `Unexplained` leaf (step 5).
   - if `!F.HasRotation`: all of `delta` goes to the `Unexplained` leaf.

   `late_inbound` never exceeds the inbound's own delay. The part of a late
   handover that the inbound's tree cannot account for (a plan too tight for
   the stand's taxi and door time, say) is honestly unexplained, not charged to
   an inbound that was on time.
4. **General rule — every other checkpoint, `delta > 0`.**
   - Clip each retained interval of F to `W` (an open interval's end is
     `ActualTick(k)`); drop empty ones.
   - **Ownership** (`10-events.md` §10.5 rule 3, first-blocker-wins): each tick
     of `W` covered by at least one clipped interval is owned by the covering
     interval with the lowest `OpenerId`. `owned_i` is the number of ticks
     interval *i* owns.
   - **Cap** (the case `10-events.md` §10.5 rule 2 leaves open, where blocking
     exceeds the gap because some of it was absorbed by slack): visit
     intervals in ascending `OpenerId`; each takes `a_i = min(owned_i,
     remaining)`, starting from `remaining = delta`. Add `a_i` to that
     interval's leaf (one leaf per `OpenerId`, created on first positive
     allocation, `RootCause = true`).
   - Any `remaining > 0` goes to F's `Unexplained` leaf (step 5).
5. **Residue** (`10-events.md` §10.5 rule 4). Each flight has at most one
   `Unexplained` leaf: category `propagated`, `RootCause = true`. Residue from
   any checkpoint is added to it.
6. **Recovery — `delta < 0`.** The flight has made up time (slack in the plan
   absorbed earlier delay). Remove `−delta` ticks from F's allocation leaves in
   **descending `DelayEventId`** — the most recently created leaf gives up its
   ticks first. A leaf reduced to 0 ticks is removed from the tree; its id is
   never reused.
7. **Commit.** `F.TotalTicks = late`, `F.LastCheckpointActual =
   ActualTick(k)`, `F.CheckpointsReached += 1`, discard intervals per §14.5,
   and if this is F's terminal checkpoint, finalise (§14.8).

A checkpoint that arrives out of the §14.4 order, or twice, is an emitter bug:
throw. After step 7, **the sum of F's leaf ticks equals `F.TotalTicks`
exactly**: step 3/4/5 add exactly `delta`, step 6 removes exactly `−delta` and
`−delta <= F.TotalTicks` because `late >= 0`. `sum_of_leaves_equals_total`
therefore holds by construction, in integers, with no rounding step at all —
see the amended `10-events.md` §10.5 rule 5.

> **LOW CONFIDENCE — the cap and the recovery order.** Both extend
> first-blocker-wins, which is itself flagged in `10-events.md` §10.5. The cap
> gives the earliest blocker the minutes when blocking exceeds the gap.
> Recovery keeps minutes on the *earliest* causes and forgives the most recent
> ones: of two causes that were each necessary for the flight to be late, the
> one that happened first keeps the blame. The alternatives, proportional
> trimming or forgiving the oldest first, are equally deterministic and change
> tree contents only, never an interface. Flagged for the human owner to judge
> once the tree is visible in a build.

---

## 14.7 The tree: nodes, ids, ordering, depth

### Shape

Each flight has exactly one `FlightTotal` node (its root) and zero or more
`Allocation` leaves whose `Parent` is that root. There are no deeper nodes at
Phase 0/1. `FlightTotal.Ticks == FlightDelay.TotalTicks` at all times, and the
leaves partition it (§14.6).

The two relations are kept apart deliberately:

- **`Parent`** is the tree edge, and the only thing `06-delay-attribution.md`
  rule 1 counts: every allocation leaf has exactly one parent, its flight's
  root. The root has none.
- **`LinkedFlight`** (on an `InboundAircraft` leaf only) is a cross-tree
  *explanation*, not a tree edge: "these minutes are explained by that flight's
  tree". `app.ui` renders the linked tree beneath the leaf, as in the
  `06-delay-attribution.md` example, but its minutes are not part of this
  flight's sum — BA411 can be 22 minutes late while it explains 18 of BA412's.

### `RootCause`

`06-delay-attribution.md` rule 1: every leaf either has a further cause or is
marked `root_cause`. At Phase 0/1: every `Allocation` leaf has `RootCause =
true` except an `InboundAircraft` leaf with a link, whose further cause is the
linked tree. `FlightTotal` has `RootCause = false`.

### `DelayEventId` allocation and ordering

- A single module-owned counter, starting at 1, incremented by one per node
  created, saved and hashed (§14.12). It is **not** an `EventId` and is never
  compared with one; it is not drawn from `IIdAllocator` either, since
  `DelayEventId` is its own type and `sim.delay` is its only allocator.
- Nodes are created only inside `sim.delay`'s event handlers, which run in the
  bus's FIFO dispatch order (`08-interfaces-core.md` §8.6). Therefore: if node
  *a* was created while handling event *e_a* and node *b* while handling *e_b*,
  and `e_a` precedes `e_b` in `EventId` order, then `a < b`. Within one
  handler, nodes are created in the order §14.6 visits them (ascending
  `OpenerId`, then `InboundAircraft`, then `Unexplained`).
- When a `DelayEvent` is published (§14.8) it additionally receives an
  envelope `EventId` from the bus, like every event. The two ids name different
  things: `DelayEventId` names the tree node, for its whole retained life and
  across save/load; the envelope `EventId` names one publication.
- **Every reference points backwards.** `Parent` always has a lower id than
  its child (the root is created at `FlightPlanPublished`, before any leaf). A
  `LinkedFlight`'s root was created before the linking leaf. `no_cycles` holds
  by construction; the test asserts the construction.

### Depth

`ChainDepth(flight)` is 1 for a tree with no leaves, and otherwise the maximum
over its leaves of 2 (a plain leaf) or `2 + ChainDepth(LinkedFlight)` (an
`InboundAircraft` leaf). It is derived, never stored.
`06-delay-attribution.md` rule 5's cap is: **`ChainDepth` never exceeds
`MAX_ATTRIBUTION_DEPTH`.** §14.6 step 3 creates a link only if
`2 + ChainDepth(R) <= MAX_ATTRIBUTION_DEPTH`; otherwise the inbound ticks go to
the `Unexplained` leaf (`root_cause: propagated`). At Phase 0/1 an arrival's
depth is at most 2 and a departure's at most 4, so the cap never binds; it is
defined now so that a later phase adding upstream legs cannot grow chains
without bound.

### Cause chains

`EventEnvelope.Cause` (`10-events.md` §10.2) is **not followed** by `sim.delay`
at Phase 0/1. Resolving an `EventRef` to an event from an earlier tick needs a
retained index of past events, which is new state with its own budget and
retention problem, and every Phase 1 emitter's explanation is already carried
in the opening event's own fields (§14.3). Following causes (e.g. "runway hold,
because declared capacity was exceeded") is an additive, later amendment: it
adds node kinds below allocation leaves and is expected to reuse the depth rule
above unchanged.

---

## 14.8 Finalisation, publication and retention

### Finalisation

A flight is **finalised** at its terminal checkpoint (§14.4). Its tree no
longer changes, except by pruning. A flight that never reaches its terminal
checkpoint (a departure whose `Catering` never gets a truck,
`13-interfaces-turnaround.md` §13.4) is never finalised; its record and open
intervals are retained indefinitely, across day boundaries, which the amended
`10-events.md` §10.3 rule 2 now permits.

### Publication

On finalisation, in the same handler, `sim.delay` publishes one `DelayEvent`
(`06-delay-attribution.md`, as amended) per node of the flight's tree: the
`FlightTotal` node first, then its leaves in ascending `DelayEventId`. Each
carries the node's fields, and the envelope `Cause` is the terminal checkpoint's
`FlightMilestoneReached`. A flight with zero delay publishes its root alone,
with `ticks = 0`, so that a later consumer counting on-time flights sees them.

`DelayEvent` is published **only** at finalisation, never for a provisional
tree: events are immutable (`10-events.md` §10.2), and a live tree can still
change by recovery (§14.6 step 6). The live tree is read through the queries
(§14.10). No module subscribes to `DelayEvent` at Phase 0/1.

### Retention and pruning

At the first tick of each sim-day (`ctx.Tick % TICKS_PER_SIM_DAY == 0`),
during `Tick`, `sim.delay` prunes. A **rotation pair** (an arrival and its
`Rotation` departure) — or a single flight with `!HasRotation` — is pruned when
**every** member is finalised with

```
FinalisedAt < (DayIndex − (DELAY_RETENTION_DAYS − 1)) × TICKS_PER_SIM_DAY
```

i.e. before the start of the previous sim-day. Pruning removes the records and
all their nodes. Pruning pairs together means a retained `InboundAircraft`
leaf's `LinkedFlight` is always retained too: no link ever dangles, and
`no_orphan_nodes` needs no exception. A pair containing an unfinalised flight
is never pruned.

Pruning is iterated in ascending `FlightId` of the pair's lower id, and is the
only work `Tick` does.

---

## 14.9 Missed passengers

`PassengersMissedFlight` (`10-events.md` §10.6, from `sim.flow`) is recorded on
the flight's record, not in its tree: its `count` is added to
`MissedPassengers`, and the first such event's `lastBlockedAt` sets
`MissedLastBlockedAt`. It is accepted whether or not the flight is finalised,
and throws only for an unknown flight.

A missed passenger is not a delay minute. It creates no node, contributes
nothing to `TotalTicks` and is not published as a `DelayEvent`. It exists so
that the one Phase 1 outcome of a bad security queue — passengers left behind —
is visible on the same flight record the player opens.

> **LOW CONFIDENCE — and a HUMAN DECISION it exposes.** At Phase 1 no flight
> ever waits for a late passenger: `Boarding` is a fixed-duration job
> (`13-interfaces-turnaround.md` §13.6) and `DoorsClosed` absorbs whoever is at
> the gate (`12-interfaces-airside.md` §12.7). So `security_queue` and
> `passenger_late` can never appear as *minutes* in a Phase 1 tree, and the
> player's one live lever (T-023, opening security lanes) is visible only as a
> missed-passenger count. Whether flights should hold for late passengers is a
> gameplay decision, and it bears directly on the Phase 1 gate question ("is
> unblocking flow fun?"). It is left to the human owner and **not** decided here;
> if the answer is yes, it is a `sim.turnaround`/`sim.airside` amendment, and
> this module then attributes the wait through an ordinary blocking interval.

---

## 14.10 Module interface

```
interface IDelaySystem : ISimSystem {

  // ---- queries, read-only, for app.ui and tests ----
  bool TryGetFlightDelay(FlightId flight, out FlightDelay delay)
  IReadOnlyList<FlightId>     RetainedFlights()                 // ascending FlightId
  IReadOnlyList<DelayEventId> LeavesOf(FlightId flight)         // ascending DelayEventId; empty if unknown
  bool TryGetNode(DelayEventId id, out DelayNode node)
}
```

- Live and final trees are read the same way; `FlightDelay.Finalised` says
  which. A provisional tree is always internally consistent (sum, parents,
  depth): the invariants hold after every handler, not only at finalisation.
- To render the `06-delay-attribution.md` example, `app.ui` reads the flight's
  root and `LeavesOf`, and for an `InboundAircraft` leaf recurses into
  `LinkedFlight`. No other query is needed, and none is offered: aggregate
  views are later scope (§14.1).
- There is no mutating entry point. `sim.delay` is read-only by
  `06-delay-attribution.md` rule 2; nothing may write to it.

Registry position is **7** (`08-interfaces-core.md` §8.5), after every module
it observes. Event handling happens in its subscribed handlers during the
dispatch phase, so a cause and its attribution land on the same tick
(`08-interfaces-core.md` §8.6 rule 3); for any one event, `sim.delay`'s handler
runs after those of `sim.airside` (3), `sim.flow` (4) and `sim.turnaround` (5).

---

## 14.11 Commands consumed

None. `sim.delay` has no player-facing state to change.

---

## 14.12 Events consumed and emitted

**Consumed** (full field lists in `10-events.md` §10.6):

| Event | From | Reaction |
|---|---|---|
| `FlightPlanPublished` | `sim.schedule` | Create the flight's record and its `FlightTotal` root (`Ticks = 0`). Reads `kind`, `rotation`, `hasRotation` (fields added to `10-events.md` §10.6 by this amendment). A second one for the same `FlightId`: throw. |
| `FlightMilestoneReached` | `sim.airside`, `sim.turnaround` | Checkpoints only (§14.4) → §14.6. Every other milestone, including `PlanPublished`, is ignored. |
| `AircraftHeldForRunway` / `AircraftHeldForRunwayReleased` | `sim.airside` | §14.5, Runway family |
| `AircraftHeldOnTaxiway` / `AircraftHeldOnTaxiwayReleased` | `sim.airside` | §14.5, Taxiway family |
| `StandUnavailable` / `StandAssigned` | `sim.airside` | §14.5, Stand family |
| `TurnaroundJobBlocked` / `TurnaroundJobUnblocked` | `sim.turnaround` | §14.5, Turnaround family; `JobDependency` ignored |
| `PassengersMissedFlight` | `sim.flow` | §14.9 |

**Deliberately not consumed at Phase 0/1**, although they exist in the
catalogue: `TurnaroundJobStarted`/`Completed` (lifecycle, not blocking),
`QueueThresholdExceeded`/`Cleared` and `FlowBlocked`/`FlowUnblocked` (no Phase 1
path from a queue to a flight's minutes, §14.9), `PassengersArrivedAtGate`,
`FlightPlanRevised`/`FlightCancelled` (not emitted at Phase 1,
`11-interfaces-schedule.md` §11.1), and every Phase 2 event. `sim.delay`
subscribes to none of them; per `10-events.md` §10.6 it has no rule for them.
Each becomes consumed by amendment, with its rule stated here.

**Emitted:** `DelayEvent` only, per §14.8.

---

## 14.13 State, hashing, RNG and budget

Hashed state, fed in this declared order (`08-interfaces-core.md` §8.9). It is
also exactly the saved state (`06-delay-attribution.md` rule 4):

1. The `DelayEventId` counter (next value).
2. Flight records, ascending `FlightId`: `Kind`, `HasRotation`, `Rotation`,
   `Root`, `TotalTicks`, `CheckpointsReached`, `LastCheckpointActual`,
   `Finalised`, `FinalisedAt`, `MissedPassengers`, `MissedLastBlockedAt`
   (presence flag, then value).
3. Retained intervals, ascending `OpenerId`: `OpenerId` (`Tick`, `Sequence`),
   `Flight`, family, `JobKind` ordinal (0 unless Turnaround), `Category`,
   `StartTick`, `EndTick`, `Explanation.A`, `Explanation.B`.
4. Nodes, ascending `DelayEventId`: every field of `DelayNode` except
   `Minutes`.

Not hashed, because derived: `Minutes`/`TotalMinutes`, `ChainDepth`, any
per-flight leaf index or interval index, `RetainedFlights()`.

`OpenerId` is a raw `EventId`. It is saved because intervals opened after a
load must still be ordered against intervals opened before it, and `EventId`
is a total order across ticks (`08-interfaces-core.md` §8.6 rule 2). The cost:
a module that starts emitting an extra event changes `sim.delay`'s hash even
where the tree is unchanged. That is a correct drift report, not a false one —
the bus did change — but a Verifier seeing only `sim.delay` disagree should
check the upstream emitters' event counts first.

**RNG: none.** Every rule in this file is a declared deterministic function of
the event stream. The module declares no stream and contributes no stream
state to its hash, the same posture as `sim.schedule`, `sim.airside` and
`sim.turnaround`.

Budget: **0.40 ms/tick at max tier** (`03-module-map.md`). The shape:

- Per event, O(1) amortised: intervals arrive in `OpenerId` order, so the
  retained-interval store is append-ordered and needs no sort; lookup of an
  open interval by key is within one flight's small interval list.
- Per checkpoint, O(k log k) for the flight's *k* retained intervals — a
  handful. Never a scan over all flights or all nodes.
- Pruning is once per sim-day, O(nodes pruned). It is one tick in 14 400, well
  outside the p99 of `03-module-map.md`'s protocol, and must still not
  allocate.
- No allocation in the update path (`07-conventions.md`): records, intervals
  and nodes live in pooled, index-stable storage; a removed leaf's slot is
  reused, its id is not.

---

## 14.14 Fixtures and tests (T-024)

### Synthetic event streams

`sim.delay` is a pure function of its input events, so its property tests feed
it events directly, with no other module registered. The Test Author writes, in
`tests/sim/delay/`, a **seeded** generator (`07-conventions.md`: no unseeded
RNG in tests) of well-formed streams — every rule of `10-events.md` §10.3 and
§14.4–§14.5 respected: publication first, checkpoints in order with
schedule-anchored `PlannedTick`s, paired intervals with overlaps, early and
late checkpoints, rotation pairs and rotation-less flights, job-dependency
waits, flights that never finalise, and day boundaries crossed. This is how
`06-delay-attribution.md`'s "1000 randomly generated flight days" is realised.

### Integrated day

One headless sim-day with `sim.schedule`, `sim.flow`, `sim.airside`,
`sim.turnaround` and `sim.delay` registered, against
`tests/fixtures/schedule/phase0-200.csv` (`11-interfaces-schedule.md` §11.10),
`tests/fixtures/airside/phase1-single-runway.*`
(`12-interfaces-airside.md` §12.13) and
`tests/fixtures/turnaround/phase1-four-vehicles.*`
(`13-interfaces-turnaround.md` §13.11). Assert every invariant below over
every retained flight, after every tick. Do **not** assert that a particular
leaf kind appears: those fixtures guarantee that a runway hold and a vehicle
wait *occur*, not that either outlasts the schedule's slack and becomes delay.
Leaf coverage per `DelaySource` is the synthetic streams' job. No new fixture
is needed; T-024 owns `tests/fixtures/delay/**` only if the Test Author finds
one necessary.

### Done-condition tests

The six `06-delay-attribution.md` requires, with the `test_` prefix of
`07-conventions.md`:

- `test_sum_of_leaves_equals_total` — on `Ticks`, after every handler, over
  1000 generated flight-days
- `test_no_orphan_nodes`
- `test_no_cycles`
- `test_depth_capped`
- `test_survives_save_load` — tree, records, intervals and counter identical
  after a round trip mid-day, with intervals open across the save
- `test_delay_module_never_writes` — static: `src/sim/delay` references no
  type outside `sim.core`, holds no reference to any other `ISimSystem`, and
  publishes no event type other than `DelayEvent`

Plus the rules this file adds:

- `test_delay_concurrent_intervals_first_blocker_wins`
- `test_delay_blocking_beyond_gap_is_capped_in_opener_order`
- `test_delay_uncovered_gap_is_single_propagated_leaf`
- `test_delay_recovery_trims_latest_leaf_first`
- `test_delay_late_inbound_capped_at_inbound_total`
- `test_delay_job_dependency_wait_is_ignored`
- `test_delay_ids_monotone_and_references_point_backwards`
- `test_delay_events_published_once_per_node_at_finalisation`
- `test_delay_rotation_pair_pruned_together_after_retention`
- `test_delay_unfinalised_flight_survives_day_boundary`
- `test_delay_unmatched_close_event_throws_with_tick`
- `test_delay_tick_consumes_no_rng`
