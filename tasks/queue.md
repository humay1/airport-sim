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
| T-020 | Minimal top-down renderer, flat colours | app.render | T-009 | BLOCKED (Q-008) |
| T-021 | One runway, taxiway graph, four contact stands | sim.airside | T-008 | QUEUED |
| T-022 | Turnaround as job list, 4 vehicles, driver assignment | sim.turnaround | T-021 | BLOCKED (Q-006) |
| T-023 | Security lanes openable/closable live, visible queues | sim.flow | T-007, T-005 | QUEUED |
| T-024 | Delay clock per flight + naive attribution log | sim.delay | T-022, T-023 | BLOCKED (Q-007, and Q-006 via T-022) |
| T-025 | Playtest build, 20 external testers | — | T-024 | BLOCKED (human gate; via T-024) |

**Gate: T-025 is a human decision, not an agent one.** One question only: is
unblocking flow fun with no construction at all? If no, kill or pivot. Do not
proceed on hope. No agent may mark this task complete.

### Phase 1 releasability

**T-023** is releasable now: it depends on T-007 (sim.flow, published
interface) and T-005 (sim.core command queue, published interface), neither
of which is blocked. It writes `src/sim/flow/**`, the same directory as
T-007/T-010/T-011, so it is released only after those three have merged, one
sim.flow task in flight at a time.

Q-004 is answered (`spec/11-interfaces-schedule.md`) and Q-005 is answered
(`spec/12-interfaces-airside.md`), so T-008, T-009 and T-021 no longer block
the chain. What remains `BLOCKED`:

- **T-020** on Q-008 (`app.render` interface/scope — still open).
- **T-022** on Q-006 (`sim.turnaround` interface — still open).
- **T-024** on Q-007 (`sim.delay` interface — still open), and transitively
  on T-022 (via Q-006).
- **T-025** is additionally a human-only gate per `spec/00-overview.md` and
  is never agent-completable regardless of blocks clearing.

T-021's dependency on T-008 is no longer a blocker — T-008 is `QUEUED` — and
Q-005 is answered, so T-021 itself is `QUEUED`. T-022's dependency on T-021 is
now an ordinary merge-order dependency, not a compounding block; Q-006 alone
still stops it. `spec/12-interfaces-airside.md` §12.8 already constrains part
of Q-006's eventual answer: `sim.turnaround` must emit
`FlightMilestoneReached` for exactly `DeboardComplete`, `ReadyToBoard`,
`BoardingComplete`, and `sim.airside` reacts to `BoardingComplete` specifically
to close the aircraft doors.

### Release order note: T-021 and T-023 both touch `sim.flow`'s consumer side

T-021 calls `IFlowSystem.Inject`/`Absorb` (currently a documented no-op for
`Inject`, per `spec/12-interfaces-airside.md` §12.7) but writes only
`src/sim/airside/**` — no file overlap with T-023's `src/sim/flow/**`. They
may run concurrently once T-021's own dependency (T-008) and T-023's
dependencies (T-007, T-005) are merged. Neither blocks the other.

## Planner scope note

This queue is expanded only through T-025 per the current planning request.
No task past T-025 is planned; Phase 2 (build order items 7–12: money,
staff, policy/reputation, incidents, progression, tutorial) is out of scope
until the human owner clears the Phase 1 gate.
