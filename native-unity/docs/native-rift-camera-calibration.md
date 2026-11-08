# Native Rift camera calibration — perspective, tilt, yaw and mouse rays

## The playtest regression

The first native Player used `RiftBootstrap.LateUpdate()` with
`sceneCamera.orthographic = true`, `cameraOrthographicSize = 520`,
`cameraPitch = 55`, an ad-hoc distance `900`, and the focus point at
`ToScene(hero)` (which includes terrain height). On actual Map11 this
made hero size and screen coverage unlike the original Summoner's Rift,
and camera elevation could bob with terrain changes.

Do not conflate the **original map's required `mapYawCorrection=180`**
(which converts glTFast's handedness and original source's Z sign into
navigation coordinates) with the **view camera yaw**. Rotating Map11
meshes alone would desynchronize all navigation, brush/fog, attack,
monster and verified spawn positions.

## Reference camera baseline

The production native view now uses:

- Perspective projection: `fieldOfView=40°`.
- Downward pitch: `56°`, measured below the horizontal.
- Zoomed-out reference offset: approximately `1289.558` units back and
  `1911.849` units up (Euclidean camera distance ~=2305.99).
- Focus pivot: `(navigationX, y=0, navigationZ)` instead of changing
  height as the character runs over slopes.
- Default yaw: `0°` in the **verified native navigation axes**;
  optional player-side rotation is camera-only. This is **not** a claim
  that the exact Riot client yaw has been independently recovered.

These values follow public Summoner's Rift camera measurements
(https://www.reddit.com/r/leagueoflegends/comments/1f12kha/what_is_the_camera_angle_and_distance_in_the_game/ )
and orthographic approximation methodology
(https://summonersrift.mvilim.dev/about.html). The gameplay view is
**perspective**, even though map tile screenshot extraction deliberately
used extremely low FOV to approximate an orthographic map.

## Local controls (gameplay only)

| Key | Effect |
| --- | --- |
| Mouse wheel | Zoom, clamped between the original-like close/far camera distances |
| Y | Toggle camera lock to player / free camera |
| Arrow keys | Pan the free camera in screen coordinates |
| Space (hold) | Temporarily center on player (or current spectator target) |
| [ / ] | Rotate camera yaw -15° / +15° for calibration |
| Page Up / Page Down | Change camera pitch +3° / -3°, safely clamped |
| 0 | Restore original-style 56° tilt, 0° navigation yaw, full zoom-out, locked |
| V | Fog-opacity-only Normal / Light / Off (unchanged) |

These inputs run only when the normal match is playing, never when
selecting heroes, the Hextech augment, or legal Q/W/E skills. The
source-backed nav grid, line-of-sight, skill hit/capture and clicked
movement are unchanged. `RiftBootstrap.TryNavigateFromScreen` and
`TryGetWorldPointFromScreen` use the live camera's
`ScreenPointToRay`, so click-to-move remains aligned with the
active projection and rotation.

## Physical GPU evidence

The exact-branch real Windows GPU workflow must validate
`cameraPerspective=true`, FOV 40, pitch 56, yaw 0, and a rendered hero
inside the camera viewport. The existing six actual scene/HUD/visible
fog screenshot tests remain. Two additional true D3D11 images capture
the *same* Map11 role screen at camera yaw -45° and +45° to allow
comparison of map orientation without moving a single map/gameplay
entity. The original `ui-role-selection.png` is the 0° reference.

The JSON report enforces that the player stays at the same world
position while changing the preview yaw and that the scene/targeting
rules remain unchanged. It is deliberately reported as
`CAPTURED_REQUIRES_VISUAL_REVIEW`: automated checks cannot confirm
that model facing, perceived pitch/zoom, camera framing and visual
fidelity exactly match an external League client screenshot.
