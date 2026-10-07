# T-053 — `app.render` Unity backend: atlas upload and quad copy

| Field | Value |
|---|---|
| Status | QUEUED (not releasable until T-052 merges) |
| Module | `app.render` (Unity backend) |
| Assigned role | worker only (CI has no behaviour test for the backend, `15` §15.3) |
| Depends on | T-052 |
| Spec source | `spec/15-interfaces-render.md` §15.3, §15.10, §15.15, §15.18 "For the Planner" task 3 (Q-130, Q-131), §15.23 (Q-132 note); `spec/16-interfaces-host.md` §16.2, §16.7 |
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

**Q-131 notes (`15` §15.10, §15.18 task 3).** (1) **No extra pass:** shadows,
the grass, surface textures and weathering arrive as ordinary quads in
`Fill`'s output, in list order. The backend adds no shadow pass, no second
texture, no second material and no sort of its own; the camera's clear
colour is the palette's background, which the ground tiles cover. (2) **No
reference kept to the atlas:** the managed atlas copy is released after
upload. The texture size and mip count come from `Art2DConstants`, not from
literals; the atlas is 4096 square with six mips (about 85 MiB of GPU
memory).

**Q-132 (`15` §15.23): this task does not wait and needs no follow-up.**
The backend calls `Fill` and copies its quads on every `Draw`, and M2
(T-057) adds no quad: it only moves and scales existing ones. The
draw-call rule, buffers and contract are unchanged.

The Reviewer checks the backend against §15.10 line by line; there is no
test to catch a deviation.
