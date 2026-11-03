# Phase 13 — Four playable human champion identities (Unity Windows native)

## Goal and scope

The previous native Player fixed human identity to **Garen** while Blitzcrank,
Riven and Yasuo were AI-only. Phase 12 removes that restriction without
claiming the three missing original models are present. The local first-release
roster remains **2–4 distinct champion slots, exactly one hunter** and allows
the human to choose either role.

## Player experience

From the **Role** screen, select **Garen / Blitzcrank / Riven / Yasuo**. The
chosen champion cannot also appear as an AI. Choose **Hunter** or **Hider**,
then an independently dealt Hextech card, then one legal Q/W/E ability.
The player may learn a second distinct legal skill when reaching level 2
during preparation (e.g. through Level Up Gold).

- Movement: right-click original Rift walkable terrain; **S** stops.
- Basic attack: **A**; only a legal hunter basic hit at the contact moment
  captures. **A** does not capture for hiders.
- Hero skills: **Q / W / E**, only if learned, available and not disabled by
  stun/silence. Skill direction is taken from mouse aim on the map.
- Target cycling: **Tab**; active Hextech: **F**.
- Trial watermark remains an unmodified non-blocking technical warning.

Garen Q and W are playable; **Garen E is pure damage, so it is excluded**.
Blitzcrank E is legal only for hunters and still only empowers the *next
legitimate basic hit*. Riven Q has three separate recasts and a final
crowd-control strike; Riven W is an area stun and E a swept dash.
Yasuo Q advances a physical projectile and stacks toward a tornado; neutral
monster contact may stack without killing it. Yasuo W is a projectile blocker;
E uses a visible enemy or a verified neutral monster as a dash target.

No hero skill deals damage or invokes the capture rule directly. All ranges
and cooldowns are **provisional** until measured and play-tested.

## Authentic source art vs. missing models

The original **Garen** skinned mesh and its idle/run/attack animation remain
in the Player baseline. The other three models are not shipped by this
repository. Phase 11's importer can optionally stage their own source assets
from the persistent runner directory. When a selected original model is
missing/invalid, the player sees a **cyan tactical ring with an explicitly
named GameObject and lobby warning**, rather than a false Garen or fake
"original" hero skin. Gameplay identity and legal Q/W/E logic do not rely on
the presence of model art.

If an original imported rig exists, its **own** verified animations may run.
The runtime refuses to invent a source `spell1 = Q` mapping: an optional
`animation-bindings.json` must be verified against actual existing clips.

## Implementation and regression boundaries

- `NativePlayerRoster.Build` is authoritative for unique four-hero roster
  composition across 2, 3 or 4 players, with 1 hunter.
- `RiftBootstrap.SelectPlayerChampion` switches to the chosen original rig
  if available, or a clearly labeled diagnostic/tactical marker. Handles
  movement, teleport, camera follow and source/model cleanup.
- `NativeMatchController` uses hero-specific independent
  `NativeHeroLoadout`, `NativeChampionCombat`, `NativeGarenSkills` and
  `NativeYasuoSkillState`. Real projectiles continue flying when their
  caster is stunned after launch; forced pulls and swept dashes preserve the
  original navigation-wall constraints.
- Garen, when moved into an AI slot, retains speed boost (Q) and tenacity
  (W) instead of silently losing both.
- `NativePlayerRosterTests` checks all 4 selected heroes for both human
  roles, no duplicate IDs/slots, 2/3/4 participants, single hunter and
  role-restricted ability legality.
- `-hextechGameplayQA` additionally builds a 4-actor roster with **each**
  of the four possible human heroes, checks the authoritative human identity
  and actual art/placeholder state, then restores the original Garen-only
  two-player pursuit/capture/survival smoke. `gameplay-result.json` reports
  `selectableHeroRostersValidated` and
  `missingHumanOriginalArt`. Every source rig remains subject to Phase 11's
  separate staging checks.

## Not yet completed

1. Actual original Blitzcrank/Riven/Yasuo geometry, skinning, textures,
   verified skill clips and audio depend on separately authorized asset input.
2. Skills currently use provisional tuning, straight-line/geometry-based
   targeting and diagnostic 3D FX. Full original League art, unique bone
   attachments, sound, combo timing and visual similarity require independent
   visual design and GPU review.
3. This is a single-player versus AI prototype, **not** multiplayer or a
   production-ready client. The actual 3D artwork of all four heroes is not
   complete just because the selector and mechanics are testable.
4. A technical CI pass does not establish rights to distribute Riot artwork or
   Unity licensing approval.

## Verify

On the Windows Runner run the **Unity Editor on Windows self-hosted (Personal)**
workflow and **Unity Windows Player real GPU visual acceptance** workflow on
this branch. A complete technical pass requires EditMode tests, original
map/alpha material audit, 3 GPU animation PNGs, gameplay `MATCH_QA_RESULT:
PASS`, four selectable hero identities, and 5 Phase 10 combat effect
scenarios. Review the screenshot ZIP in `F:\actions-runner\HextechVisualEvidence`
for visual fidelity separately.

## Integration with the existing selected-target attack rules

Phase 13 is based on `main` after the separate Phase 12 targeted-basic-attack
implementation. It preserves the authoritative `NativeTargetSelection`
pipeline for **all four human heroes**:

- Tab cycles eligible opponents, including unseen Card Sense choices when the
  corresponding augment allows them.
- Pressing A prioritizes the selected *visible, living, opposing* enemy only
  when already within basic-attack reach, else the nearest other legal target.
- Damage-free ability casts are not captures. The 0.23s contact frame still
  checks line of sight, distance, phase, invulnerability and role.
- Hidden targets do not expose their names or position in the active HUD.

The remaining source-art limitation applies to both the players and the bots:
Blitzcrank, Riven and Yasuo are selectable as functioning gameplay identities
even when only a clearly labeled tactical ring is available. This is
deliberately not a claim of completed original champion models.

The original four-hero implementation was proposed in PR #3 against an older
baseline. This Phase 13 integration rebases its functionality onto the newer
targeting implementation without dropping either feature.
