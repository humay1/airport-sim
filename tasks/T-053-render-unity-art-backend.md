# T-053 — `app.render` Unity backend: atlas upload and quad copy

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-052 merges) |
| Module | `app.render` (Unity backend) |
| Assigned role | worker only (CI has no behaviour test for the backend, `15` §15.3) |
| Depends on | T-052 |
| Spec source | `spec/15-interfaces-render.md` §15.3, §15.10, §15.15, §15.18 "For the Planner" task 3 (Q-130); `spec/16-interfaces-host.md` §16.2, §16.7 |
| Blocked by | — |

## Writable paths

```
src/app/render/Unity/**
```

The files are `RenderBackend.cs`; `DefaultPalette.asset`, which keeps its
GUID; and the `.asmdef`, which gains `AirportSim.App.Render.Art2D.dll`.

## Readable specs

`CLAUDE.md`, `spec/00-overview.md`, `spec/01-architecture.md`,
`spec/15-interfaces-render.md` §15.3, §15.10, §15.15 to §15.17,
`spec/16-interfaces-host.md` §16.2, §16.7

## Interface to implement

Exactly `spec/15-interfaces-render.md` §15.10: upload the atlas once and
copy the Art2D quads into one mesh. Copy the behaviour from the spec; this
file does not restate it.

## Events

Emitted: none. Consumed: none.

## Tests to pass

No behaviour test exists in CI (`15` §15.3). The done-condition is the
`unity-build` workflow (`16` §16.2) plus a line-by-line review against
§15.10.

## Performance budget

As `15` §15.10 and §15.14 state.

## Done when

- [ ] Backend matches `15` §15.10, checked line by line by the Reviewer
- [ ] `unity-build` green (`16` §16.2)
- [ ] No writes outside writable paths
- [ ] Reviewer approved

## Worker notes

**MERGE AS SOON AS GREEN.** From T-051 merging until this task merges, the
merged backend (`RoleCount = (int)ColourRole.LaneClosed + 1` at
`RenderBackend.cs:14`, with the palette indexed by role in `FillMesh`)
throws an out-of-range index **on every frame** of a playable build that
draws one of the four new roles, which the airside scene always does. The
checkpoint smoke is unaffected: in batch mode the bootstrap's `Awake`
deactivates the object, so the backend's `Start` and `Update` never run
(`16` §16.7). The Integrator should not queue this behind other merges.

The Reviewer checks the backend against §15.10 line by line; there is no
test to catch a deviation.
