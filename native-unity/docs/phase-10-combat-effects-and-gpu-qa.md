# Phase 10 — native 3D abilities and GPU combat-effect verification

## What is implemented

This phase extends the Phase 9 gameplay foundation on the native Rift/Unity URP Player. All hero attack/dash/skill distances, timing and FX shapes remain **provisional** unless original source animation/artist-authored VFX were separately imported and independently verified.

### Game mechanics

- **Blitzcrank Q**: real in-flight collision remains physically authoritative. A hit now interpolates the victim toward the validated pull landing rather than teleporting; it neither directly captures nor bypasses the target's existing basic-attack capture immunity. E is still consumed only on a genuine valid basic hit.
- **Riven Q/E**: successful legal dash paths are time-interpolated, preserving wall collision. Blocked or zero-length attempted dashes do not consume the cooldown. Riven W's stun consumes its cooldown only when at least one eligible physical target is hit, preserving AI's escape option where appropriate.
- **Yasuo Q**: a real `NativeDirectionalProjectile` advances on each frame and physically collides with terrain, enemy windwalls and opposing champions, including a concealed champion hit by an already-launched blind skill. It is *not* an automatic capture. Ordinary Q stacks are granted on **contact**, not on cast; third-cast tornado consumes existing stacks on launch and applies a non-damaging knockup on physical contact.
- **Yasuo E**: can now dash through a currently visible opposing champion, in addition to separately verified neutral monster targets. Both exit and full swept path must be navigable. An explicit per-target lock plus short global recast lock prevent unlimited looping. No skill directly captures or damages a monster.
- **AI loadouts**: role-aware starting and secondary skills; hider Riven/Yasuo prioritize E for escape, and learned Q after Level Up; hunters retain a Q/W control path. Blizzard E is hunter-only.
- **Garen**: actual Q movement and W tenacity windows remain, with live HUD cooldown/active state. Garen E (pure damage) remains excluded by the project's agreed no-skill-damage rules.
- **Fog/privacy**: every combat effect is culled according to the human player's or currently spectated ally's own line of sight. Physical blind hit detection is allowed by the authoritative rules, but it never automatically reveals a hidden victim's exact position through contact effects.

### Diagnostic 3D FX (not original licensed champion particle artwork)

- Blitz Q: moving spherical projectile head, a swept 3D line and short trail.
- Yasuo Q: in-flight head/trail; third-cast spiral diagnostic.
- Yasuo W: 3D wall perimeter and five vertical energy ribs, anchored to the authoritative wall collision segment.
- Riven/Yasuo E and Riven Q: continuous world-space dash trace following timed legal movement.
- Skill contact: visible-only horizontal/vertical rings that do not reveal a concealed target; the observer must be able to see the victim itself before the effect appears.

All new materials, particles and GameObjects are local to a round and released when the round ends.

## Automated test coverage

### EditMode source tests

`NativeSkillProjectileTests.cs` contains 5 tests for in-flight contact, blind collision, terrain collision, wall interception and invalid input. `NativeSkillLoadoutTests.cs` contains 5 legal loadout/role tests. `NativeChampionCombatTests.cs` adds targeted tests for Yasuo hero-target E, rejection of unseen targets, miss-only Riven W cooldown and Q stack lifecycle.

Previous Phase 10 commit `3eaec06d` passed **89/89** Unity Editor EditMode tests and the original GPU gameplay smoke test. This prior result does not verify the newly added Phase 10 GPU screenshot driver on subsequent commits.

### New, separate Windows GPU FX acceptance

The workflow `.github/workflows/unity-windows-visual.yml` now launches an additional Windows Player process with `-hextechPhase10QA`. The new `NativePhase10VisualQA`:

1. Waits for the *actual* Rift GLB and navigation to load on the hardware GPU.
2. Uses a locally verified walkable segment near the real player's calibrated position for effect placement.
3. Captures five 1280×720 PNG evidence files:
   - `phase10-blitz-hook.png`
   - `phase10-yasuo-tornado.png`
   - `phase10-yasuo-windwall.png`
   - `phase10-riven-dash.png`
   - `phase10-visible-impact.png`
4. Verifies every stage has visible Unity renderers, then tests `Tick(observer => false)` suppresses all effect renderers and `Tick(observer => true)` restores them.
5. Explicitly verifies that an *unseen impact* does not create a transient cue, while an allowed visible impact does.
6. Emits `phase10-visual-result.json` and exits with nonzero status on an error. The CI job validates the report, process code, PNG file size/signature/dimensions and evidence count.
7. Retains the screenshots and logs alongside existing GPU evidence in the local self-hosted runner's `HextechVisualEvidence/<run_id>/evidence/` folder. This path is runner-local; it is not automatically published on GitHub.

`CAPTURED_REQUIRES_VISUAL_REVIEW` means *a real screenshot was produced*, not that a human has approved model fidelity or original VFX likeness.

## Known limitations and next steps

- Real original full hero rigs for Blitzcrank, Riven and Yasuo, corresponding cast/follow-through animations, attachment sockets, shaders, sounds and particle textures have not been bundled into the repository. Source-verified animation bindings must be created separately and may only point to clips that are actually present.
- Phase 10 player input remains Garen-only; other adapted hero abilities can be used by AI in the native game. Full four-hero player selection/control remains a separate feature, not achieved merely by creating their scripts.
- The visual acceptance is a deterministic **diagnostic rendering smoke**, not a full gameplay replay for each skill. It checks visibility and the presence of renderers/screenshots, not perceptual visual quality; camera framing, proportions, aesthetics and motion blending still need human review.
- Attack keyframe contact and skill speeds/distances remain provisional. Physics should eventually read original animation event/contact markers.
- Confirm new Editor tests, new dedicated Phase 10 GPU screenshot pass and actual evidence from the specific commit before claiming successful acceptance.
