# Phase 15 — four human champions: actual Windows Player skill-cast regression

## What this phase actually tests

Phase 13 made Garen/Blitzcrank/Riven/Yasuo selectable by the **human**,
but the real Player smoke mostly asserted roster identity and hunter capture.
Phase 15 now exercises **the same NativeMatchController.TryHumanSkill**
routine used by the live keyboard Q/W/E handlers. For real Windows GPU Player
QA only, it passes a known, navigable aim vector rather than relying on
an unpredictable mouse cursor position on the automated Runner.

The authoritative game/rules, selected legal skill, lobby augments, actual
original RiftGrid, cooldown clocks, collision path and player controls
remain live. No mocked result, invented source animation or forced captured
state can satisfy the tests.

The ten verified hero/skill pathways are:

| Champion | Role | Cast | Effect expected |
| --- | --- | --- | --- |
| Garen | Hunter | Q | Brief speed buff, valid cooldown |
| Garen | Hunter | W | Tenacity window, valid cooldown |
| Blitzcrank | Hunter | Q | Actual in-flight hook starts |
| Blitzcrank | Hider | W | Movement speed boost |
| Blitzcrank | Hunter | E | Next legitimate basic is armed (skill itself never captures) |
| Riven | Hider | Q1 | Real swept-path dash begins, combo stage 1 |
| Riven | Hider | W | Real opposing target in range is briefly crowd-controlled |
| Riven | Hider | E | Real swept-path dash begins |
| Yasuo | Hider | Q | Real forward Q projectile starts |
| Yasuo | Hider | W | Actual active windwall object exists |

For every case the test builds a **fresh two-character round**, chooses
the skill through the *real* hero loadout, uses a real offered Hextech
choice, and checks:
- Hunter cannot cast any ability during the two-minute preparation and
  can do so after `rules.Advance(PreparationSeconds)`.
- Hider can cast during preparation.
- The chosen effect manifests in the actual C# actor state.
- Immediate recast is rejected by cooldown, active projectile, or dash lock.
- Skill cast cannot directly increase `CapturedCount`, eliminate the
  opposing hider or alter the current round phase.

Riven W uses a physical opposing bot positioned on the same walkable
navigation cell so that the real distance/line-of-sight checks succeed.
Riven Q/E choose the longest legal direction available among eight headings
on the actual Map11 navigation data. All other cast directions are
deterministic and unaffected by Window cursor state.

## How to inspect evidence

Existing workflow `-hextechGameplayQA` appends fields to the
`gameplay-result.json` report:

- `humanSkillSmokesExpected: 10`
- `humanSkillSmokesPassed: 10`
- `noHumanSkillDirectCaptures: true`
- `humanSkillEvidence: [...]`

These are necessary for `MATCH_QA_RESULT: PASS`. The Runner logs
`MATCH_HUMAN_SKILL_SMOKE_PASS` for every successful cast and stores
`gameplay-result.json` in the existing evidence ZIP.

This is **gameplay API-path verification running inside the real Windows
Player**, not a literal external keyboard automation. It does **not**
claim original champion rigs and source-specific spell animation/VFX have
been acquired, or that difficult target-dependent Yasuo E and all three
Riven Q recasts are exhaustively accepted. Their implementation and unit
tests remain separate pending deterministic real-world target scenarios.

## Ongoing art constraints

Garen has original art. Blitzcrank/Riven/Yasuo remain source-missing
tactical markers unless the Runner owner locally stages authorized
original assets. Phase 14 GPU model-source acceptance remains independent
and continues to run after gameplay smoke.

Unity Trial watermark stays visible, recorded and nonblocking.
