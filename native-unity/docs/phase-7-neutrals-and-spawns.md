# Phase 7 — verified jungle targets, Yasuo target interactions and calibrated spawns

## Implementation status

**Source implemented and committed; compile/Editor/Windows GPU acceptance intentionally deferred**, following the project owner's instruction to keep developing before acceptance. This phase does not alter the original Web baseline or replace the verified original Rift Map11 GLB and Garen rig.

The repository contains only the original skinned **Garen** native source data under `dist/assets/native/`. Original rigged Blitzcrank, Riven, Yasuo and jungle monster assets are **not** present in the repository. None is silently substituted with an invented champion or a claimed original monster skin.

## Added actual neutral game entities (not original art)

- `Assets/Scripts/NativeNeutralMonsters.cs`: stable per-camp target identity, world position, navigation anchor, line-skill contact and target-only dash landing. Entities are explicitly `Alive=true`, `Aggressive=false` with no health, damage, death, attack, or respawn system. A monster skill hit does **not** directly capture a hider.
- `Assets/Scripts/NativeNeutralMonsterScene.cs`: for every **verified** camp, a real Unity GameObject with trigger SphereCollider and visible light-blue debug-ring mesh renderer. This is **diagnostic target geometry, not an original wild-monster character mesh**.
- `RiftBootstrap.cs`: prepares world entities *after* loading the verified original navigation/camp catalog. With no authenticated camp location file, the scene creates zero monster entities and logs `NATIVE_NEUTRALS_WAITING_FOR_VERIFIED_CAMP_COORDINATES`; locations are never guessed.

Each verified camp currently creates **one** passive target, rather than a whole multi-monster pack. The accurate camp composition, original models and behavioral animation require additional resources.

### Required manually calibrated camp file

Supply `native-unity/Assets/StreamingAssets/Rift/camps.json` with the already supported `NativeCampCatalog` schema:

```json
{
  "version": 1,
  "manuallyVerified": false,
  "camps": []
}
```

This is an **inactive schema template**, not real camp data. To activate it, measure Map11-native x,z coordinates from the actual 3D world and nav grid, populate distinct real camp names and positions, independently verify them and **only then** set `manuallyVerified: true`. The importer checks non-NaN/finite coordinates, unique names and a navigable neighborhood. No arbitrary coordinates are committed to the repository.

## Yasuo skill interactions

`NativeYasuoSkillState` is a mechanics-only layer, intentionally not a claimed original Yasuo rig or projectile VFX.

- Q: require Yasuo to have legitimately learned Q and to be able to cast (no stun or hero-skill silence). A successful skill contact with a monster can build Q stack 1 then 2. The third cast becomes a tornado and consumes stacks even if it misses, without doing damage or capturing. Cooldowns and stack lifetime are **provisional tuning numbers**.
- Q world hit detection: a finite directional segment and transverse width against an actual verified monster target, restricted by `RiftGrid.LineClear`. It cannot hit a creature behind a solid wall or beyond skill range.
- E: a valid target-only dash may land beyond a verified monster on walkable terrain; the same monster cannot be reused until its own temporary target lock expires. This is a standalone API awaiting an imported Yasuo model/actor controller. It does not move the monster.
- `NativeMatchController.cs`: AI Yasuo (still a clearly labeled non-original tactical marker) now automatically uses Q near a legally reachable monster. Its state can stack a tornado. No Yasuo champion VFX or health/damage is fabricated.

The same skill target contract is designed to be reusable for skill borrowing by Riven and Blitzcrank once their actual originals are available; no claim is made that their skill physics and animations have been implemented here. The existing `NativeSkillCatalog` still gates all legal Q/W/E slots.

## Fountain/entry positions

- `NativeSpawnCalibration.cs` is an **optional** verified-map location parser.
- `RiftBootstrap.cs` reads `Rift/spawns.json` if present. The source must include version 1, `manuallyVerified: true`, a nonempty source note, finite hunter/hider x,z values and a credible fountain radius. Both points must resolve to navigable Map11 ground.
- `NativeMatchController.cs` uses verified hunter/hider spawn coordinates for human/AI roles when available. If not available, it retains and **labels the old locations as provisional**, rather than presenting them as the League fountain.
- The confirmed 120-second preparation lock still forbids hunter movement and attacks, independently of source coordinates. No official physical fountain geometry or exit corridor is claimed yet.

Inactive `spawns.json` schema (DO NOT set manuallyVerified until both positions are measured on Map11):

```json
{
  "version": 1,
  "manuallyVerified": false,
  "source": "",
  "hunter": {"x": 0, "z": 0},
  "hider": {"x": 0, "z": 0},
  "fountainRadius": 0
}
```

The all-zero values are intentionally invalid and are **not** in-game positions.

## Authored (not yet executed) tests

`Assets/Tests/Editor/NativeNeutralMonsterTests.cs` includes eight cases: unavailable/unverified camp fallback, stable passive monster identity, directional/range/wall hit filtering, Yasuo Q 2-stack + tornado with no damage/capture, Q stack expiry, monster-assisted E and its repeat-target lock, and legal-skill gating.

`Assets/Tests/Editor/NativeSpawnCalibrationTests.cs` includes five cases for missing optional source, valid verified synthetic coordinates, provenance rejection, missing source note and off-map coordinates. These are synthetic test fixtures; they do **not** constitute a real-world fountain/monster coordinate verification.

**Tests have not been run or visually accepted in this phase.** Continue with the planned consolidated acceptance later rather than treating CI statuses as proof of original-model fidelity.

## Next development tasks

1. Obtain and provenance-check the original game character rig assets and animations for Blitzcrank, Riven and Yasuo; port the rig/animation import path without creating fake copies.
2. Inspect Map11 scene nodes, navigation and original game references to calibrate real camps/fountain. Commit only verified source coordinates, never rough guesses.
3. Add actual target/collider contact windows for the original champion skills, E dash movement/animation binding, and non-damage Riven/Blitz mechanics.
4. Connect wild-monster visuals only when licensed/provenanced source models exist, and then run the postponed end-to-end Windows gameplay/vision acceptance.
