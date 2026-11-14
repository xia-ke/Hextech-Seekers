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

## Nov 14 League Player reference: river and jungle map region

The user provided an actual League of Legends (TM) Client recording (35.47s,
2544×1364, about 30fps) and a river screenshot (2048×1095). Unlike the old
native orthographic preview, the reference keeps the character relatively
small while showing broad terrain, both river banks and the neighboring
brush/monster sites in the same field of view. **Do not change original
Map11 model rotation to imitate the screenshot:** the renderer and navigation
have an independently verified glTF handedness correction.

The camera baseline therefore stays at perspective FOV 40°, pitch 56° and
a 2306-unit diagonal distance, with adjustable yaw, tilt and wheel zoom.
Actual GPU captures of 0°, ±45° and the mid-river hunter scene are requested
for side-by-side human framing review. The original client video is a
comparison reference, **not imported into or copied into this Git repo**.

### River-bush preparation and bounded wild-area play

The lower-half original navigation binary (`295×296` cells, 50 units each)
contains a real navigable grass group 23 (raw group ID 22, **not** the
255 no-grass sentinel) near native coordinates **x≈8524, z≈−6308**.
This position was independently selected from the untouched original Map11
bush channel, using clearance and a 3×3 grass-neighborhood interior check.
The hunter is frozen inside that river bush during the existing 120-second
preparation phase, and released once Pursuit begins. There is no fabricated
fountain or fake bush model.

`NativeJunglePlayArea` creates a **game-mode-only** walkable region using
the eight already-verified lower-jungle camp coordinates, the real river
brush, and A* connections on the original navigation. Allowed areas are
source camp neighborhoods and buffered real traversable routes. Areas
outside that region (not just outside the rectangular map bounds) reject
right-click routes, voluntary steps and skill/forced movement; both human
and AI use the SAME restricted `RiftGrid`. The raw Map11 nav bytes, camps,
visibility/brush IDs and map GLB are unchanged. The region boundary is an
intentional game design choice, not an official Riot jungle zone polygon.

A dedicated real Windows GPU gameplay check stages Garen as the hunter,
verifies actual grass group, 120-second lock/release, rejects an *originally
walkable* point outside the zone using navigation, teleport and forced
movement entry points, confirms an AI remains within the boundary, and
requires a legal route from the river brush into the Order Red camp. In
addition to the eight UI/fog/yaw PNGs, a ninth genuine GPU screenshot
`ui-hunter-river-brush.png` records the waiting area for visual comparison.

Original source camp dataset: pinned `iamywl/Web_game@
557a9d5352af12bee8bf3db879c22d04d0a0954e` Map11 placements; the
original source jungle GLB data is not checked into this repository.
