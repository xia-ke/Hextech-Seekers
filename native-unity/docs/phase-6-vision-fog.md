# Native Phase 6 — private 3D fog of war (implementation handoff)

## Status and evidence boundary

Implemented in source; **not visually accepted**. The owner explicitly requested continuing implementation without waiting for another Windows Runner acceptance cycle. The accepted Phase 3 Rift/real-Garen capture baseline is a different milestone; this feature still needs an eventual Unity shader compile, GPU screen inspection and performance measurements. This is an independent game using an original 3D Rift import; it must not be described as a pixel-identical or licensed League of Legends renderer.

## New native implementation

- `Assets/Scripts/NativeVisionGrid.cs`: 295x296 visibility/exploration history on the original `navigation.bin` coordinate grid. `Reveal(observer)` traces navigable clearance for blocking terrain, including visible near-facing wall tiles, but does not allow LOS rays through walls. Its terrain visibility ignores brush concealment so the physical floor/bush remains renderable. The separate preexisting `NativeSight.Visible` determines whether a character inside brush is visible.
- `Assets/Resources/Native/WorldDepthFog.shader`: URP fullscreen transparent pass samples the camera's scene depth and reconstructs **world-space x/z** for each rendered pixel, sampling the current private visibility mask. Because it reads the real opaque scene depth, it shades 3D landscape meshes and depth-occluding walls rather than replacing the Rift with a flat, top-down map. The current map yaw/coordinate alignment has not been visually calibrated.
- `Assets/Scripts/NativeFogOfWar.cs`: GPU overlay and color-mask upload with 0.17 second refresh. The runtime updates **one separate exploration map per participant** and presents only the human's map or a legal living ally's map when spectating. Its refresh interval and opacity are provisional. Renderer and textures are cleaned up between matches and on controller destruction.
- `Assets/Scripts/NativeMatchController.cs`: enables fog after successful initial 2–4-person setup, refreshes it during gameplay and after round conclusion, and turns it off upon returning to role/Hextech selection. If the shader fails to initialize, the UI and Player.log explicitly indicate that world fog is unavailable; the game does not claim to be visually accepted.
- `Assets/Editor/HextechNativeSetup.cs`: enables the URP depth texture on the actual generated pipeline asset, which the custom shader requires. `-hextechVisualQA` still disables the match controller and therefore will not attempt to create Phase 6 fog.

## What is hidden, and for whom

A **currently visible** grid cell has zero overlay opacity. A **previously explored** but currently unseen cell receives medium darkness. A **never explored** cell is nearly dark; no enemy marker is drawn through the darkness. Fog tracks actor-specific personal vision; hiders do not union their personal view or exploration with teammates. If a captured hider spectates a living ally, the **ally's own** explored and visible grid is used; when no legal ally exists, the screen stays dark rather than retaining a full-map wallhack from the eliminated player. The AI's navigation LOS uses existing `NativeSight`, independent of the human's overlay.

Brush does not black out its terrain as if it were a solid wall. Characters inside brush remain concealed when `NativeSight.Visible` denies sight.

## Quality/performance scope, not yet claimed

The renderer samples the **opaque camera depth** for its fog. Transparent particles, URP postprocessing, off-screen objects and unsupported depth paths may require additional treatment after actual GPU review. It is a first source implementation of the confirmed vision/darkness direction, **not** a finished recreation of League's exact vision polygons, brush-edge behavior or terrain-normal projection. The fog mask is sampled bilinearly to soften a single 50-world-unit cell edge. The provisional vision radius is `NativeSight.BasicSightDistance` and must eventually be calibrated to the target game version.

No full-map enemy positioning, spectator wallhack, rune setup, summoner spell or fictional imported hero model is introduced by the fog module.

## Tests and later acceptance

`Assets/Tests/Editor/NativeVisionGridTests.cs` adds six independent tests:

1. Initial dark state and per-observer vision radius.
2. Previously explored cells become shaded again after movement, and a new round resets history.
3. Map wall clearance blocks vision beyond the near face.
4. Brush conceals a distant enemy unit but not the local visible terrain.
5. Two hiders never share current or previous terrain exploration.
6. Correct RGBA fog mask encoding and buffer bounds rejection.

**Tests have been authored but not executed as an acceptance requirement in this iteration.** Existing source/Unity CI configurations are not removed; the Windows GPU and shader visual checks are deliberately deferred, as requested.

## Remaining game milestones

- Review fog shader visuals across actual original Rift terrain and verify map handedness, wall/brush projection, D3D11 URP depth and framebuffer alpha.
- Import licensed/allowed original rigged Blitzcrank, Riven and Yasuo assets and real ability presentations; the currently labeled tactical markers remain placeholders.
- Calibrate actual fountain locations and unlock boundaries, hit-animation contact windows, and real monster entities at confirmed jungle camps.
- Match exact original champion/LoL ability targets, speed, collision and brush visibility semantics; tune provisional numbers.
- Full visual QA and gameplay input interaction; do not infer a screenshot's artistic correctness from a green source-logic job alone.
