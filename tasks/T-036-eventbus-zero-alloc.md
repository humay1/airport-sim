# T-036 — `sim.core`: `IEventBus` zero-allocation fix

| Field | Value |
|---|---|
| Status | MERGED (PR #47) |
| Module | `sim.core` |
| Assigned role | worker |
| Depends on | T-001 |
| Spec source | `spec/08-interfaces-core.md` §8.6 "Allocation" (Q-035) |
| Blocked by | — |

## Writable paths

```
src/sim/core/EventBus.cs
src/sim/core/Channel.cs
```

No other file, and no other module. This is a fix inside `sim.core`'s
existing event-bus implementation (T-001), not a new interface — `IEventBus`,
`IEventPublisher`, `SimEventHandler<T>` and `ISimEvent` (`08` §8.6) are
unchanged. Never release this task concurrently with any other open
`src/sim/core/**` task (Q-013/`07` L8's shared-surface rule, same reasoning
as T-002/T-004/T-005/T-026/T-027's mutual serialisation) — check the queue
for any other in-flight `sim.core` task before starting.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/02-determinism.md`, `spec/03-module-map.md`, `spec/07-conventions.md`,
`spec/08-interfaces-core.md` §8.5, §8.6, §8.9

## Interface to implement

No new public signature. `IEventBus`/`IEventPublisher`/`SimEventHandler<T>`/
`ISimEvent` are exactly as `08` §8.6 already publishes them:

```
interface IEventPublisher {
  EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
}

interface IEventBus : IEventPublisher {
  void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
}

delegate void SimEventHandler<T>(in EventEnvelope envelope, in T evt, in TickContext ctx)
  where T : struct, ISimEvent

interface ISimEvent {
  // marker; the struct holds payload fields only, 10-events.md §10.2
}
```

Binding, copied from `spec/08-interfaces-core.md` §8.6, not paraphrased:

- **After `Build`, the bus allocates nothing.** That covers `Publish`,
  dispatch and the per-tick reset, for every tick of at most
  `MAX_EVENTS_PER_TICK` (4096) events. It holds from the first tick, with
  no warm-up. It does not depend on which event types have been published
  before, or on how many events an earlier tick carried. Allocations inside
  a handler are the subscriber's, not the bus's.
- **The channel set is fixed at `Build`.** Subscription closes at `Build`
  (`08` §8.11a), so the event types with at least one subscriber are known
  then. The bus creates all of their storage then, and never later.
- **A type with no subscriber stores nothing.** Its `Publish` still runs
  every check (phase, final cascade pass, `MAX_EVENTS_PER_TICK`). It still
  consumes the next `Sequence` and returns its `EventId`. It is not queued,
  because no handler could observe it. The `EventId`s and handler calls of
  every other event are unchanged, so this changes no outcome and no hash.
- **Capacity is reserved at `Build`.** The storage behind the tick's FIFO,
  and the storage of each subscribed type, can hold `MAX_EVENTS_PER_TICK`
  events without growing. A new per-tick peak later in a run therefore
  allocates nothing. How the storage is laid out is the implementer's
  choice.
- Every other rule in `08` §8.6 (envelope fields, ordering, FIFO drain,
  registry-order dispatch, the cascade-pass and per-tick-count invariants,
  events not saved) is unchanged by this task. This is an allocation fix to
  an existing implementation, not a behaviour change: `Publish`'s return
  value, dispatch order and every thrown exception stay exactly as `08`
  §8.6 already specifies them.

## Events

Emitted: none (this task changes no event's payload or emission).
Consumed: none.

## Tests to pass

```
tests/sim/core/**
```

Written by the Test Author. Expect exactly the five named in `08` §8.6
"Allocation" (Q-035):

- `test_bus_first_publish_of_unsubscribed_type_after_warm_up_allocates_nothing`
- `test_bus_first_publish_of_subscribed_type_after_warm_up_allocates_nothing`
- `test_bus_new_per_tick_peak_up_to_max_events_per_tick_allocates_nothing`
- `test_bus_unsubscribed_publish_consumes_sequence_and_returns_event_id`
- `test_bus_unsubscribed_publish_still_enforces_phase_cascade_and_limit`

**Do not edit them.** If a test contradicts `spec/08-interfaces-core.md`,
file an open question and stop.

## Performance budget

No allocation in `Publish`, dispatch or the per-tick reset after `Build`, at
every tick up to `MAX_EVENTS_PER_TICK` events. `sim.core`'s own share of the
6 ms/tick budget is otherwise unchanged (`03-module-map.md`) — this task
adds no new work, it removes allocation from work that already exists.

## Done when

- [ ] Interface matches spec exactly (unchanged — this is an allocation fix)
- [ ] All assigned tests pass
- [ ] `ci/run-checks.sh` green
- [ ] Budget met
- [ ] No writes outside writable paths
- [ ] Reviewer approved
- [ ] Verifier gates green

## Worker notes

This is Q-035: `sim.flow`'s own budget test
(`test_flow_budget_update_path_allocates_nothing`, T-007) cannot pass while
`IEventBus.Publish` allocates, because `sim.flow`'s queue update publishes
through the bus every tick a threshold or blocking state changes. T-007
depends on this task merging; this task depends on nothing unmerged — it
is a self-contained fix to `sim.core`'s existing `EventBus`/`Channel` code
from T-001, touching no other file and no other module.

Do not widen scope beyond the two named files. If the fix as specified
needs a change to `IEventBus`'s public shape, that is a spec question, not
a worker's call — file it and stop.
