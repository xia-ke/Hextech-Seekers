# Hextech-Seekers Native — Unity + URP (Windows), Phase 9 native combat presentation

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

**Phase 4 limitations at the time:** the verified fountain, additional original hero meshes/skills, complete 2–4 roster UI, fog-of-war shader, wild monsters and Hextech effects were not yet part of that earlier slice. The subsequent phases below add code but have not undergone the same visual sign-off as the Phase 3 baseline.

## Phase 5: continue development before acceptance

The next source slice has been committed without using the Unity/Windows test result as a gate, per the project owner's request. See [docs/phase-5-multi-actor-hextech.md](docs/phase-5-multi-actor-hextech.md).

This adds 2/3/4-player hero **slots** (one original Garen, 1–3 distinct AI tactical markers rather than unreconstructed models), a full-screen 3-card Hextech selection screen, the exact eight agreed augment names, individual active cooldowns, stun/silence/capture immunity, time-clock changes, two Garen skill states, and allied spectator camera behavior.

**Do not equate a hero slot with an imported original hero mesh.** Blitzcrank/Riven/Yasuo source rig and animations, LoL-accurate scene fog and fountain, original animation hit contacts, plus verified wild-monster camp coordinates and their full entities are not implemented yet. Card Sense has an optional validated camp-data interface; it refuses to invent camp names when the catalog is absent.

The prior GPU screenshot acceptance still applies to the earlier Phase 3 baseline. No new Phase 5 GPU or visual acceptance is claimed here.

## Phase 6: private world-depth fog of war (source implemented; not accepted)

See [docs/phase-6-vision-fog.md](docs/phase-6-vision-fog.md).

The native URP player now has a `NativeVisionGrid` with current, explored, and unexplored world cells; a depth-reconstructed 3D fog shader; and separate memory per hunter/hider. It is attached to the native gameplay controller after the match begins, and removed between rounds. Eliminated hiders can see a living ally's own private mask while spectating, not the union of every hider's explored territory. Enemy tactical AI markers remain governed by their personal navigation LOS and brush concealment rather than the rendered fog alone.

Six extra EditMode test cases for this logic have been authored. As requested, we are **continuing development without waiting for Windows CI or GPU visual acceptance of Phase 6**. Full LoL-equivalent vision geometry, original character asset import and enemy-only grass hiding are still not verified pixel-for-pixel.

## Phase 7: grounded neutral skill targets and source-verified spawn points

See [docs/phase-7-neutrals-and-spawns.md](docs/phase-7-neutrals-and-spawns.md).

The native project now includes passive, indestructible, non-attacking neutral Unity entities with trigger colliders, created **only** at manually verified Map11 camp locations when `Assets/StreamingAssets/Rift/camps.json` is supplied. With no verified camps, zero monsters are spawned rather than making up locations or claiming the diagnostic rings are original monster art.

A new `NativeYasuoSkillState` supports Yasuo Q's two landed stacks and third tornado, including contact with a neutral target without damage/capture, plus target-restricted E dashes over a legal monster with a per-target reuse lock. The existing Yasuo AI tactical actor can build stacks on verified neutral targets, but the actual Yasuo model/projectile/animation are still missing.

`NativeSpawnCalibration` optionally reads `Assets/StreamingAssets/Rift/spawns.json` with manually verified source coordinates and navigable fountain/hider entry anchors. When absent, the preexisting provisional staging points remain, and the UI does not pretend they have been calibrated as the original fountain.

Eight neutral/ability logic tests and five spawn provenance tests have been authored. **This phase has not been visually or operationally accepted**, as requested; the last formally accepted original-Garen GPU baseline is a separate earlier commit.

## Phase 8: native champion combat and optional real source rigs

See [docs/phase-8-champion-combat.md](docs/phase-8-champion-combat.md).

- Blitzcrank Q now uses an actual time-of-flight contact sampler along the navigable Rift, with physical enemy collision (including blind hits), AI sight-gated aiming, wall/windwall blocking and a no-damage pull.
- Riven Q is a three-cast directional dash state with a provisional recast interval and third-hit airborne effect; W stuns multiple nearby valid enemies and E dashes without letting an ordinary shield block capture.
- Yasuo Q can stack on actual verified monsters *or* enemy actors and apply third-tornado CC; W can interrupt the Blitz Q projectile, and E can borrow a verified neutral monster for escape.
- The original Garen skinned rig decoder now supports optional **source-verified** Blitzcrank/Riven/Yasuo meshes/animations and the bot controller will use them when available. No extra original champion assets are present in Git by default. Missing assets keep explicitly labeled AI tactical rings.
- `native-unity/tools/import_native_hero.py` accepts a local original `.lmesh`/`.lanim` source and stages a hash-checked model/animation conversion without downloading/committing Riot art.
- Additional source-level NUnit/EditMode combat tests are authored, **not executed or accepted for this phase** as requested. Previous Phase 3 GPU acceptance is not retroactively extended to this new code.

## Phase 9: original-source animation clips, 3D diagnostic combat FX, attack contact timing

Detailed implementation and deferred-acceptance boundary:
[docs/phase-9-animation-and-combat-vfx.md](docs/phase-9-animation-and-combat-vfx.md).

The optional source-verified `animation-bindings.json` now maps actual original hero Q/W/E spell animation tracks, including Riven Q1/Q2/Q3, without guessing mappings or substituting unrelated idle/spell animation. The model converter keeps real original `spell*` tracks, and `import_native_hero.py --bindings` cross-checks each mapping against those converted clips.

The gameplay scene now draws temporary world-space **diagnostic** skill effects for Blitz Q's actual in-flight hook position, the 3D endpoints of Yasuo W's windwall and accepted Garen/Blitz/Riven/Yasuo skill events. FX are hidden unless the current player/spectated teammate legitimately sees the caster; unseen collision victims are not exposed by an impact particle position. This is diagnostic graphics **not Riot VFX art**.

A `NativeBasicContactWindow` delays basic-attack hit resolution by a provisional 0.23s rather than instantly capturing at button press. When the contact time arrives, the game rechecks live attack phase, range, LOS, stun and actual one-use capture immunity. Blitz E empowerment is consumed on a valid attack hit rather than by an E cast or missed basic.

Fourteen focused EditMode tests are authored across `NativeAnimationContactTests.cs` (9) and `NativeDashMotionTests.cs` (5); **these tests and the new GPU features have NOT been executed or accepted in Phase 9**, at the owner's request to keep implementing before a consolidated acceptance.

Model limitations persist: only Garen's original rig is in the repository. Other heroes require separately sourced, appropriately permitted genuine models, animation and art. Original skill VFX/pose matching, exact animation contact frames, audio and visual-quality review are future work.

**Additional Phase 9 motion change:** accepted Riven Q/E and Yasuo E movements now use `NativeDashMotion` to interpolate over 0.20s on a legal Rift path, rather than changing the hero's position instantly. AI will not acquire new ability targets mid-dash; stuns interrupt motion. Original animation root-motion timing remains to be measured against verified source clips.
