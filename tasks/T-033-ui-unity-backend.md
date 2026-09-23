# T-033 — `app.ui` Unity backend

| Field | Value |
|---|---|
| Status | QUEUED |
| Module | `app.ui` (backend only) |
| Assigned role | worker |
| Depends on | T-029, T-031 |
| Spec source | `spec/17-interfaces-ui.md` §17.8 "The backend contract" |
| Blocked by | — |

## Writable paths

```
src/app/ui/Unity/**
```

Built in `app.host`'s Unity project (`16` §16.2), the same as T-032.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/17-interfaces-ui.md` §17.7, §17.8,
`spec/16-interfaces-host.md` §16.2, §16.6, §16.7

## Interface to implement

No new headless interface — this task produces `UiInput`s (`17` §17.3) and
consumes `UiFrame` (`17` §17.7).

Binding, copied from `spec/17-interfaces-ui.md` §17.8, not paraphrased:

- Draws a control strip with four controls: pause, 1x, 2x, 4x, as **icons**
  only — no player-visible text exists yet (`04-data-schemas.md`: text
  would be a `LocalisedKey` if ever added). The strip's state comes from
  `UiFrame` only.
- Reports presses on those controls as `TogglePause`/`SetSpeed`, and any
  other primary/secondary click inside the game view as
  `PrimaryClick`/`SecondaryClick` at its screen position. Optional keyboard
  shortcuts map to the same inputs.
- Collects this frame's inputs, in arrival order, for the bootstrap to put
  in `FrameInput` (`16` §16.6).
- Calls **no** sim member, never branches on sim state. Like T-032, must
  stay small enough to review line by line.

## Events

Emitted: none. Consumed: none.

## Tests to pass

None in CI (`17` §17.2: "Tested in CI: no"). Reviewer checks against
§17.8's list line by line.

## Performance budget

Not budgeted here.

## Done when

- [ ] Matches §17.8's contract exactly, checked line by line by the
      Reviewer
- [ ] No writes outside writable paths
- [ ] No sim member call, no branch on sim state
- [ ] Reviewer approved

## Worker notes

No text of any kind — icons only, at Phase 1. If a later phase needs
labels, that is a spec amendment introducing `LocalisedKey`s, not a local
string literal added here. Depends on T-031 for the same reason T-032
does: this backend is exercised end-to-end only once the host's frame loop
and Unity shell exist.
