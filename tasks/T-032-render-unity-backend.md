# T-032 — `app.render` Unity backend

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.render` (backend only) |
| Assigned role | worker |
| Depends on | T-020, T-031 |
| Spec source | `spec/15-interfaces-render.md` §15.10 "The backend contract" |
| Blocked by | — |

## Writable paths

```
src/app/render/Unity/**
```

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/15-interfaces-render.md` §15.9, §15.10, `spec/16-interfaces-host.md`
§16.2, §16.6, §16.7

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

## Worker notes

Depends on T-031 (`app.host`'s headless composition and frame loop) because
this backend is driven entirely by that frame loop and the Unity project
shell (T-034) it will eventually sit inside — build and review it against
the published contract; it cannot be exercised end-to-end until the shell
exists. Do not add the frame order, input polling ownership, or any camera
decision here that §15.10 assigns to the scene layer.
