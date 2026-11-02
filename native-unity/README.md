# Hextech-Seekers Native — Unity + URP (Windows), Phase 5 gameplay implementation

**Verified Phase 3 baseline:** Unity 6000.6.7f1, Editor tests, original Rift/Garen Windows Player and GPU screenshot technical acceptance all PASSED (Windows GPU run #41326056673; Editor run #41326056661). **Phase 4 gameplay vertical slice** (role selection, 1v1 tactical AI, preparation/roaming/capture/scoring) is now implemented in source; its newly added gameplay integration tests must pass before claiming the new phase accepted.

This directory coexists with the unchanged `dist/` web client. Open `native-unity/` with **Unity 6000.0.40f1** (Unity Hub; use same major if patch unavailable). Unity Package Manager resolves **URP 17.0.3** and **Unity glTFast 6.20.0**.

## First-run procedure

1. From the repository root: `python native-unity/tools/prepare_assets.py` — downloads the original pinned `map.glb` and copies the original `navigation.bin` into `Assets/StreamingAssets/Rift/`, verifying SHA256. The map is intentionally not checked into git.
2. Open `native-unity/` with Unity Hub. Allow Package Manager imports. Menu **Hextech Seekers → 1. Create Native Rift Phase 2** generates `Assets/Scenes/NativeRiftPhase2.unity`, a URP pipeline asset, a MainCamera and bootstrap script.
3. Run Play: it loads the **original map.glb** via glTFast (not the flattened 2D map tiles), and the **existing original navigation.bin**. Right-click to select a navigable destination. A real original skinned Garen model follows the navigation route. Press **A** to play the original `attack1` clip; idle and run clips are selected based on movement. No fake hero model or pre-rendered 2D sprite is used.
4. In Unity Editor menu **Hextech Seekers → 2. Build Windows** or batch-mode: `Unity -batchmode -nographics -quit -projectPath <absolute/native-unity> -executeMethod HextechSeekers.Native.Editor.HextechNativeSetup.BuildWindows -logFile native-unity-build.log`. Windows Build Support (IL2CPP/Mono as appropriate) and Unity licensing are required. No claim of Windows build without running this.

## Phase 1 acceptance criteria

- Original GLB SHA-256 matches `bdabbf3e...`; original nav binary is exactly 523,920 bytes.
- Scene loads native GLB and demonstrates GPU-depth-based scene occlusion; not a Canvas sprite scene.
- Navigational click moves along the original grid and cannot cross unwalkable cells; camera follows the tracked point at 55-degree pitch.
- Windows build via Unity and real GPU screenshots/FPS are **pending external Unity-capable runner**.
- The Garen skinned-geometry reconstruction and original idle/run/attack1 playback code are now in Phase 2. Unity Editor compile, on-screen validation (including mesh facing, UV orientation, bone bindposes and ground-to-map alignment) and real GPU test remain pending; four hero skills and online play are not claimed.

## Coordinate calibration

Original browser generation explicitly documents `x,y,-z`; glTFast imports glTF handedness into Unity (X inversion). Applying a 180° yaw around the original scene is the proposed transformation to align nav `x,z` with original meshes. This has **not yet been visually validated** against the real GLB in Unity. Editor exposes `mapYawCorrection` and `worldScale` for adjustment; coordinate/origin must be measured before accepting Phase 1.

## License & provenance

Original League of Legends map/champion art is attributed to **Riot Games**, distinct from MIT licenses of third-party format/conversion code. Reusing art inside a private prototype does not convey unrestricted redistribution rights. Do not distribute a game installer with original artwork without checking applicable Riot policy/permissions.
## Phase 2: original Garen rig — implementation notes

`tools/prepare_assets.py` now also copies the repository's original `garen.json`, `garen.bin` and `garen_base_tx_cm.png` into `Assets/StreamingAssets/Champions/Garen/`. It checks manifest identity and `attack1` presence; CI validates the binary buffer layout, vertex/UV/joint counts and source clip names. `Assets/Scripts/GarenRigRuntime.cs` builds the actual `SkinnedMeshRenderer`, all 68 bone transforms, bind poses and source animation tracks. Track samples are decoded once to avoid allocation every frame. It switches between idle1/run and one-shot attack1 via the shared `RiftBootstrap` navigation input.

**Current limitations**: Unity Editor C# compilation/EditMode tests have passed; Windows Player and GPU captures remain separately gated; map GLB import-to-nav handedness/rotation unverified; normals, UV-flip and bone orientation must be checked in GPU screenshots. Original animations are sampled at the source 30 FPS without crossfade; physics collider and true terrain-height snapping remain pending. This is a source implementation milestone, **not an accepted Windows playable build**.

## Phase 3: Windows Player build and real GPU evidence

The new `.github/workflows/unity-windows-visual.yml` runs on a **Windows x64 self-hosted Runner** labeled `hextech-unity` under the same desktop user whose Unity Personal license is active. Use Unity 6000.6.7f1 at `F:\\6000.6.7f1\\Editor\\Unity.exe` or set `UNITY_EDITOR_PATH`. Windows Standalone build support and a functioning interactive D3D11 desktop GPU session are required; a Windows service running in Session 0 is not a substitute for a graphical desktop.

On Unity-native code changes or manual `workflow_dispatch`, the job:
1. Checks out source and prepares/checks the pinned original Rift GLB, navigation.bin and Garen rig buffers.
2. Calls `HextechSeekers.Native.Editor.HextechNativeSetup.BuildWindows` using native Windows Unity Editor batch mode. The Player is built locally into `native-unity/Build/Windows/HextechSeekers.exe`.
3. Launches that **actual Player** on the Windows desktop with D3D11, 1280x720, `-hextechVisualQA`, and `-hextechEvidenceDir <runner-temp>/evidence`; waits for its exit with a bounded timeout.
4. Verifies a non-null hardware graphics API, loaded GLB renderers, original Garen skinned mesh, hero in the camera viewport, idle/run/attack1 state flags, frame measurements and three real screenshots.
5. Validates each captured PNG's CRC, minimum dimensions, color diversity and dynamic range using Python 3 standard library, then uploads `Player.log`, `Build.log`, `result.json`, `verification.json`, screenshots and a concise README.

**Acceptance distinctions:** A green GPU QA run proves that a real Player ran and generated non-blank native images on that Runner. It does **not** prove visual fidelity, correct skeletal deformation, map/gameplay scale, terrain alignment, materials, or screenshots' aesthetic similarity to League of Legends; review all three PNGs before signing off. A blank screen, missing map, missing animation, null/virtual graphics device, wrong checksum, startup failure or timeout fails CI rather than producing a false pass.

The native Player binary embeds original Riot artwork, so CI deliberately uploads **evidence only**, not a redistributable public game ZIP. The compiled Windows Player remains in the local Runner working tree after successful execution. Do not redistribute binaries/artwork without confirming appropriate permissions.

## Phase 4: native hide-and-seek gameplay vertical slice

Detailed rules, controls, implementation and limitations: [docs/phase-4-gameplay.md](docs/phase-4-gameplay.md).

After generating/building the scene, run without either QA flag to see the role selection menu. Play **Garen as hunter or hider** versus a visibly non-champion tactical AI ring; right-click to pathfind, S to stop and A to attack as a hunter. The rules engine uses the confirmed 120s preparation, 3/5/7-minute chase formula, 1-hit basic attack capture and source-specific survival points. The new Windows Player `-hextechGameplayQA` smoke (after the unchanged GPU screenshot acceptance) tests both roles in a native process and writes `gameplay-result.json`. See GitHub Actions to determine whether the current code passed.

**Still incomplete:** actual verified fountain, four original hero models/skills, real 2–4 participant UI, dedicated fog-of-war shader, wild monsters and eight hextech options. AI tactical ring and native-navigation LOS are diagnostic placeholders, not substitutes for an authentic League client view.

## Phase 5: continue development before acceptance

The next source slice has been committed without using the Unity/Windows test result as a gate, per the project owner's request. See [docs/phase-5-multi-actor-hextech.md](docs/phase-5-multi-actor-hextech.md).

This adds 2/3/4-player hero **slots** (one original Garen, 1–3 distinct AI tactical markers rather than unreconstructed models), a full-screen 3-card Hextech selection screen, the exact eight agreed augment names, individual active cooldowns, stun/silence/capture immunity, time-clock changes, two Garen skill states, and allied spectator camera behavior.

**Do not equate a hero slot with an imported original hero mesh.** Blitzcrank/Riven/Yasuo source rig and animations, LoL-accurate scene fog and fountain, original animation hit contacts, plus verified wild-monster camp coordinates and their full entities are not implemented yet. Card Sense has an optional validated camp-data interface; it refuses to invent camp names when the catalog is absent.

The prior GPU screenshot acceptance still applies to the earlier Phase 3 baseline. No new Phase 5 GPU or visual acceptance is claimed here.
