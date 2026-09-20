# Task queue

Maintained by the Planner. Strictly follows the build order in
`spec/00-overview.md`. Do not reorder.

Below is the Phase 0 and Phase 1 seed. The Planner expands from here.

## Phase 0 — feasibility spike (the kill gate)

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-001 | Headless harness: fixed timestep, no rendering | sim.core | — | QUEUED |
| T-002 | Seeded RNG service with per-system named streams | sim.core | T-001 | QUEUED |
| T-003 | Fixed-point math type `Fx` | sim.core | — | QUEUED |
| T-004 | State hashing + checkpoint reporting | sim.core | T-001, T-003 | QUEUED |
| T-005 | Command queue applied at tick boundaries | sim.core | T-001 | QUEUED |
| T-006 | Determinism gates in CI (same/cross process) | ci | T-004 | QUEUED |
| T-007 | Statistical flow nodes: queue with throughput model | sim.flow | T-003 | QUEUED |
| T-008 | Schedule loader from CSV fixture, 200 movements | sim.schedule | T-001 | QUEUED |
| T-009 | Run 100 sim-days in under 60s, identical across runs | sim.core | T-006, T-007, T-008 | QUEUED |
| T-010 | Cohort→agent promotion + demotion, outcome-neutral | sim.flow | T-007 | QUEUED |
| T-011 | Stress: 30,000 daily passengers within frame budget | sim.flow | T-010 | QUEUED |

**Gate:** if T-011 cannot meet budget, the architecture is redesigned here — not
later. Escalate to the human owner.

## Phase 1 — fun prototype

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-020 | Minimal top-down renderer, flat colours | app.render | T-009 | QUEUED |
| T-021 | One runway, taxiway graph, four contact stands | sim.airside | T-008 | QUEUED |
| T-022 | Turnaround as job list, 4 vehicles, driver assignment | sim.turnaround | T-021 | QUEUED |
| T-023 | Security lanes openable/closable live, visible queues | sim.flow | T-007, T-005 | QUEUED |
| T-024 | Delay clock per flight + naive attribution log | sim.delay | T-022, T-023 | QUEUED |
| T-025 | Playtest build, 20 external testers | — | T-024 | QUEUED |

**Gate: T-025 is a human decision, not an agent one.** One question only: is
unblocking flow fun with no construction at all? If no, kill or pivot. Do not
proceed on hope. No agent may mark this task complete.
