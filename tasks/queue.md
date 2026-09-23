# Task queue

Maintained by the Planner. Strictly follows the build order in
`spec/00-overview.md`. Do not reorder.

Each row's task file is `tasks/T-<nnn>-<slug>.md`. Status values follow
`tasks/README.md`'s lifecycle. `BLOCKED` rows carry an open-question id from
`spec/open-questions.md`; do not release them until that question is
answered and the row is moved back to `QUEUED` with the task file rewritten.
Numbering follows `tasks/README.md`'s rule: one id block per build-order
phase (Phase 0: `T-001`–`T-019`; Phase 1: `T-020` onward).

**As of this cycle, `spec/open-questions.md` has no open question.** Q-002
through Q-012 are all answered, and D1–D9 resolved every HUMAN DECISION
Q-007/Q-008 left open. No row below is `BLOCKED` on a spec gap.

## Phase 0 — feasibility spike (the kill gate)

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-001 | Headless harness: fixed timestep, no rendering | sim.core | — | QUEUED |
| T-002 | Seeded RNG service with per-system named streams | sim.core | T-001 | QUEUED |
| T-003 | Fixed-point math type `Fx` | sim.core | — | QUEUED |
| T-004 | State hashing + checkpoint reporting | sim.core | T-001, T-003 | QUEUED |
| T-005 | Command queue applied at tick boundaries | sim.core | T-001 | QUEUED |
| T-006 | Determinism gates in CI (same/cross process) | tools.simharness | T-004 | QUEUED |
| T-007 | Statistical flow nodes: queue with throughput model | sim.flow | T-003, T-012 | QUEUED |
| T-008 | Schedule loader from CSV fixture, 200 movements | sim.schedule | T-001 | QUEUED |
| T-009 | Run 100 sim-days in under 60s, identical across runs | sim.core | T-006, T-007, T-008, T-012 | QUEUED |
| T-010 | Cohort→agent promotion + demotion, outcome-neutral | sim.flow | T-007 | QUEUED |
| T-011 | Stress: 30,000 daily passengers within frame budget | sim.flow | T-010 | QUEUED |
| T-012 | `sim.world`: fixed landside walk graph | sim.world | T-001, T-003 | QUEUED |
| T-013 | Soak fixture + golden, mid-tier, under 0.1 ms/tick | tools.simharness | T-009 | QUEUED |

**Gate:** if T-011 cannot meet budget, the architecture is redesigned here — not
later. Escalate to the human owner.

### Release order within Phase 0 (respecting shared-path serialisation)

`src/sim/core/**` is a shared write surface across T-001–T-006 and T-026
(the `sim.core` payload-type task, Phase 1-numbered but same directory), so
those are released one at a time in dependency order even where two show no
direct `Depends` edge. Order:

1. **T-001** and **T-003** — no shared files, no shared dependency: releasable
   concurrently.
2. **T-002** — after T-001 merges.
3. **T-004** — after T-001 and T-003 merge.
4. **T-005** — after T-001 merges (may run concurrently with T-002/T-004 only
   if file-level review confirms no overlap; default to sequential). T-005 now
   also authors `PlayerId`/`CommandKind`/`ICommandHandler` (Q-010; the Planner
   folded this in rather than opening a new task), so it should not run
   concurrently with T-026 either, since both touch `src/sim/core/**`.
5. **T-026** — after T-001 merges; sequential against every other open
   `sim.core` task (T-002/T-004/T-005), same reasoning as those. It now also
   authors `NodeId`/`EdgeId` (Q-012) and the content definition types
   (Q-011), so it must merge **before** T-007, T-012, T-021, T-022, T-024 and
   T-027, not only T-021/T-022/T-024 as originally scoped.
6. **T-012** (`sim.world`) — after T-001 and T-003 merge, and after T-026
   merges (needs `NodeId`/`EdgeId`). Own directory (`src/sim/world/**`), so
   it may run concurrently with any other open `sim.core` task once its own
   dependencies are met. **T-012 must merge before T-007** — see the Q-012
   note below.
7. **T-007** — after T-003, T-012 and T-026 all merge. Different module
   directory (`src/sim/flow/**`), so it may run **concurrently** with any
   still-open `sim.core` task once its own dependencies are met.
8. **T-006** — after T-004 merges.
9. **T-008** — after T-001 merges. `src/sim/schedule/**` is its own directory,
   so it may run **concurrently** with any open `sim.core`/`sim.flow`/`sim.world`
   task. Builds and tests standalone, without `sim.flow` registered, per
   `11-interfaces-schedule.md` §11.6.
10. **T-009** — after T-006, T-007, T-008 and T-012 all merge (writes
    `tools/SimHarness/**`, shared with T-006 — do not release concurrently
    with a still-open T-006).
11. **T-010, T-011** — releasable once T-007 merges, in `src/sim/flow/**`
    sequence after T-007 (and, since T-023 also lands in that directory,
    serialised against it too — see Phase 1 below).
12. **T-013** (soak fixture) — after T-009 merges. Writes `tools/SimHarness/**`,
    shared with T-006/T-030 — do not release concurrently with either.

### Q-012 resolved: `sim.world` gates the kill-gate chain

`sim.flow` could not route without a walk graph it did not own
(`open-questions.md`, formerly Q-012). It is now answered by
`spec/18-interfaces-world.md`: a fixed, load-time-only `sim.world` subset
(`IWorldSystem.CanReach`/`CanReachVia`/`PathVia`, registry position 1).
**T-012 is a new Phase 0 task and must land before T-007** — T-007's
routing code (`09-interfaces-flow.md` §9.6, rewritten) compiles against
`IWorldSystem` and no longer computes or owns any part of the walk graph.
T-007 is **not** `BLOCKED`; it simply now depends on T-012 as well as T-003.
T-009, T-010, T-011 and T-023 pick up the same dependency transitively
through T-007, and their task files are updated for the routing rewrite,
but none of them needed a status change.

### Q-011 resolved: content types and loader

`open-questions.md`'s former Q-011 (content definition types and the
`data/` loader) is answered by `08-interfaces-core.md` §8.11: the
definition types (`ContentId`, `ContentKind`, `IContentDefinition` and its
four Phase 0/1 implementations) and a strict, hand-written, package-free
JSON loader. The Planner split this two ways by sizing, not by spec
mandate: the **types** are folded into T-026 (same "types only, no logic"
shape that task already has), and the **loader** — real parsing logic — is
its own task, **T-027**, in the Phase 1 numbering block (it is needed for
`app.host`'s real content, not for the Phase 0 kill gate, which never
touches real JSON content). A **content task, T-028**, writes the four
schemas plus ordinary (non-balance) `size_categories`/`aircraft` data;
`pax_profiles`/`queue_profiles` *values* stay human-only, per
`04-data-schemas.md` — T-028 writes only their schemas, never their data.

## Phase 1 — fun prototype

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-020 | Minimal top-down renderer, flat colours (headless scene layer only) | app.render | T-009, T-010, T-021, T-023 | QUEUED |
| T-021 | One runway, taxiway graph, four contact stands, boarding hold | sim.airside | T-008, T-026 | QUEUED |
| T-022 | Turnaround as job list, 4 vehicles, driver assignment | sim.turnaround | T-021, T-026 | QUEUED |
| T-023 | Security lanes live; `TryGetOutstanding`/`TryGetLaneState` | sim.flow | T-007, T-005 | QUEUED |
| T-024 | Delay clock per flight + naive attribution log | sim.delay | T-022, T-023, T-026 | QUEUED |
| T-025 | Playtest build, 20 external testers | — | T-024, T-031, T-032, T-033, T-034 | BLOCKED (human gate — never agent-completable) |
| T-026 | `sim.core`: Phase 1 payload types (airside/turnaround/delay/world/content) | sim.core | T-001 | QUEUED |
| T-027 | `sim.core`: strict content loader | sim.core | T-001, T-003, T-026 | QUEUED |
| T-028 | Content: Phase 0/1 schemas, size-category and aircraft data | content | — | QUEUED |
| T-029 | `app.ui` scene layer: pacing, lane click, production lane sink | app.ui | T-020, T-023, T-005 | QUEUED |
| T-030 | `tools.simharness`: `checkpoints` subcommand | tools.simharness | T-004, T-006 | QUEUED |
| T-031 | `app.host`: headless composition root, frame loop, checkpoint run | app.host | T-008, T-012, T-020, T-021, T-022, T-024, T-026, T-027, T-029, T-030 | QUEUED |
| T-032 | `app.render` Unity backend | app.render | T-020, T-031 | QUEUED |
| T-033 | `app.ui` Unity backend | app.ui | T-029, T-031 | QUEUED |
| T-034 | `app.host`: Unity project shell, bootstrap, playtest bundle | app.host | T-027, T-028, T-031, T-032, T-033 | QUEUED |
| T-035 | Cross-runtime determinism gate: buildable pieces | tools.simharness / app.host | T-030, T-031, T-034 | QUEUED — **OPTIONAL**, gates nothing, adoption PENDING HUMAN |

**Gate: T-025 is a human decision, not an agent one.** One question only: is
unblocking flow fun with no construction at all? If no, kill or pivot. Do not
proceed on hope. No agent may mark this task complete.

### Phase 1 releasability

Every Phase 0/1 spec question is answered (Q-002 through Q-012), including
all nine owner decisions (D1–D9). Every row above is `QUEUED`, subject only
to the ordinary merge-order and shared-path rules below — **T-025 excepted**,
which is a permanent human-only gate regardless of what merges beneath it.

**T-026 must merge before T-007, T-012, T-021, T-022, T-024 and T-027.**
It is the established place (from the earlier Q-007 scheduling gap) for
`sim.core` payload types nobody else is scheduled to write, and it has been
extended twice more this cycle for the same reason: `NodeId`/`EdgeId`
(Q-012, needed by T-007/T-012) and the content definition types (Q-011,
needed by T-027). It writes only `src/sim/core/**` and depends only on
T-001, so it is releasable immediately and should be prioritised ahead of
everything it gates.

**T-012 must merge before T-007, T-009, T-010, T-011 and T-023** (the last
four transitively, through T-007). See the Q-012 note in the Phase 0
section above.

T-022's dependency on T-021 is an ordinary merge-order dependency: T-022 must
build against a `sim.airside` that already includes T-021 (it exercises the
real `sim.airside`↔`sim.turnaround` handshake, `spec/12-interfaces-airside.md`
§12.8, rather than T-021's own no-`sim.turnaround` fallback), so do not
release T-022 until T-021 has merged, even though both show `QUEUED`.

**T-021 does not depend on T-023 merging**, even though its D6 boarding
hold calls `IFlowSystem.TryGetOutstanding` (T-023's addition): T-021's own
tests use a fake `IFlowSystem` for the hold, per `12-interfaces-airside.md`
§12.13's explicit framing ("run with `sim.flow` registered, or with a fake
`IFlowSystem`"). The Planner assigned the hold's tests to T-021, not to a
separate task, since it is the same module directory and the same kind of
work T-021 already does.

**T-023 now also carries `TryGetOutstanding` and `TryGetLaneState`** (D6
and Q-010 item 5) — the Planner's sizing choice, since both are small
read-only additions to the `IFlowSystem`/`src/sim/flow/**` surface T-023
already owns, rather than a new flow task. T-020 (lane pips) and T-021 (the
hold, via a fake in its own tests) both read the published shape of these
queries from T-023's task file, but only T-020 has a hard merge-order
dependency on T-023 (it calls `TryGetLaneState` for real in its own
integration test).

**T-020's dependency list grew again**, from {T-009, T-010, T-021} to also
include **T-023** (`TryGetLaneState` for lane pips, Q-010). Its own Unity
backend is now taskable as **T-032**, since every `15-interfaces-render.md`
§15.13 HUMAN DECISION has been made (D1, D4, D5, D7) — there is no more
"Unity backend not taskable" note; that note from the previous cycle is
stale and is withdrawn by this rewrite.

**New Phase 1 chain for a playable build (T-027–T-035):** D7 created
`app.host`, and D5 created a minimal `app.ui`; neither had a task before
this cycle. The Planner queues, in dependency order: T-027 (content loader),
T-028 (schemas/content, independent), T-029 (`app.ui` scene layer, after
T-020/T-023), T-030 (harness `checkpoints` subcommand, after T-004/T-006),
T-031 (`app.host` headless composition, after every Phase 1 module factory
plus T-027/T-029/T-030), T-032/T-033 (the two engine backends, after their
scene layers and T-031), T-034 (the Unity project shell, after everything
else, including content). T-035 (the buildable pieces of the proposed
`determinism_cross_runtime` gate, `16-interfaces-host.md` §16.9) is marked
**optional** — adoption is `PENDING HUMAN` because it needs a row in the
locked `02-determinism.md` gate table and a `ci/` step, both human-only.
Nothing else depends on T-035, and it does not gate T-025.

### Release order note: T-021 and T-023 both touch `sim.flow`'s consumer side

T-021 calls `IFlowSystem.Inject`/`Absorb`/`TryGetOutstanding` but writes
only `src/sim/airside/**` — no file overlap with T-023's `src/sim/flow/**`.
They may run concurrently once T-021's own dependencies (T-008, T-026) and
T-023's dependencies (T-007, T-005) are merged. Neither blocks the other.

### Release order note: `sim.core` shared surface across Phase 0 and Phase 1

`src/sim/core/**` is written by T-001–T-006 (Phase 0) and T-026/T-027
(Phase 1-numbered, same directory). All of them serialise against each
other, regardless of numbering block — the numbering rule
(`tasks/README.md`) governs id assignment, not release concurrency.

## Planner scope note

This queue is expanded through T-035 as of this cycle: T-012, T-013 close
the Q-012/soak gaps in Phase 0; T-026 (extended) and T-027–T-035 close the
Q-011/Q-010/D6/D7 gaps in Phase 1, delivering the chain to a playable
build. Every new task is infrastructure for, or a direct consequence of,
already-decided scope (D1–D9, Q-011, Q-012) — none of it is a fresh scope
expansion the Planner introduced on its own. Phase 2 (build order items
7–12: money, staff, policy/reputation, incidents, progression, tutorial)
remains out of scope until the human owner clears the Phase 1 gate (T-025).
