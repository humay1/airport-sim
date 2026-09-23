# 10 — Event catalogue: what `sim.delay` consumes

`sim.delay` is read-only and learns about the world **exclusively** through the
event bus (`03-module-map.md`, `06-delay-attribution.md` rule 2). This file is the
complete list of what it may listen to. Events are immutable value types declared
in `sim.core`; transport, ordering and limits are in `08-interfaces-core.md` §8.6.

An event that is not in this table does not exist. A module that needs a new one
files a question and the Architect adds it here first.

---

## 10.1 Why the model is milestones plus blocking intervals

Attribution must be recorded live and must never be reconstructed by heuristics
(`06-delay-attribution.md` rule 3). Two kinds of fact are enough to do that, and
a third would be chatter:

- **Milestones.** A flight passes through a fixed sequence of states. Each carries
  a planned tick and an actual tick. The difference is the delay to explain.
- **Blocking intervals.** While a flight is waiting on something, the module doing
  the waiting says so once at the start and once at the end, naming what it waited
  on. Those intervals are the explanation.

`sim.delay` then allocates the gap between two consecutive milestones across the
blocking intervals that overlap it (§10.5). Nothing is inferred; every minute is
claimed by a module that knew the reason at the time.

---

## 10.2 Envelope

Every event carries these fields. They are not repeated in the tables below.

```
readonly struct EventEnvelope {
  EventId  Id           // assigned on publish, 08 §8.6
  Tick     Tick
  SystemId Source       // emitting system, from the registry
  EventRef Cause        // the event that caused this one; EventRef.None if root
}

readonly struct EventRef { EventId Id; bool HasValue }
```

`Cause` is the whole game. An emitter that knows why it is emitting **must**
populate it: a runway hold caused by a declared-capacity breach references the
capacity event, not nothing. `06-delay-attribution.md` rule 1 — one parent per
minute — is only achievable if emitters are honest here, and a cause chain deeper
than `MAX_ATTRIBUTION_DEPTH` terminates as `root_cause: propagated` (rule 5).

Every event also carries a `LocalisedKey` plus integer/`Fx` parameters where the
table says "explanation". No literal text ever (`04-data-schemas.md`).

---

## 10.3 Emission discipline

Binding on every emitting module. These rules are what keep the event bus inside
the `sim.core` budget and keep the delay tree bounded.

1. **One event per state transition.** Never per entity per tick. A module that
   emits while a condition merely persists is wrong.
2. **Blocking events come in pairs.** Every `*Blocked` is matched by exactly one
   `*Unblocked`/`*Resolved` for the same subject. An interval still open when
   its subject leaves the simulation (a flight's last milestone — `Airborne`
   for a departure, the stand handoff for an arrival — or a cohort's
   absorption) is a broken invariant: throw. An interval **may** stay open
   across a day boundary — a job that never gets a vehicle stays blocked
   indefinitely by design (`13-interfaces-turnaround.md` §13.4), and
   `sim.delay` carries such intervals forward (`14-interfaces-delay.md` §14.8).
3. **Emit at the transition tick**, not at the next convenient one. Deferring
   shifts minutes into the wrong interval and quietly corrupts attribution.
4. **Deduplicate at the source.** Threshold events use hysteresis, declared in
   content, so a value oscillating across a threshold does not flood the bus.
5. Events carry ids and values, never references to live objects
   (`02-determinism.md` rule 7).

---

## 10.4 Flight milestones

```
enum FlightMilestone {
  PlanPublished, InboundAirborne, Landed, OffRunway, OnStand, DoorsOpen,
  DeboardComplete, ReadyToBoard, BoardingComplete, DoorsClosed, Pushback,
  TakeoffRoll, Airborne
}

event FlightMilestoneReached {
  FlightId       Flight
  FlightMilestone Milestone
  Tick           PlannedTick        // from the published schedule or the turnaround plan
  Tick           ActualTick
}
```

Emitted by whichever module owns the transition — `sim.schedule` for
`PlanPublished`; `sim.airside` for `InboundAirborne`, `Landed`, `OffRunway`,
`OnStand`, `DoorsOpen`, `DoorsClosed`, `Pushback`, `TakeoffRoll`, `Airborne`
(`12-interfaces-airside.md` §12.3 — doors are aircraft envelope, not ground
service, so they stay with the module that owns the aircraft's physical state);
`sim.turnaround` for the remaining ground-service milestones,
`DeboardComplete`, `ReadyToBoard`, `BoardingComplete`. Each is emitted **once
per flight**. `PlannedTick` is the plan as it stood when the *previous*
milestone completed, so replanning is visible rather than retroactively hiding
a delay.

`PlannedTick` is **schedule-anchored and cumulative**: it is derived from the
published schedule plus the nominal (unimpeded) durations of the steps before
it, and is never shifted by the actual lateness of an earlier milestone.
`ActualTick − PlannedTick` is therefore the flight's whole lateness at that
milestone, not the lateness added since the previous one — which is what lets
`sim.delay` measure only at a few checkpoints (`14-interfaces-delay.md`
§14.4). The per-milestone derivations are binding on their emitters:
`12-interfaces-airside.md` §12.3 and `13-interfaces-turnaround.md` §13.6.

---

## 10.5 Allocation rule

For each pair of consecutive milestones, let `gap = ActualTick(n) − PlannedTick(n)`
expressed in `SimMinutes`, minus any gap already attributed at milestone `n−1`
(that part is `late_inbound`/`propagated` and is not double-counted — rule 1).

1. Collect blocking intervals overlapping the milestone window, clipped to it.
2. If the clipped intervals cover the gap, allocate each interval its clipped
   duration.
3. If they overlap each other, allocate each overlapping minute to the interval
   with the **lowest `EventId`** — the one that was blocking first. Concurrent
   causes do not each get the full minute; that is how leaf sums stop matching the
   total.
4. Any residue not covered by any interval is a single leaf with category
   `propagated` and `root_cause` set. An unexplained residue is honest; an invented
   cause is not.
5. Arithmetic: allocate in **integer ticks**, never in `Fx`. Every milestone
   and every interval boundary is a tick, so the leaves sum to the total
   exactly with no rounding step; `SimMinutes` are derived for display only.
   `sum_of_leaves_equals_total` is asserted on ticks. (This replaces the
   earlier "allocate in `Fx`, settle by largest remainder" wording: in ticks
   there is no remainder to settle. If a later source ever reports a
   non-tick-aligned duration, largest remainder with ties by ascending
   `EventId` is the rule to reinstate, by amendment.)

The operational form of these rules — which milestones are measured, what
happens when blocking exceeds the gap, and what happens when a flight makes up
time — is `14-interfaces-delay.md` §14.4–§14.6.

> **LOW CONFIDENCE — rule 3, first-blocker-wins.** It is exact, deterministic and
> cheap, and it makes the tree readable. It will also under-report a genuine second
> cause that started later and would have delayed the flight anyway. The
> alternative, proportional splitting of concurrent causes, is more "fair" and much
> harder for a player to read. Flagged for the human owner; changing it later
> changes tree contents but no interface.

---

## 10.6 The catalogue

`Phase` is the earliest build phase in which the event must exist. Emitters may
not emit an event before its module exists; `sim.delay` ignores events it has no
rule for.

### From `sim.schedule`

| Event | Fields | Delay category | Phase |
|---|---|---|---|
| `FlightPlanPublished` | `FlightId`, `MovementKind kind`, `FlightId rotation`, `bool hasRotation`, `AirlineId`, `ContentId aircraftType`, `Tick schedArr`, `Tick schedDep`, `SimMinutes minTurnaround` | — (baseline) | 0 |
| `FlightPlanRevised` | `FlightId`, `Tick newSchedDep`, reason key | — | 1 |
| `FlightCancelled` | `FlightId`, reason key | — | 1 |

### From `sim.airside`

| Event | Fields | Delay category | Phase |
|---|---|---|---|
| `AircraftHeldForRunway` / `Released` | `FlightId`, `RunwayId`, `int queuePosition` | `runway_congestion` | 1 |
| `AircraftHeldOnTaxiway` / `Released` | `FlightId`, `TaxiEdgeId`, blocking `FlightId?` | `taxi_congestion` | 1 |
| `StandUnavailable` / `StandAssigned` | `FlightId`, `StandId?`, occupying `FlightId?` | `stand_unavailable` | 1 |
| `DepartureHeldForPassengers` / `Released` | `FlightId`, `int outstanding`, `NodeId? heldAt` (set on the opening event, null on `Released`) | `passenger_late` | 1 |
| `RunwayDirectionChanged` | `RunwayId`, `int headingDeg`, `SimMinutes settleTime` | `weather` | 2 |
| `RunwayClosed` / `Reopened` | `RunwayId`, reason key | `weather`, `incident` | 2 |
| `DeicingStarted` / `Completed` | `FlightId`, `SimMinutes duration` | `deicing` | 2 |
| `AtcFlowRestrictionApplied` / `Lifted` | `FlightId`, `SimMinutes slotDelay` | `atc_flow` | 2 |

### From `sim.flow`

| Event | Fields | Delay category | Phase |
|---|---|---|---|
| `QueueThresholdExceeded` / `Cleared` | `NodeId`, `Fx waitMinutes`, `int serversOpen`, `int serverCount` | `security_queue`, `immigration_queue` | 0 |
| `FlowBlocked` / `FlowUnblocked` | `CohortId`, `NodeId held`, `NodeId blockedBy` | per node kind | 0 |
| `PassengersArrivedAtGate` | `FlightId`, `int count` | — | 1 |
| `PassengersMissedFlight` | `FlightId`, `int count`, `NodeId lastBlockedAt` | `passenger_late` | 1 |

The category for a queue node comes from the node's content definition, not from
a branch in `sim.delay`. That is how `immigration_queue` exists without
`sim.delay` knowing what immigration is.

`DepartureHeldForPassengers` (from `sim.airside`, above) is how passenger
lateness becomes delay *minutes* at Phase 1 (D6, `12-interfaces-airside.md`
§12.8). Its leaf is `passenger_late`, and `heldAt` names the node where most
of the late passengers were. That node is usually a security queue, whose own
category becomes reachable through `Cause` chains later
(`14-interfaces-delay.md` §14.7).

### From `sim.turnaround`

| Event | Fields | Delay category | Phase |
|---|---|---|---|
| `TurnaroundJobStarted` / `Completed` | `FlightId`, `JobKind`, `Tick plannedStart` | — | 1 |
| `TurnaroundJobBlocked` / `Unblocked` | `FlightId`, `JobKind`, `ResourceKind waitingOn`, `EntityId?`, `DelayCategory category` | `ground_handling`, `fuel`, `catering`, `cleaning`, `loading` | 1 |
| `CrewUnavailable` / `CrewReady` | `FlightId`, reason key | `crew` | 2 |

`JobKind` maps to the delay category through the job's catalogue definition
(`13-interfaces-turnaround.md` §13.4), and the emitter copies it into the
event's `category` field: the catalogue is `sim.turnaround`'s own data, which
`sim.delay` may not read (`06-delay-attribution.md` rule 2). `pushback` as a category is reserved
for the aircraft-side act itself (`sim.airside`'s `Pushback` milestone);
`sim.turnaround`'s `PushbackPrep` job is always `ground_handling` (§13.4), so
`pushback` is removed from this row's category list.

### From `sim.baggage`

| Event | Fields | Delay category | Phase |
|---|---|---|---|
| `BaggageLoadingBlocked` / `Unblocked` | `FlightId`, `NodeId`, `int bagsOutstanding` | `baggage` | 2 |
| `BagsMishandled` | `FlightId`, `int count`, reason key | `baggage` | 2 |

### From `sim.staff`, `sim.policy`, `sim.incident`

| Event | Fields | Delay category | Phase |
|---|---|---|---|
| `StaffShortfallStarted` / `Ended` | `RoleId`, `NodeId?`, `int shortfall` | per the blocked job | 2 |
| `ShiftChangeoverStarted` / `Ended` | `RoleId`, `NodeId?` | per the blocked job | 2 |
| `PolicyConstraintBlocked` / `Released` | `PolicyId`, subject id, constraint key | `policy_constraint` | 2 |
| `IncidentStarted` / `IncidentEnded` | `IncidentId`, `ContentId definition`, affected ids | `incident` | 2 |

Staff and policy events carry no category of their own: they are causes *of* a
blocked job, referenced through `Cause`, and the leaf takes the job's category.
This keeps one delayed minute in one branch (`06-delay-attribution.md` rule 1).

---

## 10.7 Events `sim.delay` emits

Only `DelayEvent`, exactly as specified in `06-delay-attribution.md`, one per
tree node, published when a flight's attribution is finalised and never for a
provisional tree (`14-interfaces-delay.md` §14.8). `sim.delay` publishes no
other event and mutates nothing outside its own tree.
`delay_module_never_writes` is the static check that enforces it.

`FlightPlanPublished`'s `kind`, `rotation` and `hasRotation` fields exist for
`sim.delay`: it may not call `IScheduleSystem.TryGetRotation`, so the rotation
link has to arrive on the bus (`11-interfaces-schedule.md` §11.7).

---

## 10.8 Phase 0 subset

For the feasibility spike, only these must exist: `FlightPlanPublished`,
`FlightMilestoneReached`, `QueueThresholdExceeded`/`Cleared`,
`FlowBlocked`/`FlowUnblocked`. Everything else is defined here so that the shape
is fixed before eight modules invent eight variations of it, not because Phase 0
needs it.
