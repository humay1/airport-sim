# 06 — Delay attribution

The headline feature. Simulation games die when failure is opaque. Every delayed
minute must trace back to a cause the player can see.

Built at position 6 in the build order, before the economy, because every balance
decision afterwards depends on being able to see why things failed.

## The model

Every delay minute carries a **causal parent chain**. The result is a tree the
player opens from any flight:

```
BA412 dep 07:35 — 34 min late
├─ 18 min  late inbound aircraft (BA411 arr, 22 min late)
│   └─ 22 min  holding, runway 27 single-runway congestion 06:40–07:05
│       └─ cause: declared capacity 32/hr exceeded, 38 movements scheduled
├─ 11 min  fuel truck arrived 09 min late
│   └─ cause: 3 trucks serving 5 concurrent turnarounds, stand H7 furthest
└─  5 min  3 passengers cleared security at 07:28
    └─ cause: security queue 19 min, 4 of 8 lanes open, shift changeover 07:00
```

## Rules

1. **Every delay minute has exactly one parent**, or is marked `root_cause`.
   Minutes must never be double-counted across branches: the sum of the leaf
   minutes equals the total delay exactly. This is a test.
2. `sim.delay` is **read-only**. It learns about the world exclusively through the
   event bus and never queries another module directly.
3. Attribution is recorded live, as it happens. Never reconstructed afterwards from
   heuristics — reconstruction produces plausible lies.
4. The tree is part of sim state and is saved. A player must be able to open
   yesterday's disaster.
5. Tree depth is capped at `MAX_ATTRIBUTION_DEPTH` (default 6) to bound memory;
   deeper chains terminate in `root_cause: propagated`.

## Event contract

Every module that can cause delay emits:

```
DelayEvent {
  tick          : uint64
  subject       : FlightId | PassengerCohortId | VehicleId
  minutes       : Fx
  category      : DelayCategory
  parent        : DelayEventId?   // null means root cause
  explanation   : LocalisedKey + params
}
```

`DelayCategory` (fixed, extend only via spec amendment):
`late_inbound`, `runway_congestion`, `taxi_congestion`, `stand_unavailable`,
`ground_handling`, `fuel`, `catering`, `cleaning`, `loading`, `pushback`,
`crew`, `passenger_late`, `security_queue`, `immigration_queue`, `baggage`,
`weather`, `deicing`, `atc_flow`, `incident`, `policy_constraint`, `propagated`.

## Aggregate views

The same data powers, without additional systems:

- Daily on-time performance with cause breakdown
- Season-long ranking of the player's top recurring bottlenecks
- The morning advisor's "top three constraints today"
- Post-incident reports

Build the tree once and get all four. This is why it sits early in the order.

## Tests the Test Author must write

- `sum_of_leaves_equals_total` for 1000 randomly generated flight days
- `no_orphan_nodes` — every non-root node's parent exists
- `no_cycles`
- `depth_capped` at `MAX_ATTRIBUTION_DEPTH`
- `survives_save_load` — tree identical after round-trip
- `delay_module_never_writes` — static analysis: `sim.delay` holds no references to
  other sim modules
