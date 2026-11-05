# Phase 18A — original skinned Blue Sentinel art and idle on native Map11

## Goal

Replace the **Chaos Blue** camp's cyan diagnostic ring with one authentic
source-verified Blue Sentinel skinned GLB and its source idle animation. All
other seven verified Map11 camps remain passive diagnostic targets in this
phase. This work is a native **GPU rendering acceptance**, distinct from the
already-passing Phase17B Yasuo E interaction and collider rules.

## Asset chain and copyright boundary

- The source is the previously pinned upstream repository
  `iamywl/Web_game` at commit
  `557a9d5352af12bee8bf3db879c22d04d0a0954e`.
- Upstream asset: `public/assets/units/jungle/buff_blue.glb`,
  exact source length **3,179,272 bytes** and SHA-256
  `6d3181d21998e13783f0bd05ff571217b86714d0ec0f3bbaab424d5979fee1c9`.
- Upstream jungle manifest reports Blue Sentinel idle clip
  `sru_blue_idle_normal`, source skeleton and original animations.
- `native-unity/tools/stage_original_neutral_art.py` downloads a **pinned**
  source into persistent runner cache (hash and size checked), checks glTF
  skins/meshes/idle, converts any embedded WebP textures to PNG with the
  existing verified pipeline, and writes a local SHA-256 receipt plus
  `Assets/StreamingAssets/Neutrals/buff_blue.glb`.
- The original third-party GLB, derived GLB and texture bytes are **not
  checked into Hextech-Seekers Git**. They remain local to the Windows build
  and the generated Player, so use/distribution still depends on asset rights
  and should not be treated as a license grant.

## Unity behavior

`NativeNeutralMonsterScene` continues to create one passive, kinematic
trigger target at each source-backed camp. Before `RiftBootstrap.IsReady`
becomes true it checks the Blue source receipt, recomputes the local file
hash, imports the glTF with glTFast, requires real `SkinnedMeshRenderer`
vertices and the exact `sru_blue_idle_normal` legacy clip, loops that idle
and *only then* disables Chaos Blue's diagnostic ring. Missing source in
ordinary local runs retains the honest ring fallback; a partial or mismatched
source never masquerades as original art.

## GPU acceptance

The Windows Player gameplay workflow stages and **requires** the source art.
It checks: original blue art count 1; skinned vertices positive; real idle
clip running; renderer inside the real camera frustum; and a PNG D3D11 screenshot
`original-neutral-blue-gpu.png` with a valid PNG signature. The earlier
**10 ordinary + 4 targeted + 1 vision + 1 neutral** skill smoke gates remain.
A completed PNG is technical rendering evidence only; mesh appearance,
placement and fidelity to the live League client still need human image review.

## Remaining

- Replace other seven debug camp rings after this one-source pipeline is
  validated on a real GPU. Do not call an unimported model an original rig.
- Evaluate original model facing, foot-to-ground alignment, animation scale,
  transition behavior and lighting visually, then refine without altering
  passive/un-killable monster rules.
- No claims about license to redistribute Riot artwork.
