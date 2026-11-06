# Native Unity UI visual acceptance — real GPU screenshot protocol

The first launcher/HUD/fog redesign was merged in PR #24. The screenshot
acceptance in PR #25 records the actual Windows Player (not painted
mockups) so playtest reviewers can compare it with the original screenshots
showing an exposed startup skybox, basic IMGUI controls and over-dark fog.

## Exact-revision reproduction

On a Windows machine with the fully assembled native Player build, run from
PowerShell (change the installation path and evidence destination):

```powershell
$game = 'F:\actions-runner\HextechBuilds\latest\HextechSeekers.exe'
$evidence = 'F:\actions-runner\HextechUiReview'
& $game -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -hextechUiVisualQA -hextechEvidenceDir $evidence
```

The game waits for the **real pinned Map11 and resource initialization**
then uses the actual champion/role -> independent Hextech -> legal skill ->
match route. It writes:

- `ui-role-selection.png`: selectable Garen/Blitzcrank/Riven/Yasuo
  and original-rig/marker status.
- `ui-hextech-draft.png`: real three independently dealt augments.
- `ui-skill-selection.png`: Garen's legal Q/W/E selection.
- `ui-hud-fog-normal.png`: actual hider preparation HUD in Normal fog.
- `ui-hud-fog-light.png`: the same match with the Light visual setting.
- `ui-hud-fog-off.png`: same match with fog visual overlay disabled.
- `ui-visual-result.json`: screenshot integrity and private visibility
  invariants; success status `CAPTURED_REQUIRES_VISUAL_REVIEW`.

In CI the six PNGs and result JSON are preserved in
`F:\actions-runner\HextechVisualEvidence\<run-id>\evidence\`,
with a run-local ZIP under the parent folder. They are not published as
a public GitHub artifact because the screenshots contain third-party
Summoner's Rift art.

## Distinguish technical evidence from acceptance

Check that images were actually rendered at 1280×720, not blank, missing
or corrupted. Then review actual UI text contrast, button hierarchy,
hero visibility, countdown/cooldown readability and the same location under
Normal, Light and Off modes. Visible terrain should not become darker in
Normal; Light should be brighter than Normal; Off is a **visual debugging**
mode only. Compare screenshot region luminance where possible, but do not
mark visual quality as accepted based only on PNG file size or header.

Manual click testing remains necessary: role/hero selection, the selected
Hextech card, keyboard A/Q/W/E/F, escape/focus, resize, all normal game
state transitions, and any persistent overlay. This capture task does
not alter actual vision, targeting or scoring rules and makes no claim
of pixel-identical League of Legends artwork.
