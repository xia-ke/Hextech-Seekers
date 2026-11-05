# Phase 16 — real Map11 player skill chain and targeted-dash regression (implementation pending GPU acceptance)

## Scope

This change extends the existing `-hextechGameplayQA` Windows Player test. It retains Phase 15's ten established player-cast cases and adds **four** distinct evidence entries without changing the selected champion roster, capture rules, original Rift navigation or champion-art status.

### Riven Q1 / Q2 / Q3

A fresh two-actor human Riven hider round is configured through the existing role, offered augment and legal skill selection. Each Q is issued through the same `TryHumanSkill` method used by live keyboard input. The harness computes a navigable direction against the **actual loaded Map11** and advances the real `NativeDashMotion` to its destination. Each stage checks the source combat-stage transition, immediate second-press rejection, navigable swept line and absence of direct capture. For Q3, an actual opposing bot is staged at the computed walkable dash landing; the cast must lock that real bot through the regular controller control path, without changing capture state. After Q3 the test rejects a fourth press during the original Q cooldown.

The harness advances `NativeChampionCombat` by the minimum recast interval between casts rather than waiting for OS/frame scheduling; it does not use mock `NativeSkillImpact` or directly set the Riven Q stage. This is deterministic API-path Player validation, **not physical key injection** or timed animation sign-off.

### Yasuo E versus a visible enemy

A separate human Yasuo hider round is configured through the live roster/augment/skill flow. An opposing bot is placed on a real navigable, visible Map11 line segment with a valid legal landing beyond it. The test casts through `TryHumanSkill(E)` and completes the real dash, checking path validity and no captured/eliminated target. Then the real skill clock advances past its global cooldown; the same target must still be rejected by the longer per-target reuse lock.

No fake target struct is passed to the *successful* skill cast; the opposing bot appears in the regular `GetSkillTargets` list. The follow-up lock assertion calls `TryEDashThroughHero` on the actual player skill-state instance with that real target.

### Evidence and acceptance

`gameplay-result.json` adds:

- `targetedSkillSmokesExpected = 4`
- `targetedSkillSmokesPassed = 4` on complete success
- `targetedSkillEvidence` array: Riven Q1, Q2, Q3 and Yasuo E

`.github/workflows/unity-windows-visual.yml` now independently rejects absent, duplicate or incomplete Phase 16 entries. A generic `status=PASS` does not replace the per-case checks.

**Acceptance is pending:** source was changed and pushed to a branch but no actual Windows Unity Editor or GPU Player result has been inspected for this branch at authorship. If navigation placement/visualization fails in the real Runner, investigate with the resulting `gameplay-result.json` and `Player.log`, not by bypassing assertions.

## Not claimed

The EditMode suite also checks Yasuo E rejection of hidden, allied and eliminated hero targets without spending cooldown, followed by valid hero casting and per-target lock. This test uses a synthetic fully walkable grid; it must not be described as proof of original Map11 fog rendering.\n\nNo proof of Yasuo E on neutral monsters in a live Player, actual visually observed Riven Q3 knockup animation, 1:1 original LoL animation/VFX, literal external key automation, or general frame-rate performance. These remain separate follow-up acceptance paths.
