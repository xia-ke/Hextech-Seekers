# Hextech-Seekers — Unity + URP migration review / Phase 1 handoff

State: Native project SOURCE COMMITTED; static verification passed; Unity Editor and Windows GPU verification pending.

## Source-grounded audit (GitHub main at start of migration: 48c3a20)

| Source artifact | Observed repo state | Unity migration action |
|---|---|---|
| `dist/assets/visual/summoners-rift/map.json` | 8192x7168 flattened tile extent, source map11 GLB pinned to `iamywl/Web_game` commit `557a9d...`, SHA256 `bdabbf...`, documented transform `x,y,-z` | Recover original `map.glb` by pinned URL and hash; import with glTFast directly as GPU 3D mesh. Do **not** use 2D tiles for native visuals. |
| `dist/assets/visual/summoners-rift/navigation.bin` | 523,920 bytes, 295×296, cell 50, native walkability, signed heights, flags and brush ids | Carry as raw, exact byte-for-byte binary into Unity StreamingAssets; C# port of nearest/path/clearance/lower-half. |
| `dist/assets/native/garen.json`, `.bin`, `.png` | Garen original converted triangle mesh, 3D bones, UV, weights, joint indices and animation tracks still stored in repo | Convert with source converter to Unity-compatible skinned mesh/animation in Phase 2. No need for already rasterized 2D sprite atlas. |
| Blitzcrank/Riven/Yasuo in `dist/assets/visual/*` | Rendered 2D sprite + depth atlases, but raw lmesh/lanim are not committed in `dist/assets/native` | Redownload fixed raw `.lmesh/.lanim/.png` through `scripts/fetch-visual-sources.py --issue006`; native rigs/animations are a separate format conversion task. |
| `dist/rebuild/match.js` | 2–4 total, hunter/hider, 120s prep, match durations, score and caught state | Port contract into C# deterministic pure domain models in later stage; do not try to invoke JS engine in native client. |
| `dist/rebuild/navigation.js` | A*, grid clearance, no diagonal corner cut, lower-half rule | Phase 1 C# port `RiftGrid.cs`; Unity test fixtures included. |
| `dist/rebuild/vision-baseline.js` | Range + raw nav-wall LOS + bush IDs, explicitly not LoL-identical | Use as preliminary reference only, never advertise as completed native LoL fog-of-war. |
| `dist/rebuild/live-session.js`, `encounter.js`, `attack-profile.js` | Single-process AI simulation and 2D attack geometry; timing data is estimated | Stage 2+ port and verify; native 3D geometry must not reuse approximate 2D hit boxes as final collision. |

## Phase 1 scope (implemented source)

- New `native-unity/` project, Unity 6000.0 LTS + URP + glTFast, Windows desktop target.
- Asset-preparation tool obtains original GLB, validates hash, copies nav binary; no original artwork is re-committed or bundled into this repository.
- Unity editor menu generates bootstrap scene, URP camera and sunlight. Original GLB loaded at runtime, not a substitute 2D map.
- Port original lower-jungle nav grid, valid-cell rules, near-target snapping, A* pathfinding, click-to-move and 55-degree following camera.
- Temporary cyan **navigation diagnostic marker**, clearly not a champion. No fake hero meshes or fabricated animation assets.
- Source/static CI (5 Python checks), Unity NUnit test files, Windows BuildPipeline entrypoint.

## Explicit acceptance gates not yet satisfied

1. Unity Package Manager resolve and C# compilation must be run in an installed Unity Editor. Current environment has no Unity Editor or license; this is NOT performed.
2. GLB material/mesh rendering, orientation, nav-to-map alignment and shadows must be confirmed in an actual Unity scene. The proposed map yaw of 180 degrees is not proven.
3. Windows .exe output and GPU FPS/frame-time trace must be captured on a real Windows GPU system. No 60FPS claims.
4. Four 3D hero skinned meshes/animation clips and interactions are not in this first-stage native scene.
5. Public distribution of Riot-origin artwork needs a separate rights/policy review.

## Next native stages

- Stage 1b: Run Unity Editor, inspect map import/coordinate handedness, generate first screenshot, validate click navigation against actual map mesh, build Windows smoke-test executable.
- Stage 2: Original 3D Garen skeleton/animation import and then Blitzcrank, Riven, Yasuo; no fallback to fake models.
- Stage 3: Native C# match timer/score/AI rules and player vision.
- Stage 4: Q/W/E skills, eight augments, real-world visual fidelity, balance and 2–4 player networking as required.

The old web version remains in `dist/` and is intentionally untouched.