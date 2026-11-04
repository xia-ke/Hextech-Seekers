# Phase 16 — Yasuo E hero contact + complete Riven Q combo on Windows GPU

This phase upgrades the real Unity Windows Player acceptance suite beyond
single Q/W/E casts. Two more **human-controlled** skill scenarios call the
**same `NativeMatchController.TryHumanSkill` entry path as player input**
with true map navigation, real skill cooldowns and real actor loadouts.

## 1. Yasuo E through a visible opposing champion

The test starts a fresh two-participant Hider round, learns E through the
legal selection screen, then finds a camp-free, unobstructed 340-world-unit
corridor on the actual imported RiftGrid (initially searching near the
verified Hider spawn, then real nav-grid centers). It does **not** disable
fog, mark a hidden enemy as visible, allow a blocked dash, or use a fake
skill-hit return.

The opposing hunter is placed 170 units in front of the human at a valid
navigable point. The test confirms the opponent is actually visible,
casts the real E, and confirms the destination passes **beyond** the target
while the entire swept path is clear. The dash completes using
`RiftBootstrap.TrySetExternalPosition(humanDash.Step(...))`, the same
motion code used by `Update`.

After E's global 0.5-second recast delay expires, the test returns the
player to the initial location and checks that casting E on the same target
is **still rejected** by Yasuo's independent 8-second per-target lock.
The corridor intentionally excludes neutral monster targets so that a
different legally available monster E cannot create a false failure.

## 2. Riven Q1 → Q2 → Q3 against a physical enemy

The test creates a fresh two-player Hider round, selects Q, and casts each
stage via the real player entry point. The current actual RiftGrid decides
the longest safe dash direction out of eight headings at each stage.
Each stage must begin a real nav-authorized dash, must reject a duplicate
cast while moving, and must finish through
`RiftBootstrap.TrySetExternalPosition(humanDash.Step(...))` before the
minimum recast delay advances. The internal combo stage must follow
`1 → 2 → 0` (third completes the combo).

Just before Q3, the opponent is positioned at the actual reachable
landing point. The test verifies the third stage applies the control
effect and starts the complete Q cooldown. In particular, Q itself does
**not** eliminate the opposing hider or increment any capture score.

## Evidence and gates

Existing `-hextechGameplayQA` now adds:

- `advancedHumanSkillSmokesExpected: 2`
- `advancedHumanSkillSmokesPassed: 2`
- `noAdvancedSkillDirectCaptures: true`
- `advancedHumanSkillEvidence` with exact `Yasuo:Hider:E:...` and
  `Riven:Hider:Q123:...` records.

The real Windows GPU GitHub Actions workflow rejects a generic
`MATCH_QA_RESULT: PASS` unless both named advanced scenarios also match
their exact case signatures. Original map, playable match, 10 ordinary
human skill cases, original champion art QA, and five combat VFX screenshot
gates remain independently required.

This does not mean exact Riot skill timings, sounds, models or animations
were reconstructed. The champion rules are adapted to the agreed
hide-and-seek game. Optional original Blitzcrank/Riven/Yasuo 3D meshes
are still not bundled; staged source records and the Phase 14 real-GPU art
QA continue to distinguish missing assets from source-backed original rigs.
