# Hextech-Seekers Native — Unity + URP (Windows), Phase 1

**Status: SOURCE / CONTRACT CHECKED; Unity Editor, real Windows build and GPU rendering NOT YET VERIFIED.**

This directory coexists with the unchanged `dist/` web client. Open `native-unity/` with **Unity 6000.0.40f1** (Unity Hub; use same major if patch unavailable). Unity Package Manager resolves **URP 17.0.3** and **Unity glTFast 6.20.0**.

## First-run procedure

1. From the repository root: `python native-unity/tools/prepare_assets.py` — downloads the original pinned `map.glb` and copies the original `navigation.bin` into `Assets/StreamingAssets/Rift/`, verifying SHA256. The map is intentionally not checked into git.
2. Open `native-unity/` with Unity Hub. Allow Package Manager imports. Menu **Hextech Seekers → 1. Create Native Rift Phase 1** generates `Assets/Scenes/NativeRiftPhase1.unity`, a URP pipeline asset, a MainCamera and bootstrap script.
3. Run Play: it loads the **original map.glb** via glTFast (not the flattened 2D map tiles), and the **existing original navigation.bin**. Right-click to select a navigable destination. A cyan disc indicates the debug navigation position, **not an actual hero or gameplay model**.
4. In Unity Editor menu **Hextech Seekers → 2. Build Windows** or batch-mode: `Unity -batchmode -nographics -quit -projectPath <absolute/native-unity> -executeMethod HextechSeekers.Native.Editor.HextechNativeSetup.BuildWindows -logFile native-unity-build.log`. Windows Build Support (IL2CPP/Mono as appropriate) and Unity licensing are required. No claim of Windows build without running this.

## Phase 1 acceptance criteria

- Original GLB SHA-256 matches `bdabbf3e...`; original nav binary is exactly 523,920 bytes.
- Scene loads native GLB and demonstrates GPU-depth-based scene occlusion; not a Canvas sprite scene.
- Navigational click moves along the original grid and cannot cross unwalkable cells; camera follows the tracked point at 55-degree pitch.
- Windows build via Unity and real GPU screenshots/FPS are **pending external Unity-capable runner**.
- A separate 3D champion skeleton/animation conversion and exact map orientation verification are **Phase 2**, not silently represented by debug marker.

## Coordinate calibration

Original browser generation explicitly documents `x,y,-z`; glTFast imports glTF handedness into Unity (X inversion). Applying a 180° yaw around the original scene is the proposed transformation to align nav `x,z` with original meshes. This has **not yet been visually validated** against the real GLB in Unity. Editor exposes `mapYawCorrection` and `worldScale` for adjustment; coordinate/origin must be measured before accepting Phase 1.

## License & provenance

Original League of Legends map/champion art is attributed to **Riot Games**, distinct from MIT licenses of third-party format/conversion code. Reusing art inside a private prototype does not convey unrestricted redistribution rights. Do not distribute a game installer with original artwork without checking applicable Riot policy/permissions.