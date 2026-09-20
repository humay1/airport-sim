# T-001 — Headless harness: fixed timestep, no rendering

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | — |
| Spec source | `spec/01-architecture.md` "Layer separation", "Command pattern"; `spec/08-interfaces-core.md` §8.2, §8.5 |
| Blocked by | — |

## Writable paths

```
src/sim/core/**, tests/sim/core/**, AirportSim.sln (create), tools/SimHarness/** (create)
```

Anything else is read-only. Writing outside these paths is an automatic
rejection. `tools/SimHarness` is the console entry point CI's
`ci/run-checks.sh` already invokes (`dotnet run --project tools/SimHarness`);
create it as a thin host over `ISimHost`, not as a place to put sim logic.

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

`spec/open-questions.md` Q-002 leaves `SIM_SECONDS_PER_TICK` provisional at 6.
Build against that value (permitted explicitly by Q-002's status line) but do
not author golden hashes against it — that is `soak_500_days`, out of scope
here.
