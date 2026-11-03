# Phase 12 — selected-target melee contact and fog-safe targeting

## User-visible change

The native Windows build previously let the player press **Tab** to select an
enemy, but **A** would always wind up on the nearest visible enemy, ignoring
that selection. The selection is now meaningful:

- **Tab** cycles available enemies (the existing Card Sense augment can retain
  an unseen selection for its own information mechanic).
- **A** arms contact for the explicitly selected enemy **if they are alive,
  opposing, visible to the local player and within the 130-unit basic reach**.
- Otherwise A falls back to the nearest valid enemy already inside reach.
  Without any eligible enemy, the attack animation may play, but no capture
  contact is registered.
- The original **0.23-second contact window** still rechecks sight, distance,
  game phase, target elimination, control effects, and capture immunity.
  Selection never guarantees a capture.
- The HUD shows a currently visible target's name, or **UNSEEN (no auto-attack)**
  without displaying a hidden target's live world position. Spectating never
  permits targeting via another teammate's private sight.

The existing original Garen mesh, ability Q/W selection, AI bots, Hextech,
fog-of-war and real map/animation/VFX implementations remain untouched.

## Code and verification

`NativeTargetSelection.cs` is an isolated C# target-arbitration component,
without rendering or Unity Scene dependencies, using a deterministic nearest
fallback. It excludes invalid ranges, non-finite distances, allies, eliminated
actors and unseen targets. The existing `NativeMatchController` invokes it
only when the human begins the real Garen basic-attack windup.

`NativeTargetSelectionTests.cs` covers preferred target priority, stale/out-
of-range fallback, unseen Card Sense selection, ally and elimination filtering,
reach bounds, malformed values, and spectator HUD privacy.

The Windows real Player's `-hextechGameplayQA` additionally asserts that the
selected target resolves on the actual loaded Map11 navigation and is rejected
once the real hunter basic attack eliminates it. These assertions are recorded
in `gameplay-result.json` as `preferredTargetAcquiredOnRealRift` and
`eliminatedTargetRejectedOnRealRift`.

Technical smoke validation is separate from final art review; the owner
previously explicitly allowed the Unity Trial watermark to remain visible.
