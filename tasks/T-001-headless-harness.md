# T-001 — Headless harness: fixed timestep, no rendering

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | — |
| Spec source | `spec/01-architecture.md` "Platform decisions" (D1), "Layer separation", "Command pattern"; `spec/08-interfaces-core.md` §8.2, §8.5, §8.11a; `spec/07-conventions.md` "Runtime portability" |
| Blocked by | — |

**Status note:** released for this cycle; the Test Author is authoring
`tests/sim/core/**` for this task now. Status stays `QUEUED` until those
tests land, then moves to `TESTS_AUTHORED` and the worker may start. Do not
begin implementation before that move.

## Writable paths

```
src/sim/core/**, tests/sim/core/**, AirportSim.sln (create), tools/SimHarness/** (create)
```

Anything else is read-only. Writing outside these paths is an automatic
rejection. `tools/SimHarness` is the console entry point CI's
`ci/run-checks.sh` already invokes (`dotnet run --project tools/SimHarness`);
create it as a thin host over `ISimHost`, not as a place to put sim logic.

**Targets (D1, `01-architecture.md` "Platform decisions"):** `AirportSim.sln`
must build `src/sim/**` (and every headless `app.*` layer later) as
`netstandard2.1`, `LangVersion 9`, zero engine references. `tests/**` and
`tools/SimHarness/**` target `net8.0` and consume the sim unchanged. This is
this task's job to set up correctly at solution-creation time — a later
retarget is expensive across every sim project.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md`

## Interface to implement

```
type Tick = uint64

interface ISimClock {
  Tick       CurrentTick   { get }
  uint32     DayIndex      { get }
  uint32     SecondOfDay   { get }
  SimMinutes MinutesBetween(Tick a, Tick b)
  Tick       TickOfDayTime(uint32 dayIndex, uint32 secondOfDay)
}

interface ISimSystem {
  SystemId Id   { get }
  string   Name { get }
  void     Tick(in TickContext ctx)
  uint64   ComputeStateHash()
}

readonly struct TickContext {
  Tick            Tick
  ISimClock       Clock
  IEventPublisher Events
  IRandomService  Rng
  IContentIndex   Content
  ISimLog         Log
}

interface ISimHost {
  Tick   CurrentTick { get }
  void   Step(uint32 ticks)
  uint64 WorldStateHash()
  bool   TrySubmit(in Command cmd, out CommandRejection reason)
}
```

Construction (`08` §8.11a, Q-009 — the same surface every later module
factory, `app.host` and `tools.simharness` build against):

```
readonly struct SimHostConfig {
  uint64          MasterSeed
  IContentIndex   Content
  ICheckpointSink Checkpoints
  ISimLog         Log
}

readonly struct SystemServices {
  IEventBus               Events
  IIdAllocator            Ids
  IContentIndex           Content
  ICommandHandlerRegistry Commands
}

interface ISimHostBuilder {
  SystemServices Services { get }
  void     Register(ISimSystem system)     // strictly ascending registry position
  ISimHost Build()                         // once
}

SimHostFactory.CreateBuilder(in SimHostConfig config) -> ISimHostBuilder
```

This task builds `ISimHostBuilder`/`SimHostFactory` themselves; it does not
need `ICommandHandlerRegistry` to do anything beyond exist as a seam (T-005
gives it behaviour) and does not need `ContentIndexFactory` to load real
content (T-026/T-027 do). `Register` out of order, twice for one `SystemId`,
or after `Build`, throws; so does `Subscribe` after `Build`. `Build` creates
the RNG service from `MasterSeed`, wires the sinks, and returns the host at
tick 0. A builder cannot be reused after `Build`.

Fixed phase order per tick (`08-interfaces-core.md` §8.5), binding and not to
be reordered: (1) command application, (2) system update in registry order,
(3) event dispatch, (4) checkpoint if due. Phase 0 has no registered systems
yet beyond a `NoOp`-accepting stub; this task proves the loop shape, not any
system's content. `RngService`, real event dispatch and checkpoint hashing are
separate tasks (T-002, T-004, T-005) — stub their seams here (e.g. an
`IEventPublisher` that queues but need not fully implement cascade rules yet)
so this task stays about the loop, not about those systems' internals. Do not
implement `Command` admission logic here beyond accepting the shape; that is
T-005.

`Step` must be synchronous, take no time argument, and must not read
wall-clock time anywhere in `src/sim`.

## Events

Emitted: none (this task wires transport only; no real events exist yet)
Consumed: none

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. **Do not edit them.** Expect at minimum: a
headless-day test that builds `ISimHost` with zero systems registered and
steps a full sim-day of ticks with no engine reference anywhere in `src/sim`
(enforced by `ci/run-checks.sh`'s grep gate), and a test asserting `Step`
never reads wall-clock time (`DateTime.Now` grep gate). If a test contradicts
this interface, file an open question and stop.

## Performance budget

`0.25` ms/tick at max tier (`sim.core` loop, commands, event dispatch —
`spec/03-module-map.md`). This task's own fixture is empty (no systems), so
the budget test here is a sanity check, not the real measurement — that comes
with later systems.

## Done when

- [ ] Interface matches spec exactly
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

`SIM_SECONDS_PER_TICK = 6` is now a confirmed **HUMAN DECISION** (D2,
`08-interfaces-core.md` §8.1/§8.2), not provisional — the earlier note here
that it was provisional and that golden hashes must not be authored against
it is stale and is withdrawn by this amendment. Golden hashes may now be
authored (the soak fixture and its golden are T-013's job, not this one's).

Also stale, replaced above: the earlier assumption that the sim targets
`net8.0`. D1 retargets the sim (and every headless `app.*` layer later) to
`netstandard2.1`/`LangVersion 9`; only `tests/**` and `tools/SimHarness/**`
stay on `net8.0`. Read `spec/07-conventions.md` "Runtime portability" before
writing anything that sorts, hashes, or parses a string or a number — Mono
and CoreCLR must agree, and that section's seven rules are binding on
`src/sim/**` from this task onward.
