# Task queue

Maintained by the Planner. Strictly follows the build order in
`spec/00-overview.md`. Do not reorder.

Each row's task file is `tasks/T-<nnn>-<slug>.md`. Status values follow
`tasks/README.md`'s lifecycle. `BLOCKED` rows carry an open-question id from
`spec/open-questions.md`; do not release them until that question is
answered and the row is moved back to `QUEUED` with the task file rewritten.

## Phase 0 — feasibility spike (the kill gate)

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-001 | Headless harness: fixed timestep, no rendering | sim.core | — | QUEUED |
| T-002 | Seeded RNG service with per-system named streams | sim.core | T-001 | QUEUED |
| T-003 | Fixed-point math type `Fx` | sim.core | — | QUEUED |
| T-004 | State hashing + checkpoint reporting | sim.core | T-001, T-003 | QUEUED |
| T-005 | Command queue applied at tick boundaries | sim.core | T-001 | QUEUED |
| T-006 | Determinism gates in CI (same/cross process) | tools.simharness | T-004 | QUEUED |
| T-007 | Statistical flow nodes: queue with throughput model | sim.flow | T-003 | QUEUED |
| T-008 | Schedule loader from CSV fixture, 200 movements | sim.schedule | T-001 | QUEUED |
| T-009 | Run 100 sim-days in under 60s, identical across runs | sim.core | T-006, T-007, T-008 | QUEUED |
| T-010 | Cohort→agent promotion + demotion, outcome-neutral | sim.flow | T-007 | QUEUED |
| T-011 | Stress: 30,000 daily passengers within frame budget | sim.flow | T-010 | QUEUED |

**Gate:** if T-011 cannot meet budget, the architecture is redesigned here — not
later. Escalate to the human owner.

### Release order within Phase 0 (respecting shared-path serialisation)

`src/sim/core/**` is a shared write surface across T-001–T-006, so those are
released one at a time in dependency order even where two show no direct
`Depends` edge (e.g. T-003 has no declared dependency but still may not run
concurrently with another sim.core task touching the same files). Order:

1. **T-001** and **T-003** — no shared files, no shared dependency: releasable
   concurrently.
2. **T-002** — after T-001 merges.
3. **T-004** — after T-001 and T-003 merge.
4. **T-005** — after T-001 merges (may run concurrently with T-002/T-004 only
   if file-level review confirms no overlap; default to sequential).
5. **T-007** — after T-003 merges. Different module directory
   (`src/sim/flow/**`) from T-001–T-006, so it may run **concurrently** with
   any still-open `sim.core` task once T-003 itself has merged.
6. **T-006** — after T-004 merges.
7. **T-008** — after T-001 merges. `src/sim/schedule/**` is its own directory,
   so it may run **concurrently** with any open `sim.core`/`sim.flow` task.
   Unblocked by the Architect's answer to Q-004
   (`spec/11-interfaces-schedule.md`); builds and tests standalone, without
   `sim.flow` registered, per that file's §11.6.
8. **T-009** — after T-006, T-007 and T-008 all merge (writes
   `tools/SimHarness/**`, shared with T-006 — do not release concurrently
   with a still-open T-006).
9. **T-010, T-011** — releasable once T-007 merges, in `src/sim/flow/**`
   sequence after T-007 (and, since T-023 also lands in that directory,
   serialised against it too — see Phase 1 below).

## Phase 1 — fun prototype

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-020 | Minimal top-down renderer, flat colours (headless scene layer only) | app.render | T-009, T-010, T-021 | QUEUED |
| T-021 | One runway, taxiway graph, four contact stands | sim.airside | T-008, T-026 | QUEUED |
| T-022 | Turnaround as job list, 4 vehicles, driver assignment | sim.turnaround | T-021, T-026 | QUEUED |
| T-023 | Security lanes openable/closable live, visible queues | sim.flow | T-007, T-005 | QUEUED |
| T-024 | Delay clock per flight + naive attribution log | sim.delay | T-022, T-023, T-026 | QUEUED |
| T-025 | Playtest build, 20 external testers | — | T-024 | BLOCKED (human gate; additionally on the HUMAN DECISIONS of Q-008 §15.13, which block the `app.render` engine backend and therefore any playable build) |
| T-026 | `sim.core`: Phase 1 event payload types (airside/turnaround/delay) | sim.core | T-001 | QUEUED |

**Gate: T-025 is a human decision, not an agent one.** One question only: is
unblocking flow fun with no construction at all? If no, kill or pivot. Do not
proceed on hope. No agent may mark this task complete.

### Phase 1 releasability

**T-023** is releasable now: it depends on T-007 (sim.flow, published
interface) and T-005 (sim.core command queue, published interface), neither
of which is blocked. It writes `src/sim/flow/**`, the same directory as
T-007/T-010/T-011, so it is released only after those three have merged, one
sim.flow task in flight at a time.

Q-004 (`spec/11-interfaces-schedule.md`), Q-005
(`spec/12-interfaces-airside.md`), Q-006 (`spec/13-interfaces-turnaround.md`),
Q-007 (`spec/14-interfaces-delay.md`) and the headless half of Q-008
(`spec/15-interfaces-render.md`) are all answered. Every Phase 1 task through
T-024 is therefore `QUEUED`, not `BLOCKED`, subject to the merge-order and
shared-path rules below. Only **T-025** remains blocked, as a human-only gate
per `spec/00-overview.md` — never agent-completable — additionally on the
HUMAN DECISIONS `spec/15-interfaces-render.md` §15.13 leaves open (Unity vs.
`net8.0`, the engine project shell, the composition root, game speeds, and
the player-facing `SetServersOpen` lever), all of which block the
`app.render` engine backend and so any playable build.

**T-026 must merge before T-021, T-022 or T-024 are released.** Writing
`spec/14-interfaces-delay.md` (Q-007) surfaced a scheduling gap: event
payload types (`RunwayId`, `TaxiEdgeId`, `StandId`, `TaxiNodeId`, `JobKind`,
`VehicleKind`, `JobStatus`, `ResourceKind`, `DelayCategory`, `DelayEventId`,
`DelaySource`, `DelayExplanation`) have to live in `src/sim/core/**`
(`03-module-map.md`: events are defined in `sim.core`), but T-021 and T-022
may write only their own module directories, so nobody was scheduled to
author them there. `03-module-map.md`/`10-events.md` cover the event
catalogue's `sim.core` ownership, so this is traceable to a spec section —
not a new open question — and `tasks/T-026-core-event-payload-types.md`
sequences the work. It writes only `src/sim/core/**` and depends only on
T-001, so it is releasable immediately and should be prioritised ahead of
T-021.

T-022's dependency on T-021 is an ordinary merge-order dependency: T-022 must
build against a `sim.airside` that already includes T-021 (it exercises the
real `sim.airside`↔`sim.turnaround` handshake, `spec/12-interfaces-airside.md`
§12.8, rather than T-021's own no-`sim.turnaround` fallback), so do not
release T-022 until T-021 has merged, even though both show `QUEUED`.

**Note on a same-cycle correction (Q-006):** writing `spec/13-interfaces-turnaround.md`
surfaced a real gap in `spec/12-interfaces-airside.md` (an arrival and its
departure are separate `FlightId`s, so the nine airside/turnaround milestones
split across two tracks, not one — see that file's changelog entry). The
Architect amended `12-interfaces-airside.md` §12.3/§12.7/§12.8/§12.11 before
either branch merged, and T-021's task file has been updated to match. If
T-021 is picked up from an older local checkout, re-pull the task file before
starting.

**Note on two further same-cycle amendments (Q-007, Q-008):** before T-021's
own branch merges, it also picked up (a) the `PlannedTick` table added to
`spec/12-interfaces-airside.md` §12.3 by Q-007, and (b) the `Layout()` query
and the `AtNode`-stays-set-during-`OnEdge` clarification added to §12.9 by
Q-008. T-022 separately picked up the `category` field on
`TurnaroundJobBlocked`/`Unblocked` and the `ReadyToBoard`/`BoardingComplete`
`PlannedTick` rules added to `spec/13-interfaces-turnaround.md` §13.6/§13.9
by Q-007. All four task files (T-008, T-021, T-022, T-024) have been
rewritten to match; re-pull them if working from an older local checkout.

**T-020's scope narrowed.** Q-008 answered only the headless half of
`app.render`: the scene layer (`src/app/render/Scene/**`) is fully specified
and releasable now, with dependencies growing from T-009 alone to T-009,
T-010 and T-021 (it compiles against `SetPromoted`/`AgentsAt`, T-010, and
`IAirsideSystem.Layout()`, T-021). The Unity backend
(`src/app/render/Unity/**`) is **not** taskable yet: it is a contract only
(`spec/15-interfaces-render.md` §15.10) pending the five HUMAN DECISIONS of
§15.13. No task is queued for it, and none should be until those decisions
are made — inventing one now would mean guessing at (a) whether the sim
multi-targets `net8.0`/`netstandard2.1`, (b) who owns the Unity project
shell, (c) the composition root, (d) game speeds, and (e) how a player
issues `SetServersOpen`, none of which the Planner may decide.

### Release order note: T-021 and T-023 both touch `sim.flow`'s consumer side

T-021 calls `IFlowSystem.Inject`/`Absorb` (currently a documented no-op for
`Inject`, per `spec/12-interfaces-airside.md` §12.7) but writes only
`src/sim/airside/**` — no file overlap with T-023's `src/sim/flow/**`. They
may run concurrently once T-021's own dependency (T-008, and now T-026) and
T-023's dependencies (T-007, T-005) are merged. Neither blocks the other.

### Release order note: T-026 gates T-021, T-022 and T-024

T-026 writes only `src/sim/core/**`, so it may run concurrently with any
open `sim.flow`/`sim.airside`/`sim.turnaround`/`sim.delay` task, but per the
Phase 0 rule it does not run concurrently with another open `sim.core` task
(same shared-write-surface reasoning as T-001–T-006). Release it first among
Phase 1 tasks it gates: T-021 and T-022 cannot compile without it, and T-024
needs its `DelayEventId`/`DelaySource`/`DelayExplanation`/`DelayCategory`
types regardless of T-022/T-023's own status.

## Planner scope note

This queue is expanded only through T-025 per the current planning request,
plus T-026, added this cycle to close the `sim.core` payload-type scheduling
gap Q-007 surfaced — it is infrastructure for the already-planned T-021/
T-022/T-024 chain, not a scope expansion. No task past T-025 is planned;
Phase 2 (build order items 7–12: money,
staff, policy/reputation, incidents, progression, tutorial) is out of scope
until the human owner clears the Phase 1 gate.
