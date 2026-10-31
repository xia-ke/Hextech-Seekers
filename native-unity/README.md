# Hextech-Seekers Native — Unity + URP (Windows), Phase 2

**Status: SOURCE / CONTRACT CHECKED; original skinned Garen importer added. Unity Editor compilation, Windows build, real GPU screenshots and character-animation visual checks remain NOT YET VERIFIED.**

This directory coexists with the unchanged `dist/` web client. Open `native-unity/` with **Unity 6000.0.40f1** (Unity Hub; use same major if patch unavailable). Unity Package Manager resolves **URP 17.0.3** and **Unity glTFast 6.20.0**.

## First-run procedure

1. From the repository root: `python native-unity/tools/prepare_assets.py` — downloads the original pinned `map.glb` and copies the original `navigation.bin` into `Assets/StreamingAssets/Rift/`, verifying SHA256. The map is intentionally not checked into git.
2. Open `native-unity/` with Unity Hub. Allow Package Manager imports. Menu **Hextech Seekers → 1. Create Native Rift Phase 1** generates `Assets/Scenes/NativeRiftPhase1.unity`, a URP pipeline asset, a MainCamera and bootstrap script.
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

**Current limitations**: no Unity Editor compile/Play Mode evidence; map GLB import-to-nav handedness/rotation unverified; normals, UV-flip and bone orientation must be checked in GPU screenshots. Original animations are sampled at the source 30 FPS without crossfade; physics collider and true terrain-height snapping remain pending. This is a source implementation milestone, **not an accepted Windows playable build**.
