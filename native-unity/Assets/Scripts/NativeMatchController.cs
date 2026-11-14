using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Native 3D hide-and-seek alpha. A human may choose any of four heroes,
    /// with 1–3 unique AI slots. Missing original hero art stays an explicitly
    /// labeled tactical marker. Gameplay identity never depends on model files.
    /// Hero/role -> three Hextech cards -> legal Q/W/E -> preparation -> pursuit.
    /// </summary>
    public sealed class NativeMatchController : MonoBehaviour
    {
        const int HumanId = 1;
        const float AiThinkSeconds = .65f;
        const float HunterSpeed = 365f;
        const float HiderSpeed = 340f;
        const float HumanBasicCooldown = .8f;
        const float AiBasicCooldown = 1.1f;
        static readonly Vector2 HiderStart = new Vector2(7765f, -4020f);
        // Hunter preparation is staged in genuine navigable mid-river
        // grass, selected from the preserved original Map11 bush channel.
        Vector2 HunterSpawn => rift != null && rift.JungleArea != null
            ? rift.JungleArea.HunterRiverBrush :
            throw new InvalidOperationException(
                "The hunter requires real Map11 river grass staging");
        Vector2 HiderSpawn => rift && rift.SpawnCalibration != null
            ? rift.SpawnCalibration.HiderSpawn : HiderStart;
        enum Menu { Role, Augment, Skill, Playing }
        sealed class Bot
        {
            public int Id;
            public string Hero;
            public MatchRole Role;
            public Vector2 Position;
            public Vector2 Destination;
            public readonly List<Vector2> Path = new List<Vector2>();
            public readonly List<Vector2> Candidate = new List<Vector2>();
            public int Waypoint;
            public float ThinkCooldown;
            public float AttackCooldown;
            public GameObject Marker;
            public LineRenderer Ring;
            public Material RingMaterial;
            public GarenRigRuntime RealRig;
            public NativeHeroLoadout Loadout;
            public readonly NativeDashMotion Dash = new NativeDashMotion();
            public readonly NativeDashMotion Pull = new NativeDashMotion();
            public Vector2 VisualPreviousPosition;
            public NativeYasuoSkillState YasuoSkill;
            public NativeDirectionalProjectile YasuoQFlight;
            public bool YasuoQTornadoFlight;
            public NativeChampionCombat Combat;
            public NativeGarenSkills GarenSkills;
            public NativeAbilityAnimationBindings AnimationBindings;
            public readonly NativeBasicContactWindow BasicContact = new NativeBasicContactWindow();
        }

        readonly List<Bot> bots = new List<Bot>();
        readonly System.Random random = new System.Random(1439);
        RiftBootstrap rift;
        NativeMatchRules rules;
        NativeHextechRuntime hextech;
        NativeHeroControl heroControl;
        NativeFogOfWar privateFog;
        NativeCombatVfx combatVfx;
        NativeAbilityAnimationBindings humanAnimationBindings;
        NativeChampionCombat humanCombat;
        NativeYasuoSkillState humanYasuo;
        NativeDirectionalProjectile humanYasuoQFlight;
        bool humanYasuoQTornadoFlight;
        bool humanQNeutralStacked;
        NativeDashMotion humanDash = new NativeDashMotion();
        Vector2 humanFacing = Vector2.right;
        string selectedHero = "Garen";
        NativeBasicContactWindow humanContact = new NativeBasicContactWindow();
        NativeDashMotion humanPull = new NativeDashMotion();
        Vector2 previousHumanVisualPosition;
        NativeHeroLoadout humanLoadout;
        NativeGarenSkills garenSkills;
        NativeHextechDefinition[] offered;
        MatchRole humanRole;
        Menu menu = Menu.Role;
        int rosterSize = 2;
        float basicCooldown;
        bool loggedFinish;
        string feedback = "";
        int preferredTargetId = -1;
        int preferredSpectateId = -1;
        bool qaEnforceJungle;

        void Awake()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechVisualQA") >= 0)
            {
                enabled = false;
                return;
            }
            rift = GetComponent<RiftBootstrap>();
            if (!rift)
            {
                Debug.LogError("NativeMatchController requires RiftBootstrap on the same object");
                enabled = false;
            }
        }

        void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechUiVisualQA") >= 0)
                StartCoroutine(VerifyUiVisual());
            else if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechGameplayQA") >= 0)
                StartCoroutine(VerifyGameplay());
        }

        void Configure(MatchRole role)
        {
            if (!rift || !rift.IsReady) return;
            CleanupBots();
            if (privateFog) privateFog.End();
            // Gameplay uses the bounded jungle/river region. Existing full-map
            // skill smokes retain their original geometry; a real GPU smoke
            // explicitly enables the strict region and verifies its gates.
            bool enforceRegion = qaEnforceJungle ||
                !HasAutomatedVisualFlag() &&
                Array.IndexOf(Environment.GetCommandLineArgs(),
                              "-hextechUiVisualQA") < 0;
            if (!rift.SetJungleOnly(enforceRegion))
                throw new InvalidOperationException(
                    "Verified jungle-only Map11 region unavailable");
            humanRole = role;
            if (!rift.SelectPlayerChampion(selectedHero))
                throw new InvalidOperationException("Cannot choose native player hero: " + selectedHero);
            var participants = NativePlayerRoster.Build(selectedHero, role, rosterSize);
            rules = new NativeMatchRules(participants);
            hextech = new NativeHextechRuntime(rules);
            heroControl = new NativeHeroControl();
            humanLoadout = new NativeHeroLoadout(selectedHero, role);
            garenSkills = new NativeGarenSkills();
            humanCombat = selectedHero == "Garen" ? null :
                new NativeChampionCombat(HumanId, selectedHero,
                    humanLoadout, rules, hextech, rift.Navigation);
            humanYasuo = selectedHero == "Yasuo" ? new NativeYasuoSkillState() : null;
            humanYasuoQFlight = null;
            humanYasuoQTornadoFlight = false;
            humanQNeutralStacked = false;
            humanDash.Cancel();
            humanFacing = Vector2.right;
            humanAnimationBindings = null;
            offered = NativeHextechPool.Deal(role, random);
            preferredTargetId = -1;
            preferredSpectateId = -1;
            basicCooldown = 0;
            humanContact = new NativeBasicContactWindow();
            humanPull.Cancel();
            previousHumanVisualPosition = rift.PlayerPosition;
            loggedFinish = false;
            feedback = "Choose one of your three independent Hextech cards.";
            menu = Menu.Augment;
            rift.ExternalGameplayControl = true;
            rift.ExternalMoveEnabled = false;
            rift.StopMovement();

            for (int i = 1; i < rosterSize; i++)
            {
                var p = participants[i];
                var bot = new Bot {
                    Id = p.Id, Hero = p.HeroKey, Role = p.Role,
                    Loadout = new NativeHeroLoadout(p.HeroKey, p.Role),
                };
                NativeSkillSlot primarySkill =
                    p.Role == MatchRole.Hider &&
                    (bot.Hero == "Riven" || bot.Hero == "Yasuo") ?
                    NativeSkillSlot.E : NativeSkillSlot.Q;
                bot.Loadout.Choose(primarySkill);
                Debug.Log("NATIVE_AI_PRIMARY_SKILL: " + bot.Hero + " " + primarySkill);
                if (string.Equals(bot.Hero, "Yasuo", StringComparison.OrdinalIgnoreCase))
                    bot.YasuoSkill = new NativeYasuoSkillState();
                bot.Combat = new NativeChampionCombat(bot.Id, bot.Hero,
                    bot.Loadout, rules, hextech, rift.Navigation);
                if (bot.Hero == "Garen")
                    bot.GarenSkills = new NativeGarenSkills();
                var offeredBot = NativeHextechPool.Deal(p.Role, random);
                // Each AI rolls and selects independently; duplicate choices
                // across different characters are intentionally legal.
                var selected = offeredBot[random.Next(offeredBot.Length)].Id;
                hextech.Select(p.Id, selected);
                Vector2 basePoint = p.Role == MatchRole.Hunter ?
                    HunterSpawn : HiderSpawn + new Vector2(i * 345f, i * 175f);
                if (!rift.Navigation.TryNearest(basePoint, out bot.Position, 65))
                    bot.Position = rift.PlayerPosition;
                CreateMarker(bot);
                bots.Add(bot);
            }
            Debug.Log("MATCH_ROSTER_CREATED: participants=" + rosterSize +
                      " human=" + role + " distinctHeroSlots=true");
        }

        void SelectAugment(NativeHextech selected)
        {
            if (menu != Menu.Augment || rules == null) return;
            bool offeredToHuman = false;
            foreach (var choice in offered)
                if (choice.Id == selected) offeredToHuman = true;
            if (!offeredToHuman || !hextech.Select(HumanId, selected)) return;
            feedback = "Choose one legal " + selectedHero + " Q/W/E skill for this round.";
            menu = Menu.Skill;
        }

        void SelectSkill(NativeSkillSlot slot)
        {
            if (menu != Menu.Skill || !humanLoadout.Choose(slot)) return;
            StartRound();
        }

        void StartRound()
        {
            // Hunter must be staged for the entire preparation interval.
            bool spawned = rift.TryTeleportTo(humanRole == MatchRole.Hunter ?
                HunterSpawn : HiderSpawn);
            if (!spawned)
            {
                feedback = "Player staging is outside navigable bounds";
                menu = Menu.Role;
                CleanupBots();
                rules = null;
                rift.ExternalGameplayControl = false;
                return;
            }
            rules.Begin();
            if (!combatVfx) combatVfx = GetComponent<NativeCombatVfx>() ??
                                       gameObject.AddComponent<NativeCombatVfx>();
            try { combatVfx.Begin(rift.Navigation); }
            catch (Exception fxError)
            {
                Debug.LogWarning("NATIVE_DEBUG_FX_UNAVAILABLE: " + fxError.Message);
            }
            // Use exact hero-specific verified source clip mappings only.
            // No guessed 'spell1 = Q' mapping when original clips are missing.
            if (rift.HeroRig)
            {
                string heroFolder = Path.Combine(Application.streamingAssetsPath,
                                                 "Champions", selectedHero);
                humanAnimationBindings = LoadBindings(heroFolder, selectedHero,
                                                      rift.HeroRig);
            }
            hextech.ApplyPreparationPassives(HumanId, humanLoadout);
            foreach (var bot in bots)
            {
                hextech.ApplyPreparationPassives(bot.Id, bot.Loadout);
                if (bot.Loadout.Level >= 2 && !bot.Loadout.Secondary.HasValue)
                {
                    // The second skill is an independent learned slot, never
                    // an automatic unlearned ability granted to every bot.
                    NativeSkillSlot secondary;
                    if (bot.Hero == "Blitzcrank")
                        secondary = bot.Role == MatchRole.Hunter ?
                            NativeSkillSlot.E : NativeSkillSlot.W;
                    else if (bot.Hero == "Riven" || bot.Hero == "Yasuo")
                        secondary = bot.Loadout.Primary == NativeSkillSlot.E ?
                            NativeSkillSlot.Q : NativeSkillSlot.W;
                    else
                        secondary = NativeSkillSlot.W;
                    if (!bot.Loadout.Choose(secondary))
                        Debug.LogWarning("NATIVE_AI_SECONDARY_SKILL_REJECTED: " +
                                         bot.Hero + " " + secondary);
                    else
                        Debug.Log("NATIVE_AI_SKILLS_READY: " + bot.Hero + " " +
                                  bot.Loadout.Primary + "+" + bot.Loadout.Secondary);
                }
            }
            rift.ExternalMoveEnabled = rules.CanMove(HumanId);
            rift.ResetCameraForMatch();
            menu = Menu.Playing;
            string startMessage = "120s preparation: hunter waiting inside" +
                " verified mid-river grass; jungle and river boundary active.";
            if (!privateFog) privateFog = GetComponent<NativeFogOfWar>() ??
                                         gameObject.AddComponent<NativeFogOfWar>();
            try
            {
                privateFog.Begin(rift.Navigation, rules, Camera.main);
                privateFog.UpdateView(rules, HumanId, GetPosition);
            }
            catch (Exception fogError)
            {
                Debug.LogError("NATIVE_FOG_INITIALIZATION_FAILED: " + fogError);
                startMessage = "Fog shader unavailable: inspect Player.log (not a visual PASS).";
            }
            feedback = startMessage;
            Debug.Log("MATCH_BEGIN: participants=" + rosterSize + " human=" + humanRole +
                      " hero=" + selectedHero + " originalArt=" + rift.PlayerHasOriginalRig +
                      " hextech=" + hextech.Selected(HumanId));
        }

        bool HoveringHud(Vector2 mouse)
        {
            // Match the visual UI rectangles. Input.mousePosition has a
            // bottom-left origin; OnGUI uses a top-left origin and the same
            // reference scaling through NativeGameUi.
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f,
                                                 Screen.height / 720f),
                                      .65f, 2.5f);
            float x = mouse.x / scale;
            float y = (Screen.height - mouse.y) / scale;
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            return (x < 376f && y < 345f) ||
                   (x > (w - 300f) && y > h - 180f) ||
                   (x > (w - 450f) * .5f && x < (w + 450f) * .5f &&
                    y > h - 116f);
        }

        void Update()
        {
            if (!rift || !rift.IsReady || rules == null || menu != Menu.Playing)
                return;
            // Presentation-only diagnostics; original private sight, target
            // arbitration, capture and fog history remain untouched.
            if (Input.GetKeyDown(KeyCode.V) && privateFog && privateFog.Running)
                feedback = "Fog display: " + privateFog.CyclePresentation() +
                           " (visual only; targeting vision unchanged)";
            float dt = Time.deltaTime;
            if (rules.Phase != MatchPhase.Finished)
            {
                hextech.Advance(dt);
                garenSkills.Advance(dt);
                humanCombat?.Advance(dt);
                humanYasuo?.Advance(dt);
                heroControl.Advance(dt);
                rules.Advance(dt);
            }

            // A caught hider may switch between living allied viewpoints.
            // Each view has its own fog history; we never merge hider vision.
            if (rules.Find(HumanId).Eliminated && Input.GetKeyDown(KeyCode.Tab))
                CycleSpectatedAlly();
            int spectatedId = SpectatedAlly();
            rift.CameraFollowOverride = spectatedId >= 0 ?
                (Vector2?)GetPosition(spectatedId) : null;
            rift.ExternalMoveEnabled = rules.CanMove(HumanId) &&
                !hextech.Stunned(HumanId) && !heroControl.Locked(HumanId) &&
                !humanPull.Active && !humanDash.Active;
            rift.ExternalMoveMultiplier =
                (humanRole == MatchRole.Hunter ? HunterSpeed / HiderSpeed : 1f) *
                hextech.MoveMultiplier(HumanId) *
                (selectedHero == "Garen" ? garenSkills.MovementMultiplier : 1f) *
                (humanCombat != null ? humanCombat.MoveMultiplier : 1f);
            if (!rift.ExternalMoveEnabled) rift.StopMovement();
            if (humanPull.Active)
            {
                humanDash.Cancel();
                Vector2 pulled = humanPull.Step(dt);
                if (!rift.TrySetExternalPosition(pulled)) humanPull.Cancel();
            }
            if (humanDash.Active)
            {
                if (hextech.Stunned(HumanId) || heroControl.Locked(HumanId) ||
                    !rules.CanMove(HumanId))
                    humanDash.Cancel();
                else if (!rift.TrySetExternalPosition(humanDash.Step(dt)))
                    humanDash.Cancel();
            }
            // Already fired projectiles continue after their caster is stunned.
            // None of these skills can directly capture a hider.
            TickHumanProjectiles(dt);
            if (rules.Phase == MatchPhase.Finished)
            {
                rift.StopMovement();
                if (!loggedFinish)
                {
                    loggedFinish = true;
                    if (combatVfx) combatVfx.End();
                    feedback = rules.HunterWon ? "Hunter captured every hider" : "Hiders survived";
                    Debug.Log("MATCH_FINISHED: winner=" +
                              (rules.HunterWon ? "Hunter" : "Hiders") +
                              " humanScore=" + rules.Find(HumanId).Score);
                }
                humanContact.Cancel();
                foreach (var bot in bots) bot.BasicContact.Cancel();
                UpdateMarkers();
                UpdatePrivateFog();
                TickCombatPresentation();
                return;
            }

            if (rift.ExternalMoveEnabled)
            {
                if (Input.GetKeyDown(KeyCode.S)) rift.StopMovement();
                if (Input.GetMouseButtonDown(1) && !HoveringHud(Input.mousePosition))
                    rift.TryNavigateFromScreen(Input.mousePosition);
                if (Input.GetKeyDown(KeyCode.Q)) TryHumanSkill(NativeSkillSlot.Q);
                if (Input.GetKeyDown(KeyCode.W)) TryHumanSkill(NativeSkillSlot.W);
                if (Input.GetKeyDown(KeyCode.E)) TryHumanSkill(NativeSkillSlot.E);
            }
            basicCooldown = Mathf.Max(0f, basicCooldown - dt);
            if (!rules.Find(HumanId).Eliminated && Input.GetKeyDown(KeyCode.Tab))
                CycleVisibleTarget();
            if (!rules.Find(HumanId).Eliminated && Input.GetKeyDown(KeyCode.F))
                TryUseHextech(0);
            if (Input.GetKeyDown(KeyCode.A) && rules.CanAttack(HumanId) &&
                !hextech.Stunned(HumanId) && !heroControl.Locked(HumanId) &&
                basicCooldown <= 0f)
            {
                basicCooldown = HumanBasicCooldown;
                rift.PerformBasicAttack();
                int targetId = ChooseHumanBasicTarget();
                if (targetId >= 0)
                    humanContact.Begin(targetId);
                combatVfx?.Pulse(HumanId, rift.PlayerPosition, selectedHero + " Basic");
                feedback = targetId >= 0
                    ? "Attack windup: contact will recheck sight, reach and immunity."
                    : "Attack animation: no visible enemy in basic-attack reach.";
            }

            ResolveHumanBasicContact(dt);
            foreach (var bot in bots) UpdateBot(bot, dt);
            UpdateMarkers();
            UpdatePrivateFog();
            bool abilityDash = humanDash.Active ||
                selectedHero == "Garen" && garenSkills.QActive && rift.PlayerMoving;
            combatVfx?.SetDash(HumanId, abilityDash,
                previousHumanVisualPosition, rift.PlayerPosition, selectedHero);
            if (humanCombat != null)
            {
                combatVfx?.SetHook(HumanId, humanCombat.HookActive,
                    humanCombat.HookLaunchPosition, humanCombat.HookTipPosition);
                combatVfx?.SetWindWall(HumanId, humanCombat.WindWall);
            }
            if (humanYasuoQFlight != null)
                combatVfx?.SetYasuoQ(HumanId, humanYasuoQFlight.Active,
                    humanYasuoQFlight.Origin, humanYasuoQFlight.Position,
                    humanYasuoQTornadoFlight);
            previousHumanVisualPosition = rift.PlayerPosition;
            TickCombatPresentation();
        }

        bool CanSeeVisualCaster(int actorId)
        {
            if (rules == null) return false;
            int viewerId = rules.Find(HumanId).Eliminated ?
                SpectatedAlly() : HumanId;
            if (viewerId < 0) return false;
            var actor = rules.Find(actorId);
            return actor != null && !actor.Eliminated &&
                   (actorId == viewerId || VisibleTo(viewerId, actorId));
        }

        void TickCombatPresentation()
        {
            if (combatVfx && combatVfx.Ready)
                combatVfx.Tick(CanSeeVisualCaster);
        }

        void UpdateBotCombatVfx(Bot bot, bool forceDashTrail = false)
        {
            if (!combatVfx || !combatVfx.Ready) return;
            if (bot.Combat != null)
            {
                combatVfx.SetHook(bot.Id, bot.Combat.HookActive,
                                  bot.Combat.HookLaunchPosition,
                                  bot.Combat.HookTipPosition);
                combatVfx.SetWindWall(bot.Id, bot.Combat.WindWall);
            }
            bool movingByAbility = forceDashTrail || bot.Dash.Active || bot.Pull.Active;
            combatVfx.SetDash(bot.Id, movingByAbility, bot.VisualPreviousPosition,
                              bot.Position, bot.Hero);
            bool yasuoQActive = bot.YasuoQFlight != null && bot.YasuoQFlight.Active;
            if (bot.YasuoQFlight != null)
                combatVfx.SetYasuoQ(bot.Id, yasuoQActive,
                    bot.YasuoQFlight.Origin, bot.YasuoQFlight.Position,
                    bot.YasuoQTornadoFlight);
        }

        void ResolveHumanBasicContact(float dt)
        {
            if (!humanContact.Tick(dt, out int targetId)) return;
            var target = rules.Find(targetId);
            if (target == null || target.Eliminated || !rules.CanAttack(HumanId) ||
                hextech.Stunned(HumanId) || heroControl.Locked(HumanId)) return;
            bool seen = VisibleTo(HumanId, targetId);
            float distance = Vector2.Distance(rift.PlayerPosition, GetPosition(targetId));
            if (!rules.IsValidCaptureHit(HumanId, targetId, true, seen, distance))
            {
                feedback = "Contact missed or target moved out of range.";
                return;
            }
            if (humanCombat != null &&
                humanCombat.ConsumeEmpoweredBasicAttack(out float airborne))
                ApplyChampionControl(targetId, airborne);
            bool caught = hextech.TryBasicAttackCapture(
                HumanId, targetId, true, seen, distance);
            feedback = caught ? "Contact: captured " + target.HeroKey :
                "Contact: missed, evaded or capture immunity applied.";
            Debug.Log("NATIVE_BASIC_CONTACT_HUMAN: caught=" + caught +
                      " target=" + targetId + " visible=" + seen);
        }

        void ResolveBotBasicContact(Bot bot, float dt)
        {
            if (!bot.BasicContact.Tick(dt, out int targetId)) return;
            var target = rules.Find(targetId);
            if (target == null || target.Eliminated || !rules.CanAttack(bot.Id) ||
                hextech.Stunned(bot.Id) || heroControl.Locked(bot.Id)) return;
            bool seen = VisibleTo(bot.Id, targetId);
            float distance = Vector2.Distance(bot.Position, GetPosition(targetId));
            if (!rules.IsValidCaptureHit(bot.Id, targetId, true, seen, distance))
                return;
            // E is consumed by an actual valid basic hit even when Mirror
            // protects the target from the capture. The skill itself does
            // not score a capture, and an attack miss cannot consume E.
            if (bot.Combat != null &&
                bot.Combat.ConsumeEmpoweredBasicAttack(out float airborne))
                ApplyChampionControl(targetId, airborne);
            bool caught = hextech.TryBasicAttackCapture(
                bot.Id, targetId, true, seen, distance);
            Debug.Log("NATIVE_BASIC_CONTACT_AI: actor=" + bot.Hero +
                      " target=" + target.HeroKey + " captured=" + caught);
        }

        NativeAbilityAnimationBindings LoadBindings(
            string folder, string hero, GarenRigRuntime rig)
        {
            if (!rig) return null;
            try
            {
                return NativeAbilityAnimationBindings.LoadVerified(
                    Path.Combine(folder, "animation-bindings.json"),
                    hero, rig.HasClip);
            }
            catch (Exception e)
            {
                Debug.LogWarning("NATIVE_ANIMATION_MAPPING_REJECTED: " +
                                 hero + " " + e.Message);
                return null;
            }
        }

        void PlayBotOriginalSkill(Bot bot, NativeSkillSlot slot, int stage = 1)
        {
            if (!bot.RealRig || bot.AnimationBindings == null) return;
            string clip = bot.AnimationBindings.Resolve(slot, stage);
            if (!string.IsNullOrEmpty(clip))
                bot.RealRig.TryPlayOriginalSkillClip(clip);
        }

        void UpdatePrivateFog()
        {
            if (!privateFog || !privateFog.Running || rules == null) return;
            int viewerId = rules.Find(HumanId).Eliminated ? SpectatedAlly() : HumanId;
            privateFog.UpdateView(rules, viewerId, GetPosition);
        }

        Vector2 HumanAimDirection()
        {
            Vector2 origin = rift.PlayerPosition;
            if (!HoveringHud(Input.mousePosition) &&
                rift.TryGetWorldPointFromScreen(Input.mousePosition, out var hit))
            {
                Vector2 aim = hit - origin;
                if (aim.sqrMagnitude >= 4f)
                    humanFacing = aim.normalized;
            }
            return humanFacing;
        }

        void PlayHumanOriginalSkill(NativeSkillSlot slot, int stage = 1)
        {
            if (!rift.HeroRig || humanAnimationBindings == null) return;
            string clip = humanAnimationBindings.Resolve(slot, stage);
            if (!string.IsNullOrEmpty(clip))
                rift.HeroRig.TryPlayOriginalSkillClip(clip);
        }

        void ApplyHumanSkillImpact(NativeSkillImpact impact)
        {
            if (impact == null) return;
            if (impact.CasterLanding.HasValue)
            {
                if (!humanDash.Begin(rift.Navigation, rift.PlayerPosition,
                                    impact.CasterLanding.Value))
                    Debug.LogWarning("NATIVE_HUMAN_DASH_REJECTED: invalid swept path");
                else
                {
                    rift.StopMovement();
                    humanContact.Cancel();
                }
            }
            if (impact.TargetId >= 0)
            {
                var target = FindBot(impact.TargetId);
                if (target != null && !rules.Find(target.Id).Eliminated)
                {
                    if (impact.TargetLanding.HasValue)
                    {
                        target.Dash.Cancel();
                        target.Pull.Cancel();
                        target.Path.Clear();
                        target.Waypoint = 0;
                        target.BasicContact.Cancel();
                        if (!target.Pull.Begin(rift.Navigation, target.Position,
                                               impact.TargetLanding.Value))
                            Debug.LogWarning("NATIVE_HUMAN_HOOK_PULL_REJECTED: unsafe path");
                    }
                    if (impact.LockSeconds > 0f)
                        ApplyChampionControl(target.Id, impact.LockSeconds);
                    if (VisibleTo(HumanId, target.Id))
                        combatVfx?.Impact(HumanId, target.Position, impact.Skill, true);
                }
            }
            foreach (int id in impact.AdditionalHitTargets)
            {
                var target = FindBot(id);
                if (target == null || rules.Find(id).Eliminated) continue;
                if (impact.LockSeconds > 0f)
                    ApplyChampionControl(id, impact.LockSeconds);
                if (VisibleTo(HumanId, id))
                    combatVfx?.Impact(HumanId, target.Position, impact.Skill, true);
            }
            int stage = impact.Skill.EndsWith("Q3", StringComparison.Ordinal) ? 3 :
                        impact.Skill.EndsWith("Q2", StringComparison.Ordinal) ? 2 : 1;
            if (impact.Skill.StartsWith("Riven Q", StringComparison.Ordinal))
                PlayHumanOriginalSkill(NativeSkillSlot.Q, stage);
            else if (impact.Skill == "Riven W")
                PlayHumanOriginalSkill(NativeSkillSlot.W);
            else if (impact.Skill == "Riven E")
                PlayHumanOriginalSkill(NativeSkillSlot.E);
            combatVfx?.Pulse(HumanId, rift.PlayerPosition, impact.Skill);
            feedback = impact.Skill + " applied (no skill capture or damage)";
            Debug.Log("NATIVE_HUMAN_SKILL: " + impact.Skill +
                      " target=" + impact.TargetId + " capture=false");
        }

        bool TryHumanYasuoQ(Vector2 origin, Vector2 direction)
        {
            if (humanYasuo == null ||
                humanYasuoQFlight != null && humanYasuoQFlight.Active)
                return false;
            if (!humanYasuo.TryQ(humanLoadout, rules, hextech, HumanId,
                                 false, out bool tornado))
                return false;
            float reach = tornado ? 660f : NativeYasuoSkillState.QReachProvisional;
            float width = tornado ? 90f : NativeYasuoSkillState.QHalfWidthProvisional;
            var shot = new NativeDirectionalProjectile();
            if (!shot.Begin(HumanId, humanRole,
                            tornado ? "Yasuo Q Tornado" : "Yasuo Q",
                            origin, direction, reach, 1900f, width,
                            rift.Navigation))
            {
                Debug.LogWarning("NATIVE_HUMAN_YASUO_Q_PROJECTILE_REJECTED");
                return false;
            }
            humanYasuoQFlight = shot;
            humanYasuoQTornadoFlight = tornado;
            humanQNeutralStacked = false;
            // Real neutral monsters can grant normal Yasuo Q stacks without
            // receiving damage. This uses the verified nav/neutral contact.
            if (!tornado && rift.NeutralMonsters != null &&
                rift.NeutralMonsters.HasVerifiedCamps)
            {
                var monster = rift.NeutralMonsters.FirstLineContact(
                    origin, direction, reach, width);
                if (monster != null &&
                    rift.Navigation.LineClear(origin, monster.WalkableAnchor))
                {
                    humanYasuo.RegisterQHit(false);
                    humanQNeutralStacked = true;
                    Debug.Log("NATIVE_HUMAN_YASUO_Q_NEUTRAL: " + monster.CampName);
                }
            }
            PlayHumanOriginalSkill(NativeSkillSlot.Q, tornado ? 3 : 1);
            combatVfx?.Pulse(HumanId, origin,
                            tornado ? "Yasuo Q Tornado" : "Yasuo Q");
            feedback = tornado ? "Yasuo tornado launched" : "Yasuo Q launched";
            return true;
        }

        bool TryHumanYasuoE(Vector2 origin)
        {
            if (humanYasuo == null) return false;
            Vector2 destination;
            if (humanYasuo.TryEDashThroughNearestHero(
                    humanLoadout, rules, hextech, HumanId, origin,
                    GetSkillTargets(HumanId), rift.Navigation,
                    out destination, out var selectedTargetId))
            {
                if (!humanDash.Begin(rift.Navigation, origin, destination))
                    return false;
                rift.StopMovement();
                humanContact.Cancel();
                PlayHumanOriginalSkill(NativeSkillSlot.E);
                combatVfx?.Pulse(HumanId, origin, "Yasuo E Hero");
                feedback = "Yasuo E through visible enemy " + selectedTargetId + "; no capture";
                return true;
            }
            if (rift.NeutralMonsters == null ||
                !rift.NeutralMonsters.HasVerifiedCamps) return false;
            foreach (var monster in rift.NeutralMonsters.Monsters)
            {
                if ((monster.WalkableAnchor - origin).sqrMagnitude >
                    NativeYasuoSkillState.EDashRangeProvisional *
                    NativeYasuoSkillState.EDashRangeProvisional) continue;
                if (!humanYasuo.TryEDashToMonster(
                        humanLoadout, rules, hextech, HumanId, origin,
                        monster.Id, rift.NeutralMonsters, out destination))
                    continue;
                if (!humanDash.Begin(rift.Navigation, origin, destination))
                    return false;
                rift.StopMovement();
                humanContact.Cancel();
                PlayHumanOriginalSkill(NativeSkillSlot.E);
                combatVfx?.Pulse(HumanId, origin, "Yasuo E Neutral");
                feedback = "Yasuo E through neutral; monster remains alive";
                return true;
            }
            return false;
        }

        // Player input and real GPU QA use the same authoritative cast path.
        // A QA-only aim override avoids relying on an OS mouse cursor on
        // headless/interactive Windows runner desktops.
        bool TryHumanSkill(NativeSkillSlot slot, Vector2? qaAim = null)
        {
            if (humanLoadout == null || !humanLoadout.Knows(slot) ||
                !rules.CanMove(HumanId) || !hextech.CanUseHeroSkill(HumanId) ||
                heroControl.Locked(HumanId) || humanDash.Active ||
                humanPull.Active)
            {
                feedback = "Skill unavailable: unlearned, silenced, stunned or moving";
                return false;
            }
            if (selectedHero == "Garen")
                return TryGarenSkill(slot);
            Vector2 origin = rift.PlayerPosition;
            Vector2 direction = qaAim.HasValue ? qaAim.Value.normalized : HumanAimDirection();
            bool cast = false;
            if (selectedHero == "Blitzcrank")
            {
                switch (slot)
                {
                    case NativeSkillSlot.Q:
                        cast = humanCombat.TryBlitzHook(origin, direction);
                        break;
                    case NativeSkillSlot.W:
                        cast = humanCombat.TryBlitzW();
                        break;
                    case NativeSkillSlot.E:
                        cast = humanCombat.TryBlitzE();
                        break;
                }
                if (cast)
                {
                    PlayHumanOriginalSkill(slot);
                    combatVfx?.Pulse(HumanId, origin, "Blitzcrank " + slot);
                    feedback = "Blitzcrank " + slot + " cast; no skill capture";
                }
            }
            else if (selectedHero == "Riven")
            {
                NativeSkillImpact impact = slot == NativeSkillSlot.Q ?
                    humanCombat.TryRivenQ(origin, direction, GetSkillTargets(HumanId)) :
                    slot == NativeSkillSlot.W ?
                    humanCombat.TryRivenW(origin, GetSkillTargets(HumanId)) :
                    humanCombat.TryRivenE(origin, direction);
                cast = impact != null;
                if (cast) ApplyHumanSkillImpact(impact);
            }
            else if (selectedHero == "Yasuo")
            {
                if (slot == NativeSkillSlot.Q)
                    cast = TryHumanYasuoQ(origin, direction);
                else if (slot == NativeSkillSlot.W)
                {
                    cast = humanCombat.TryYasuoWindWall(origin, direction);
                    if (cast)
                    {
                        PlayHumanOriginalSkill(slot);
                        combatVfx?.Pulse(HumanId, origin, "Yasuo W");
                    }
                }
                else if (slot == NativeSkillSlot.E)
                    cast = TryHumanYasuoE(origin);
            }
            if (!cast) feedback = selectedHero + " " + slot +
                " unavailable (cooldown, range, obstacle or no valid target)";
            return cast;
        }

        void TickHumanProjectiles(float dt)
        {
            if (rules.Phase == MatchPhase.Finished) return;
            if (humanCombat != null && humanCombat.HookActive)
                ApplyHumanSkillImpact(humanCombat.StepBlitzHook(
                    dt, GetSkillTargets(HumanId), ActiveEnemyWindWall(HumanId)));
            if (humanYasuoQFlight == null || !humanYasuoQFlight.Active) return;
            var impact = humanYasuoQFlight.Step(dt, rift.Navigation,
                GetSkillTargets(HumanId), ActiveEnemyWindWall(HumanId),
                humanCombat != null ? humanCombat.Elapsed : hextech.Elapsed);
            if (impact == null) return;
            if (impact.TargetId >= 0 && !rules.Find(impact.TargetId).Eliminated)
            {
                if (humanYasuoQTornadoFlight)
                    ApplyChampionControl(impact.TargetId, .75f);
                else if (!humanQNeutralStacked)
                    humanYasuo?.RegisterQHit(false);
                if (VisibleTo(HumanId, impact.TargetId))
                    combatVfx?.Impact(HumanId, GetPosition(impact.TargetId),
                                      impact.Skill, true);
            }
            Debug.Log("NATIVE_HUMAN_Q_CONTACT: " + impact.Skill +
                      " target=" + impact.TargetId +
                      " blocked=" + impact.ProjectileBlocked +
                      " capture=false");
        }

        bool TryGarenSkill(NativeSkillSlot slot)
        {
            if (garenSkills.TryCast(slot, humanLoadout, rules, hextech, HumanId, false))
            {
                PlayHumanOriginalSkill(slot);
                combatVfx?.Pulse(HumanId, rift.PlayerPosition, "Garen " + slot);
                feedback = slot == NativeSkillSlot.Q ?
                    "Garen Q: slow cleanse + brief movement speed (tuning pending)" :
                    "Garen W: tenacity window; ordinary shield does NOT block capture";
                Debug.Log("MATCH_GAREN_SKILL: " + slot + " (non-damaging adapted mechanics)");
                return true;
            }
            feedback = "Skill unavailable: check chosen skills, cooldown, stun or silence";
            return false;
        }

        Bot FindBot(int id)
        {
            foreach (var bot in bots) if (bot.Id == id) return bot;
            return null;
        }

        Vector2 GetPosition(int id) =>
            id == HumanId ? rift.PlayerPosition : FindBot(id).Position;

        bool VisibleTo(int observerId, int targetId) =>
            NativeSight.Visible(rift.Navigation, GetPosition(observerId), GetPosition(targetId));

        int ChooseHumanBasicTarget()
        {
            var candidates = new List<NativeTargetSelection.Candidate>(bots.Count);
            Vector2 origin = rift.PlayerPosition;
            foreach (var bot in bots)
            {
                var participant = rules.Find(bot.Id);
                bool opponent = participant != null && participant.Role != humanRole;
                bool alive = participant != null && !participant.Eliminated;
                bool visible = opponent && alive && VisibleTo(HumanId, bot.Id);
                candidates.Add(new NativeTargetSelection.Candidate(
                    bot.Id, opponent, alive, visible,
                    Vector2.Distance(origin, bot.Position)));
            }
            return NativeTargetSelection.ChooseBasicTarget(
                preferredTargetId, candidates, NativeMatchRules.BasicAttackReach);
        }

        string SelectedTargetStatus()
        {
            if (rules == null || preferredTargetId < 0) return "AUTO NEAREST";
            var target = rules.Find(preferredTargetId);
            if (target == null || target.Eliminated || target.Role == humanRole)
                return "UNAVAILABLE";
            bool eliminated = rules.Find(HumanId).Eliminated;
            bool visible = !eliminated && VisibleTo(HumanId, preferredTargetId);
            return NativeTargetSelection.CanDisplaySelectedEnemy(
                eliminated, target.Role != humanRole, !target.Eliminated, visible)
                ? target.HeroKey + " (visible)" : "UNSEEN (no auto-attack)";
        }

        int GetTargetForActor(int actorId)
        {
            var p = rules.Find(actorId);
            if (p == null) return -1;
            int chosen = -1;
            float nearest = float.MaxValue;
            foreach (var target in rules.Players)
            {
                if (target.Id == actorId || target.Role == p.Role || target.Eliminated)
                    continue;
                if (!VisibleTo(actorId, target.Id)) continue;
                float distance = Vector2.Distance(GetPosition(actorId), GetPosition(target.Id));
                if (distance < nearest) { chosen = target.Id; nearest = distance; }
            }
            return chosen;
        }

        void CycleVisibleTarget()
        {
            var eligible = new List<int>();
            bool senseCanTargetUnseen = hextech.Selected(HumanId) == NativeHextech.CardSenseSilver;
            foreach (var p in rules.Players)
                if (p.Id != HumanId && p.Role != humanRole && !p.Eliminated &&
                    (senseCanTargetUnseen || VisibleTo(HumanId, p.Id)))
                    eligible.Add(p.Id);
            if (eligible.Count == 0)
            {
                preferredTargetId = -1;
                feedback = "No eligible target (Card Sense may select unseen enemies)";
                return;
            }
            int index = eligible.IndexOf(preferredTargetId);
            preferredTargetId = eligible[(index + 1) % eligible.Count];
            feedback = "Hextech target: " + rules.Find(preferredTargetId).HeroKey;
        }

        int FirstLivingEnemy()
        {
            foreach (var p in rules.Players)
                if (p.Id != HumanId && p.Role != humanRole && !p.Eliminated)
                    return p.Id;
            return -1;
        }

        string VerifiedCampSense(NativeHextech selected, int targetId)
        {
            var catalog = rift.CampCatalog;
            if (catalog == null || catalog.Camps.Count == 0)
                return null; // no artificial camp-name responses
            if (selected == NativeHextech.CardSenseSilver)
            {
                var target = rules.Find(targetId);
                if (target == null || target.Eliminated) return null;
                string camp = catalog.NearestName(GetPosition(targetId));
                return target.HeroKey + ": nearest jungle camp " + camp;
            }
            if (selected == NativeHextech.CardSenseGold)
            {
                var lines = new List<string>();
                foreach (var p in rules.Players)
                {
                    if (p.Id == HumanId || p.Role == humanRole || p.Eliminated) continue;
                    lines.Add(p.HeroKey + ": " + catalog.NearestName(GetPosition(p.Id)));
                }
                return lines.Count == 0 ? null : "Enemy nearest camps: " + string.Join("; ", lines);
            }
            return null;
        }

        void TryUseHextech(int clockDirection)
        {
            if (hextech == null) return;
            var selected = hextech.Selected(HumanId);
            if (!selected.HasValue) return;
            bool senseCanTargetUnseen = selected == NativeHextech.CardSenseSilver;
            int target = preferredTargetId;
            if (target < 0 || rules.Find(target) == null ||
                rules.Find(target).Eliminated ||
                (!senseCanTargetUnseen && !VisibleTo(HumanId, target)))
                target = senseCanTargetUnseen ? FirstLivingEnemy() : GetTargetForActor(HumanId);
            bool sees = target >= 0 && VisibleTo(HumanId, target);
            string campInfo = VerifiedCampSense(selected.Value, target);
            if (!hextech.TryActive(HumanId, target, sees, clockDirection, campInfo,
                                    out feedback))
                Debug.Log("MATCH_HEXTECH_REJECTED: " + feedback);
            else Debug.Log("MATCH_HEXTECH_USED: " + feedback);
        }

        void BotChoosePath(Bot bot, Vector2 destination)
        {
            bot.Candidate.Clear();
            if (!rift.Navigation.TryPath(bot.Position, destination, bot.Candidate) ||
                bot.Candidate.Count == 0) return;
            bot.Path.Clear();
            bot.Path.AddRange(bot.Candidate);
            bot.Waypoint = 0;
            bot.Destination = destination;
        }

        void BotWander(Bot bot)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                float radius = 350f + (float)random.NextDouble() * 750f;
                Vector2 raw = bot.Position +
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (!rift.Navigation.TryNearest(raw, out var goal, 15)) continue;
                BotChoosePath(bot, goal);
                if (bot.Waypoint < bot.Path.Count) return;
            }
        }

        List<NativeSkillTarget> GetSkillTargets(int actorId)
        {
            var current = rules.Find(actorId);
            var targets = new List<NativeSkillTarget>(rules.Players.Count);
            foreach (var participant in rules.Players)
            {
                if (participant.Id == actorId || participant.Eliminated ||
                    participant.Role == current.Role) continue;
                targets.Add(new NativeSkillTarget(participant.Id,
                    GetPosition(participant.Id), participant.Role,
                    participant.Eliminated, VisibleTo(actorId, participant.Id)));
            }
            return targets;
        }

        NativeWindWall ActiveEnemyWindWall(int actorId)
        {
            var actor = rules.Find(actorId);
            if (actorId != HumanId && humanCombat != null &&
                humanRole != actor.Role && humanCombat.WindWall != null)
                return humanCombat.WindWall;
            foreach (var bot in bots)
                if (bot.Id != actorId && bot.Role != actor.Role &&
                    bot.Combat != null && bot.Combat.WindWall != null)
                    return bot.Combat.WindWall;
            return null;
        }

        void ApplyChampionControl(int targetId, float duration)
        {
            // Garen W's active tenacity also reduces champion-control windows,
            // not just Hextech crowd control.
            float durationFactor = targetId == HumanId && selectedHero == "Garen" ?
                garenSkills.CrowdControlDurationFactor :
                FindBot(targetId)?.GarenSkills != null ?
                FindBot(targetId).GarenSkills.CrowdControlDurationFactor : 1f;
            heroControl.Apply(targetId, duration * durationFactor);
        }

        void ApplyBotSkillImpact(Bot caster, NativeSkillImpact impact)
        {
            if (impact == null) return;
            if (impact.CasterLanding.HasValue)
            {
                // A completed cast authorizes a target landing; the actor
                // travels there over time, not by an instant teleport.
                if (caster.Dash.Begin(rift.Navigation, caster.Position,
                                      impact.CasterLanding.Value))
                {
                    caster.Path.Clear();
                    caster.Waypoint = 0;
                    caster.BasicContact.Cancel();
                }
            }
            if (impact.TargetId >= 0)
            {
                Vector2 visibleImpactPosition = GetPosition(impact.TargetId);
                // Target capture is *never* called here. Q, W, E only move,
                // block or control and cannot consume Mirror capture immunity.
                if (impact.TargetLanding.HasValue)
                {
                    if (impact.TargetId == HumanId)
                    {
                        humanPull.Cancel();
                        rift.StopMovement();
                        if (!humanPull.Begin(rift.Navigation, rift.PlayerPosition,
                                             impact.TargetLanding.Value))
                            Debug.LogWarning("NATIVE_HOOK_PULL_REJECTED: player path is not navigable");
                    }
                    else
                    {
                        var target = FindBot(impact.TargetId);
                        if (target != null)
                        {
                            target.Dash.Cancel();
                            target.Pull.Cancel();
                            target.Path.Clear();
                            target.Waypoint = 0;
                            if (!target.Pull.Begin(rift.Navigation, target.Position,
                                                   impact.TargetLanding.Value))
                                Debug.LogWarning("NATIVE_HOOK_PULL_REJECTED: AI path is not navigable");
                        }
                    }
                }
                if (impact.LockSeconds > 0)
                    ApplyChampionControl(impact.TargetId, impact.LockSeconds);
                combatVfx?.Impact(caster.Id, visibleImpactPosition, impact.Skill,
                                  CanSeeVisualCaster(impact.TargetId));
            }
            if (impact.LockSeconds > 0)
                foreach (int affectedId in impact.AdditionalHitTargets)
                {
                    ApplyChampionControl(affectedId, impact.LockSeconds);
                    combatVfx?.Impact(caster.Id, GetPosition(affectedId), impact.Skill,
                                      CanSeeVisualCaster(affectedId));
                }
            combatVfx?.Pulse(caster.Id, caster.Position, impact.Skill);
            if (impact.Skill.StartsWith("Riven Q", StringComparison.Ordinal))
                PlayBotOriginalSkill(caster, NativeSkillSlot.Q,
                    impact.Skill.EndsWith("Q3", StringComparison.Ordinal) ? 3 :
                    impact.Skill.EndsWith("Q2", StringComparison.Ordinal) ? 2 : 1);
            else if (impact.Skill == "Riven W")
                PlayBotOriginalSkill(caster, NativeSkillSlot.W);
            else if (impact.Skill == "Riven E")
                PlayBotOriginalSkill(caster, NativeSkillSlot.E);
            Debug.Log("NATIVE_AI_SKILL_CONTACT: " + caster.Hero + " " +
                      impact.Skill + " target=" + impact.TargetId +
                      " moved=" + impact.TargetLanding.HasValue +
                      " blocked=" + impact.ProjectileBlocked +
                      " capture=false");
        }

        void BotCastChampionSkill(Bot bot, int enemyId)
        {
            if (bot.Combat == null || enemyId < 0 || bot.Dash.Active ||
                !hextech.CanUseHeroSkill(bot.Id)) return;
            Vector2 direction = GetPosition(enemyId) - bot.Position;
            if (direction.sqrMagnitude < 1f) return;
            var targets = GetSkillTargets(bot.Id);
            if (bot.Hero == "Garen")
            {
                if (bot.GarenSkills.TryCast(NativeSkillSlot.Q, bot.Loadout,
                    rules, hextech, bot.Id, false))
                {
                    PlayBotOriginalSkill(bot, NativeSkillSlot.Q);
                    combatVfx?.Pulse(bot.Id, bot.Position, "Garen Q");
                    return;
                }
                if (bot.GarenSkills.TryCast(NativeSkillSlot.W, bot.Loadout,
                    rules, hextech, bot.Id, false))
                {
                    PlayBotOriginalSkill(bot, NativeSkillSlot.W);
                    combatVfx?.Pulse(bot.Id, bot.Position, "Garen W");
                }
            }
            else if (bot.Hero == "Blitzcrank")
            {
                // A fleeing hider does not automatically pull the hunter onto
                // themselves. Q is legal, but this AI tactic reserves it for
                // hunter mode; W is used for escape by either role.
                if (bot.Role == MatchRole.Hunter &&
                    bot.Loadout.Knows(NativeSkillSlot.Q) &&
                    bot.Combat.TryBlitzHook(bot.Position, direction))
                {
                    PlayBotOriginalSkill(bot, NativeSkillSlot.Q);
                    combatVfx?.Pulse(bot.Id, bot.Position, "Blitzcrank Q");
                    Debug.Log("NATIVE_BLITZ_Q_FIRED: actor=" + bot.Id);
                    return; // one fresh champion cast per AI decision
                }
                if (bot.Loadout.Knows(NativeSkillSlot.W) &&
                    bot.Combat.TryBlitzW())
                {
                    PlayBotOriginalSkill(bot, NativeSkillSlot.W);
                    combatVfx?.Pulse(bot.Id, bot.Position, "Blitzcrank W");
                    return;
                }
                if (bot.Role == MatchRole.Hunter &&
                    bot.Loadout.Knows(NativeSkillSlot.E) &&
                    bot.Combat.TryBlitzE())
                {
                    PlayBotOriginalSkill(bot, NativeSkillSlot.E);
                    combatVfx?.Pulse(bot.Id, bot.Position, "Blitzcrank E");
                }
            }
            else if (bot.Hero == "Riven")
            {
                Vector2 castDirection = bot.Role == MatchRole.Hider ?
                    -direction : direction;
                var q = bot.Combat.TryRivenQ(bot.Position, castDirection, targets);
                if (q != null)
                {
                    ApplyBotSkillImpact(bot, q);
                    return;
                }
                if (bot.Loadout.Knows(NativeSkillSlot.W))
                {
                    var w = bot.Combat.TryRivenW(bot.Position, targets);
                    if (w != null) { ApplyBotSkillImpact(bot, w); return; }
                }
                if (bot.Loadout.Knows(NativeSkillSlot.E))
                    ApplyBotSkillImpact(bot,
                        bot.Combat.TryRivenE(bot.Position, castDirection));
            }
            else if (bot.Hero == "Yasuo")
            {
                if (BotYasuoHeroQ(bot, direction)) return;
                if (bot.Loadout.Knows(NativeSkillSlot.W))
                {
                    // No additional enemy reveal: each AI has already
                    // verified personal vision before casting.
                    if (bot.Combat.TryYasuoWindWall(bot.Position, direction))
                    {
                        PlayBotOriginalSkill(bot, NativeSkillSlot.W);
                        combatVfx?.Pulse(bot.Id, bot.Position, "Yasuo W");
                        return;
                    }
                }
                if (bot.Loadout.Knows(NativeSkillSlot.E))
                    BotYasuoHeroE(bot, targets);
            }
        }

        bool BotYasuoHeroQ(Bot bot, Vector2 direction)
        {
            if (bot.YasuoSkill == null || !bot.Loadout.Knows(NativeSkillSlot.Q) ||
                bot.YasuoSkill.CooldownQ > 0 || !hextech.CanUseHeroSkill(bot.Id) ||
                direction.sqrMagnitude < 1f ||
                (bot.YasuoQFlight != null && bot.YasuoQFlight.Active))
                return false;
            if (!bot.YasuoSkill.TryQ(bot.Loadout, rules, hextech, bot.Id,
                                    false, out bool tornado))
                return false;
            float reach = tornado ? 660f : NativeYasuoSkillState.QReachProvisional;
            float width = tornado ? 90f : NativeYasuoSkillState.QHalfWidthProvisional;
            var projectile = new NativeDirectionalProjectile();
            if (!projectile.Begin(bot.Id, bot.Role,
                    tornado ? "Yasuo Q Tornado" : "Yasuo Q",
                    bot.Position, direction, reach, 1900f, width,
                    rift.Navigation))
            {
                Debug.LogWarning("NATIVE_YASUO_Q_PROJECTILE_REJECTED: actor=" + bot.Id);
                return true; // the accepted cast still owns its cooldown
            }
            bot.YasuoQFlight = projectile;
            bot.YasuoQTornadoFlight = tornado;
            PlayBotOriginalSkill(bot, NativeSkillSlot.Q, tornado ? 3 : 1);
            combatVfx?.Pulse(bot.Id, bot.Position,
                tornado ? "Yasuo Q Tornado" : "Yasuo Q");
            Debug.Log("NATIVE_YASUO_Q_LAUNCHED: actor=" + bot.Id +
                      " tornado=" + tornado + " stacks=" + bot.YasuoSkill.QStacks +
                      " range=" + reach + " speed=1900 damage=0 capture=false");
            return true;
        }

        bool BotYasuoHeroE(Bot bot, IReadOnlyList<NativeSkillTarget> targets)
        {
            if (bot.YasuoSkill == null || !bot.Loadout.Knows(NativeSkillSlot.E) ||
                bot.Dash.Active || targets == null) return false;
            NativeSkillTarget selected = default;
            float nearest = float.PositiveInfinity;
            bool found = false;
            foreach (var target in targets)
            {
                if (target.Eliminated || !target.VisibleToCaster) continue;
                float distance = (target.Position - bot.Position).sqrMagnitude;
                if (distance >= nearest) continue;
                selected = target;
                nearest = distance;
                found = true;
            }
            if (!found || !bot.YasuoSkill.TryEDashThroughHero(
                bot.Loadout, rules, hextech, bot.Id, bot.Position,
                selected, rift.Navigation, out var landing))
                return false;
            if (!bot.Dash.Begin(rift.Navigation, bot.Position, landing))
                return false;
            PlayBotOriginalSkill(bot, NativeSkillSlot.E);
            combatVfx?.Pulse(bot.Id, bot.Position, "Yasuo E Hero");
            bot.Path.Clear();
            bot.Waypoint = 0;
            bot.BasicContact.Cancel();
            Debug.Log("NATIVE_YASUO_E_HERO: actor=" + bot.Id +
                      " target=" + selected.Id + " visible=true" +
                      " landing=" + landing + " damage=0 capture=false");
            return true;
        }

        void BotYasuoNeutralEscape(Bot bot, int hunterId)
        {
            if (bot.YasuoSkill == null ||
                !bot.Loadout.Knows(NativeSkillSlot.E) ||
                rift.NeutralMonsters == null ||
                !rift.NeutralMonsters.HasVerifiedCamps) return;
            Vector2 hunter = GetPosition(hunterId);
            NativeNeutralMonster selected = null;
            float farthest = (bot.Position - hunter).sqrMagnitude;
            foreach (var monster in rift.NeutralMonsters.Monsters)
            {
                float away = (monster.WalkableAnchor - hunter).sqrMagnitude;
                if (away <= farthest) continue;
                float distance = (monster.WalkableAnchor - bot.Position).sqrMagnitude;
                if (distance > NativeYasuoSkillState.EDashRangeProvisional *
                               NativeYasuoSkillState.EDashRangeProvisional)
                    continue;
                selected = monster;
                farthest = away;
            }
            if (selected != null &&
                bot.YasuoSkill.TryEDashToMonster(bot.Loadout, rules, hextech,
                    bot.Id, bot.Position, selected.Id, rift.NeutralMonsters,
                    out var landed))
            {
                if (!bot.Dash.Begin(rift.Navigation, bot.Position, landed))
                    return;
                PlayBotOriginalSkill(bot, NativeSkillSlot.E);
                combatVfx?.Pulse(bot.Id, bot.Position, "Yasuo E");
                bot.Path.Clear();
                bot.Waypoint = 0;
                bot.BasicContact.Cancel();
                Debug.Log("NATIVE_YASUO_E_NEUTRAL_ESCAPE: " + selected.CampName +
                          " monsterAlive=true damage=0 capture=false");
            }
        }

        void BotYasuoNeutralQ(Bot bot)
        {
            if (bot.YasuoSkill == null || bot.Dash.Active ||
                rift.NeutralMonsters == null ||
                !rift.NeutralMonsters.HasVerifiedCamps ||
                bot.YasuoSkill.CooldownQ > 0 || !hextech.CanUseHeroSkill(bot.Id))
                return;

            NativeNeutralMonster candidate = null;
            float nearest = float.PositiveInfinity;
            foreach (var monster in rift.NeutralMonsters.Monsters)
            {
                Vector2 vector = monster.WalkableAnchor - bot.Position;
                if (vector.sqrMagnitude < 1f ||
                    vector.sqrMagnitude > NativeYasuoSkillState.QReachProvisional *
                                         NativeYasuoSkillState.QReachProvisional ||
                    vector.sqrMagnitude >= nearest ||
                    !rift.Navigation.LineClear(bot.Position, monster.WalkableAnchor))
                    continue;
                nearest = vector.sqrMagnitude;
                candidate = monster;
            }
            if (candidate == null) return;
            var direction = candidate.WalkableAnchor - bot.Position;
            var hit = rift.NeutralMonsters.FirstLineContact(
                bot.Position, direction,
                NativeYasuoSkillState.QReachProvisional,
                NativeYasuoSkillState.QHalfWidthProvisional);
            if (bot.YasuoSkill.TryQ(bot.Loadout, rules, hextech, bot.Id,
                                    hit != null, out bool tornado))
            {
                PlayBotOriginalSkill(bot, NativeSkillSlot.Q, tornado ? 3 : 1);
                combatVfx?.Pulse(bot.Id, bot.Position,
                    tornado ? "Yasuo Q Tornado" : "Yasuo Q Neutral");
                Debug.Log("NATIVE_YASUO_Q_NEUTRAL: actor=" + bot.Id +
                          " camp=" + (hit != null ? hit.CampName : "miss") +
                          " stacks=" + bot.YasuoSkill.QStacks +
                          " tornado=" + tornado +
                          " damage=0 monsterStillAlive=true");
            }
        }

        void UpdateBot(Bot bot, float dt)
        {
            if (bot.YasuoSkill != null) bot.YasuoSkill.Advance(dt);
            if (bot.Combat != null) bot.Combat.Advance(dt);
            bot.GarenSkills?.Advance(dt);
            var person = rules.Find(bot.Id);
            if (person.Eliminated || rules.Phase == MatchPhase.Finished) return;
            // Active projectiles keep flying even if the caster becomes
            // stunned after cast. Motion and fresh casts remain blocked.
            if (bot.Combat != null && bot.Combat.HookActive)
                ApplyBotSkillImpact(bot, bot.Combat.StepBlitzHook(
                    dt, GetSkillTargets(bot.Id), ActiveEnemyWindWall(bot.Id)));
            if (bot.YasuoQFlight != null && bot.YasuoQFlight.Active)
            {
                var qImpact = bot.YasuoQFlight.Step(dt, rift.Navigation,
                    GetSkillTargets(bot.Id), ActiveEnemyWindWall(bot.Id),
                    bot.Combat != null ? bot.Combat.Elapsed : hextech.Elapsed);
                if (qImpact != null)
                {
                    if (qImpact.TargetId >= 0)
                    {
                        if (bot.YasuoQTornadoFlight)
                            ApplyChampionControl(qImpact.TargetId, .75f);
                        else
                            bot.YasuoSkill.RegisterQHit(false);
                        combatVfx?.Impact(bot.Id, GetPosition(qImpact.TargetId),
                            qImpact.Skill, CanSeeVisualCaster(qImpact.TargetId));
                        Debug.Log("NATIVE_YASUO_Q_CONTACT: actor=" + bot.Id +
                            " target=" + qImpact.TargetId +
                            " tornado=" + bot.YasuoQTornadoFlight +
                            " stacks=" + bot.YasuoSkill.QStacks +
                            " skillCapture=false damage=0");
                    }
                    else if (qImpact.ProjectileBlocked)
                        Debug.Log("NATIVE_YASUO_Q_BLOCKED: actor=" + bot.Id +
                                  " by=terrain_or_windwall");
                }
            }
            // A landed Blitz hook is an already-authorized forced pull.
            // It continues through the short crowd-control window, unlike
            // voluntary movement/dashes which are cancelled by stun.
            if (bot.Pull.Active)
            {
                Vector2 previous = bot.Position;
                bot.VisualPreviousPosition = previous;
                Vector2 pulled = bot.Pull.Step(dt);
                if (rift.JungleOnlyActive &&
                    (!rift.Navigation.CanWalk(pulled) ||
                     !rift.Navigation.LineClear(previous, pulled)))
                {
                    bot.Pull.Cancel();
                    Debug.Log("NATIVE_JUNGLE_BOUNDARY_BLOCK: actor=ai pull");
                    return;
                }
                bot.Position = pulled;
                PlaceMarker(bot);
                UpdateBotCombatVfx(bot, true);
                if (bot.RealRig)
                {
                    Vector2 delta = bot.Position - previous;
                    if (delta.sqrMagnitude > .0001f)
                        bot.RealRig.transform.rotation = Quaternion.LookRotation(
                            new Vector3(delta.x, 0, delta.y), Vector3.up);
                    bot.RealRig.Tick(dt, true);
                }
                return;
            }

            if (!rules.CanMove(bot.Id) || hextech.Stunned(bot.Id) ||
                heroControl.Locked(bot.Id))
            {
                bot.Path.Clear();
                bot.Waypoint = 0;
                bot.Dash.Cancel();
                if (bot.RealRig) bot.RealRig.Tick(dt, false);
                bot.BasicContact.Cancel();
                UpdateBotCombatVfx(bot);
                return;
            }

            if (bot.Dash.Active)
            {
                Vector2 previous = bot.Position;
                bot.VisualPreviousPosition = previous;
                Vector2 dashed = bot.Dash.Step(dt);
                if (rift.JungleOnlyActive &&
                    (!rift.Navigation.CanWalk(dashed) ||
                     !rift.Navigation.LineClear(previous, dashed)))
                {
                    bot.Dash.Cancel();
                    Debug.Log("NATIVE_JUNGLE_BOUNDARY_BLOCK: actor=ai dash");
                    return;
                }
                bot.Position = dashed;
                PlaceMarker(bot);
                ResolveBotBasicContact(bot, dt);
                UpdateBotCombatVfx(bot, true);
                if (bot.RealRig)
                {
                    Vector2 delta = bot.Position - previous;
                    if (delta.sqrMagnitude > .0001f)
                        bot.RealRig.transform.rotation = Quaternion.LookRotation(
                            new Vector3(delta.x, 0, delta.y), Vector3.up);
                    bot.RealRig.Tick(dt, true);
                }
                return;
            }

            bot.ThinkCooldown -= dt;
            if (bot.ThinkCooldown <= 0f)
            {
                bot.ThinkCooldown = AiThinkSeconds;
                int enemyId = GetTargetForActor(bot.Id);
                if (enemyId >= 0)
                {
                    Vector2 enemy = GetPosition(enemyId);
                    if (bot.Role == MatchRole.Hunter)
                        BotChoosePath(bot, enemy);
                    else if (rules.Phase == MatchPhase.Pursuit)
                    {
                        Vector2 direction = (bot.Position - enemy).normalized;
                        if (direction.sqrMagnitude < .1f) direction = Vector2.right;
                        if (rift.Navigation.TryNearest(bot.Position + direction * 900f,
                                                       out var escape, 17))
                            BotChoosePath(bot, escape);
                    }
                }
                else if (bot.Waypoint >= bot.Path.Count)
                    BotWander(bot);

                // Yasuo Q may legitimately hit a neutral monster to build
                // tornado stacks, without damage, killing or capturing.
                // This is mechanics-only; Yasuo's rig/VFX are not imported.
                if (bot.Role == MatchRole.Hider && enemyId >= 0)
                    BotYasuoNeutralEscape(bot, enemyId);
                BotCastChampionSkill(bot, enemyId);
                // Champion contact is preferred when visible. Otherwise
                // neutral monsters may legitimately build Q stacks.
                BotYasuoNeutralQ(bot);

                // AI can use control augments on enemies within its own
                // perception. Its hextech selection/cooldown is independent.
                if (enemyId >= 0 && hextech.CanUseActiveHextech(bot.Id))
                {
                    var skill = hextech.Selected(bot.Id);
                    if (skill == NativeHextech.FreezeGold ||
                        skill == NativeHextech.AstralSilenceSilver)
                    {
                        float tenacity = enemyId == HumanId && selectedHero == "Garen" ?
                            garenSkills.CrowdControlDurationFactor :
                            FindBot(enemyId)?.GarenSkills != null ?
                            FindBot(enemyId).GarenSkills.CrowdControlDurationFactor : 1f;
                        if (hextech.TryActive(bot.Id, enemyId, true, 0, null,
                                              out var message, tenacity))
                            Debug.Log("MATCH_AI_HEXTECH: " + bot.Hero + " " + message);
                    }
                    else if (skill == NativeHextech.TimeClockSilver)
                    {
                        int direction = bot.Role == MatchRole.Hunter ? -1 : 1;
                        hextech.TryActive(bot.Id, enemyId, true, direction, null,
                                          out var message);
                    }
                }
            }

            // Do not mix ordinary path movement into the same frame as a
            // newly-authorized ability dash. The timed dash owns the next frame.
            if (bot.Dash.Active)
            {
                bot.VisualPreviousPosition = bot.Position;
                if (bot.RealRig) bot.RealRig.Tick(dt, false);
                UpdateBotCombatVfx(bot, true);
                return;
            }

            Vector2 beforeMove = bot.Position;
            if (bot.Waypoint < bot.Path.Count)
            {
                float allowance = (bot.Role == MatchRole.Hunter ? HunterSpeed : HiderSpeed)
                    * hextech.MoveMultiplier(bot.Id) *
                    (bot.Combat != null ? bot.Combat.MoveMultiplier : 1f) *
                    (bot.GarenSkills != null ? bot.GarenSkills.MovementMultiplier : 1f) * dt;
                while (allowance > 0 && bot.Waypoint < bot.Path.Count)
                {
                    Vector2 goal = bot.Path[bot.Waypoint];
                    Vector2 difference = goal - bot.Position;
                    float length = difference.magnitude;
                    if (length < .001f || length <= allowance)
                    {
                        if (rift.JungleOnlyActive &&
                            (!rift.Navigation.CanWalk(goal) ||
                             !rift.Navigation.LineClear(bot.Position, goal)))
                        {
                            bot.Path.Clear();
                            bot.Waypoint = 0;
                            Debug.Log("NATIVE_JUNGLE_BOUNDARY_BLOCK: actor=ai path");
                            break;
                        }
                        bot.Position = goal;
                        allowance -= length;
                        bot.Waypoint++;
                    }
                    else
                    {
                        Vector2 next = bot.Position + difference.normalized * allowance;
                        if (rift.Navigation.CanWalk(next) &&
                            (!rift.JungleOnlyActive ||
                             rift.Navigation.LineClear(bot.Position, next)))
                            bot.Position = next;
                        allowance = 0f;
                    }
                }
            }
            PlaceMarker(bot);
            bot.VisualPreviousPosition = beforeMove;
            ResolveBotBasicContact(bot, dt);
            UpdateBotCombatVfx(bot);
            if (bot.RealRig)
            {
                Vector2 change = bot.Position - beforeMove;
                if (change.sqrMagnitude > .0001f)
                    bot.RealRig.transform.rotation = Quaternion.LookRotation(
                        new Vector3(change.x, 0, change.y), Vector3.up);
                bot.RealRig.Tick(dt, change.sqrMagnitude > .0001f);
            }

            bot.AttackCooldown = Mathf.Max(0f, bot.AttackCooldown - dt);
            if (bot.Role == MatchRole.Hunter && rules.CanAttack(bot.Id) &&
                bot.AttackCooldown <= 0)
            {
                int targetId = GetTargetForActor(bot.Id);
                if (targetId >= 0)
                {
                    float distance = Vector2.Distance(bot.Position, GetPosition(targetId));
                    if (distance <= NativeMatchRules.BasicAttackReach &&
                        !heroControl.Locked(bot.Id))
                    {
                        bot.AttackCooldown = AiBasicCooldown;
                        if (bot.RealRig && bot.RealRig.HasClip("attack1"))
                            bot.RealRig.PlayAttack();
                        bot.BasicContact.Begin(targetId);
                        combatVfx?.Pulse(bot.Id, bot.Position, bot.Hero + " Basic");
                    }
                }
            }
        }

        void CreateMarker(Bot bot)
        {
            // Optional, externally provided ORIGINAL source meshes. The repo
            // contains only Garen by default. Never silently substitute an
            // invented skinned mesh for Blitzcrank/Riven/Yasuo.
            string folder = Path.Combine(Application.streamingAssetsPath,
                                          "Champions", bot.Hero);
            if (GarenRigRuntime.SourceFilesPresent(folder, bot.Hero))
            {
                try
                {
                    bot.RealRig = GarenRigRuntime.LoadChampion(transform, folder, bot.Hero);
                    bot.AnimationBindings = LoadBindings(folder, bot.Hero, bot.RealRig);
                    bot.Marker = bot.RealRig.gameObject;
                    PlaceMarker(bot);
                    Debug.Log("NATIVE_ORIGINAL_BOT_RIG_READY: " + bot.Hero);
                    return;
                }
                catch (Exception loadFailure)
                {
                    bot.RealRig = null;
                    Debug.LogError("NATIVE_ORIGINAL_BOT_RIG_FAILED: " +
                                   bot.Hero + " " + loadFailure);
                }
            }
            bot.Marker = new GameObject("AI_TACTICAL_MARKER_" + bot.Hero + "_NOT_CHAMPION_ART");
            bot.Ring = bot.Marker.AddComponent<LineRenderer>();
            bot.Ring.useWorldSpace = false;
            bot.Ring.loop = true;
            bot.Ring.positionCount = 32;
            bot.Ring.startWidth = 12f;
            bot.Ring.endWidth = 12f;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32f;
                bot.Ring.SetPosition(i,
                    new Vector3(Mathf.Cos(angle) * 55f, 14f,
                                Mathf.Sin(angle) * 55f));
            }
            var template = Resources.Load<Material>("Native/GarenLit");
            if (!template) throw new InvalidOperationException(
                "Native/GarenLit is absent from the Player build");
            bot.RingMaterial = new Material(template);
            bot.RingMaterial.name = "AI tactical ring for " + bot.Hero;
            bot.RingMaterial.SetColor("_BaseColor", bot.Role == MatchRole.Hunter ?
                new Color(1f, .25f, .2f) : new Color(1f, .75f, .25f));
            bot.Ring.sharedMaterial = bot.RingMaterial;
            PlaceMarker(bot);
        }

        void PlaceMarker(Bot bot)
        {
            if (bot.Marker && rift && rift.Navigation != null)
                bot.Marker.transform.position = new Vector3(
                    bot.Position.x, rift.Navigation.HeightAt(bot.Position),
                    bot.Position.y);
        }

        int SpectatedAlly()
        {
            if (rules == null || !rules.Find(HumanId).Eliminated)
                return -1;
            var preferred = FindBot(preferredSpectateId);
            if (preferred != null && preferred.Role == humanRole &&
                !rules.Find(preferred.Id).Eliminated)
                return preferred.Id;
            foreach (var bot in bots)
                if (bot.Role == humanRole && !rules.Find(bot.Id).Eliminated)
                    return bot.Id;
            return -1;
        }

        void CycleSpectatedAlly()
        {
            if (rules == null || !rules.Find(HumanId).Eliminated) return;
            var eligible = new List<int>();
            foreach (var bot in bots)
                if (bot.Role == humanRole && !rules.Find(bot.Id).Eliminated)
                    eligible.Add(bot.Id);
            if (eligible.Count == 0)
            {
                preferredSpectateId = -1;
                feedback = "No living ally to spectate; private vision is blank";
                return;
            }
            int current = eligible.IndexOf(SpectatedAlly());
            preferredSpectateId = eligible[(current + 1) % eligible.Count];
            feedback = "Spectating " + rules.Find(preferredSpectateId).HeroKey +
                       " (only this ally's private vision)";
        }

        void UpdateMarkers()
        {
            if (rules == null) return;
            bool humanEliminated = rules.Find(HumanId).Eliminated;
            int observerId = humanEliminated ? SpectatedAlly() : HumanId;
            // With no surviving ally there is no legal active vision source.
            // Never leak enemies through the eliminated player's last position.
            foreach (var bot in bots)
            {
                bool shown = observerId >= 0 &&
                    !rules.Find(bot.Id).Eliminated &&
                    (bot.Id == observerId || VisibleTo(observerId, bot.Id));
                // The spectated ally is the viewer, not a hidden enemy:
                // display its own authentic rig/diagnostic marker while
                // keeping other enemies subject to that ally's private LOS.
                if (bot.Ring) bot.Ring.enabled = shown;
                if (bot.RealRig) bot.RealRig.gameObject.SetActive(shown);
            }
        }

        void CleanupBots()
        {
            foreach (var bot in bots)
            {
                if (bot.RingMaterial) Destroy(bot.RingMaterial);
                if (bot.Marker) Destroy(bot.Marker);
            }
            bots.Clear();
        }

        void ReturnToRoleSelection()
        {
            if (privateFog) privateFog.End();
            if (combatVfx) combatVfx.End();
            humanContact.Cancel();
            humanPull.Cancel();
            humanDash.Cancel();
            humanYasuoQFlight?.Cancel();
            humanCombat = null;
            humanYasuo = null;
            humanAnimationBindings = null;
            CleanupBots();
            menu = Menu.Role;
            rules = null;
            hextech = null;
            heroControl = null;
            offered = null;
            preferredSpectateId = -1;
            rift.StopMovement();
            rift.ExternalGameplayControl = false;
            rift.ExternalMoveEnabled = true;
            rift.ExternalMoveMultiplier = 1f;
            rift.SetJungleOnly(false);
            rift.CameraInputEnabled = false;
            rift.CameraFollowOverride = null;
            rift.TryTeleportTo(HiderSpawn);
        }

        static bool HasAutomatedVisualFlag()
        {
            var args = Environment.GetCommandLineArgs();
            return Array.IndexOf(args, "-hextechVisualQA") >= 0 ||
                   Array.IndexOf(args, "-hextechGameplayQA") >= 0 ||
                   Array.IndexOf(args, "-hextechChampionVisualQA") >= 0 ||
                   Array.IndexOf(args, "-hextechPhase10QA") >= 0;
        }

        void OnGUI()
        {
            if (!rift || !rift.IsReady)
            {
                // Present a real loading/error state instead of an empty sky
                // while the pinned map and source rigs load asynchronously.
                // Automated GPU captures still exercise the exact scene.
                if (!HasAutomatedVisualFlag())
                    NativeGameUi.DrawLoading(rift ? rift.LoadProblem :
                        "RiftBootstrap not found", rift && rift.HasLoadFailed);
                return;
            }
            if (menu == Menu.Augment)
            {
                if (NativeHextechDraftUI.Draw(offered, out var selection))
                    SelectAugment(selection);
                return;
            }

            NativeGameUi.Begin();
            try
            {
                if (menu == Menu.Role) DrawRoleSelectionUi();
                else if (menu == Menu.Skill) DrawSkillSelectionUi();
                else if (rules != null) DrawInGameUi();
            }
            finally { NativeGameUi.End(); }
        }

        void DrawRoleSelectionUi()
        {
            float x = 38f, y = Mathf.Max(32f, (NativeGameUi.Height - 466f) * .5f);
            NativeGameUi.Panel(new Rect(x, y, 555f, 466f), true);
            NativeGameUi.Text(new Rect(x + 28f, y + 20f, 500f, 40f),
                "HEXTECH  SEEKERS", 30, NativeGameUi.Accent, true);
            NativeGameUi.Text(new Rect(x + 28f, y + 60f, 500f, 22f),
                "SUMMONER'S RIFT    /    NATIVE 3D ALPHA", 13,
                NativeGameUi.Muted, true);
            NativeGameUi.Rule(x + 28f, y + 93f, 499f);
            NativeGameUi.Text(new Rect(x + 28f, y + 105f, 485f, 27f),
                "CHOOSE YOUR CHAMPION", 17, NativeGameUi.Bright, true);
            for (int i = 0; i < NativePlayerRoster.Heroes.Length; i++)
            {
                string hero = NativePlayerRoster.Heroes[i];
                bool active = hero == selectedHero;
                float cx = x + 27f + i * 125f;
                if (NativeGameUi.Button(new Rect(cx, y + 141f, 116f, 67f),
                    hero, isSelected: active))
                {
                    if (rift.SelectPlayerChampion(hero))
                    {
                        selectedHero = hero;
                        feedback = rift.PlayerHasOriginalRig ?
                            "Original " + hero + " model ready" :
                            hero + ": tactical marker; original art is not installed";
                    }
                }
                NativeGameUi.Text(new Rect(cx + 6f, y + 207f, 107f, 18f),
                    active ? "SELECTED" : "AVAILABLE", 10,
                    active ? NativeGameUi.Teal : NativeGameUi.Muted, true,
                    TextAnchor.MiddleCenter);
            }
            NativeGameUi.Pill(new Rect(x + 28f, y + 238f, 168f, 25f),
                rift.PlayerHasOriginalRig ? "ORIGINAL RIG READY" :
                "TACTICAL MARKER ONLY", rift.PlayerHasOriginalRig);
            NativeGameUi.Text(new Rect(x + 210f, y + 239f, 313f, 23f),
                "Selected: " + selectedHero, 14, NativeGameUi.Bright, true);
            NativeGameUi.Rule(x + 28f, y + 280f, 499f);
            NativeGameUi.Text(new Rect(x + 28f, y + 288f, 300f, 27f),
                "PLAYERS IN MATCH", 14, NativeGameUi.Muted, true);
            if (NativeGameUi.Button(new Rect(x + 340f, y + 284f, 44f, 38f),
                "-", isSmall: true))
                rosterSize = Mathf.Max(2, rosterSize - 1);
            NativeGameUi.Text(new Rect(x + 387f, y + 286f, 43f, 36f),
                rosterSize.ToString(), 22, NativeGameUi.Bright, true,
                TextAnchor.MiddleCenter);
            if (NativeGameUi.Button(new Rect(x + 434f, y + 284f, 44f, 38f),
                "+", isSmall: true))
                rosterSize = Mathf.Min(4, rosterSize + 1);
            if (NativeGameUi.Button(new Rect(x + 28f, y + 340f, 234f, 55f),
                "PLAY AS HUNTER", isPrimary: true))
                Configure(MatchRole.Hunter);
            if (NativeGameUi.Button(new Rect(x + 273f, y + 340f, 250f, 55f),
                "PLAY AS HIDER"))
                Configure(MatchRole.Hider);
            NativeGameUi.Text(new Rect(x + 28f, y + 405f, 497f, 23f),
                "2-minute preparation  |  PvE AI  |  unique champions", 12,
                NativeGameUi.Muted);
            NativeGameUi.Text(new Rect(x + 28f, y + 432f, 497f, 20f),
                string.IsNullOrWhiteSpace(feedback) ?
                "Original art not installed? Gameplay still uses tactical markers." :
                feedback, 11, NativeGameUi.Teal);
        }

        void DrawSkillSelectionUi()
        {
            float w = 536f;
            float x = (NativeGameUi.Width - w) * .5f;
            float y = (NativeGameUi.Height - 255f) * .5f;
            NativeGameUi.Panel(new Rect(x, y, w, 255f), true);
            NativeGameUi.Text(new Rect(x + 25f, y + 21f, w - 50f, 32f),
                selectedHero.ToUpperInvariant() + "  /  CHOOSE ONE SKILL",
                23, NativeGameUi.Accent, true);
            NativeGameUi.Text(new Rect(x + 26f, y + 65f, w - 52f, 25f),
                "Your first Q / W / E skill is learned before the round.",
                14, NativeGameUi.Muted);
            foreach (NativeSkillSlot slot in new[] {
                NativeSkillSlot.Q, NativeSkillSlot.W, NativeSkillSlot.E })
            {
                if (!NativeSkillCatalog.Allowed(selectedHero, humanRole, slot))
                    continue;
                int index = (int)slot;
                if (NativeGameUi.Button(
                    new Rect(x + 25f + index * 164f, y + 115f, 155f, 63f),
                    slot.ToString() + "   /   LEARN", isPrimary: true))
                    SelectSkill(slot);
            }
            NativeGameUi.Text(new Rect(x + 25f, y + 202f, w - 50f, 29f),
                "No summoner spells. Garen E (damage-only) is not selectable.",
                12, NativeGameUi.Muted);
        }

        float DisplayCooldown(NativeSkillSlot slot)
        {
            if (humanLoadout == null || !humanLoadout.Knows(slot)) return -1;
            if (selectedHero == "Garen")
                return slot == NativeSkillSlot.Q ? garenSkills.CooldownQ :
                       slot == NativeSkillSlot.W ? garenSkills.CooldownW : -1f;
            if (selectedHero == "Yasuo")
                return slot == NativeSkillSlot.Q ? humanYasuo.CooldownQ :
                       slot == NativeSkillSlot.E ? humanYasuo.CooldownE :
                       humanCombat.CooldownW;
            if (humanCombat == null) return -1;
            return slot == NativeSkillSlot.Q ? humanCombat.CooldownQ :
                   slot == NativeSkillSlot.W ? humanCombat.CooldownW :
                   humanCombat.CooldownE;
        }

        void DrawInGameUi()
        {
            float w = NativeGameUi.Width, h = NativeGameUi.Height;
            string phase = rules.Phase == MatchPhase.Preparation ? "PREPARATION" :
                           rules.Phase == MatchPhase.Pursuit ? "PURSUIT" : "FINISHED";
            var player = rules.Find(HumanId);
            NativeGameUi.Panel(new Rect(18, 16, 344, 149));
            NativeGameUi.Text(new Rect(34, 25, 302, 24),
                "HEXTECH  SEEKERS   /   MATCH", 13, NativeGameUi.Accent, true);
            NativeGameUi.Rule(34, 53, 308);
            NativeGameUi.Text(new Rect(34, 62, 307, 26),
                selectedHero + "  /  " + humanRole.ToString().ToUpperInvariant(), 18,
                NativeGameUi.Bright, true);
            NativeGameUi.Text(new Rect(34, 95, 307, 23),
                "Captured  " + rules.CapturedCount + "/" + rules.HiderCount +
                "     SCORE  " + player.Score, 14, NativeGameUi.Teal, true);
            NativeGameUi.Text(new Rect(34, 123, 309, 25),
                "TARGET   " + SelectedTargetStatus(), 12, NativeGameUi.Muted);

            float stageX = (w - 326f) * .5f;
            NativeGameUi.Panel(new Rect(stageX, 15f, 326f, 80f), true);
            NativeGameUi.Text(new Rect(stageX + 14, 22, 298f, 20f),
                phase, 14, NativeGameUi.Accent, true, TextAnchor.MiddleCenter);
            NativeGameUi.Text(new Rect(stageX + 14, 42, 298f, 38f),
                Mathf.CeilToInt(rules.RemainingSeconds) + "s",
                28, NativeGameUi.Bright, true, TextAnchor.MiddleCenter);

            var chosen = hextech.Selected(HumanId);
            string augment = chosen.HasValue ?
                NativeHextechPool.Find(chosen.Value).Name : "None";
            NativeGameUi.Panel(new Rect(18, 178, 344, 84));
            NativeGameUi.Text(new Rect(34, 190, 313, 23),
                "HEXTECH   " + augment, 14, NativeGameUi.Accent, true);
            NativeGameUi.Text(new Rect(34, 223, 311, 20),
                "F  /  " + Mathf.CeilToInt(hextech.RemainingCooldown(HumanId)) +
                "s cooldown     V  /  fog: " +
                (privateFog && privateFog.Running ?
                    privateFog.DisplayMode.ToString() : "Unavailable"), 12,
                NativeGameUi.Muted);

            // These buttons were present in the previous diagnostic UI; keep
            // their actions unchanged while reducing their visual footprint.
            float actionY = 272f;
            if (chosen == NativeHextech.TimeClockSilver &&
                rules.Phase == MatchPhase.Pursuit)
            {
                if (NativeGameUi.Button(new Rect(18, actionY, 167, 39),
                    "CLOCK  -1 MIN", isSmall: true))
                    TryUseHextech(-1);
                if (NativeGameUi.Button(new Rect(195, actionY, 167, 39),
                    "CLOCK  +1 MIN", isSmall: true))
                    TryUseHextech(1);
                actionY += 44f;
            }
            if (humanLoadout != null && humanLoadout.Level == 2 &&
                !humanLoadout.Secondary.HasValue &&
                rules.Phase == MatchPhase.Preparation)
            {
                int i = 0;
                foreach (NativeSkillSlot slot in new[] {
                    NativeSkillSlot.Q, NativeSkillSlot.W, NativeSkillSlot.E })
                {
                    if (slot == humanLoadout.Primary ||
                        !NativeSkillCatalog.Allowed(selectedHero, humanRole, slot))
                        continue;
                    if (NativeGameUi.Button(new Rect(18 + 173 * i,
                        actionY, 165, 38), "LEVEL 2  +  " + slot,
                        isSmall: true))
                        humanLoadout.Choose(slot);
                    i++;
                }
            }

            // Input hints are displayed as text only. Q/W/E/F/A are still
            // handled exclusively by the existing controller input paths.
            float barX = (w - 450f) * .5f, barY = h - 116f;
            NativeGameUi.Panel(new Rect(barX, barY, 450f, 99f));
            NativeGameUi.Text(new Rect(barX + 14, barY + 7, 423, 19),
                "ABILITIES    /    KEYBOARD", 11, NativeGameUi.Muted, true);
            for (int i = 0; i < 5; i++)
            {
                string key = i == 0 ? "A" : i == 4 ? "F" :
                             ((NativeSkillSlot)(i - 1)).ToString();
                float cd = i == 0 ? basicCooldown : i == 4 ?
                    hextech.RemainingCooldown(HumanId) :
                    DisplayCooldown((NativeSkillSlot)(i - 1));
                bool usable = i == 0 || i == 4 || cd >= 0;
                float slotX = barX + 13 + i * 87f;
                NativeGameUi.Fill(new Rect(slotX, barY + 28, 77f, 56f),
                    usable ? new Color(.13f, .26f, .35f, .96f) :
                             new Color(.09f, .12f, .17f, .96f));
                NativeGameUi.Text(new Rect(slotX, barY + 30, 77f, 29f),
                    key, 21, usable ? NativeGameUi.Accent :
                    NativeGameUi.Muted, true, TextAnchor.MiddleCenter);
                NativeGameUi.Text(new Rect(slotX, barY + 60, 77f, 21f),
                    !usable ? "LOCKED" : cd > .01f ?
                    Mathf.CeilToInt(cd) + "s" :
                    i == 0 ? "ATTACK" : i == 4 ? "HEXTECH" : "READY",
                    11, usable ? NativeGameUi.Teal : NativeGameUi.Muted,
                    true, TextAnchor.MiddleCenter);
            }
            // Camera controls are presentation-only; never unlock target
            // visibility or change the Map11 navigation/world transform.
            NativeGameUi.Panel(new Rect(Mathf.Max(12f, w - 287f),
                h - 171f, 270f, 154f));
            NativeGameUi.Text(new Rect(w - 272f, h - 165f, 238f, 21f),
                "CAMERA   " + rift.CameraStatus, 11,
                NativeGameUi.Accent, true);
            NativeGameUi.Text(new Rect(w - 272f, h - 138f, 238f, 20f),
                "Y  FREE / LOCK      SPACE  RECENTER", 11,
                NativeGameUi.Bright, true);
            NativeGameUi.Text(new Rect(w - 272f, h - 115f, 238f, 20f),
                "WHEEL  ZOOM       ARROWS  PAN", 11,
                NativeGameUi.Muted, true);
            NativeGameUi.Text(new Rect(w - 272f, h - 92f, 238f, 20f),
                "[ ]  ROTATE       PGUP / PGDN  TILT", 10,
                NativeGameUi.Muted);
            NativeGameUi.Text(new Rect(w - 272f, h - 69f, 238f, 20f),
                "0  RESET  /  V  FOG  /  TAB  TARGET", 10,
                NativeGameUi.Teal);
            NativeGameUi.Text(new Rect(w - 272f, h - 43f, 238f, 18f),
                "RMB MOVE     A ATTACK     S STOP", 10,
                NativeGameUi.Muted);
            if (!string.IsNullOrWhiteSpace(feedback))
            {
                NativeGameUi.Panel(new Rect(18, h - 100f, 350f, 82f));
                NativeGameUi.Text(new Rect(32, h - 93f, 323f, 19f),
                    "MATCH UPDATE", 11, NativeGameUi.Accent, true);
                NativeGameUi.Text(new Rect(32, h - 68f, 323f, 41f),
                    feedback, 12, NativeGameUi.Bright);
            }
            if (player.Eliminated)
                NativeGameUi.Text(new Rect(stageX - 20f, 99f, 366f, 27f),
                    "SPECTATING  /  TAB cycles living allies",
                    13, NativeGameUi.Teal, true, TextAnchor.MiddleCenter);

            if (rules.Phase == MatchPhase.Finished)
            {
                float endX = (w - 425f) * .5f, endY = (h - 285f) * .5f;
                NativeGameUi.Panel(new Rect(endX, endY, 425f, 285f), true);
                NativeGameUi.Text(new Rect(endX + 18, endY + 19, 390f, 33f),
                    rules.HunterWon ? "HUNTER VICTORY" : "HIDERS SURVIVE",
                    23, NativeGameUi.Accent, true,
                    TextAnchor.MiddleCenter);
                int row = 0;
                foreach (var participant in rules.Players)
                {
                    NativeGameUi.Text(new Rect(endX + 28, endY + 67 + row * 30,
                        375f, 27f), participant.HeroKey + "   /   " +
                        participant.Role + "   /   " + participant.Score +
                        (participant.Eliminated ? "   OUT" : "   ALIVE"), 13,
                        NativeGameUi.Bright);
                    row++;
                }
                if (NativeGameUi.Button(new Rect(endX + 48, endY + 220,
                    329f, 45f), "RETURN TO ROLE SELECTION", isPrimary: true))
                    ReturnToRoleSelection();
            }
        }

        [Serializable]
        sealed class GameplayReport
        {
            public string status;
            public string reason;
            public bool loadedRift;
            public bool hunterLockedDuringPreparation;
            public bool hunterReleasedAfterPreparation;
            public bool hunterCapturedUsingBasicAttack;
            public bool preferredTargetAcquiredOnRealRift;
            public bool eliminatedTargetRejectedOnRealRift;
            public bool caughtHiderEliminated;
            public bool hunterEarlyBonusApplied;
            public bool hiderCanMoveDuringPreparation;
            public bool hiderWinsBySurviving;
            public int numberOfActors;
            public int optionalOriginalSources, optionalOriginalRigs;
            public int selectableHeroRostersValidated, missingHumanOriginalArt;
            public int humanSkillSmokesPassed, humanSkillSmokesExpected;
            public bool noHumanSkillDirectCaptures;
            public string[] humanSkillEvidence;
            public int targetedSkillSmokesExpected, targetedSkillSmokesPassed;
            public string[] targetedSkillEvidence;
            public int visionSkillSmokesExpected, visionSkillSmokesPassed;
            public string[] visionSkillEvidence;
            public int neutralSkillSmokesExpected, neutralSkillSmokesPassed;
            public string[] neutralSkillEvidence;
            public int originalNeutralModelsExpected, originalNeutralModelsLoaded;
            public int originalBlueMeshVertices;
            public bool originalBlueIdlePlaying;
            public bool originalBlueVisualSnapshotCaptured;
        }

        // This test intentionally calls the SAME private cast entry used by Q/W/E
        // key handling. It supplies only a deterministic mouse aim, never a
        // synthetic skill-success flag or a forged capture result.
        Vector2 QaSafeDashDirection(float length)
        {
            Vector2 origin = rift.PlayerPosition;
            var choices = new[] {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1, 1).normalized, new Vector2(1, -1).normalized,
                new Vector2(-1, 1).normalized, new Vector2(-1, -1).normalized
            };
            float bestDistance = 0f;
            Vector2 bestDirection = Vector2.right;
            foreach (var direction in choices)
            {
                Vector2 landing = NativeSkillGeometry.WalkableDash(
                    rift.Navigation, origin, direction, length);
                float distance = Vector2.Distance(origin, landing);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestDirection = direction;
                }
            }
            if (bestDistance < 8f)
                throw new InvalidOperationException("No safe Map11 dash path for human skill QA");
            return bestDirection;
        }

        void VerifyHumanSkillOnRealRift(string hero, MatchRole role,
                                       NativeSkillSlot slot, List<string> audit)
        {
            ReturnToRoleSelection();
            selectedHero = hero;
            rosterSize = 2;
            Configure(role);
            SelectAugment(offered[0].Id);
            SelectSkill(slot);
            if (menu != Menu.Playing || !humanLoadout.Knows(slot) ||
                rules.Players.Count != 2)
                throw new InvalidOperationException("QA skill loadout not playable: " + hero + " " + slot);

            bool lockedDuringPreparation = role == MatchRole.Hunter;
            if (lockedDuringPreparation)
            {
                if (TryHumanSkill(slot, Vector2.right))
                    throw new InvalidOperationException("Hunter cast during preparation: " + hero);
                rules.Advance(NativeMatchRules.PreparationSeconds);
                if (!rules.CanMove(HumanId))
                    throw new InvalidOperationException("Hunter remained locked after preparation");
            }
            else if (!rules.CanMove(HumanId) || rules.Phase != MatchPhase.Preparation)
                throw new InvalidOperationException("Hider could not cast during preparation");

            if (!rift.TryTeleportTo(HiderSpawn))
                throw new InvalidOperationException("Map11 QA staging is not navigable");
            // Target-dependent Riven W requires a real opposing actor in
            // physical melee proximity. No target is auto-captured.
            if (hero == "Riven" && slot == NativeSkillSlot.W)
            {
                bots[0].Position = rift.PlayerPosition;
                if (!VisibleTo(HumanId, bots[0].Id))
                    throw new InvalidOperationException("Nearby Riven W target not visible");
            }
            Vector2 direction = (hero == "Riven" &&
                (slot == NativeSkillSlot.E || slot == NativeSkillSlot.Q))
                ? QaSafeDashDirection(slot == NativeSkillSlot.Q
                    ? NativeChampionCombat.RivenQDashProvisional
                    : NativeChampionCombat.RivenEDashProvisional)
                : Vector2.right;

            int capturesBefore = rules.CapturedCount;
            MatchPhase phaseBefore = rules.Phase;
            bool cast = TryHumanSkill(slot, direction);
            bool active = false;
            switch (hero)
            {
                case "Garen":
                    active = slot == NativeSkillSlot.Q
                        ? garenSkills.QActive : garenSkills.WActive;
                    break;
                case "Blitzcrank":
                    active = slot == NativeSkillSlot.Q ? humanCombat.HookActive :
                        slot == NativeSkillSlot.W ? humanCombat.BlitzBoostRemaining > 0f :
                        humanCombat.BlitzEArmed;
                    break;
                case "Riven":
                    active = slot == NativeSkillSlot.W
                        ? heroControl.Locked(bots[0].Id) :
                        humanDash.Active && (slot != NativeSkillSlot.Q ||
                                             humanCombat.RivenQStage == 1);
                    break;
                case "Yasuo":
                    active = slot == NativeSkillSlot.Q
                        ? humanYasuoQFlight != null && humanYasuoQFlight.Active
                        : humanCombat.WindWallRemaining > 0f;
                    break;
            }

            bool repeatRejected = !TryHumanSkill(slot, direction);
            bool noCapture = rules.CapturedCount == capturesBefore &&
                             rules.Phase == phaseBefore &&
                             !rules.Find(bots[0].Id).Eliminated;
            if (!cast || !active || !repeatRejected || !noCapture)
                throw new InvalidOperationException(
                    "Real human skill smoke failed: " + hero + " " + role + " " + slot +
                    " cast=" + cast + " active=" + active +
                    " repeatRejected=" + repeatRejected + " noCapture=" + noCapture);
            string evidence = hero + ":" + role + ":" + slot +
                              ":cast=true:repeatRejected=true:capture=false";
            audit.Add(evidence);
            Debug.Log("MATCH_HUMAN_SKILL_SMOKE_PASS: " + evidence);
        }

        // Phase 16: exercise the complete Riven Q chain through the same
        // input entry used by the player, including real Rift swept dashes.
        void VerifyHumanRivenComboOnRealRift(List<string> audit)
        {
            ReturnToRoleSelection();
            selectedHero = "Riven";
            rosterSize = 2;
            Configure(MatchRole.Hider);
            SelectAugment(offered[0].Id);
            SelectSkill(NativeSkillSlot.Q);
            if (menu != Menu.Playing || !rift.TryTeleportTo(HiderSpawn))
                throw new InvalidOperationException("Phase16 Riven staging failed");
            int captures = rules.CapturedCount;
            MatchPhase phase = rules.Phase;
            for (int stage = 1; stage <= 3; stage++)
            {
                Vector2 direction = QaSafeDashDirection(
                    NativeChampionCombat.RivenQDashProvisional);
                Vector2 before = rift.PlayerPosition;
                if (stage == 3)
                {
                    // A real enemy is staged at the *computed physical Q3
                    // landing*, so the authentic contact geometry and
                    // controller crowd-control path must both execute.
                    Vector2 q3Landing = NativeSkillGeometry.WalkableDash(
                        rift.Navigation, before, direction,
                        NativeChampionCombat.RivenQDashProvisional);
                    if (!rift.Navigation.CanWalk(q3Landing) ||
                        !rift.Navigation.LineClear(before, q3Landing))
                        throw new InvalidOperationException(
                            "Phase16 Riven Q3 target position is obstructed");
                    bots[0].Position = q3Landing;
                    if (!VisibleTo(HumanId, bots[0].Id) ||
                        heroControl.Locked(bots[0].Id))
                        throw new InvalidOperationException(
                            "Phase16 Riven Q3 target is hidden or already stunned");
                }
                if (!TryHumanSkill(NativeSkillSlot.Q, direction) ||
                    !humanDash.Active ||
                    humanCombat.RivenQStage != (stage == 3 ? 0 : stage))
                    throw new InvalidOperationException("Phase16 Riven Q" + stage +
                        " did not start from the real human cast entry");
                if (stage == 3 && !heroControl.Locked(bots[0].Id))
                    throw new InvalidOperationException(
                        "Phase16 Riven Q3 failed to control the real enemy");
                if (TryHumanSkill(NativeSkillSlot.Q, direction))
                    throw new InvalidOperationException(
                        "Phase16 Riven Q accepted an immediate recast");
                Vector2 landing = humanDash.Step(
                    NativeDashMotion.ProvisionalDashSeconds);
                if (humanDash.Active || !rift.Navigation.CanWalk(landing) ||
                    !rift.Navigation.LineClear(before, landing) ||
                    !rift.TrySetExternalPosition(landing))
                    throw new InvalidOperationException(
                        "Phase16 Riven Q swept dash was not navigable");
                humanCombat.Advance(
                    NativeChampionCombat.RivenQRecastMinimumProvisional + .01f);
                if (rules.CapturedCount != captures || rules.Phase != phase ||
                    rules.Find(bots[0].Id).Eliminated)
                    throw new InvalidOperationException(
                        "Phase16 Riven Q caused a direct capture");
                string record = "Riven:Hider:Q" + stage +
                    ":entry=TryHumanSkill:dash=real-rift:recast=blocked" +
                    (stage == 3 ? ":real-enemy-stunned=true" : "") +
                    ":capture=false";
                audit.Add(record);
                Debug.Log("MATCH_TARGETED_SKILL_SMOKE_PASS: " + record);
            }
            if (TryHumanSkill(NativeSkillSlot.Q, Vector2.right) ||
                humanCombat.CooldownQ <= 0f)
                throw new InvalidOperationException(
                    "Phase16 Riven Q bypassed post-combo cooldown");
        }

        // The bot is positioned only on a genuine visible Map11 walkable
        // segment, not a fabricated skill-target record or forced success.
        void VerifyHumanYasuoEOnRealRift(List<string> audit)
        {
            ReturnToRoleSelection();
            selectedHero = "Yasuo";
            rosterSize = 2;
            Configure(MatchRole.Hider);
            SelectAugment(offered[0].Id);
            SelectSkill(NativeSkillSlot.E);
            if (menu != Menu.Playing || !rift.TryTeleportTo(HiderSpawn))
                throw new InvalidOperationException("Phase16 Yasuo staging failed");
            Vector2 origin = rift.PlayerPosition;
            Vector2[] headings = {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1, 1).normalized, new Vector2(1, -1).normalized,
                new Vector2(-1, 1).normalized, new Vector2(-1, -1).normalized
            };
            bool staged = false;
            foreach (Vector2 heading in headings)
            {
                Vector2 target = origin + heading * 120f;
                Vector2 exit = origin + heading *
                    (120f + NativeYasuoSkillState.EDashBeyondProvisional);
                if (!rift.Navigation.CanWalk(target) ||
                    !rift.Navigation.CanWalk(exit) ||
                    !rift.Navigation.LineClear(origin, exit)) continue;
                bots[0].Position = target;
                if (!VisibleTo(HumanId, bots[0].Id)) continue;
                staged = true;
                break;
            }
            if (!staged)
                throw new InvalidOperationException(
                    "Phase16 no visible Map11 Yasuo E target/exit path");
            if (TryHumanSkill(NativeSkillSlot.E) != true ||
                !humanDash.Active)
                throw new InvalidOperationException(
                    "Phase16 Yasuo E failed real target-dependent cast");
            Vector2 landing = humanDash.Step(
                NativeDashMotion.ProvisionalDashSeconds);
            if (humanDash.Active || !rift.Navigation.CanWalk(landing) ||
                !rift.Navigation.LineClear(origin, landing) ||
                !rift.TrySetExternalPosition(landing))
                throw new InvalidOperationException(
                    "Phase16 Yasuo E real dash path failed");
            if (rules.CapturedCount != 0 || rules.Find(bots[0].Id).Eliminated)
                throw new InvalidOperationException(
                    "Phase16 Yasuo E directly captured a target");
            // Test the actual per-target lock, not just the shorter global
            // cooldown. Restore the legal start before attempting again.
            humanYasuo.Advance(.6f);
            if (!rift.TryTeleportTo(origin))
                throw new InvalidOperationException(
                    "Phase16 Yasuo E reset to verified staging failed");
            NativeSkillTarget locked = default;
            bool found = false;
            foreach (var target in GetSkillTargets(HumanId))
                if (target.Id == bots[0].Id) { locked = target; found = true; break; }
            if (!found || !locked.VisibleToCaster ||
                humanYasuo.TryEDashThroughHero(
                    humanLoadout, rules, hextech, HumanId,
                    origin, locked, rift.Navigation, out var ignoredLanding))
                throw new InvalidOperationException(
                    "Phase16 Yasuo E per-target reuse lock failed");
            const string record =
                "Yasuo:Hider:E:entry=TryHumanSkill:target=visible-opponent:dash=real-rift:target-lock=blocked:capture=false";
            audit.Add(record);
            Debug.Log("MATCH_TARGETED_SKILL_SMOKE_PASS: " + record);
        }

        // Phase17A: physical Map11 bush concealment, using live opponent
        // positions and the same TryHumanSkill(E) path as keyboard input.
        // No fabricated VisibleToCaster flags or synthetic target records.
        void VerifyHumanYasuoEHiddenTargetsOnRealRift(List<string> audit)
        {
            ReturnToRoleSelection();
            selectedHero = "Yasuo";
            rosterSize = 3;
            Configure(MatchRole.Hunter);
            SelectAugment(offered[0].Id);
            SelectSkill(NativeSkillSlot.E);
            if (menu != Menu.Playing || bots.Count != 2)
                throw new InvalidOperationException("Phase17A Yasuo hunter roster staging failed");
            rules.Advance(NativeMatchRules.PreparationSeconds);
            if (!rules.CanMove(HumanId))
                throw new InvalidOperationException("Phase17A hunter preparation lock never released");

            var nav = rift.Navigation;
            Vector2[] directions = {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1, 1).normalized, new Vector2(1, -1).normalized,
                new Vector2(-1, 1).normalized, new Vector2(-1, -1).normalized
            };
            Vector2 origin = default, hidden = default, visible = default;
            bool staged = false;
            // The target must be in an actual bush in the pinned Map11
            // navigation binary. Both approach paths must be walkable; the
            // closer enemy must be concealed specifically by bush vision.
            for (int y = 2; y < RiftGrid.Height - 2 && !staged; y += 2)
            for (int x = 2; x < RiftGrid.Width - 2 && !staged; x += 2)
            {
                Vector2 bushTarget = nav.Center(x, y);
                if (!nav.CanWalk(bushTarget) || nav.BushAt(bushTarget) == 0)
                    continue;
                foreach (Vector2 direction in directions)
                {
                    Vector2 observer = bushTarget - direction * 200f;
                    if (!nav.CanWalk(observer) || nav.BushAt(observer) != 0 ||
                        !nav.LineClear(observer, bushTarget) ||
                        NativeSight.Visible(nav, observer, bushTarget))
                        continue;
                    foreach (Vector2 candidateDirection in directions)
                    {
                        Vector2 candidate = observer + candidateDirection * 335f;
                        Vector2 exit = candidate + candidateDirection *
                            NativeYasuoSkillState.EDashBeyondProvisional;
                        if (!nav.CanWalk(candidate) || !nav.CanWalk(exit) ||
                            !nav.LineClear(observer, exit) ||
                            !NativeSight.Visible(nav, observer, candidate))
                            continue;
                        origin = observer;
                        hidden = bushTarget;
                        visible = candidate;
                        staged = true;
                        break;
                    }
                    if (staged) break;
                }
            }
            if (!staged || !rift.TryTeleportTo(origin))
                throw new InvalidOperationException(
                    "Phase17A no authentic Map11 bush concealment and valid E exit geometry");

            bots[0].Position = hidden;
            bots[1].Position = visible;
            if (Vector2.Distance(origin, hidden) >= Vector2.Distance(origin, visible) ||
                VisibleTo(HumanId, bots[0].Id) ||
                !VisibleTo(HumanId, bots[1].Id))
                throw new InvalidOperationException(
                    "Phase17A actual nearer hidden/farther visible opponent setup invalid");

            // Prove the concealed target cannot be used alone. Park the
            // second real bot on a valid but out-of-sight Map11 cell.
            Vector2 far = default;
            bool farFound = false;
            for (int y = 2; y < RiftGrid.Height - 2 && !farFound; y += 8)
            for (int x = 2; x < RiftGrid.Width - 2 && !farFound; x += 8)
            {
                Vector2 candidate = nav.Center(x, y);
                if (nav.CanWalk(candidate) &&
                    Vector2.Distance(candidate, origin) >
                    NativeSight.BasicSightDistance + 100f)
                {
                    far = candidate;
                    farFound = true;
                }
            }
            if (!farFound)
                throw new InvalidOperationException(
                    "Phase17A cannot stage real distant Map11 opponent");
            bots[1].Position = far;
            if (VisibleTo(HumanId, bots[1].Id) ||
                TryHumanSkill(NativeSkillSlot.E) || humanDash.Active)
                throw new InvalidOperationException(
                    "Phase17A Yasuo E selected a concealed Map11 enemy");

            // Restore the real second opponent, and prove selection skips
            // the nearer concealed enemy rather than suppressing the cast.
            bots[1].Position = visible;
            if (!TryHumanSkill(NativeSkillSlot.E) || !humanDash.Active)
                throw new InvalidOperationException(
                    "Phase17A Yasuo E failed to skip hidden nearer enemy");
            Vector2 landing = humanDash.Step(NativeDashMotion.ProvisionalDashSeconds);
            if (humanDash.Active || !nav.CanWalk(landing) ||
                !nav.LineClear(origin, landing) ||
                rules.CapturedCount != 0 || rules.Find(bots[0].Id).Eliminated ||
                rules.Find(bots[1].Id).Eliminated)
                throw new InvalidOperationException(
                    "Phase17A legal visible E dash/capture invariant failed");
            const string record =
                "Yasuo:Hunter:E:real-map11-bush=true:hidden-nearest-rejected=true:visible-farther-dashed=true:capture=false";
            audit.Add(record);
            Debug.Log("MATCH_VISION_SKILL_SMOKE_PASS: " + record);
        }

        // Phase17B: source-backed Map11 jungle camps must exist as actual
        // passive Unity targets. Exercise the SAME E entry used by Q/W/E input.
        void VerifyHumanYasuoENeutralOnRealRift(List<string> audit)
        {
            var world = rift.NeutralMonsters;
            if (rift.CampCatalog == null || world == null ||
                !world.HasVerifiedCamps || world.Monsters.Count != 8)
                throw new InvalidOperationException(
                    "Phase17B expected eight source-verified Map11 neutral entities");

            ReturnToRoleSelection();
            selectedHero = "Yasuo";
            rosterSize = 2;
            Configure(MatchRole.Hider);
            SelectAugment(offered[0].Id);
            SelectSkill(NativeSkillSlot.E);
            if (menu != Menu.Playing || !rules.CanMove(HumanId) ||
                bots.Count != 1 || !humanLoadout.Knows(NativeSkillSlot.E))
                throw new InvalidOperationException(
                    "Phase17B Yasuo hider E preparation failed");

            var nav = rift.Navigation;
            Vector2[] directions = {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1, 1).normalized, new Vector2(1, -1).normalized,
                new Vector2(-1, 1).normalized, new Vector2(-1, -1).normalized
            };
            NativeNeutralMonster testedMonster = null;
            Vector2 origin = default, expectedLanding = default;
            foreach (var monster in world.Monsters)
            {
                if (testedMonster != null) break;
                foreach (var heading in directions)
                {
                    if (testedMonster != null) break;
                    foreach (float range in new[] { 160f, 220f, 300f })
                    {
                        Vector2 candidate = monster.WalkableAnchor - heading * range;
                        if (!nav.CanWalk(candidate) ||
                            !world.TryDashLanding(monster.Id, candidate,
                                NativeYasuoSkillState.EDashRangeProvisional,
                                NativeYasuoSkillState.EDashBeyondProvisional,
                                out var candidateLanding) ||
                            !nav.LineClear(candidate, candidateLanding))
                            continue;
                        // Ensure no second camp can be chosen instead of the
                        // intended monster, especially during target-lock replay.
                        bool competing = false;
                        foreach (var other in world.Monsters)
                        {
                            if (other.Id != monster.Id &&
                                Vector2.Distance(candidate, other.WalkableAnchor) <=
                                NativeYasuoSkillState.EDashRangeProvisional)
                                competing = true;
                        }
                        if (competing) continue;
                        testedMonster = monster;
                        origin = candidate;
                        expectedLanding = candidateLanding;
                        break;
                    }
                }
            }
            if (testedMonster == null)
                throw new InvalidOperationException(
                    "Phase17B source-verified camp has no walkable E approach/exit");

            Vector2 far = default;
            bool farFound = false;
            for (int y = 2; y < RiftGrid.Height - 2 && !farFound; y += 8)
            for (int x = 2; x < RiftGrid.Width - 2 && !farFound; x += 8)
            {
                Vector2 candidate = nav.Center(x, y);
                if (nav.CanWalk(candidate) &&
                    Vector2.Distance(candidate, origin) >
                    NativeSight.BasicSightDistance + 100f)
                {
                    far = candidate;
                    farFound = true;
                }
            }
            if (!farFound || !rift.TryTeleportTo(origin))
                throw new InvalidOperationException(
                    "Phase17B failed to stage real Map11 caster/remote opponent");
            bots[0].Position = far;
            if (VisibleTo(HumanId, bots[0].Id))
                throw new InvalidOperationException(
                    "Phase17B opponent still visible during neutral-only E test");

            if (!TryHumanSkill(NativeSkillSlot.E) || !humanDash.Active)
                throw new InvalidOperationException(
                    "Phase17B Yasuo E did not dash through real verified neutral");
            Vector2 landing = humanDash.Step(NativeDashMotion.ProvisionalDashSeconds);
            if (humanDash.Active || !nav.CanWalk(landing) ||
                !nav.LineClear(origin, landing) ||
                Vector2.Distance(landing, expectedLanding) > 1f ||
                !rift.TrySetExternalPosition(landing) ||
                !testedMonster.Alive || testedMonster.Aggressive ||
                rules.CapturedCount != 0 || rules.Find(bots[0].Id).Eliminated)
                throw new InvalidOperationException(
                    "Phase17B neutral E legal landing or passive/no-capture invariant failed");

            // Global E cooldown is 0.5 seconds. After that expires, the
            // particular neutral remains target-locked independently.
            humanYasuo.Advance(.6f);
            if (!rift.TryTeleportTo(origin) ||
                TryHumanSkill(NativeSkillSlot.E) || humanDash.Active)
                throw new InvalidOperationException(
                    "Phase17B same neutral E repeat lock failed after global cooldown");
            string record =
                "Yasuo:Hider:E:real-source-camp=" + testedMonster.CampName +
                ":dash=real-rift:target-lock=blocked:neutral-alive=true:capture=false";
            audit.Add(record);
            Debug.Log("MATCH_NEUTRAL_SKILL_SMOKE_PASS: " + record);
        }

        // These are actual Windows D3D11 Player frames, not static mockups.
        // Screens are preserved on the self-hosted runner for human review.
        [Serializable]
        sealed class UiVisualReport
        {
            public string status = "FAIL";
            public string reason = "";
            public bool originalMapReady;
            public bool hardwareGpu;
            public bool cameraPerspective, cameraHeroInFrame;
            public bool cameraYawDidNotMovePlayer;
            public float cameraFieldOfView, cameraPitchDegrees, cameraDefaultYaw;
            public int screenshots;
            public string[] screenshotNames;
            public bool fogDoesNotAlterTargetVisibility;
            public bool returnedToNormal;
            public float normalExploredAlpha, normalUnknownAlpha;
            public float lightExploredAlpha, lightUnknownAlpha;
            public int screenWidth, screenHeight;
        }

        static readonly string[] UiEvidenceScreens = {
            "ui-role-selection.png", "ui-hextech-draft.png",
            "ui-skill-selection.png", "ui-hud-fog-normal.png",
            "ui-hud-fog-light.png", "ui-hud-fog-off.png",
            "ui-camera-yaw-minus45.png", "ui-camera-yaw-plus45.png"
        };

        IEnumerator CaptureActualUiFrame(string destination)
        {
            // OnGUI is drawn by this Player; the actual frame is captured
            // after rendering, never drawn into a mocked RenderTexture.
            yield return new WaitForEndOfFrame();
            if (File.Exists(destination)) File.Delete(destination);
            ScreenCapture.CaptureScreenshot(destination);
            for (int frame = 0; frame < 180; frame++)
            {
                if (File.Exists(destination) &&
                    new FileInfo(destination).Length >= 16384)
                    break;
                yield return null;
            }
        }

        static bool IsRealUiPng(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 16384)
                return false;
            byte[] actual = new byte[8];
            using (var stream = File.OpenRead(path))
                if (stream.Read(actual, 0, actual.Length) != actual.Length)
                    return false;
            byte[] expected = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < expected.Length; i++)
                if (actual[i] != expected[i]) return false;
            return true;
        }

        void FinishUiEvidence(UiVisualReport report, string evidence,
                              string reason = null)
        {
            report.status = reason == null ?
                "CAPTURED_REQUIRES_VISUAL_REVIEW" : "FAIL";
            report.reason = reason ?? "";
            File.WriteAllText(Path.Combine(evidence, "ui-visual-result.json"),
                              JsonUtility.ToJson(report, true));
            Debug.Log("NATIVE_UI_VISUAL_QA_RESULT: " + report.status +
                      (reason == null ? "" : " reason=" + reason));
            Application.Quit(reason == null ? 0 : 1);
        }

        IEnumerator VerifyUiVisual()
        {
            string evidence = Path.Combine(Application.persistentDataPath,
                                           "HextechUiVisualQA");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-hextechEvidenceDir")
                    evidence = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(evidence);
            var report = new UiVisualReport {
                screenshotNames = UiEvidenceScreens,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                hardwareGpu = SystemInfo.graphicsDeviceType !=
                    UnityEngine.Rendering.GraphicsDeviceType.Null
            };
            float started = Time.realtimeSinceStartup;
            while (rift != null && !rift.IsReady && !rift.HasLoadFailed &&
                   Time.realtimeSinceStartup - started < 180f)
                yield return null;
            report.originalMapReady = rift && rift.IsReady;
            if (!report.originalMapReady || !report.hardwareGpu)
            {
                FinishUiEvidence(report, evidence,
                    "Genuine pinned Map11 or hardware graphics unavailable");
                yield break;
            }

            // 1. Verify real perspective camera projection/hero framing,
            // then capture genuinely different yaw angles at the same map
            // coordinates. This is evidence for visual comparison, never a
            // claim that a guessed yaw matches Riot's exact original camera.
            yield return null; // allow RiftBootstrap.LateUpdate to apply pose
            var camera = Camera.main;
            if (!camera)
            {
                FinishUiEvidence(report, evidence, "No actual Player camera");
                yield break;
            }
            report.cameraPerspective = !camera.orthographic;
            report.cameraFieldOfView = camera.fieldOfView;
            report.cameraPitchDegrees = rift.cameraPitch;
            report.cameraDefaultYaw = rift.cameraYaw;
            Vector3 heroViewport = rift.HeroRig ?
                camera.WorldToViewportPoint(rift.HeroRig.transform.position) :
                camera.WorldToViewportPoint(
                    NativeRiftCameraProfile.FocusOnMapPlane(rift.PlayerPosition));
            report.cameraHeroInFrame = heroViewport.z > 0f &&
                heroViewport.x > .1f && heroViewport.x < .9f &&
                heroViewport.y > .1f && heroViewport.y < .9f;
            if (!report.cameraPerspective ||
                Mathf.Abs(report.cameraFieldOfView -
                          NativeRiftCameraProfile.FieldOfView) > .1f ||
                Mathf.Abs(report.cameraPitchDegrees - 56f) > .1f ||
                Mathf.Abs(report.cameraDefaultYaw) > .1f ||
                !report.cameraHeroInFrame)
            {
                FinishUiEvidence(report, evidence,
                    "Source-faithful 40FOV/56pitch perspective or hero framing missing");
                yield break;
            }

            yield return CaptureActualUiFrame(Path.Combine(evidence, UiEvidenceScreens[0]));
            report.screenshots += IsRealUiPng(
                Path.Combine(evidence, UiEvidenceScreens[0])) ? 1 : 0;

            Vector2 positionBeforeYaw = rift.PlayerPosition;
            rift.cameraYaw = -45f;
            yield return null;
            yield return CaptureActualUiFrame(Path.Combine(evidence, UiEvidenceScreens[6]));
            report.screenshots += IsRealUiPng(
                Path.Combine(evidence, UiEvidenceScreens[6])) ? 1 : 0;
            rift.cameraYaw = 45f;
            yield return null;
            yield return CaptureActualUiFrame(Path.Combine(evidence, UiEvidenceScreens[7]));
            report.screenshots += IsRealUiPng(
                Path.Combine(evidence, UiEvidenceScreens[7])) ? 1 : 0;
            rift.cameraYaw = NativeRiftCameraProfile.DefaultYaw;
            yield return null;
            report.cameraYawDidNotMovePlayer =
                Vector2.Distance(positionBeforeYaw, rift.PlayerPosition) < .01f;

            // 2. Real role -> independent Hextech -> legal hero skill flow.
            selectedHero = "Garen";
            rosterSize = 2;
            Configure(MatchRole.Hider);
            if (menu != Menu.Augment || offered == null || offered.Length != 3)
            {
                FinishUiEvidence(report, evidence, "Augment staging failed");
                yield break;
            }
            yield return CaptureActualUiFrame(Path.Combine(evidence, UiEvidenceScreens[1]));
            report.screenshots += IsRealUiPng(
                Path.Combine(evidence, UiEvidenceScreens[1])) ? 1 : 0;
            SelectAugment(offered[0].Id);
            if (menu != Menu.Skill)
            {
                FinishUiEvidence(report, evidence, "Hero skill selection staging failed");
                yield break;
            }
            yield return CaptureActualUiFrame(Path.Combine(evidence, UiEvidenceScreens[2]));
            report.screenshots += IsRealUiPng(
                Path.Combine(evidence, UiEvidenceScreens[2])) ? 1 : 0;

            SelectSkill(NativeSkillSlot.Q);
            if (menu != Menu.Playing || !privateFog || !privateFog.Running ||
                privateFog.DisplayMode != NativeFogOfWar.FogDisplay.Normal)
            {
                FinishUiEvidence(report, evidence, "Actual preparation match fog missing");
                yield break;
            }
            bool opponentInitiallyVisible = VisibleTo(HumanId, bots[0].Id);
            int capturesBefore = rules.CapturedCount;
            var normal = NativeFogOfWar.DisplayOpacity(
                NativeFogOfWar.FogDisplay.Normal);
            var light = NativeFogOfWar.DisplayOpacity(
                NativeFogOfWar.FogDisplay.Light);
            report.normalExploredAlpha = normal.x;
            report.normalUnknownAlpha = normal.y;
            report.lightExploredAlpha = light.x;
            report.lightUnknownAlpha = light.y;

            // 3. Same actual camera/match and independent per-player fog
            // history across Normal -> Light -> Off. Do not change the
            // authoritative NativeSight/NativeVisionGrid/targeting logic.
            for (int mode = 0; mode < 3; mode++)
            {
                string imagePath = Path.Combine(evidence, UiEvidenceScreens[3 + mode]);
                yield return CaptureActualUiFrame(imagePath);
                report.screenshots += IsRealUiPng(imagePath) ? 1 : 0;
                if (mode < 2) privateFog.CyclePresentation();
            }
            report.fogDoesNotAlterTargetVisibility =
                opponentInitiallyVisible == VisibleTo(HumanId, bots[0].Id) &&
                capturesBefore == rules.CapturedCount;
            privateFog.CyclePresentation(); // Off -> Normal
            report.returnedToNormal =
                privateFog.DisplayMode == NativeFogOfWar.FogDisplay.Normal;

            if (report.screenshots != UiEvidenceScreens.Length ||
                !report.cameraPerspective || !report.cameraHeroInFrame ||
                !report.cameraYawDidNotMovePlayer ||
                !report.fogDoesNotAlterTargetVisibility ||
                !report.returnedToNormal || report.screenWidth < 800 ||
                report.screenHeight < 450)
            {
                FinishUiEvidence(report, evidence,
                    "Real screenshots missing, display-mode cycle or targeting changed");
                yield break;
            }
            Debug.Log("NATIVE_UI_GPU_SCREENSHOTS_CAPTURED: " +
                      string.Join(",", UiEvidenceScreens) +
                      " modes=Normal/Light/Off privateSightUnchanged=true" +
                      " appearance=REQUIRES_HUMAN_REVIEW");
            FinishUiEvidence(report, evidence);
        }

        IEnumerator VerifyGameplay()
        {
            var result = new GameplayReport { status = "FAIL" };
            string evidence = Path.Combine(Application.persistentDataPath, "HextechGameplayQA");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-hextechEvidenceDir") evidence = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(evidence);
            float started = Time.realtimeSinceStartup;
            while (rift != null && !rift.IsReady && !rift.HasLoadFailed &&
                   Time.realtimeSinceStartup - started < 180f)
                yield return null;
            try
            {
                if (!rift || !rift.IsReady)
                    throw new InvalidOperationException("Rift unavailable " +
                                                        (rift ? rift.LoadProblem : "no bootstrap"));
                result.loadedRift = true;
                result.originalNeutralModelsExpected = 1;
                var neutralScene = rift.NeutralScene;
                result.originalNeutralModelsLoaded =
                    neutralScene ? neutralScene.OriginalArtCount : 0;
                result.originalBlueMeshVertices =
                    neutralScene ? neutralScene.OriginalBlueVertices : 0;
                result.originalBlueIdlePlaying =
                    neutralScene && neutralScene.OriginalBlueIdlePlaying;
                if (result.originalNeutralModelsLoaded != 1 ||
                    result.originalBlueMeshVertices <= 0 ||
                    !result.originalBlueIdlePlaying ||
                    !neutralScene.OriginalBlueRenderer ||
                    !neutralScene.OriginalBlueRenderer.enabled)
                    throw new InvalidOperationException(
                        "Phase18A real blue sentinel skinned mesh/idle art not available");
                Debug.Log("MATCH_ORIGINAL_NEUTRAL_MODEL_PASS: camp=Chaos Blue" +
                          " skinnedVertices=" + result.originalBlueMeshVertices +
                          " originalIdle=true sourceHashVerified=true");
                // Exercise ALL three optional original champion rigs when their
                // locally verified assets were staged in this Player. This is
                // a distinct source/mesh smoke, not an artistic fidelity claim.
                // Keep the established two-actor gameplay test unchanged below.
                rosterSize = 4;
                Configure(MatchRole.Hunter);
                foreach (var bot in bots)
                {
                    string folder = Path.Combine(Application.streamingAssetsPath,
                                                 "Champions", bot.Hero);
                    if (!GarenRigRuntime.SourceFilesPresent(folder, bot.Hero))
                    {
                        Debug.Log("MATCH_OPTIONAL_ORIGINAL_HERO_ABSENT: " + bot.Hero +
                                  " tactical marker only");
                        continue;
                    }
                    result.optionalOriginalSources++;
                    var renderer = bot.RealRig ?
                        bot.RealRig.GetComponent<SkinnedMeshRenderer>() : null;
                    if (!bot.RealRig || bot.RealRig.HeroName != bot.Hero ||
                        !bot.RealRig.HasClip("idle1") && !bot.RealRig.HasClip("idle") ||
                        !bot.RealRig.HasClip("run") || !bot.RealRig.HasClip("attack1") ||
                        !renderer || !renderer.sharedMesh ||
                        renderer.sharedMesh.vertexCount <= 0)
                        throw new InvalidOperationException(
                            "Staged original champion rig failed Unity GPU load: " + bot.Hero);
                    result.optionalOriginalRigs++;
                    Debug.Log("MATCH_OPTIONAL_ORIGINAL_HERO_READY: " + bot.Hero +
                              " vertices=" + renderer.sharedMesh.vertexCount +
                              " skillBindings=" + (bot.AnimationBindings != null));
                }
                if (result.optionalOriginalRigs != result.optionalOriginalSources)
                    throw new InvalidOperationException("Optional original hero rig count mismatch");

                // Real Windows Player acceptance for the selected human identity,
                // no duplicate hero slots, and honest marker-vs-original source.
                foreach (string candidate in NativePlayerRoster.Heroes)
                {
                    ReturnToRoleSelection();
                    selectedHero = candidate;
                    if (!rift.SelectPlayerChampion(candidate))
                        throw new InvalidOperationException("Player could not select " + candidate);
                    Configure(MatchRole.Hunter);
                    if (rules.Players.Count != 4 ||
                        rules.Find(HumanId).HeroKey != candidate ||
                        humanLoadout.Hero != candidate)
                        throw new InvalidOperationException(
                            "Human champion identity was replaced: " + candidate);
                    var seenHeroes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var participant in rules.Players)
                        if (!seenHeroes.Add(participant.HeroKey))
                            throw new InvalidOperationException(
                                "Duplicate champion in player roster: " + candidate);
                    string heroFolder = Path.Combine(Application.streamingAssetsPath,
                                                     "Champions", candidate);
                    bool sourceAvailable = candidate == "Garen" ||
                        GarenRigRuntime.SourceFilesPresent(heroFolder, candidate);
                    if (sourceAvailable != rift.PlayerHasOriginalRig ||
                        rift.PlayerHeroName != candidate)
                        throw new InvalidOperationException(
                            "Human original art/missing-art marker mismatch: " + candidate);
                    if (!sourceAvailable) result.missingHumanOriginalArt++;
                    result.selectableHeroRostersValidated++;
                    Debug.Log("MATCH_HUMAN_HERO_SELECTION_PASS: " + candidate +
                              " originalArt=" + sourceAvailable);
                }
                // Exercise nine actual human key-equivalent casts on the
                // loaded original Map11 with live rules, skill whitelists,
                // cooldown and navigation. This is not a simulated keyboard
                // event or a claim of completed original ability artwork.
                var skillAudit = new List<string>();
                VerifyHumanSkillOnRealRift("Garen", MatchRole.Hunter,
                    NativeSkillSlot.Q, skillAudit);
                VerifyHumanSkillOnRealRift("Garen", MatchRole.Hunter,
                    NativeSkillSlot.W, skillAudit);
                VerifyHumanSkillOnRealRift("Blitzcrank", MatchRole.Hunter,
                    NativeSkillSlot.Q, skillAudit);
                VerifyHumanSkillOnRealRift("Blitzcrank", MatchRole.Hider,
                    NativeSkillSlot.W, skillAudit);
                VerifyHumanSkillOnRealRift("Blitzcrank", MatchRole.Hunter,
                    NativeSkillSlot.E, skillAudit);
                VerifyHumanSkillOnRealRift("Riven", MatchRole.Hider,
                    NativeSkillSlot.Q, skillAudit);
                VerifyHumanSkillOnRealRift("Riven", MatchRole.Hider,
                    NativeSkillSlot.W, skillAudit);
                VerifyHumanSkillOnRealRift("Riven", MatchRole.Hider,
                    NativeSkillSlot.E, skillAudit);
                VerifyHumanSkillOnRealRift("Yasuo", MatchRole.Hider,
                    NativeSkillSlot.Q, skillAudit);
                VerifyHumanSkillOnRealRift("Yasuo", MatchRole.Hider,
                    NativeSkillSlot.W, skillAudit);
                result.humanSkillSmokesExpected = 10;
                result.humanSkillSmokesPassed = skillAudit.Count;
                result.noHumanSkillDirectCaptures = skillAudit.Count == 10;
                result.humanSkillEvidence = skillAudit.ToArray();
                // Phase 16 target-dependent and chained human-skill paths.
                var targetedAudit = new List<string>();
                VerifyHumanRivenComboOnRealRift(targetedAudit);
                VerifyHumanYasuoEOnRealRift(targetedAudit);
                result.targetedSkillSmokesExpected = 4;
                result.targetedSkillSmokesPassed = targetedAudit.Count;
                result.targetedSkillEvidence = targetedAudit.ToArray();
                var visionAudit = new List<string>();
                VerifyHumanYasuoEHiddenTargetsOnRealRift(visionAudit);
                result.visionSkillSmokesExpected = 1;
                result.visionSkillSmokesPassed = visionAudit.Count;
                result.visionSkillEvidence = visionAudit.ToArray();
                var neutralAudit = new List<string>();
                VerifyHumanYasuoENeutralOnRealRift(neutralAudit);
                result.neutralSkillSmokesExpected = 1;
                result.neutralSkillSmokesPassed = neutralAudit.Count;
                result.neutralSkillEvidence = neutralAudit.ToArray();

                ReturnToRoleSelection();
                selectedHero = "Garen";
                if (!rift.SelectPlayerChampion(selectedHero))
                    throw new InvalidOperationException("Cannot restore original Garen");
                // Unity defers Destroy until frame end; the removed optional
                // actors no longer participate in the authoritative rules.
                // Do not yield inside this try/catch (C# CS1626).
                rosterSize = 2;
                Configure(MatchRole.Hunter);
                SelectAugment(offered[0].Id);
                SelectSkill(NativeSkillSlot.Q);
                result.numberOfActors = rules.Players.Count;
                result.hunterLockedDuringPreparation = !rules.CanMove(HumanId) &&
                    rules.CanMove(2);
                rules.Advance(120f);
                result.hunterReleasedAfterPreparation = rules.CanMove(HumanId);
                rift.TryTeleportTo(bots[0].Position);
                bool visible = VisibleTo(HumanId, 2);
                preferredTargetId = 2;
                result.preferredTargetAcquiredOnRealRift =
                    visible && ChooseHumanBasicTarget() == 2;
                rift.PerformBasicAttack();
                result.hunterCapturedUsingBasicAttack = rules.TryCapture(HumanId, 2,
                    true, visible, Vector2.Distance(rift.PlayerPosition, bots[0].Position));
                result.caughtHiderEliminated = rules.Find(2).Eliminated &&
                                               rules.Phase == MatchPhase.Finished;
                result.eliminatedTargetRejectedOnRealRift =
                    ChooseHumanBasicTarget() == -1;
                result.hunterEarlyBonusApplied = rules.Find(HumanId).Score == 2;

                ReturnToRoleSelection();
                Configure(MatchRole.Hider);
                SelectAugment(offered[0].Id);
                SelectSkill(NativeSkillSlot.Q);
                result.hiderCanMoveDuringPreparation = rules.CanMove(HumanId) &&
                                                       !rules.CanMove(2);
                rules.Advance(120f + NativeMatchRules.ChaseSeconds(1));
                result.hiderWinsBySurviving = rules.Phase == MatchPhase.Finished &&
                    !rules.HunterWon && rules.Find(HumanId).Score == 1;
                if (!result.loadedRift || !result.hunterLockedDuringPreparation ||
                    !result.hunterReleasedAfterPreparation ||
                    !result.hunterCapturedUsingBasicAttack ||
                    !result.preferredTargetAcquiredOnRealRift ||
                    !result.eliminatedTargetRejectedOnRealRift ||
                    !result.caughtHiderEliminated || !result.hunterEarlyBonusApplied ||
                    !result.hiderCanMoveDuringPreparation || !result.hiderWinsBySurviving ||
                    result.numberOfActors != 2 ||
                    result.selectableHeroRostersValidated != 4 ||
                    result.humanSkillSmokesExpected != 10 ||
                    result.humanSkillSmokesPassed != 10 ||
                    !result.noHumanSkillDirectCaptures ||
                    result.targetedSkillSmokesExpected != 4 ||
                    result.targetedSkillSmokesPassed != 4 ||
                    result.visionSkillSmokesExpected != 1 ||
                    result.visionSkillSmokesPassed != 1 ||
                    result.neutralSkillSmokesExpected != 1 ||
                    result.neutralSkillSmokesPassed != 1 ||
                    result.originalNeutralModelsExpected != 1 ||
                    result.originalNeutralModelsLoaded != 1 ||
                    !result.originalBlueIdlePlaying ||
                    result.originalBlueMeshVertices <= 0)
                    throw new InvalidOperationException("Gameplay smoke assertions failed");
                result.status = "PASS";
            }
            catch (Exception ex)
            {
                result.status = "FAIL";
                result.reason = ex.ToString();
                Debug.LogException(ex);
            }
            // The model may be intact yet invisible; obtain an actual D3D11
            // screenshot with the camera over the authenticated Chaos Blue camp.
            // This happens outside the test try/catch so C# coroutine yields
            // remain legal (CS1626); failed evidence changes PASS to FAIL.
            if (result.status == "PASS")
            {
                NativeNeutralMonster blue = null;
                foreach (var monster in rift.NeutralMonsters.Monsters)
                    if (monster.CampName == "Chaos Blue") { blue = monster; break; }
                if (blue == null || !rift.TryTeleportTo(blue.WalkableAnchor))
                {
                    result.status = "FAIL";
                    result.reason = "Phase18A original neutral screenshot staging failed";
                }
                else
                {
                    rift.CameraFollowOverride = blue.WalkableAnchor;
                    yield return new WaitForEndOfFrame();
                    var mainCamera = Camera.main;
                    var model = rift.NeutralScene.OriginalBlueRenderer;
                    if (!mainCamera || !model ||
                        !GeometryUtility.TestPlanesAABB(
                            GeometryUtility.CalculateFrustumPlanes(mainCamera),
                            model.bounds))
                    {
                        result.status = "FAIL";
                        result.reason = "Phase18A skinned blue sentinel not inside GPU camera";
                    }
                    else
                    {
                        string screenshot = Path.Combine(
                            evidence, "original-neutral-blue-gpu.png");
                        if (File.Exists(screenshot)) File.Delete(screenshot);
                        ScreenCapture.CaptureScreenshot(screenshot);
                        for (int frame = 0; frame < 180; frame++)
                        {
                            if (File.Exists(screenshot) &&
                                new FileInfo(screenshot).Length > 10240)
                                break;
                            yield return null;
                        }
                        if (!File.Exists(screenshot) ||
                            new FileInfo(screenshot).Length <= 10240)
                        {
                            result.status = "FAIL";
                            result.reason = "Phase18A real GPU blue screenshot not written";
                        }
                        else
                        {
                            result.originalBlueVisualSnapshotCaptured = true;
                            Debug.Log("MATCH_ORIGINAL_NEUTRAL_VISUAL_GPU_PASS: " +
                                      "camp=Chaos Blue model=original-skin idle=playing" +
                                      " screenshot=original-neutral-blue-gpu.png" +
                                      " bytes=" + new FileInfo(screenshot).Length);
                        }
                    }
                }
                rift.CameraFollowOverride = null;
            }
            File.WriteAllText(Path.Combine(evidence, "gameplay-result.json"),
                              JsonUtility.ToJson(result, true));
            Debug.Log("MATCH_QA_RESULT: " + result.status);
            if (!Application.isEditor) Application.Quit(result.status == "PASS" ? 0 : 1);
        }

        void OnDestroy()
        {
            if (privateFog) privateFog.End();
            if (combatVfx) combatVfx.End();
            humanPull.Cancel();
            CleanupBots();
        }
    }
}
