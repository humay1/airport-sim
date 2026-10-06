# T-032 — `app.render` Unity backend

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.render` (backend only) |
| Assigned role | worker |
| Depends on | T-020, T-031 |
| Spec source | `spec/15-interfaces-render.md` §15.10 "The backend contract" (graphics knobs added by D10/Q-034) |
| Blocked by | — |

**Amendment (D10/Q-034, this cycle):** the backend now applies three
graphics knobs from `RenderFrame.Graphics` (`FrameRateCap`,
`ResolutionScalePercent`, `AntiAliasing`) and is bound by a draw-call cap
tied to `DrawLayer`/`ColourRole`, never to primitive count. This does not
change this task's dependency list.

## Writable paths

```
src/app/render/Unity/**
unity/AirportSim/Packages/manifest.json
```

The `manifest.json` write is one line only: this backend's own package entry
(`16` §16.2, `15` §15.10).

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/15-interfaces-render.md` §15.9, §15.10, §15.11, §15.14,
`spec/16-interfaces-host.md` §16.2, §16.6, §16.7, §16.10

## Interface to implement

No new headless interface — this task consumes `RenderFrame`/`CameraView`
(`15` §15.9) and never calls a sim member.

Binding, copied from `spec/15-interfaces-render.md` §15.10, not
paraphrased:

- Consumes `RenderFrame` only, draws its primitives in list order. Maps
  `ColourRole` to colour through a palette asset.
- Turns input (pan, zoom) into a `CameraView`, keeping `ViewHeight` and
  `Aspect` positive.
- Does **not** run the frame order — `app.host`'s frame loop does (`16`
  §16.6), and the Unity bootstrap converts the frame delta (`16` §16.7).
  This backend supplies the `CameraView` and draws the `RenderFrame` it is
  handed.
- References the scene layer's types only. Calls **no** sim member, never
  branches on sim state. Anything needing a decision belongs in the scene
  layer (T-020), where it is testable.
- Issues no commands at Phase 1.
- **Graphics (D10, §15.14).** Applies `RenderFrame.Graphics`'s backend
  knobs (`FrameRateCap`, `ResolutionScalePercent`, `AntiAliasing`) through
  the engine's own settings, and only when they differ from those last
  applied. Which engine API it uses is its own choice. It reads no other
  source of quality settings, and never Unity's quality levels on their
  own. Resolution scale changes only the rendered image — the camera, the
  screen size in `FrameInput` and every reported click stay in
  full-screen pixels. It draws every primitive it is handed at every
  setting (`15` §15.14's invariant) — it never filters, thins or hides a
  primitive by graphics setting itself.
- **Draw calls (Q-034).** Integrated graphics is the minimum GPU (`15`
  §15.11). The number of draw calls per frame is bounded by the number of
  `DrawLayer`s and `ColourRole`s, never by the number of primitives. It
  creates no engine object per primitive and allocates no engine object
  per frame after the first. **LOW CONFIDENCE**: this binds the
  implementation more tightly than the rest of the contract; it is the
  Architect's reading of what `Low` needs to hold budget on integrated
  graphics.
- Because it cannot be tested in CI, it must stay small enough for the
  Reviewer to check against this list line by line.

## Events

Emitted: none. Consumed: none.

## Tests to pass

None in CI (`15` §15.3: "Tested in CI: no"). The Reviewer checks this
task's code against §15.10's list line by line instead of a test suite.

## Performance budget

Not budgeted here — `15` §15.11: "The backend's draw cost is not budgeted
here. It has no test to carry a number."

## Done when

- [ ] Matches §15.10's contract exactly, checked line by line by the
      Reviewer
- [ ] No writes outside writable paths
- [ ] No sim member call, no branch on sim state
- [ ] Reviewer approved
- [ ] `unity-build` green on its PR, with its package referenced from `Packages/manifest.json` (`16` §16.11)
- [ ] Graphics knobs applied only on change, and every primitive still
      drawn at every setting (D10, Q-034)

## Worker notes

Depends on T-031 (`app.host`'s headless composition and frame loop) because
this backend is driven entirely by that frame loop and the Unity project
shell (T-034) it will eventually sit inside — build and review it against
the published contract; it cannot be exercised end-to-end until the shell
exists. Do not add the frame order, input polling ownership, or any camera
decision here that §15.10 assigns to the scene layer.

**Graphics (D10/Q-034) does not change this task's dependency list.** The
three backend knobs and the draw-call bound are additive to the same
contract this task already implements. Which control or knob is shown, and
whether the panel pauses, are `app.ui`'s decisions (T-029/T-033), not this
task's — this backend only reads `RenderFrame.Graphics` and applies its
three fields. The draw-call bound is LOW CONFIDENCE and the tightest part
of this contract; if it cannot be met as written, file an open question
rather than loosening it unilaterally.
