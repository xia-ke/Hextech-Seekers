# Phase 8 — native champion ability contact and optional original rig loading

**Status: SOURCE IMPLEMENTATION COMMITTED; Windows Unity compile, shader/animation review and real GPU gameplay acceptance intentionally deferred as requested.** The accepted Phase 3 original Garen/Rift visual baseline is a separate prior result. This document does not claim that Blitzcrank/Riven/Yasuo's original artwork is available in this repository or that patch-perfect League of Legends abilities have been recreated.

## What is actually in source

- `Assets/Scripts/NativeChampionCombat.cs` is a reusable, actor-specific non-damage ability executor tied to each actor's legal `NativeHeroLoadout`, match phase, existing Hextech disable states and Map11 navigation geometry.
- `NativeSkillImpact` carries skill name, affected actors, optional movement landing, control duration and projectile-blocked status. **No skill calls the capture engine.** A capture still requires a hunter's subsequent valid basic attack through the existing Hextech/Mirror capture path.
- `NativeHeroControl` stores staggered stun/airborne lock windows independently for each participant, allowing incoming skills to stop human movement/attacks and AI navigation/casting without creating a fake health/damage system.
- `NativeMatchController.cs` now manages independent ability state per bot, advances ability clocks and in-flight projectiles, commits legal displacement/CC, applies basic-attack empowerment only when an actual AI basic hit occurs, respects player/hider personal sight and retains unique 2–4-person roster identities.

## Champion ability mechanics — provisional, not original patch-perfect

| Champion | Current integration | Important scope boundary |
| --- | --- | --- |
| Blitzcrank Q | Directional launched projectile with finite flight speed, continuous path sampling against the *actual navigation grid*, first physically collided enemy (including blind hits into grass), a pull landing near caster and small control window; stops on terrain walls and an active Yasuo windwall | Hook visual mesh, original cast windup, multi-hit physics and monster-pull policy require original-asset/mechanics review |
| Blitzcrank W | Own movement-speed window followed by a self-slow window, with a cooldown | Tuning constants provisional |
| Blitzcrank E | Arms next actual legal basic attack to knock up; arm alone never catches | Only legal for hunter in V1 skill whitelist |
| Riven Q | Three separately cast directional dashes; minimum recast delay, combo timeout, first-cast cooldown, dash stops at blocked terrain, third cast knocks up every valid enemy in its landing area | Multi-attack animation root motion/cast orientation and exact original timing not yet integrated |
| Riven W | Affects **all** visible enemy actors in a short-radius AoE; non-damage stun | No particles/audio or exact patch numbers yet |
| Riven E | Independent directional dash respecting walkability, no capture immunity from its ordinary shield | Source shield visuals/resources not loaded |
| Yasuo Q | Already supports stacking on actual verified non-aggressive neutral targets; now also checks living visible hero targets, two landed Qs charge a third tornado with knockup, never directly captures | Third Q tornado hit uses provisional collision footprint rather than finished projectile/VFX |
| Yasuo W | Creates a timed line-segment windwall; hostile Blitz Q projectiles crossing it are blocked; it does **not** block hunter melee basic attacks | Full League projectile/type table and visual wall animation pending |
| Yasuo E | Previously added legal verified-monster dash with per-target re-use lock; AI can now use a neutral target to escape a visible pursuer if it increases separation | Enemy-target dashes and exact original interpolation/FX still pending |

**Sight and collision are separate.** AI skill **aim decisions** use only the caster's independent `NativeSight` (no wallhack), but a skillshot/area ability **already fired** may physically collide with a currently concealed enemy along the path. This collision alone is never a valid basic-attack capture, and the hit test itself is not supplied to the AI as a new precise position oracle. This preserves the difference between brush concealment and projectile collision.

No unrelated rune, summoner spell, traditional damage/health, turret or minion systems were added.

### Model loading

`GarenRigRuntime.cs` remains named for compatibility with the earlier accepted Garen GPU visual build. Its core skinned-mesh and animation decoder now exposes a restricted `LoadChampion(parent, folder, hero)` entry for **Garen, Blitzcrank, Riven and Yasuo**. The manifest and buffer identity are checked, files must exist, and the real original idle/run clips are required. The existing Garen `Load()` path delegates to the same implementation.

The bot roster tries to load a real, locally supplied rig from `Assets/StreamingAssets/Champions/<Hero>/` and animates its original idle/run/attack1 tracks when those source clips are present. It hides the original rig outside the viewer's legal personal vision. When the source is missing, it retains a clearly named tactical marker; **it does not synthesize a counterfeit hero model**. The spectated ally's own rig remains visible without exposing other unseen enemies.

**Currently the repository contains the original native Garen files only.** Other real champion meshes and animations are not automatically downloaded.

To import already acquired and lawfully usable original source files locally, use the source converter wrapper:

```bash
python native-unity/tools/import_native_hero.py \
  --hero Riven \
  --source /private/licensed-source/riven \
  --stem <your-original-model-stem>
```

The source directory must contain a suitable `.lmesh`, its referenced `.lanim` and source texture. The existing `scripts/convert-native-champion.py` decodes source formats; the wrapper checks hero identity, native rig manifest, source mesh SHA-256, usable idle/run clips and converted files before installing them under `Assets/StreamingAssets/Champions/Riven`. Equivalent commands work for Blitzcrank and Yasuo. These local art paths are explicitly in `native-unity/.gitignore`.

The MIT license of the **model-format converter implementation** does not transfer Riot's ownership of champion textures, meshes, animation or packaging rights.

## Tests authored, not accepted

`Assets/Tests/Editor/NativeChampionCombatTests.cs` currently defines 13 test cases for hook travel time, wall-blocked attacks and blind hits on concealed targets, windwall intercept, Riven Q staged recasts and wall safety, multi-target W control, Garen-source fallback rules, W self-slow, E empowered basic semantics, status expiry and non-capturing skill effects.

The tests are committed but **no new Unity Editor/Windows Player run is being claimed as proof of this phase**. Existing automated workflows remain installed; they are not being used as a blocking acceptance gate for this continued development request.

## What is still needed

- **Source assets and permissions** for real Blitzcrank/Riven/Yasuo models, skin weights, textures, accurate skill clips and effect meshes; visually review each rig before removing tactical fallback.
- The exact source game's target-version skill ranges, projectile windup/acceleration, collision, attack contact-frame markers, bone attachment, animation blends, source physics, Yasuo E enemy target/stack resolution and Riven root-motion timings.
- Verified Map11 camp and fountain coordinates, real source monster meshes, actual terrain/fog visual calibration and staged match UI cleanup.
- Full native 3D ability visual QA with screenshots/video and real human mouse/keyboard control after the owner chooses to resume acceptance.

This is a substantial gameplay mechanics integration milestone, **not a completed four-hero native game or official League of Legends replica**.
