# Native Unity Phase 2 — real Garen skinned 3D model

Date: 2026-10-31. Scope: original 3D Garen rendering and native motion; existing `dist/` web client remains untouched.

## Source inventory and actual reuse

- Source `dist/assets/native/garen.json` + `garen.bin` + `garen_base_tx_cm.png` already in repository; 3,932 mesh vertices, 19,008 indices (6,336 triangles), **68 bones**, original idle1, run, attack1 and spell1 motion tracks at 30 FPS.
- Original Map11 `map.glb` remains downloaded by pinned SHA-256 before Unity editing; original 295×296 navigation.bin remains shared with the web baseline. The Unity gameplay does not repurpose WebP character sprites.
- The other three champion native sources are not committed; do not claim four native playable models. They will be prepared from pinned original .lmesh/.lanim/texture inputs in a later phase.

## Changes

- `GarenRigRuntime.cs`: native geometry buffers, UV, joint indices, weights, texture, 68 skeleton transforms and bind poses loaded; `SkinnedMeshRenderer` constructed at runtime. Old format bone variants with no local matrix derive local rest pose from the parent/world bind transform. Motion tracks are decoded once, not every frame.
- `RiftBootstrap.cs`: after glTFast Map11 import, spawn original Garen model at navigable world position, update 3D actor location/forward rotation via existing A* waypoint motion. Input right-click = move, A = original attack1; idle/run chosen by travel. **No stand-in marker** is accepted as player art.
- `prepare_assets.py`: fetch/verify original Rift GLB and copy Garen original buffers/PNG. `HextechNativeSetup.BuildWindows()`: require all Garen files before packaging Windows output.
- CI: `validate_migration.py` cross-checks the actual original mesh binary view offsets, attribute counts, 68-bone hierarchy and available motion names; original GLB fetch and Python syntax checks preserved.

## Validation gates

- GitHub Actions Python source/data contract: automated and expected; record latest workflow result.
- Unity Editor C# compilation, package import, GLB handedness and original skinned mesh appearance: **NOT EXECUTED** because this tool environment has no Unity Editor or licensed Windows GPU runner.
- Windows Build Support / EXE packaging: **NOT EXECUTED**. Source code alone is not proof of a runnable game.
- Visual acceptance to run next: show original Garen in real Rift mesh (idle/run/attack1), verify visible triangle winding and UV orientation, root height, character direction, 55° camera, path collision. Capture screenshots and Unity Player.log; check stable FPS on Windows hardware.

## Known limitations

Source skeleton is a direct original-format port, not a Unity-exported prefabricated Animator controller. Clip sampling is currently source 30 Hz without interpolation/blending. Current basic navigation height remains approximate pending original GLB coordinate mapping calibration. Shader culling is temporarily disabled to diagnose orientation and must be revisited after visual acceptance. Dynamic spell/capture and all other champions are future milestones.

Original art copyright remains with Riot Games; source converter's MIT license does not automatically grant distribution rights to game artwork. Keep raw original assets in ignored StreamingAssets and verify distribution policy before releasing any Windows installer.
