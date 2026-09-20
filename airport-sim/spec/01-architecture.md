# 01 — Architecture

> **Locked document.** Agents may not modify this file. Changes require human
> sign-off, recorded in `spec/CHANGELOG.md`.

## TODO for the human owner before the Architect runs

- [ ] Engine: _(Unity + ECS/DOTS | Godot | custom)_
- [ ] Language: _(C# | C++ | Rust)_
- [ ] Target platform and minimum spec:
- [ ] Target airport size at max tier (daily movements / daily passengers):
- [ ] Frame budget for the sim layer at max tier:

Nothing downstream is valid until these are filled in.

## Layer separation

Three layers, strictly one-directional dependencies:

```
  presentation  ──reads──▶  simulation  ──reads──▶  content data
  (render, UI, input)       (headless)              (JSON, read-only)
```

- **Simulation** must build and run headless with zero rendering references. This is
  the single most important architectural constraint in the project: it is what
  makes the soak test, the determinism gate and reproducible bug reports possible.
- **Presentation** may read sim state and issue commands. It may never mutate sim
  state directly.
- All player actions enter the sim as **commands** on the command queue, applied at
  tick boundaries. Never as direct method calls.

## Command pattern

```
Command {
  tick        : uint64     // tick at which it applies
  issuer      : PlayerId
  kind        : CommandKind
  payload     : bytes      // schema per kind
}
```

The command log plus the initial seed fully reproduces any session. This gives
replays, save integrity and bug reports for free. It only works if rule 1 below
holds.

## Hierarchical simulation — the central performance decision

Passenger counts reach tens of thousands per day. Individually simulating them does
not fit any frame budget. Therefore:

**Passengers exist in two representations:**

| Representation | When | Cost |
|---|---|---|
| **Flow** (statistical cohort in a queue or corridor node) | default | O(nodes) |
| **Agent** (individually simulated body with position) | on-camera, or when a decision depends on this individual | O(agents) |

**Promotion rules:**

1. A cohort promotes to agents when its containing node enters the camera frustum
   at or below zoom level `AGENT_ZOOM_THRESHOLD`.
2. A cohort promotes when an incident or player command requires individual
   identity (e.g. a passenger missing a connection is named in the delay tree).
3. Agents demote back to a cohort when their node leaves the frustum, preserving
   aggregate state exactly.
4. **Promotion and demotion must not change simulation outcomes.** This is a test:
   `test_promotion_is_outcome_neutral` runs the same day twice, once with the
   camera parked on a gate and once headless, and asserts identical results.

If rule 4 cannot be satisfied for a system, that system does not get promotion —
it stays statistical, always.

**Queues are objects, not crowds.** A security queue is a throughput model with a
population count. It renders as bodies; it does not simulate as bodies.

## Pathfinding

- Flow-field / vector-field pathfinding over a navigation grid, computed per
  destination, shared by all agents heading there.
- **Never per-agent A\*.** This is a rejection criterion in code review.
- Fields recompute on construction changes only, not per tick.

## Data-driven content

Aircraft, airlines, buildable objects, incidents and policies live in external data
files validated against `data/schemas/`. Designers and modders use the same pipeline.
Hot reload in development builds.

Code must never hardcode a content value. A hardcoded aircraft weight is a
rejection criterion.

## Save format

- Snapshot of sim state plus the command log since the snapshot.
- Versioned with a migration path from the first playable build onward.
- Save/load round-trip is a determinism gate: load a save, run 1000 ticks, compare
  against the same 1000 ticks run without the save/load cycle. Must match exactly.

## Module boundaries

See `03-module-map.md`. Cross-module communication is via published interfaces and
the event bus only. No module reaches into another's internal state, ever.
