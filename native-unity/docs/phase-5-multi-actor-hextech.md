# Native Phase 5 — multi-actor and eight-Hextech implementation handoff

Implementation commit series: NativeHextech.cs, NativeGarenSkills.cs, NativeMatchController.cs,
NativeHextechDraftUI.cs, NativeCampCatalog.cs, NativeHextechTests.cs, and RiftBootstrap.cs.

## Status boundary

**Implementation in the Unity source is committed; this phase is deliberately not being visually accepted now.** The project owner requested continuing implementation before acceptance. Phase 3's previously accepted original Rift + Garen GPU baseline is unchanged in principle; this phase introduces new gameplay code that still requires later compile, mouse-control, performance and artwork review.

The original 3D map remains the verified Map11 GLB; the actual player remains the original skinned Garen. The three additional champion names are only **unique AI roster slots with clearly labeled tactical rings**, not fake reconstructed champion rigs. No minions/turrets, default rune pages or summoner spells have been added.

## Player flow (source implemented)

1. Open the existing native Rift scene. Configure total participants 2, 3 or 4 and play as hunter or hider. One hunter exists in either case.
2. Each AI independently rolls three unique eligible options from exactly eight Hextech entries; AI picks one. Players may receive the same option in separate rolls; hero identities may not duplicate.
3. The human sees a full-screen, three-card, silver/gold augment draft and selects one. This is an original runtime-rendered visual frame, **not** original TFT imagery.
4. Select Garen Q or W. Under Level Up Gold, receive level 2 during preparation and select the other skill. Garen E remains disabled under current pure-damage prohibition.
5. Start 120-second preparation: hunter immobilized. Begin pursuing for 3, 5 or 7 minutes according to hider count. AI searches, follows and flees using the same lower-half Rift navigation.
6. A successful basic attack within range/vision eliminates a hider; mirror protection can absorb one *valid* capture and grants a short provisional invulnerability window. Ordinary shields do not protect against capture.
7. Finish when all hiders are caught or the pursuit clock expires. Calculate the confirmed per-player score/ranking. A caught human hider can spectate a surviving allied hider; spectator tracking does not reveal extra hidden opponents.
8. Return to role selection and start a new single round.

## Implemented Hextech mechanisms

| Name | Engine behavior | Current limitation |
| --- | --- | --- |
| 冻结·金 | Valid visible enemy stunned 2s, disabling navigation, basic attack and hero skills; individual 120s cooldown | No source stun VFX yet |
| 星体结界·银 | Visible enemy silenced 5s; hero skills blocked but active Hextech remains available; 120s cooldown | No source silence VFX yet |
| 卡牌感应·银 | May select an **unseen** enemy identity; gives the nearest verified camp name only, without coordinates | Requires calibrated `Rift/camps.json` from real Map11; no names are invented |
| 卡牌感应·金 | Names nearest verified camps for all living enemies, without precise positions | Same verified coordinate dependency |
| 时光发条·银 | Pursuit duration ±60s chosen via UI, once per round; early-all-capture bonus threshold unchanged | UI still uses original alpha IMGUI outside the draft |
| 镜花水月·银 | Hider only: consumes first valid incoming capture and protects temporarily | 2.5s protection is provisional, not user-confirmed |
| 准备动手·银 | Passive movement modifier independent per participant | +8% provisional, needs balance and actual shoe representation |
| 升级咯·金 | Grants level 2 during preparation, allowing a second distinct whitelisted Q/W/E | Full original skill implementation only started for Garen Q/W |

Every active effect uses its holder's own 120s cooldown. Targets are checked by role, elimination and visibility, **except Card Sense's explicit information-access exception**. Stun prevents using active Hextech, silence does not. Target W-tenacity can reduce incoming control duration.

## Hero skills and artificial opponents

`NativeSkillCatalog` enforces the agreed initial skill choices:
- Garen: Q/W, no E.
- Blitzcrank: Q/W; E is hunter-only.
- Riven: Q/W/E.
- Yasuo: Q/W/E.

`NativeGarenSkills` implements initial Q movement speed, slow-clearing capability and W tenacity state with **provisional** tunables. Original damage is disabled. Q's enhanced next basic attack, exact LoL cast timing, particles and audio are not yet reproduced. For other hero keys, only the skill-selection whitelist exists, not original hook/dash/windwall/tornado physics. **Do not present the AI ring as an implemented champion.**

2–4 participants share `NativeMatchRules`; bot identities are Garen's non-duplicated counterparts Blitzcrank/Riven/Yasuo. Bots have individual pathfinders, perception and Hextech cooldowns; their source champion skills are pending models and animation import.

## Required camp provenance

`NativeCampCatalog` only loads `Assets/StreamingAssets/Rift/camps.json` when marked `manuallyVerified=true`, version 1, with distinct camp names and actual native-world x,z coordinates near the navigable lower-half Map11 region. Example **schema only**, not a real data source:

```json
{
  "version": 1,
  "manuallyVerified": false,
  "camps": []
}
```

The code refuses to return a fabricated camp name or spend the card's cooldown until **real** data has been supplied and verified. Real monster entities, legal ability targets, non-attacking/non-dying behavior and Yasuo Q stacking on monsters remain separate tasks.

## Known follow-on work (not represented as complete)

- Original Blitzcrank/Riven/Yasuo meshes, skeletons and verified animation assets.
- Full hero original Q/W/E cast targeting, projectile collision and wall-wind interactions.
- Original fountain coordinates/zone; current staging uses provisional lower-jungle navigation coordinates.
- Frame/contact-based basic-attack validation rather than provisional distance + current sight check.
- Complete League-style fog-of-war shaders and actual 3D wall/bush visibility occlusion, not just navigation-cell LOS.
- Verified camp coordinates and actual neutral monster entities; complete bot behaviors for all eight active cards.
- Full champion selection (at present only Garen is a physical player model), spectator switching and native visual fidelity/QA.

## Source test coverage

`NativeMatchRulesTests.cs` covers 120-second preparation, 3/5/7 minute pursuit, capture and original scoring.
`NativeHextechTests.cs` covers the eight-item pool, three independent choices, no duplicate hero IDs, stun, silence, mirror protection, one-shot clock changes, level 2 second skill, shoes, provisional Garen Q/W and 4-player roster.
The existing Windows CI is retained; **no Phase 5 acceptance is claimed in this handoff**.
