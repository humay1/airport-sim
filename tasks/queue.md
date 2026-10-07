# Task queue

Maintained by the Planner. Strictly follows the build order in
`spec/00-overview.md`. Do not reorder.

Each row's task file is `tasks/T-<nnn>-<slug>.md`. Status values follow
`tasks/README.md`'s lifecycle. `BLOCKED` rows carry an open-question id from
`spec/open-questions.md`; do not release them until that question is
answered and the row is moved back to `QUEUED` with the task file rewritten.
Numbering follows `tasks/README.md`'s rule: one id block per build-order
phase (Phase 0: `T-001`–`T-019`; Phase 1: `T-020` onward).

**As of this cycle, `spec/open-questions.md` has no open question blocking a
task file rewrite.** Q-002 through Q-034 are all answered, and D1–D10
resolved every HUMAN DECISION Q-007/Q-008/Q-034 left open. Q-035 (the
EventBus allocation fix) was open earlier this cycle and is now **ANSWERED
and MERGED** (spec PR #45, commit `f8721c7`) — see the T-036 row and the
Q-035 note below. No row below is `BLOCKED` on a spec gap.

**D10/Q-034 (player-adjustable graphics quality) applied this cycle:** the
owner's decision (CHANGELOG.md, 2026-09-27) adds a settings panel and a
`GraphicsSettings` mechanism, presentation-only, to five already-queued
Phase 1 tasks. T-020 (render scene): `Build`/`Update` take
`GraphicsSettings`, `RenderFrame.Graphics`, `RenderFactory.GraphicsForPreset`/
`ValidateGraphics`, render-driven promotion gated on `DrawAgents`, and six
new tests. T-029 (UI scene): three new `UiInputKind` values,
`SettingsOpen`/`Graphics` state and the modal-panel rule, the preference
codec, and seven new tests. T-031 (headless host): an `IPreferenceStore`
parameter on `IPresentationComposer.Compose`, frame-loop steps 2/4/5 now
pass and persist `Ui.Graphics`, a `Medium` default, and four new tests.
T-032 (render Unity backend): applies the three backend knobs and a
draw-call bound. T-033 (UI Unity backend): draws the settings icon and
panel, the first player-visible text (`LocalisedKey`s). None of these five
tasks' dependency lists changed — this is additive scope inside directories
each task already owns. T-025 (playtest) gains a done-condition: measure
the `Low` preset on a minimum-spec machine (60 fps, ≤ 2 GB) and record it in
`CHANGELOG.md`. The `Low`/`Medium` preset values and the 2 GB memory budget
stay LOW CONFIDENCE, the owner's to revise; no task file states them as
final.

**Q-035 (EventBus zero-allocation fix, MERGED, spec PR #45, `f8721c7`)
applied this cycle:** `08` §8.6 "Allocation" binds `IEventBus` to allocate
nothing after `Build`. T-007's own budget test (no allocation in the update
path) was unreachable while the bus it publishes through allocated, so a
new task, **T-036** (`sim.core`, `src/sim/core/EventBus.cs`/`Channel.cs`
only, depends on T-001, five named tests), closed the gap. T-007 depended
on T-036 as well, with no code change of its own. **Both are now MERGED**
(T-036 PR #47, T-007 PR #48) — status correction, this cycle; the previous
rows here were stale. See the T-036/T-007 rows and the release-order entry
below.

**Q-035 flake fix, this cycle:** T-036's own zero-allocation tests (and the
pre-existing `BudgetTests`/`RandomServiceTests`) flaked under `GC.GetAlloc-
atedBytesForCurrentThread` — a spurious non-zero delta when the runtime
retires a partly used allocation context mid-window, not a real allocation.
The CI path guard scopes a `test-author/T-036-*` branch to T-036's own
writable paths (`src/sim/core/EventBus.cs`/`Channel.cs` only, no `tests/`),
so the fix needs its own task: **T-037** (`tests/sim/core/**`,
`tests/sim/world/**` only, depends on T-036 merged), a shared
`Allocation.Start()`/`Since()` meter that forces a full GC before the
baseline read. Assertions stay exactly `0`, no tolerance. See the T-037 row.
**T-037 is now MERGED** (PR #51, `7bb30b5`) — status correction, this
cycle; the previous "not yet merged" text here was stale.

**T-038, this cycle:** `tests/sim/flow/**` (arrived with T-007, merged)
carries three of its own zero-allocation assertions
(`BoardingTests.cs`/`FlowBudgetTests.cs`/`OutstandingCostTests.cs`) reading
`GC.GetAllocatedBytesForCurrentThread` directly, outside T-037's writable
paths, so they get the same flake and their own task, **T-038**
(`tests/sim/flow/**` only, depends on T-037 and T-007). **Release order,
binding:** T-037 has merged; **T-010 has not** — T-010's worker and Test
Author branches, and the T-011 Test Author's branch, are all currently
writing `tests/sim/flow/**`, the same directory this task writes.
Releasing T-038 before T-010 actually merges risks clobbering or
conflicting with their tests. See the T-038 row and its own worker notes.
(historical, resolved: T-037 #51, T-010 #56, T-011 #59 and T-038 #60 are all merged)

**T-011, this cycle:** its Test Author's PR (`test-author/T-011-stress-30k-
tests`, `a885923`, `tests/sim/flow/StressDay.cs`/`FlowStressBudgetTests.cs`)
hit the same path-guard failure PR #49 hit — a `test-author/T-011-*` branch
is checked against T-011's own writable paths, which were `src/sim/flow/**`
only, so `tests/sim/flow/**` is added back to T-011's writable paths
(`ci/check-paths.sh`'s `protected_for_role` already blocks a *worker* from
`tests/`, so this does not reopen Q-021). T-011's "Spec source" line was
also wrong: `spec/00-overview.md` carries no kill-gate text at all — the
kill-gate framing is this file's own "Phase 0 — feasibility spike (the kill
gate)" heading and **Gate:** note below, not a spec section; T-011's task
file is corrected to cite `tasks/queue.md`, not `00-overview.md`. The tests
pass the budget against `main` today (mean `0.50`–`0.63` ms, p99
`2.60`–`3.26` ms against `2.5`/`5.0`, `0` bytes allocated, peak `252` live
cohorts), but merge only after **T-010** merges, same reasoning as T-038 —
`tests/sim/flow/**` is shared with T-010's still-open branches. A worker is
needed only if the budget fails once T-010's code lands. (historical, resolved: T-010 merged #56, T-011 merged #59, no worker was needed)

**Q-040 (a), this cycle, spec PR #55 (merged, `3a6ba7c`):** at Phase 0/1
there are no arriving or transferring passengers, so `Inject` must reject
`key.Direction ≠ Departing` as `09` §9.7's check 4, appended after
unknown-`at`/not-a-`Source`/`count<=0`. Merged T-007 code
(`src/sim/flow/FlowSystem.cs` `Inject`) implements only the first three
checks. New task **T-039** (`src/sim/flow/**` only, depends on **T-010**,
PR #56, open) closes the gap; the Test Author's own test is
`test_inject_rejects_non_departing_direction` (`09` §9.7). **T-039 must not
release before T-010 merges** — T-010's own PR also edits
`FlowSystem.cs`. See the T-039 row and its release-order entry. (historical, resolved: T-010 merged #56, T-039 merged #63)

**Architect batch 2–5 (Q-016–Q-022, commit `db78df0` on
`architect/Q-013-solution-layout`, not yet on `main`) applied this cycle:** (historical, resolved: merged as PR #13)
Q-016 (what counts as "green" before T-006 merges) is now a **HUMAN
DECISION, owner, 2026-09-24**, not pending: until T-006 merges, green =
`ci/run-checks.sh`'s `path-guard` and `build-and-test` (`--fast`) jobs,
building and testing through `AirportSim.sln`; the full script becomes
mandatory the instant T-006 merges. Reflected in the Done-when lines of
every task released before T-006 (T-001, T-002, T-003, T-004, T-005, T-007,
T-008, T-012, T-026, T-027). Q-017 ships `StateHasher`/`CoreHash`/
`Checkpoint.CoreHash` with T-001 (T-004 now only proves the golden vectors
and per-system discipline, authoring no production interface of its own).
Q-018 puts every `10-events.md` §10.9 event struct, plus `FlightMilestone`/
`AirlineId`/`MovementKind`/`CohortId`/`DelayNode`/`DelayNodeKind`, in
`sim.core` via T-026, which also now owns `ContentIndexFactory`
(coordinator decision) and must merge before T-007, T-008, T-012, T-021,
T-022, T-024 and T-027 (T-008 newly added to that list). Q-019 pins the RNG
exact reference and golden vectors for T-002. Q-020 pins command-queue
semantics for T-005, including `ISimHost.CommandLogSince`. Q-021 means
every worker task's writable paths drop their `tests/**` grants (the path
guard already blocks them; granting one was always a no-op) — applied
across every task file, T-001–T-035. Q-022 confirms `ComposedSim.World`
matches T-031's own interface block, already correct there. Full detail is
in each affected task file's own "Scope amendment"/"Correction" notes; this
paragraph is the index, not the source.

**Owner-approved CI change, this cycle:** the human owner approved a
`ci/check-paths.sh` change on `owner/ci-path-guard-test-pairing`: a worker
branch may carry `tests/` files that are byte-identical to its
corresponding test-author branch (the path guard no longer rejects a
worker branch on that basis alone). Merge order for this cycle: architect
→ planner → `owner/ci-path-guard-test-pairing` → T-028 → T-003 → T-001 →
T-002/T-004 → T-005. **Update (Integrator, trunk green):** architect spec,
owner CI change, planner's task edits and T-028 are merged (`main`, all
checks passing). T-003, T-001, T-004, T-002 (#21) and T-005 (#26) are now
all MERGED — **the Phase 0 `sim.core` chain T-001 through T-005 is fully
merged.** T-006 (#31) and T-027 (#37) are now also MERGED, and T-026 is
MERGED. **The Q-016 interim-green rule has ended with T-006's merge:**
green is now the full `ci/run-checks.sh`, not just `path-guard` and
`build-and-test --fast`, for every task released from here on. **The
admin-merge exception for failing `determinism` ended with T-006** — admin
merge is still used (per `merge-policy`), but only because the shared
GitHub account (`humay1`) cannot record its own PR approvals, never again
to wave through a failing `determinism` check.

## Phase 0 — feasibility spike (the kill gate)

| ID | Task | Module | Depends | Status |
|---|---|---|---|---|
| T-001 | Headless harness: fixed timestep, no rendering | sim.core | T-003 | MERGED |
| T-002 | Seeded RNG service with per-system named streams | sim.core | T-001 | MERGED |
| T-003 | Fixed-point math type `Fx` | sim.core | — | MERGED |
| T-004 | State hashing + checkpoint reporting | sim.core | T-001, T-003 | MERGED |
| T-005 | Command queue applied at tick boundaries | sim.core | T-001 | MERGED |
| T-006 | Determinism gates in CI (`tools.simharness` CLI + gates) | tools.simharness | T-004, T-005 | MERGED |
| T-007 | Statistical flow nodes: queue with throughput model | sim.flow | T-003, T-012, T-026, T-036 | MERGED (PR #48) |
| T-008 | Schedule loader from CSV fixture, 200 movements | sim.schedule | T-001, T-003, T-026, T-007 | MERGED (PR #54) |
| T-009 | Run 100 sim-days in under 60s, identical across runs | tools.simharness | T-006, T-007, T-008, T-012 | MERGED (PR #74, `3ec3c59`, 2026-10-01) |
| T-010 | Cohort→agent promotion + demotion, outcome-neutral | sim.flow | T-007 | MERGED (PR #56) |
| T-011 | Stress: 30,000 daily passengers within frame budget | sim.flow | T-010 | MERGED (PR #59) |
| T-012 | `sim.world`: fixed landside walk graph | sim.world | T-001, T-003, T-026 | MERGED |
| T-013 | Soak fixture + golden, mid-tier, under 0.1 ms/tick | tools.simharness | T-009 (merged #74), T-030 (merged #90) | MERGED (PR #94, `4466a26`, 2026-10-02; the golden `tests/golden/soak-500.hashes` was committed by the owner in `c4cbbf4`) |
| T-014 | Harness: `determinism_promotion` promotes the lowest-id `Gate` and draws it every tick (Q-084) | tools.simharness | T-009 (merged #74), T-010 (merged #56), T-030 (merged #90), T-013 (merged #94), T-049 (merged #99) | MERGED (PR #105 with tests #102, 2026-10-03; `19` §19.2d and §19.9, spec PR #95) |
| T-036 | `sim.core`: `IEventBus` zero-allocation fix (Q-035) | sim.core | T-001 | MERGED (PR #47) |
| T-037 | Shared allocation-measurement helper for zero-allocation tests | sim.core / sim.world tests | T-036 | MERGED (PR #51, `7bb30b5`) |
| T-038 | `sim.flow` tests: switch to the forced-GC allocation meter | sim.flow tests | T-037, T-007 | MERGED (PR #60, `e99bc0d`) |
| T-039 | `sim.flow`: `Inject` rejects non-`Departing` direction (Q-040) | sim.flow | T-010 | MERGED (PR #63, `e33570b`) |
| T-040 | Tag six merged tests `Slow` (L11a rule (b)) | sim.flow / sim.schedule tests | — | MERGED (PR #64, `83dd23c`) |
| T-041 | `sim.world` budget test: per-tick mean and p99 (flake fix) | sim.world tests | — | MERGED (PR #80; owner-approved 2026-09-29; test-author, own PR; rewritten 2026-10-01 to the 14 400-sample window and `long` arithmetic of spec PR #68) |
| T-042 | `sim.core` budget tests conform to `03` window and arithmetic (Q-044, Q-045) | sim.core tests | — | MERGED (PR #81) |
| T-043 | `sim.flow` budget tests conform to `03` window, arithmetic and handler timing (drop `Int128`; Q-061 `Apply` in the allocation window; Q-064 handler timers) | sim.flow tests | T-039, T-040, T-023 | MERGED (PR #85) |
| T-044 | `sim.schedule` budget tests conform to `03` window and arithmetic | sim.schedule tests | T-040 | MERGED (PR #82) |
| T-045 | `tools.simharness` `budget --tier max` rounds up to the `03` rule (`19` §19.4) | tools.simharness | T-009 (merged #74), T-013 (merged #94), T-014 (merged #105), T-030 (merged #90) | MERGED (#108, tests #107, 2026-10-04; spec gap closed by Q-058, PR #70: `HarnessGates.BudgetFromSamples`) |
| T-046 | `balance.schema.json` requires `doors_open_delay_minutes` | content | T-028 | DONE by the owner (commit `3a00a78`, 2026-10-01): schema requires both keys, balance file has `doors_open_delay_minutes: 2` |
| T-047 | `sim.core` test: `ApplyDue` of every kind allocates nothing (Q-065, `08` §8.7) | sim.core tests | T-042 | MERGED (PR #86; spec PR #77, `1574534`; ran after T-042, both write `tests/sim/core/**`) |

**T-041 note (2026-09-29) — SUPERSEDED 2026-10-01** by spec PR #68 and the rewritten T-041 (14 400-sample window, nearest-rank p99 rule; both gaps below are closed):  `WorldBudgetTests` failed in CI three times in two days (102 us, 150 us, and a third on `main` after #64) and passed on every rerun, because it asserts one 2000-tick aggregate mean instead of `03`'s statistic (mean <= budget and p99 <= 2x budget over per-tick samples) and logs only on failure. T-041 writes only `tests/sim/world/**` and shares no path with T-009, T-039 or T-040. The budget value (100 us) is unchanged. Two spec gaps are recorded in the task file and go to the Architect, not to the Test Author: the test runs 2000 ticks, not `03`'s one full sim-day (14 400 ticks), and `03` does not define the p99 rank rule.

**T-040 note (2026-09-29):** T-040 adds `[Trait("Category", "Slow")]` to six merged tests named in L11a's CHANGELOG entry, writing only `tests/sim/flow/**` and `tests/sim/schedule/**`. It shares no file with T-039 (a new file in `tests/sim/flow/`) or T-009 (`tests/sim/core/**`, `tools/SimHarness/**`); the conflict risk with T-039 is textual and low. T-009's kill-gate test is Slow by rule (a): its PR needs the manual pre-merge `slow-tests` run green on its head before the Integrator merges.

**Harness writers, one at a time (2026-10-01; SUPERSEDED by the post-#95 sync below, T-030 and T-013 are merged).** T-009, T-013, T-014, T-030, T-048 and T-045 all write `tools/SimHarness/**` and `tests/tools/simharness/**`. Order, binding: T-009 (merged, #74), then T-030, then T-013 and T-014 one at a time (either order, never together), then T-048 (once T-021, T-022, T-024 and T-030 have merged; never beside another harness writer), then T-045 last. T-045 is last because nothing depends on its rounding (its former spec gap, Q-058, is closed). T-013, T-030 and T-014 tests are all in `tests/tools/simharness/**` (Q-041), so `tests/sim/core/**` is free for T-042. T-047 (Q-065 allocation test) writes `tests/sim/core/**` too, so it follows T-042.

**Post-spec sync (2026-10-01), spec PRs #66, #67 and #68.**

- **T-009 (#66, `6c4509c`).** Its tests move to `tests/tools/simharness/**` (`19` §19.6 Q-041); one Phase 0 composition, world (1), schedule (2), a boarding stand-in (3) that absorbs at `NodeId(9)` at STD and hashes `0` (owner accepted 2026-09-29), flow (4), built from Test Author fixtures including `tests/fixtures/harness/phase0-content.files`, never from `data/`. The kill gate is a whole-run wall-clock check under 60 s (§19.6(a)). The seven `EmptyCompositionFinalHash` expectations in `HarnessCliTests` become the kit's `FinalHash`.
- **#68 (`7c9a373`) budget statistic.** `03` "Budget tests: window and arithmetic": exactly 14 400 consecutive per-tick samples after warm-up, non-overlapping windows, not day-aligned, `long`-only `u = min(ceil(d x 10^6 / f), B x n + 1)`, mean passes iff `Σu <= B x n`, p99 `= u[(99n + 99) / 100 - 1]` passes iff `<= 2B`. Non-conforming tests and their tasks: `sim.world` WorldBudgetTests (T-041, rewritten), `sim.airside` AirsideBudgetTests (T-021, requirement written in), `sim.core` BudgetTests x2 and CommandQueueTests x1 (**T-042**), `sim.flow` FlowBudgetTests, FlowStressBudgetTests (`Int128`) and PromotionBudgetTests (**T-043**), `sim.schedule` BudgetTests x2 (**T-044**), the harness's own `budget` subcommand (**T-045**).
- **Release order for the budget rewrites.** T-041 and T-042 write disjoint paths and have no open dependency, so they may run now. T-043 waits for T-039, T-040 and T-023, which all write `tests/sim/flow/**` (historical, resolved: merged #63, #64, #71). T-044 waits for T-040 (`tests/sim/schedule/**`) (historical, resolved: merged #64). T-045 waits for T-009 (merged #74), T-013, T-014 and T-030 (its seam, `HarnessGates.BudgetFromSamples`, is specified by Q-058). Each task carries its own L11a Slow-tag consideration; the `sim.flow` window at 2.5 ms is about 36 s, so Slow is expected there.
- **T-041 and Slow.** One 14 400-tick window plus a warm-up of at most 14 400 ticks is under L11a rule (a)'s 144 000. At the 100 us budget a window is about 1.44 s, so about 2.9 s with the warm-up, under rule (b)'s 5 s. It starts untagged; the Test Author adds `Slow` only if the first CI measurement is over 5 s.
- **T-023 (tests-only).** T-007 (PR #48) implemented the behaviour. T-023 writes `tests/sim/flow/**` only and is done when its ten coverage tests pass on `main`. It shares that path with T-039, T-040 and (later) T-043: release one at a time. (historical, resolved: T-039 #63, T-040 #64 and T-023 #71 are merged; only T-043 remains)
- **T-021 (#67, `8cf445c`).** §12.8a step order S1 to S7, the §12.11 pending list with exact removal, `STAND_WAIT_CAPACITY` 1024, `PENDING_FLIGHTS_CAPACITY` 2048, `LogKey.AirsideReassignStandNoOp = 1` in `src/sim/core/LogKey.cs` (now a T-021 writable path), `DoorsOpenDelayMinutes` in `AirsideRules`, a door delay of 2 min (HUMAN DECISION, owner 2026-09-30), the least-queue runway stopgap the owner accepted, 17 new §12.13 tests and the fixture `tests/fixtures/airside/phase1-single-runway.json`. `src/sim/core/**` is a shared surface: do not release T-021 beside another task that writes it.
- **T-046 (closed 2026-10-01).** The owner committed the schema change and `doors_open_delay_minutes: 2` together in `3a00a78`. T-031 can load `airside_rules.json`.
- **Post-#70 sync (2026-10-01).** Spec PR #70 (`b00adcb`) closes Q-057 (`soak` in `19` §19.2b and §19.3, unblocking T-013), Q-058 (`HarnessGates.BudgetFromSamples`, unblocking T-045) and Q-059 (no `--report`; the owner removed it from `nightly.yml`, so there is no task).

- **Post-#73/#75 sync (2026-10-01), Q-060..Q-064.** (#73 `7d14761`, #75 `33900f2`.) **T-021** cites `12` for Q-060 (taxi `Blocking`), Q-062 (handed-off arrival leaves tracked state, handoff only after `DoorsOpen`, hashed and saved `AircraftTrack.RecordedCause`) and Q-063 (runway `QueuePosition` 1-based, `Released` carries 0); its tests gain six §12.13 tests, an allocation test covering the handlers and `ReassignStand` `Apply` (Q-061), and `AirsideBudgetTests` with handler timers (Q-064). **T-022 and T-024** cite Q-061/Q-064; T-024's budget is LOW CONFIDENCE: `sim.delay`'s 0.40 ms now covers its handlers, and an overrun goes to the owner, not to a rule change. **Folded into T-043** (no new task ids): Q-061 puts the `SetServersOpen` `Apply` inside the lane-switching allocation test's window, and Q-064 adds handler timers to the flow timed tests (`FlowStressBudgetTests`, `SecurityLaneBudgetTests`, `PromotionBudgetTests`, `FlowBudgetTests`). T-043 is already serialised after the other `tests/sim/flow/**` writers (T-039, T-040; T-023 merged), so no two flow test writers run at once. T-009 and T-023 are MERGED.
- **Releasable now (2026-10-01, SUPERSEDED by the 2026-10-02 list below; corrected after T-039 #63 and T-040 #64 merged):** T-030 (T-009 merged; first of the harness writers), T-041 and T-042 (disjoint test paths, no dependency), T-044 (`tests/sim/schedule/**`, T-040 merged) and T-043 (`tests/sim/flow/**`, T-039, T-040, T-023 all merged). T-043 is the only `tests/sim/flow/**` writer left, so nothing else may write that path while it runs. Harness order stays T-030, then T-013 and T-014 one at a time, then T-045 last. **Not yet:** T-013/T-014 until T-030 merges; T-020 until T-021 merges (T-009, T-010, T-023 are done); T-021 until `test-author-t021` finishes the additions above; T-022 after T-021; T-024 after T-022.

- **Post-#83/#84 sync (2026-10-02), Q-066..Q-077.** (#83 `6ad1efa`; #84 `d7de41a` MERGED.) T-041 (#80), T-042 (#81), T-043 (#85), T-044 (#82) and T-047 (#86) are MERGED. **T-030** is synced to `19` §19.2c/§19.8 and stays Phase 0 only (world, schedule, flow). **New T-048** is the Phase 1 harness stage (airside, turnaround, delay), after T-021, T-022, T-024 and T-030, and before T-031. **T-031** gains T-048 as a dependency, submits no command, and moves the D7 test to `tests/integration/` (Q-077, merged #84). **T-034** and **T-035** are synced to `16` §16.3 and §16.9. Harness writers now run: T-030, then T-013/T-014 one at a time, then T-048 (when releasable), T-045 last; none together.
- **Releasable now (2026-10-02, SUPERSEDED by the post-#95 list below; supersedes the 2026-10-01 list):** T-030 (T-009 merged; first harness writer, once its tests are authored). **In progress, not releasable:** T-021 (worker has it implemented locally at 106/110, blocked on Q-078/Q-079 which an Architect is answering in a separate spec PR; its test PR #78 is approved). T-041, T-042, T-043, T-044 and T-047 are done (merged). **Not yet:** T-013/T-014 until T-030 merges; T-045 last; T-048 until T-021, T-022, T-024 and T-030 merge; T-020 until T-021 merges; T-022 after T-021; T-024 after T-022; T-031 until T-048 and the rest merge.

- **Post-#95 sync (2026-10-02), Q-080..Q-085.** (#93 `1154c2a`, #94 T-013, the owner's soak golden `c4cbbf4`, #95 `8f66470` MERGED; #91 T-021 implementation, #96 Q-085 and #78 T-021 tests open.) T-030 (#90) and T-013 (#94) are MERGED. Q-084 adds `NodeKind KindOf(NodeId)` to `09` §9.7 and `19` §19.2d/§19.9 defines the promotion gate (the owner decided to promote the lowest-id `Gate` and call `AgentsAt` every tick in run 2). **New T-049** adds `KindOf` to `sim.flow`; **T-014** is synced to §19.2d/§19.9 and depends on T-049. T-021 is IN PROGRESS (#91) with Q-080..Q-083 applied and Q-085 open. T-022 cites `13` §13.9, §13.4 and the two new §13.11 tests.
- **Harness writers, current order (2026-10-02, supersedes the 2026-10-01 paragraph):** T-009 (merged #74), T-030 (merged #90) and T-013 (merged #94) are done. Remaining, one at a time: **T-014** (after T-049, which is after T-021), then **T-048** (once T-021, T-022, T-024 merge; never beside another harness writer), then **T-045** last (after T-014). T-048 and T-014 are independent of each other, so either may go first if both are releasable.
- **Releasable now (2026-10-02, SUPERSEDED by the 2026-10-04 list below):** **nothing.** Every queued task waits on T-021 (#91) or on a task that does. **Not releasable:** T-049 until T-021 merges (T-021 adds `FakeFlowBase : IFlowSystem` in `tests/sim/airside/AirsideRigs.cs`, which breaks once `KindOf` joins the interface; T-049's Test Author also edits that file), then its Test Author first and its worker after the tests are authored; T-014 until T-049 merges and its tests are authored; T-045 until T-014 merges; T-022 until T-021 merges; T-024 after T-022; T-048 until T-021, T-022, T-024 merge; T-020 until T-021 merges; T-031 until T-048 and the rest merge. **In progress:** T-021 (#91). T-049 and T-021 are serialised, not concurrent.
- **Releasable now (2026-10-04, after #111; supersedes the earlier 2026-10-04 list; main `772de49`):** **nothing new to a worker.** Merged: T-045 (#108, tests #107), the Q-095 airside fixture fix (#109: sinks 901-904 to 9, `AirsideTestKit` `FixtureLayout.Sink` to 9), T-022 (#103, tests #100; five-vehicle fixture `phase1-five-vehicles.json` per Q-088) and T-020 (#111, tests #101). **In progress:** T-024 and T-029 (Test Authors writing tests; T-024's spec gaps, including the `14` §14.14 test-project reach, go to one batched Architect PR). **First-of-module rule:** the T-024 and T-029 workers both edit `AirportSim.sln`, so they must not run concurrently. **Next releasable:** T-048 once T-024 merges (the only harness writer left; T-021, T-022, T-030 and Q-095 are done). **Not yet:** T-031 after T-048 and the rest; T-032/T-033 after their scene layers and T-031; T-034 last.

**Gate:** if T-011 cannot meet budget, the architecture is redesigned here — not
later. Escalate to the human owner.

### Release order within Phase 0 (respecting shared-path serialisation)

**Historical (2026-10-01).** Items 1 to 14 below are the original release gates. Every gate through item 12b has resolved: T-007 (#48), T-008 (#54), T-009 (#74), T-010 (#56), T-011 (#59), T-038 (#60), T-039 (#63) and T-040 (#64) are all merged. Only items 13 and 14 still gate anything, and they are superseded by the "Harness writers" paragraph above (T-030, then T-013 and T-014 one at a time, then T-045). Read the present state from the table, not from "open" or "not yet" wording below.

`src/sim/core/**` is a shared write surface across T-001–T-006 and T-026
(the `sim.core` payload-type task, Phase 1-numbered but same directory), so
those are released one at a time in dependency order even where two show no
direct `Depends` edge. Order:

1. **T-003** — no dependency, releases first. **Correction (systematic
   type-dependency recheck):** T-001 and T-003 are *not* independent —
   `ISimClock.MinutesBetween` returns `SimMinutes`, which is `Fx` (`08`
   §8.2), so T-001 cannot compile without it. T-003 merges **before** T-001,
   with no placeholder `Fx` stub. The earlier "releasable concurrently"
   framing here is withdrawn.
2. **T-001** — after T-003 merges (`Depends on` amended, above).
3. **T-002** — after T-001 merges.
4. **T-004** — after T-001 and T-003 merge (unchanged: already depended on
   both).
5. **T-005** — after T-001 merges (may run concurrently with T-002/T-004 only
   if file-level review confirms no overlap; default to sequential). T-005 now
   also authors `PlayerId`/`CommandKind`/`ICommandHandler` (Q-010; the Planner
   folded this in rather than opening a new task), so it should not run
   concurrently with T-026 either, since both touch `src/sim/core/**`.
6. **T-026** — after T-001 **and T-003** merge (`Depends on` amended, above:
   `PaxProfileDefinition`/`QueueProfileDefinition` are `Fx`-valued, `08`
   §8.11); sequential against every other open `sim.core` task
   (T-002/T-004/T-005), same reasoning as those. It now also authors
   `NodeId`/`EdgeId` (Q-012) and the content definition types (Q-011), so it
   must merge **before** T-007, T-012, T-021, T-022, T-024 and T-027, not
   only T-021/T-022/T-024 as originally scoped.
7. **T-012** (`sim.world`) — after T-001, T-003 **and T-026** all merge
   (`Depends on` amended, above: `NodeId`/`EdgeId` are T-026 types). Own
   directory (`src/sim/world/**`), so it may run concurrently with any
   other open `sim.core` task once its own dependencies are met. **T-012
   must merge before T-007** — see the Q-012 note below. **T-012 is now
   MERGED** (status correction, this cycle — the previous `IN_PROGRESS` row
   was stale).
7a. **T-036** (`sim.core`, Q-035's EventBus zero-allocation fix) — after
   T-001 merges. Touches only `src/sim/core/EventBus.cs`/`Channel.cs`, so it
   serialises against any other open `sim.core` task by the same shared-path
   rule as T-002/T-004/T-005/T-026/T-027, but depends on none of them
   directly. **T-036 must merge before T-007** — its budget test needs a
   non-allocating bus to publish through (see the Q-035 note above). **T-036
   is now MERGED** (PR #47, status correction this cycle).
7b. **T-037** (`tests/sim/core/**`, `tests/sim/world/**` only — the flaky
   zero-allocation measurement fix) — after T-036 merges; touches only
   `tests/`, so it does not serialise against any `sim.core` `src/` task.
   Assigned to a Test Author, not a worker. **T-037 is now MERGED** (PR #51,
   `7bb30b5`, status correction this cycle). T-038 (Phase 1, `sim.flow`
   tests) depends on it — see 12a below.
8. **T-007** — after T-003, T-012, T-026 **and T-036** all merge (`Depends
   on` amended, above: `CohortKey.PaxProfile` is `ContentId`, a T-026 type,
   in addition to the `NodeId`/`EdgeId` it already picks up transitively
   through T-012; and its own budget test needed T-036's fix, Q-035).
   Different module directory (`src/sim/flow/**`), so it may run
   **concurrently** with any still-open `sim.core` task once its own
   dependencies are met. **T-007 is now MERGED** (PR #48, status correction
   this cycle — the previous `IN_PROGRESS` row was stale).
9. **T-006** — after T-004 merges.
10. **T-008** — after T-001, T-003, T-026 **and now T-007** all merge
    (`Depends on` amended, above: `FlightRecord.MinTurnaround` is
    `SimMinutes`/`Fx`, and `FlightRecord.EntryNode`/`AircraftType`/
    `PaxProfile` are `NodeId`/`ContentId`, both T-026 types). **Correction
    (Test Author, found compiling T-008):** the earlier text here said T-008
    "builds and tests standalone, without `sim.flow` registered" and could
    release right after T-026 — that conflated `11-interfaces-schedule.md`
    §11.6's **runtime** claim (this module's state hash is identical with or
    without the injector wired in) with a **compile-time** one. It cannot:
    `AirportSim.Sim.Schedule.csproj` references `AirportSim.Sim.Flow` (`07`
    L2) and this task's own factory takes an `IFlowSystem`, so `sim.schedule`
    cannot compile until `src/sim/flow/**` exists. **T-008 now depends on
    T-007 as well and cannot release before it merges**, on top of
    T-001/T-003/T-026. Once all four are merged, `src/sim/schedule/**` is its
    own directory, so T-008 may then run concurrently with any other open
    `sim.core`/`sim.world` task (not concurrently with a still-open T-007,
    which it now depends on directly).
    **Not yet an edit, pending the Architect:** T-008's writable path
    `tests/fixtures/schedule/**` may need to be dropped from this task if the
    Architect confirms the Test Author, not the worker, authors
    `phase0-200.csv` (`11-interfaces-schedule.md` §11.10) — flagging per the
    team lead's note, not changing the task file until that's confirmed.
11. **T-009** — after T-006, T-007, T-008 and T-012 all merge (writes
    `tools/SimHarness/**`, shared with T-006 — do not release concurrently
    with a still-open T-006).
12. **T-010, T-011** — releasable once T-007 merges, in `src/sim/flow/**`
    sequence after T-007 (and, since T-023 also lands in that directory,
    serialised against it too — see Phase 1 below). **T-011's Test
    Author's tests are done and green against `main`** (see the T-011 note
    above), **but that PR does not merge before T-010** — both write
    `tests/sim/flow/**`, and T-010 is still open. (historical, resolved: T-010 merged #56, T-011 merged #59) T-011's own writable
    paths now include `tests/sim/flow/**` (Q-021's worker restriction is
    unaffected; see T-011's task file), so its Test Author's branch can
    pass the path guard, the same fix T-038 needed for T-036/T-037.
12a. **T-038** (`tests/sim/flow/**` only, the flow flake fix) — **T-037 has
    merged** (PR #51, `7bb30b5`); still waiting on **T-010**. Shares
    `tests/sim/flow/**` with T-010's and T-011's Test Author branches,
    currently open — release only once T-010 has actually merged, not
    merely once it is `QUEUED`/`IN_PROGRESS`, to avoid conflicting with
    tests those branches are still writing. (historical, resolved: T-010 merged #56, T-011 #59, T-038 #60)
12b. **T-039** (`src/sim/flow/**` only, the `Inject` direction-rejection
    fix, Q-040) — after **T-010** merges (PR #56, open as of this cycle,
    also edits `src/sim/flow/FlowSystem.cs`). Release only once T-010 has
    actually merged, same reasoning as T-038/12a, to avoid a conflicting
    edit to the same file. (historical, resolved: T-010 merged #56, T-039 merged #63)
13. **T-013** (soak fixture) — after T-009 merges (historical, resolved: T-009 merged #74; now ordered after T-030, see "Harness writers"). Writes `tools/SimHarness/**`,
    shared with T-006/T-030/T-014 — do not release concurrently with any of
    them.
14. **T-014** (harness promotion gate, Q-033 item e) — after T-009 **and**
    T-010 both merge (historical, resolved: T-009 #74 and T-010 #56 merged; now ordered after T-030, see "Harness writers") (amends `HarnessGates.Promotion` to call
    `IFlowSystem.SetPromoted`, needing both `sim.flow` in the CLI
    composition and real promotion behaviour). Writes `tools/SimHarness/**`,
    shared with T-006/T-013/T-030 — do not release concurrently with any of
    them.

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
| T-020 | Minimal top-down renderer, flat colours (headless scene layer only) | app.render | T-009, T-010, T-021 (merged #91), T-023, T-026 | MERGED (#111, tests #101, 2026-10-04; Q-095 fixture fix #109 merged) |
| T-021 | One runway, taxiway graph, four contact stands, boarding hold | sim.airside | T-003 (merged #18), T-005 (merged #26), T-008 (merged #54), T-026 (merged #29) | MERGED (PR #91 with tests #78, 2026-10-03; Q-085 PR #96 applied). Historical detail: (implementation PR #91; test PR #78 approved, merging via #91; Q-078/Q-079 and Q-080..Q-083 answered, spec PR #93 `1154c2a` merged; Q-085 (tracked bound, PR #96) open) (its allocation test relies on Q-065, merged #77: command application is allocation-free on ticks that complete normally; test PR #78 is open; task file synced to spec PR #67/#68 on 2026-10-01 and to Q-060..Q-064 (PRs #73, #75) the same day; also writes `src/sim/core/LogKey.cs`; tests authored by `test-author-t021`, who must add the six new §12.13 tests, the handler allocation test, `RecordedCause` in track helpers and handler timers in `AirsideBudgetTests` before release) |
| T-022 | Turnaround as job list, five vehicles, driver assignment | sim.turnaround | T-008 (merged #54), T-021 (merged #91), T-026 (merged #29) | MERGED (#103, tests #100, 2026-10-04; fixture `phase1-five-vehicles.json` per Q-088; Q-088 owner decision 2026-10-03: fleet of FIVE vehicles, title superseded, fixture `phase1-five-vehicles.json`). Historical: (synced 2026-10-02 to `13` §13.9 Cause table, §13.4 no zero durations and the two new §13.11 tests, Q-080, spec PR #93; T-021 now merged) |
| T-023 | Security lanes live; `TryGetOutstanding`/`TryGetLaneState` (**tests-only**) | sim.flow tests | T-005, T-007, T-026 | MERGED (PR #71, 2026-10-01) |
| T-024 | Delay clock per flight + naive attribution log | sim.delay | T-022, T-023, T-026 | MERGED (PR #119, merge commit ed6b8b9; tests PR #115; spec PR #117, Q-106..112) |
| T-025 | Playtest build, 20 external testers | — | T-024, T-031, T-032, T-033, T-034, T-050 | BLOCKED (human gate — never agent-completable) |
| T-026 | `sim.core`: Phase 1 payload types (airside/turnaround/delay/world/content) | sim.core | T-001, T-003 | MERGED |
| T-027 | `sim.core`: strict content loader | sim.core | T-001, T-003, T-026 | MERGED |
| T-028 | Content: Phase 0/1 schemas, size-category and aircraft data | content | — | MERGED |
| T-029 | `app.ui` scene layer: pacing, lane click, production lane sink | app.ui | T-005, T-020, T-023, T-026 | MERGED (PR #118; tests PR #113, 2026-10-04; spec PR #116, Q-102..105) |
| T-030 | `tools.simharness`: `checkpoints` subcommand (Phase 0 stage: world, schedule, flow) | tools.simharness | T-004, T-006, T-009 | MERGED (PR #90, 2026-10-02; `19` §19.2c and §19.8, spec PR #83 `6ad1efa`, Q-066..Q-076) |
| T-031 | `app.host`: headless composition root, frame loop, checkpoint run | app.host | T-008, T-012, T-020, T-021, T-022, T-023, T-024, T-026, T-027, T-029, T-030, T-048 (merged #126) | MERGED (#130; tests #127; historical: tests PR #127; all deps merged; synced 2026-10-07 to `16` at main 4d9424b: PascalCase `FrameInput`, `HostFactory.LoadContent`, `Run` failure test per Q-120/§16.11; synced 2026-10-02: D7 test lives in `tests/integration/` per Q-077 (merged #84); its Test Author's grant gains `tests/integration/**` and its `.sln` edit adds that project; synced 2026-10-06, Q-114: its command-line test covers `TryParse` ignoring engine args per `16` §16.8) |
| T-032 | `app.render` Unity backend | app.render | T-020, T-031 | MERGED (#132; historical: rescoped 2026-10-06, Q-114: done-when adds `unity-build` green; writable paths gain one line in `unity/AirportSim/Packages/manifest.json`) |
| T-033 | `app.ui` Unity backend | app.ui | T-029, T-031, T-050 | IN PROGRESS (PR #131 approved except the Q-125 text change; waiting on T-050, synced 2026-10-07: scope gains accepting the `IStringTable` through its own API, every label drawn as `Resolve` of its key, on/off text for boolean knobs and "Uncapped" for a frame-rate cap of 0, `17` §17.4b; historical: rescoped 2026-10-06, Q-114: done-when adds `unity-build` green; writable paths gain one line in `unity/AirportSim/Packages/manifest.json`) |
| T-034 | `app.host`: Unity project shell, bootstrap, playtest bundle | app.host | T-027, T-028, T-031, T-032, T-033, T-050 | QUEUED (synced 2026-10-07, Q-125/Q-129: `LoadStringTable` call and hand-off to the UI backend (`16` §16.7), Console.Error line-break rule, and the merged T-032 facts (RenderBackend on the main camera, built-in render pipeline, Input Manager or Both, `ReadCameraView`/`Draw`, `Show`/`Inputs`/`ClearInputs`); synced 2026-10-02: bundle file list is `16` §16.3's eight-row table, `world.fixture` included; content copied to `StreamingAssets/Content/`; synced 2026-10-06: extends the committed skeleton `unity/AirportSim/` (owner commit c48e163, Unity 6000.3.25f1, workflow `unity-build`) rather than creating it; rescoped 2026-10-06, Q-114: scope is the skeleton, the bootstrap and an editor build step in `Assets/Editor/` that fills `StreamingAssets/`; done-when adds `unity-build` green with the smoke step run; its `airside.fixture` source is `tests/fixtures/harness/checkpoints-phase1/airside.fixture`; synced 2026-10-07, Q-120/Q-124/Q-115: `AssetDatabase.Refresh` in the build step, player `AirportSim.x86_64`, batch-mode `Console.Error` forwarding, done-when notes the non-required `cross-runtime` job) |
| T-035 | Cross-runtime determinism gate: buildable pieces | tools.simharness / app.host | T-030, T-048, T-031, T-034 | QUEUED — **OPTIONAL**, gates nothing, adoption PENDING HUMAN |
| T-050 | `app.ui` scene layer: `LocalisedKey`, `IStringTable`, `UiFactory.LoadStringTable`, `data/strings/en.json` and its schema (`17` §17.4b, Q-125) | app.ui | T-029 (merged #118), T-027 (merged) | QUEUED, releasable now (Test Author first, then worker; writes `src/app/ui/Scene/**`, `data/strings/en.json`, `data/schemas/strings.schema.json`; Test Author writes `tests/app/ui/**` incl. the Q-125 change to `test_ui_public_surface_matches_spec`; must merge before T-033 and T-034) |
| T-049 | `sim.flow`: `IFlowSystem.KindOf(NodeId)` (Q-084, `09` §9.7) | sim.flow | T-021 (merged #91), T-007 (merged #48), T-023 (merged #71) | MERGED (PR #99 with tests #98, 2026-10-03). Historical: (Test Author first: `test_kind_of_returns_graph_kinds_and_throws_on_unknown_node` in `tests/sim/flow/**`, plus `KindOf` on `RecordingFlow` (`tests/sim/schedule/ScheduleTestKit.cs`) and `FakeFlowBase`; then the worker in `src/sim/flow/**`. The test-only PR shows path-guard red by design and never merges alone; the worker branch carries the tests byte-identical. Gates T-014) |
| T-048 | `tools.simharness`: `checkpoints` composes `sim.airside`, `sim.turnaround`, `sim.delay` (Phase 1 stage) | tools.simharness | T-021 (merged #91), T-022 (merged #103), T-024 (merged #119), T-030 (merged #90), Q-095 fixture fix (merged #109) | MERGED (#126; tests #120; synced 2026-10-07) (`19` §19.2c; the only harness writer left after T-045; Test Author writes `tests/fixtures/harness/checkpoints-phase1/**`; harness writer, serial with T-013/T-014/T-045; must merge before T-031) |

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
needed by T-027). It writes only `src/sim/core/**` and depends on
T-001 and T-003 (amended by the systematic type-dependency recheck:
`PaxProfileDefinition`/`QueueProfileDefinition` are `Fx`-valued), so it is
releasable as soon as those two merge and should be prioritised ahead of
everything it gates.

**Systematic type-dependency recheck, this cycle:** T-026 also turns out to
gate T-020, T-023 and T-029 (each references a T-026 type — `NodeId` in
`FlowNodeBox`/`OutstandingPassengers`/`LaneState`/`ILaneCommandSink`), and
T-022 additionally depends directly on T-008 (`IScheduleSystem`, passed to
`TurnaroundFactory.CreateSystem`), not only transitively through T-021.
Every task file's `Depends on` header and this table's `Depends` column are
now amended to name every type-level dependency explicitly, not only the
ones each worker happened to flag in its own notes. This changes no release
ordering that was already being followed correctly (T-026 was already being
merged ahead of all of these per the existing prose), it only makes each
task file self-contained for a worker reading it in isolation.

**T-012 must merge before T-007, T-009, T-010, T-011 and T-023** (the last
four transitively, through T-007). See the Q-012 note in the Phase 0
section above.

T-022's dependency on T-021 is an ordinary merge-order dependency: T-022 must
build against a `sim.airside` that already includes T-021 (it exercises the
real `sim.airside`↔`sim.turnaround` handshake, `spec/12-interfaces-airside.md`
§12.8, rather than T-021's own no-`sim.turnaround` fallback), so do not
release T-022 until T-021 has merged, even though both show `QUEUED`. T-022
also depends directly on T-008 now (`TurnaroundFactory.CreateSystem` takes
an `IScheduleSystem`), amended by the systematic recheck — previously only
implied transitively through T-021's own dependency on T-008.

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
T-050 (`app.ui` string table, after T-029/T-027; before T-033 and T-034),
T-031 (`app.host` headless composition, after every Phase 1 module factory
plus T-027/T-029/T-030/T-048; T-048 is the harness's Phase 1 stage, Q-066..Q-076), T-032/T-033 (the two engine backends, after their
scene layers and T-031), T-034 (the Unity project shell, after everything
else, including content). T-035 (the buildable pieces of the proposed
`determinism_cross_runtime` gate, `16-interfaces-host.md` §16.9) is marked
**optional** — adoption is `PENDING HUMAN` because it needs a row in the
locked `02-determinism.md` gate table and a `ci/` step, both human-only.
Nothing else depends on T-035, and it does not gate T-025.

### Release order note: T-021 and T-023 both touch `sim.flow`'s consumer side

T-021 calls `IFlowSystem.Inject`/`Absorb`/`TryGetOutstanding` but writes
only `src/sim/airside/**` — no file overlap with T-023's `src/sim/flow/**`.
They may run concurrently once T-021's own dependencies (T-003, T-005,
T-008, T-026) and T-023's dependencies (T-005, T-007, T-026) are merged.
Neither blocks the other. (historical, resolved: T-023 merged #71; T-021 is not yet released)

### Release order note: `sim.core` shared surface across Phase 0 and Phase 1

`src/sim/core/**` is written by T-001–T-006 (Phase 0) and T-026/T-027
(Phase 1-numbered, same directory). All of them serialise against each
other, regardless of numbering block — the numbering rule
(`tasks/README.md`) governs id assignment, not release concurrency.

### Q-013–Q-016 amendments (Architect batch, branch `architect/Q-013-solution-layout`, not yet on `main`) (historical, resolved: merged as PR #13)

The Architect answered Q-013 (solution layout/build/test framework, `07`
"Solution layout and build"), Q-014 (the `sim.core` loop/host/bus contract
at T-001's edges), Q-015 (`Fx` edge semantics and C# shape) and Q-016
(no task before T-006 can pass the full `ci/run-checks.sh` — **left OPEN,
PENDING HUMAN**, (historical, resolved: HUMAN DECISION, owner, 2026-09-24, see above) since it changes the CI gate definition, which is the
human owner's call, not the Architect's or this Planner's). Applied this
cycle:

1. **URGENT, a CI bug, not a spec change:** `ci/check-paths.sh` reads each
   line of a task's "Writable paths" fenced block as **one** glob pattern.
   A comma-joined line like `src/sim/core/**, tests/sim/core/**` therefore
   matches nothing, and every task with more than one path on a line would
   fail the path guard. Every task file `T-001`–`T-035` (and any future one)
   now lists **one path per line**, with no trailing prose on the same
   line as a path — explanatory text that used to trail a path (T-011,
   T-035) moved to ordinary prose below the fenced block.
2. **`T-003` before `T-001` (`07` L8, Q-014):** `Fx` merges first, because
   `ISimClock.MinutesBetween` returns `SimMinutes = Fx`. This confirms and
   extends the systematic-recheck correction already applied earlier this
   cycle (T-003 releases first, T-001 depends on it). T-003 additionally
   **creates `AirportSim.sln`** (`07` L8) and the byte-for-byte
   `src/sim/core/AirportSim.Sim.Core.csproj` (`07` L2) — both added to its
   writable paths. Its `Depends on` is unchanged (`—`).
3. **T-001's scope grew** (coordinator decision on the Architect's
   recommendation): it now also implements the **full `08` §8.6 event
   bus/dispatch** (previously unowned) and declares a set of **shape-only**
   types other tasks give behaviour to later (`Command`/`PlayerId`/
   `CommandKind{NoOp=0}`/`CommandRejection`/`ICommandHandler`/
   `ICommandHandlerRegistry`, `IRandomService`/`IRandomStream`/
   `RngStreamName` with a throwing placeholder, `IContentIndex`/
   `IContentDefinition`/`ContentId`/`ContentKind`, `IIdAllocator`/
   `EntityId`). T-001 also now implements `Checkpoint`/`ICheckpointSink`
   **with the real phase-4 cadence and world-hash fold of §8.9, not a
   stub** — moved from T-004 (below). It adds `tools/SimHarness` to
   `AirportSim.sln` (which T-003 already created); it does not create the
   `.sln` itself.
4. **T-004's scope narrowed to match:** it no longer records checkpoints or
   computes the world-hash fold (T-001 does both now). It keeps
   `IStateHasher` and is responsible for pinning its exact FNV-1a-64
   byte-level behaviour with reference-vector tests. `IStateHasher`'s owner
   may move to T-001 in the Architect's next batch, since T-001 already
   implements the fold that feeds it — noted in T-004's own file, not
   pre-empted here.
5. **First task of each new module grants `AirportSim.sln`** (`07` L8:
   "the first task that creates a module's production project adds that
   project and its test project [to `AirportSim.sln`]... the Planner lists
   `AirportSim.sln` in that task's writable paths and never releases two
   such tasks concurrently, because concurrent `.sln` edits conflict").
   That is **T-007** (`sim.flow`), **T-008** (`sim.schedule`), **T-012**
   (`sim.world`), **T-020** (`app.render` scene layer), **T-021**
   (`sim.airside`), **T-022** (`sim.turnaround`), **T-024** (`sim.delay`),
   **T-029** (`app.ui` scene layer) and **T-031** (`app.host`) — each now
   lists `AirportSim.sln` in its writable paths and carries a note that it
   must not release concurrently with any of the other eight, on top of
   whatever dependency-based serialisation already applied. This is a new,
   independent serialisation axis from the `src/sim/core/**` one above —
   two tasks in different module directories with no file overlap and no
   `Depends` edge between them can still conflict on `AirportSim.sln` alone.
   `tools.simharness` is not in this list (T-001 already created its
   project); nor are the Unity backends/shell (T-032/T-033/T-034) or
   `content` (T-028), which have no `.csproj` and are not in the solution
   at all (`07` L1).
5a. **Two more edges from the same recheck (team lead's follow-up):** T-030
   also depends on **T-009** — it "composes a fixture the same way T-009's
   harness already does," reusing T-009's harness composition of
   `sim.world`/`sim.schedule`/`sim.flow`, not only T-004's/T-006's surfaces.
   T-031 now names **T-023** explicitly in its `Depends on`, rather than
   relying on the transitive edge through T-020/T-029 (both of which
   already require T-023) — `RenderSources`/the lane sink call
   `IFlowSystem.TryGetLaneState`/`TryGetOutstanding` through the same wiring.
   Neither changes release *order* (T-009 already merges well before T-030
   is reachable; T-023 was already effectively required before T-031),
   only makes each task file self-contained.
6. **Noted, not tasked yet:** `IIdAllocator`'s real counting behaviour and
   `ContentIndexFactory` have **no owning task**. T-001 declares both
   interfaces' shape only (Q-014 A3) and does not implement either's real
   behaviour. This is not blocking any currently-released task, so it is
   not filed as a fresh open question by this Planner — it is the
   Architect's next batch to settle, per the coordinator's instruction.

## Planner scope note

This queue is expanded through T-035 as of this cycle: T-012, T-013 close
the Q-012/soak gaps in Phase 0; T-026 (extended) and T-027–T-035 close the
Q-011/Q-010/D6/D7 gaps in Phase 1, delivering the chain to a playable
build. Every new task is infrastructure for, or a direct consequence of,
already-decided scope (D1–D9, Q-011, Q-012) — none of it is a fresh scope
expansion the Planner introduced on its own. Phase 2 (build order items
7–12: money, staff, policy/reputation, incidents, progression, tutorial)
remains out of scope until the human owner clears the Phase 1 gate (T-025).
