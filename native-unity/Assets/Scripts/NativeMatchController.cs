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
        // Original fountain bounds still require geometric calibration.
        static readonly Vector2 HunterStaging = new Vector2(13000f, -1100f);
        Vector2 HunterSpawn => rift && rift.SpawnCalibration != null
            ? rift.SpawnCalibration.HunterSpawn : HunterStaging;
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
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechGameplayQA") >= 0)
                StartCoroutine(VerifyGameplay());
        }

        void Configure(MatchRole role)
        {
            if (!rift || !rift.IsReady) return;
            CleanupBots();
            if (privateFog) privateFog.End();
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
            menu = Menu.Playing;
            string startMessage = rift.SpawnCalibration != null
                ? "120s preparation: verified fountain; hunter cannot leave." :
                  "120s preparation: hunter locked at provisional staging (fountain unverified).";
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

        bool HoveringHud(Vector2 mouse) =>
            mouse.x < 545 && mouse.y > Screen.height -
            (rules != null && rules.Phase == MatchPhase.Finished ? 475 : 325);

        void Update()
        {
            if (!rift || !rift.IsReady || rules == null || menu != Menu.Playing)
                return;
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
            NativeSkillTarget chosen = default;
            bool found = false;
            float nearest = float.PositiveInfinity;
            foreach (var target in GetSkillTargets(HumanId))
            {
                if (target.Eliminated || !target.VisibleToCaster) continue;
                float d = (target.Position - origin).sqrMagnitude;
                if (d < nearest)
                {
                    chosen = target;
                    nearest = d;
                    found = true;
                }
            }
            Vector2 destination;
            if (found && humanYasuo.TryEDashThroughHero(
                    humanLoadout, rules, hextech, HumanId, origin, chosen,
                    rift.Navigation, out destination))
            {
                if (!humanDash.Begin(rift.Navigation, origin, destination))
                    return false;
                rift.StopMovement();
                humanContact.Cancel();
                PlayHumanOriginalSkill(NativeSkillSlot.E);
                combatVfx?.Pulse(HumanId, origin, "Yasuo E Hero");
                feedback = "Yasuo E through visible enemy; no capture";
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

        void TryHumanSkill(NativeSkillSlot slot)
        {
            if (humanLoadout == null || !humanLoadout.Knows(slot) ||
                !rules.CanMove(HumanId) || !hextech.CanUseHeroSkill(HumanId) ||
                heroControl.Locked(HumanId) || humanDash.Active ||
                humanPull.Active)
            {
                feedback = "Skill unavailable: unlearned, silenced, stunned or moving";
                return;
            }
            if (selectedHero == "Garen")
            {
                TryGarenSkill(slot);
                return;
            }
            Vector2 origin = rift.PlayerPosition;
            Vector2 direction = HumanAimDirection();
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

        void TryGarenSkill(NativeSkillSlot slot)
        {
            if (garenSkills.TryCast(slot, humanLoadout, rules, hextech, HumanId, false))
            {
                PlayHumanOriginalSkill(slot);
                combatVfx?.Pulse(HumanId, rift.PlayerPosition, "Garen " + slot);
                feedback = slot == NativeSkillSlot.Q ?
                    "Garen Q: slow cleanse + brief movement speed (tuning pending)" :
                    "Garen W: tenacity window; ordinary shield does NOT block capture";
                Debug.Log("MATCH_GAREN_SKILL: " + slot + " (non-damaging adapted mechanics)");
            }
            else feedback = "Skill unavailable: check chosen skills, cooldown, stun or silence";
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
                bot.Position = bot.Pull.Step(dt);
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
                bot.Position = bot.Dash.Step(dt);
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
                        bot.Position = goal;
                        allowance -= length;
                        bot.Waypoint++;
                    }
                    else
                    {
                        Vector2 next = bot.Position + difference.normalized * allowance;
                        if (rift.Navigation.CanWalk(next)) bot.Position = next;
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
            rift.CameraFollowOverride = null;
            rift.TryTeleportTo(HiderSpawn);
        }

        void OnGUI()
        {
            if (!rift || !rift.IsReady) return;
            if (menu == Menu.Augment)
            {
                if (NativeHextechDraftUI.Draw(offered, out var selected))
                    SelectAugment(selected);
                return;
            }
            float width = 525;
            bool tall = menu == Menu.Augment || menu == Menu.Playing;
            float boxHeight = menu == Menu.Role ? 340f :
                rules != null && rules.Phase == MatchPhase.Finished
                ? 330f + rosterSize * 24f : (tall ? 310f : 218f);
            GUI.Box(new Rect(12, 12, width, boxHeight), "");
            GUI.Label(new Rect(25, 19, 495, 26), "HEXTECH SEEKERS | NATIVE 3D | GAMEPLAY ALPHA");
            if (menu == Menu.Role)
            {
                GUI.Label(new Rect(25, 50, 475, 26),
                    "Select your champion (no duplicate AI hero)");
                for (int i = 0; i < NativePlayerRoster.Heroes.Length; i++)
                {
                    string choice = NativePlayerRoster.Heroes[i];
                    if (GUI.Button(new Rect(25 + i * 121f, 80, 112, 38),
                                   (choice == selectedHero ? "* " : "") + choice))
                    {
                        if (rift.SelectPlayerChampion(choice))
                        {
                            selectedHero = choice;
                            feedback = rift.PlayerHasOriginalRig ?
                                "Original " + choice + " rig loaded" :
                                choice + ": tactical marker, original model not supplied";
                        }
                    }
                }
                GUI.Label(new Rect(25, 130, 475, 24),
                    rift.PlayerHasOriginalRig ? "Original rig available: " + selectedHero :
                    "ART NOT INSTALLED: " + selectedHero + " tactical marker");
                GUI.Label(new Rect(25, 164, 460, 25),
                    "Participants: " + rosterSize + " (one hunter, remaining hiders)");
                if (GUI.Button(new Rect(295, 161, 45, 30), "-"))
                    rosterSize = Mathf.Max(2, rosterSize - 1);
                if (GUI.Button(new Rect(350, 161, 45, 30), "+"))
                    rosterSize = Mathf.Min(4, rosterSize + 1);
                if (GUI.Button(new Rect(25, 213, 223, 44), "Play as HUNTER"))
                    Configure(MatchRole.Hunter);
                if (GUI.Button(new Rect(260, 213, 225, 44), "Play as HIDER"))
                    Configure(MatchRole.Hider);
                GUI.Label(new Rect(25, 269, 475, 22),
                    "No source assets? Skills still run using a labeled tactical ring.");
                GUI.Label(new Rect(25, 296, 475, 22), feedback);
            }
            else if (menu == Menu.Skill)
            {
                GUI.Label(new Rect(25, 52, 478, 30),
                    selectedHero + ": choose ONE legal skill to begin.");
                foreach (NativeSkillSlot slot in new[] {
                             NativeSkillSlot.Q, NativeSkillSlot.W, NativeSkillSlot.E })
                {
                    if (!NativeSkillCatalog.Allowed(selectedHero, humanRole, slot))
                        continue;
                    int index = (int)slot;
                    if (GUI.Button(new Rect(25 + index * 155f, 94, 148, 47),
                                   slot + " | " + selectedHero))
                        SelectSkill(slot);
                }
                GUI.Label(new Rect(25, 156, 470, 25),
                    "Garen E is damage-only and not selectable.");
                GUI.Label(new Rect(25, 185, 475, 22),
                    "No normal summoner spells, runes or damage-only abilities.");
            }
            else if (rules != null)
            {
                string phase = rules.Phase == MatchPhase.Preparation ? "PREPARATION" :
                               rules.Phase == MatchPhase.Pursuit ? "PURSUIT" : "FINISHED";
                GUI.Label(new Rect(25, 47, 482, 24),
                    selectedHero + " | " + humanRole + " | " + phase + " | " +
                    Mathf.CeilToInt(rules.RemainingSeconds) + "s | " + rosterSize + " actors");
                GUI.Label(new Rect(25, 76, 480, 22),
                    "Captured " + rules.CapturedCount + "/" + rules.HiderCount +
                    " | Your score " + rules.Find(HumanId).Score +
                    (rules.Find(HumanId).Eliminated ?
                      " | SPECTATING " + (SpectatedAlly() >= 0 ?
                            rules.Find(SpectatedAlly()).HeroKey : "none") : ""));
                var selected = hextech.Selected(HumanId);
                GUI.Label(new Rect(25, 101, 480, 25),
                    "Hextech: " + (selected.HasValue ? NativeHextechPool.Find(selected.Value).Name : "-") +
                    " | Cooldown " + Mathf.CeilToInt(hextech.RemainingCooldown(HumanId)) + "s");
                GUI.Label(new Rect(25, 127, 485, 23),
                    "Target: " + SelectedTargetStatus() + " | A basic | Tab | Q/W/E | F");
                if (selected == NativeHextech.TimeClockSilver &&
                    rules.Phase == MatchPhase.Pursuit)
                {
                    if (GUI.Button(new Rect(25, 158, 213, 34), "Clock: -1 min"))
                        TryUseHextech(-1);
                    if (GUI.Button(new Rect(251, 158, 220, 34), "Clock: +1 min"))
                        TryUseHextech(1);
                }
                if (humanLoadout != null && humanLoadout.Level == 2 &&
                    !humanLoadout.Secondary.HasValue &&
                    rules.Phase == MatchPhase.Preparation)
                {
                    int optionIndex = 0;
                    foreach (NativeSkillSlot slot in new[] {
                                 NativeSkillSlot.Q, NativeSkillSlot.W, NativeSkillSlot.E })
                    {
                        if (slot == humanLoadout.Primary ||
                            !NativeSkillCatalog.Allowed(selectedHero, humanRole, slot))
                            continue;
                        if (GUI.Button(new Rect(25 + 240 * optionIndex, 195, 224, 32),
                                       "Level 2: learn " + slot))
                            humanLoadout.Choose(slot);
                        optionIndex++;
                    }
                }
                if (rules.Phase == MatchPhase.Finished)
                {
                    GUI.Label(new Rect(25, 172, 475, 25),
                        rules.HunterWon ? "Hunter wins" : "Hiders survived");
                    if (GUI.Button(new Rect(275, 196, 215, 33), "Return to role select"))
                        ReturnToRoleSelection();
                }
                GUI.Label(new Rect(25, 238, 490, 25), feedback);
                GUI.Label(new Rect(25, 263, 490, 25),
                    rules.Find(HumanId).Eliminated && SpectatedAlly() >= 0 ?
                    "Spectating: Tab cycles allies; fog follows ONLY the chosen ally" :
                    (privateFog && privateFog.Running ?
                     "Fog: private 3D terrain vision + explored darkness" :
                     "Fog unavailable: no full-map sight protection"));
                if (humanLoadout != null)
                {
                    string cooldowns = selectedHero == "Garen"
                        ? "Q " + Mathf.CeilToInt(garenSkills.CooldownQ) +
                          "s  W " + Mathf.CeilToInt(garenSkills.CooldownW) + "s"
                        : selectedHero == "Yasuo"
                        ? "Q " + Mathf.CeilToInt(humanYasuo.CooldownQ) +
                          "s  W " + Mathf.CeilToInt(humanCombat.CooldownW) +
                          "s  E " + Mathf.CeilToInt(humanYasuo.CooldownE) + "s"
                        : "Q " + Mathf.CeilToInt(humanCombat.CooldownQ) +
                          "s  W " + Mathf.CeilToInt(humanCombat.CooldownW) +
                          "s  E " + Mathf.CeilToInt(humanCombat.CooldownE) + "s";
                    GUI.Label(new Rect(25, 286, 490, 20),
                              selectedHero + " cooldowns: " + cooldowns);
                }
                if (rules.Phase == MatchPhase.Finished)
                {
                    int row = 0;
                    foreach (var participant in rules.Players)
                    {
                        GUI.Label(new Rect(25, 305 + row * 23, 480, 22),
                            participant.HeroKey + " / " + participant.Role +
                            " / score " + participant.Score +
                            (participant.Eliminated ? " / eliminated" : " / alive"));
                        row++;
                    }
                }
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
                    result.selectableHeroRostersValidated != 4)
                    throw new InvalidOperationException("Gameplay smoke assertions failed");
                result.status = "PASS";
            }
            catch (Exception ex)
            {
                result.status = "FAIL";
                result.reason = ex.ToString();
                Debug.LogException(ex);
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
