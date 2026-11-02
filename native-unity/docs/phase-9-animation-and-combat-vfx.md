# Phase 9 — animation binding, 3D combat FX and melee hit-frame contact

## Status and scope

**Source implementation committed; Unity Editor compile, Windows Player run, gameplay screenshots and visual sign-off are NOT claimed for this phase.** The owner explicitly asked to continue development before resuming acceptance. The previous verified Phase 3 Garen/Rift GPU screenshot is a baseline, not evidence that these new Phase 9 changes work visually.

The task is an independent native 3D hide-and-seek game using an original imported Map11 scene. The project must not misrepresent unimported champion models, VFX or arbitrary animation tracks as original League artwork.

## Implementation

### 1. Real source animation binding, never inferred by clip naming

`GarenRigRuntime.cs` preserves the accepted Garen idle/run/attack baseline and now supports `TryPlayOriginalSkillClip(exactClipName)`. It only plays an actually imported and existing clip, maintains an exclusive action state, then returns to idle/run when the clip completes. No game logic will claim a skill animation played when the source binding was absent.

`NativeAnimationContact.cs` defines `NativeAbilityAnimationBindings`. A hero's optional `animation-bindings.json` must be manually checked against that hero's actual, converted clip list. The loader rejects mismatched hero, absent sourceVerification flag, missing source clip names, invalid names and empty mappings. Riven Q stage 1/2/3 can reference **three separate actual source clips**; W and E are independent entries. A missing file resolves to `null`, not a guessed default.

Manual schema, **INACTIVE / UNVERIFIED EXAMPLE**:

```json
{
  "version": 1,
  "hero": "Riven",
  "sourceVerified": false,
  "q1": "",
  "q2": "",
  "q3": "",
  "w": "",
  "e": ""
}
```

Set `sourceVerified` to `true` **only** after inspecting and linking exact animation clip names from the appropriate original hero rig. Never invent `spell1=Q` merely because the spell name looks plausible. The optional file goes in `Assets/StreamingAssets/Champions/<Hero>/animation-bindings.json`.

`scripts/convert-native-champion.py` now preserves additional **real** `spell*` source tracks (and attack2) rather than discarding everything except spell1/spell1a. `native-unity/tools/import_native_hero.py` accepts an optional `--bindings <verified-json>` flag, cross-checks every mapped name against converted source clips before copying, and saves the binding alongside the local, Git-ignored original hero assets. Converter logic is MIT, but game art/model redistribution requires independent permission.

### 2. World-space diagnostic VFX grounded in the real skill state

`NativeCombatVfx.cs` draws real 3D world-position traces using the native Rift navigation height and a serialized URP material. It does **not** use original hero particle artwork or fake 2D sprites.

- Blitzcrank Q: an advancing beam from launch origin to **actual in-flight projectile tip**, updated after every authoritative flight step, disappearing on hit, terrain block, windwall interception or range expiry.
- Yasuo W: a vertical wireframe plane defined by **exact real windwall collision endpoints** and enabled only while the wall exists.
- Riven Q/W/E, Yasuo Q/E, Blitzcrank W/E and Garen actions: bounded brief pulse rings at the **caster location** after a real accepted ability event.
- Materials and GameObjects are created only when Phase 9 gameplay is started, reused for persistent traces and released on round exit. Transient effects have a cap of 32 and per-cue material cleanup.
- Visibility is evaluated for the **human player or the currently spectated ally** before each frame's effect presentation. An enemy caster hidden by wall or grass does not leak its position through beams/particles. Impacts show only a caster-origin effect, never an unseen victim's exact contact point. An independently controlled AI may aim only at targets in its own personal sight, but physical blind hits from existing projectiles remain possible.

These are **diagnostic** visuals. They do not reproduce original champion VFX, bone-attachment sockets, particles, audio or animation root motion.

### 3. Basic attacks now have a separate contact moment

`NativeBasicContactWindow` decouples attack initiation from the hit. The current provisional melee contact offset is **0.23s**. Human A input and AI hunter attack start an animation/contact intent; reaching the timestamp resolves the currently selected target **only if** it is still alive, legally visible, in range, and the hunter can attack. If the target has moved, hidden, the caster has been stunned or the phase ended, the contact does not capture. Mirror-type capture protection remains authoritative.

Blitzcrank E's next-hit empowerment is consumed by a valid physical basic contact, **not on E cast and not on a missed attack**. Its knockup can occur even when the target's specific capture immunity absorbs the subsequent capture. No champion **skill** calls `TryBasicAttackCapture` or `NativeMatchRules.TryCapture`; only a legitimate hunter basic attack contact does.

This is the first animation-aligned attack timing layer, **not** an imported per-hero attack-clip contact-frame database. Later original animation markers should replace the fixed provisional offset.

### 4. Existing game systems retained

- Original 3D Rift and original Garen skinned mesh rendering.
- 2–4 participant roster with one hunter, original role scoring, preparation/pursuit timing, individual hextech cooldown and skill whitelists.
- Per-actor fog exploration and hider spectator-camera switch.
- Original source-model loader for Blitzcrank, Riven and Yasuo where local source files exist; no fake model substitution. Verified clips are optional, and missing original model art keeps the labeled tactical marker.

## Source test coverage added

`Assets/Tests/Editor/NativeAnimationContactTests.cs` defines **9 targeted tests**: attack windup timing, duplicate hit suppression/rearming, invalid timing, missing binding fallback, Riven 3-stage clip mapping, unverified/wrong-hero source rejection, missing-original-clip rejection, empty map rejection and hero identity allow-list enforcement.

The prior Phase 8 combat geometry and Phase 7 neutral/camp tests remain committed. **These are authored test cases, not a claim that Unity has compiled or passed them for Phase 9.**

## Remaining work / deferred acceptance

1. Acquire authorized, provenance-checked original Blitzcrank/Riven/Yasuo meshes, skeletons, textures, spell animations and real effect art. Inspect/import each actual clip and write verified hero-specific bindings.
2. Match attack hit timestamps to real original clip frame events instead of the 0.23s provisional delay; validate idle/run/skill blending, clip speed and interruption in an actual Player.
3. Replace diagnostic 3D beams and rings with appropriately licensed effect assets, particle renderers and bone attachment sockets only when available.
4. Review real Windows URP output, shader/FX transparency, personal-vision rendering, map scale, animation root motion, original visual fidelity and hit registration through a consolidated later acceptance.
5. Fix discovered gameplay defects as the real native UI/visual acceptance becomes available; do not treat the Phase 3 static screenshots as Phase 9 evidence.

## Files changed

- New: `Assets/Scripts/NativeAnimationContact.cs`, `Assets/Scripts/NativeCombatVfx.cs`, `Assets/Tests/Editor/NativeAnimationContactTests.cs`
- Updated: `Assets/Scripts/NativeMatchController.cs`, `Assets/Scripts/GarenRigRuntime.cs`, `Assets/Scripts/NativeChampionCombat.cs`
- Updated tools: `native-unity/tools/import_native_hero.py`, `scripts/convert-native-champion.py`
- This handoff: `native-unity/docs/phase-9-animation-and-combat-vfx.md`
