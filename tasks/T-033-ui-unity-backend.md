# T-033 — `app.ui` Unity backend

| Field | Value |
|---|---|
| Status | IN PROGRESS (PR #131 approved except the Q-125 text change; waiting on T-050) |
| Module | `app.ui` (backend only) |
| Assigned role | worker |
| Depends on | T-029, T-031, T-050 |
| Spec source | `spec/17-interfaces-ui.md` §17.8 "The backend contract" (settings panel and control added by D10/Q-034) |
| Blocked by | — |

**Amendment (D10/Q-034, this cycle):** the control strip gains a fifth,
settings icon reporting `ToggleSettings`, and a modal panel drawn from
`UiFrame.Graphics`/`SettingsOpen` — the first player-visible text at Phase 1
(`LocalisedKey`s). This is additive to this task's existing scope and does
not change its dependency list.

## Writable paths

```
src/app/ui/Unity/**
unity/AirportSim/Packages/manifest.json
```

The `manifest.json` write is one line only: this backend's own package entry
(`16` §16.2, `17` §17.8).

Built in `app.host`'s Unity project (`16` §16.2), the same as T-032.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/17-interfaces-ui.md` §17.4a,
§17.4b, §17.7, §17.8, `spec/15-interfaces-render.md` §15.14, `spec/04-data-schemas.md`
(`LocalisedKey`), `spec/16-interfaces-host.md` §16.2, §16.6, §16.7

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
- **Settings (D10, §17.4a).** A fifth control, a settings icon, reports
  `ToggleSettings`. While `UiFrame.SettingsOpen`, it draws a panel from
  `UiFrame.Graphics`: one control per preset (`Low`, `Medium`, `High`),
  reporting `SetGraphicsPreset`, and one control per knob of `15` §15.14,
  reporting `SetGraphicsSettings` with that knob changed. Panel text is
  `LocalisedKey`s (`04-data-schemas.md`) — the first player-visible text at
  Phase 1. A click on the panel is never also reported as a world click.
  The layout and look of the panel are this task's own choice.
- **Text (Q-125, §17.4b).** The panel shows the title, the three preset
  controls, each knob's label and each knob's value, with exactly the keys
  of §17.4b's table and its "Knob values" rule. The backend accepts the
  `IStringTable` from the bootstrap through its own API, before its first
  draw. Every piece of text it draws is `IStringTable.Resolve` of its key; it
  never draws a key's `Value` and never holds an English literal (it may keep
  its own internal key constants whose values are exactly §17.4b's). Boolean
  knobs show the text of `ui.settings.value.on` or `ui.settings.value.off`;
  integer knobs show invariant decimal digits, except that a `FrameRateCap`
  of 0 shows the text of `ui.settings.value.uncapped`.
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
- [ ] `unity-build` green on its PR, with its package referenced from `Packages/manifest.json` (`16` §16.11)
- [ ] Every label and value is `IStringTable.Resolve` of a §17.4b key, with on/off text for boolean knobs and "Uncapped" for a frame-rate cap of 0 (Q-125)
- [ ] Settings icon and panel present, reading only `UiFrame`, and every
      other control inert while `SettingsOpen` (D10, Q-034)

## Worker notes

The four pause/speed controls stay icons only, at Phase 1 — no text of any
kind. If a later phase needs labels on them, that is a spec amendment
introducing `LocalisedKey`s, not a local string literal added here. Depends
on T-031 for the same reason T-032 does: this backend is exercised
end-to-end only once the host's frame loop and Unity shell exist.

**Settings panel (D10/Q-034) does not change this task's dependency
list.** It is the first — and, at Phase 1, only — control this task draws
with text, and that text is `LocalisedKey`s from the start, never a raw
string: the "icons only" rule above is unchanged for the other four
controls. The panel's own layout is this task's choice; which presets and
knobs it exposes, and what pressing them means, is fixed by `17` §17.8 and
§17.4a and is not this task's to reinterpret. Do not decide whether the
panel pauses here — that is `app.ui`'s scene layer (T-029), which already
reflects it in `UiFrame.Pacing`.
