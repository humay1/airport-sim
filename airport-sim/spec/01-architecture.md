# 01 — Architecture

> **Locked document.** Agents may not modify this file. Changes require human
> sign-off, recorded in `spec/CHANGELOG.md`.

## Platform decisions

Set by the human owner. Locked. Changing any of these invalidates downstream work
and requires a recorded sign-off in `spec/CHANGELOG.md`.

| Decision | Value |
|---|---|
| Simulation | Pure C# class library, .NET 8, **zero engine references** |
| Presentation | Unity 6 LTS, importing the sim library as a compiled assembly |
| Language | C# for both layers |
| Target platform | Windows and Linux desktop; macOS best-effort |
| Minimum spec | 4-core CPU, 8 GB RAM, GPU with 2 GB VRAM |
| Max tier size | 800 daily movements, 90,000 daily passengers, 60 stands, 3 runways |
| Sim tick rate | 10 Hz (`TICK_MS = 100`) at 1× game speed |
| Sim frame budget | **6 ms per tick** at max tier, total across all sim modules |
| Render target | 60 fps at max tier on minimum spec |

### Why the sim is a plain .NET library, not an engine project

This is the decision everything else rests on, so the reasoning is recorded here:

1. **The determinism gates become trivial.** `dotnet test` runs the whole sim in CI
   with no engine, no license, no GPU, no headless display. A gate that is hard to
   run is a gate that gets disabled.
2. **The soak test is possible at all.** 500 simulated days must run in minutes on
   a build agent. That rules out driving the sim through an engine's update loop.
3. **Layer separation is enforced by the compiler**, not by review. The sim library
   has no engine reference, so a worker *cannot* call into rendering by accident.
   This is the cheapest possible enforcement of the rule in "Layer separation".
4. **Swapping the presentation layer stays possible.** If Unity turns out wrong,
   the sim is unaffected.

The cost is that the sim cannot use the engine's job system or ECS. That is
acceptable: the hierarchical simulation described below exists precisely so that
the per-tick work stays small enough not to need it.

### Budget allocation at max tier

6 ms per tick, apportioned by the Architect in `03-module-map.md`. Starting split,
to be refined once T-011 measures reality:

| Module | ms/tick |
|---|---|
| `sim.flow` | 2.5 |
| `sim.baggage` | 1.0 |
| `sim.airside` | 0.8 |
| `sim.turnaround` | 0.5 |
| `sim.delay` | 0.4 |
| everything else combined | 0.8 |

`sim.world` flow-field recomputation is amortised and excluded from the per-tick
budget; it has its own budget of 50 ms per construction change, off the hot path.

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
