using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Native 3D hide-and-seek alpha. One original skinned Garen plus 1-3 AI
    /// tactical markers using distinct hero SLOTS. A marker is never presented
    /// as the actual unimported Blitzcrank/Riven/Yasuo character artwork.
    /// Selection -> three Hextech cards -> Garen skill -> preparation -> pursuit.
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
        static readonly string[] BotHeroSlots = { "Blitzcrank", "Riven", "Yasuo" };

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
            public NativeHeroLoadout Loadout;
        }

        readonly List<Bot> bots = new List<Bot>();
        readonly System.Random random = new System.Random(1439);
        RiftBootstrap rift;
        NativeMatchRules rules;
        NativeHextechRuntime hextech;
        NativeFogOfWar privateFog;
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
            var participants = new MatchParticipant[rosterSize];
            participants[0] = new MatchParticipant(HumanId, "Garen", role, true);
            for (int i = 1; i < rosterSize; i++)
            {
                var botRole = role == MatchRole.Hunter ? MatchRole.Hider :
                    (i == 1 ? MatchRole.Hunter : MatchRole.Hider);
                participants[i] = new MatchParticipant(i + 1, BotHeroSlots[i - 1], botRole, false);
            }
            rules = new NativeMatchRules(participants);
            hextech = new NativeHextechRuntime(rules);
            humanLoadout = new NativeHeroLoadout("Garen", role);
            garenSkills = new NativeGarenSkills();
            offered = NativeHextechPool.Deal(role, random);
            preferredTargetId = -1;
            basicCooldown = 0;
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
                bot.Loadout.Choose(NativeSkillSlot.Q);
                var offeredBot = NativeHextechPool.Deal(p.Role, random);
                // Each AI rolls and selects independently; duplicate choices
                // across different characters are intentionally legal.
                var selected = offeredBot[random.Next(offeredBot.Length)].Id;
                hextech.Select(p.Id, selected);
                Vector2 basePoint = p.Role == MatchRole.Hunter ?
                    HunterStaging : HiderStart + new Vector2(i * 345f, i * 175f);
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
            feedback = "Choose Garen Q or W as your starting skill.";
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
                HunterStaging : HiderStart);
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
            hextech.ApplyPreparationPassives(HumanId, humanLoadout);
            foreach (var bot in bots) hextech.ApplyPreparationPassives(bot.Id, bot.Loadout);
            rift.ExternalMoveEnabled = rules.CanMove(HumanId);
            menu = Menu.Playing;
            string startMessage = "120-second preparation started. Hunter cannot leave staging.";
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
                rules.Advance(dt);
            }

            // A caught hider may observe surviving allied hiders, never
            // arbitrary hidden enemy positions. No shared vision while alive.
            int spectatedId = SpectatedAlly();
            rift.CameraFollowOverride = spectatedId >= 0 ?
                (Vector2?)GetPosition(spectatedId) : null;
            rift.ExternalMoveEnabled = rules.CanMove(HumanId) &&
                !hextech.Stunned(HumanId);
            rift.ExternalMoveMultiplier =
                (humanRole == MatchRole.Hunter ? HunterSpeed / HiderSpeed : 1f) *
                hextech.MoveMultiplier(HumanId) * garenSkills.MovementMultiplier;
            if (!rift.ExternalMoveEnabled) rift.StopMovement();
            if (rules.Phase == MatchPhase.Finished)
            {
                rift.StopMovement();
                if (!loggedFinish)
                {
                    loggedFinish = true;
                    feedback = rules.HunterWon ? "Hunter captured every hider" : "Hiders survived";
                    Debug.Log("MATCH_FINISHED: winner=" +
                              (rules.HunterWon ? "Hunter" : "Hiders") +
                              " humanScore=" + rules.Find(HumanId).Score);
                }
                UpdateMarkers();
                UpdatePrivateFog();
                return;
            }

            if (rift.ExternalMoveEnabled)
            {
                if (Input.GetKeyDown(KeyCode.S)) rift.StopMovement();
                if (Input.GetMouseButtonDown(1) && !HoveringHud(Input.mousePosition))
                    rift.TryNavigateFromScreen(Input.mousePosition);
                if (Input.GetKeyDown(KeyCode.Q)) TryGarenSkill(NativeSkillSlot.Q);
                if (Input.GetKeyDown(KeyCode.W)) TryGarenSkill(NativeSkillSlot.W);
            }
            basicCooldown = Mathf.Max(0f, basicCooldown - dt);
            if (!rules.Find(HumanId).Eliminated && Input.GetKeyDown(KeyCode.Tab))
                CycleVisibleTarget();
            if (!rules.Find(HumanId).Eliminated && Input.GetKeyDown(KeyCode.F))
                TryUseHextech(0);
            if (Input.GetKeyDown(KeyCode.A) && rules.CanAttack(HumanId) &&
                !hextech.Stunned(HumanId) && basicCooldown <= 0f)
            {
                basicCooldown = HumanBasicCooldown;
                rift.PerformBasicAttack();
                var target = NearestVisibleEnemy(HumanId);
                bool capture = target != null && hextech.TryBasicAttackCapture(
                    HumanId, target.Id, true, true,
                    Vector2.Distance(rift.PlayerPosition, target.Position));
                feedback = capture ? "Basic attack captured " + target.Hero :
                    "Basic attack did not capture (range, immunity or visibility).";
                Debug.Log("MATCH_BASIC_ATTACK: captured=" + capture);
            }

            foreach (var bot in bots) UpdateBot(bot, dt);
            UpdateMarkers();
            UpdatePrivateFog();
        }

        void UpdatePrivateFog()
        {
            if (!privateFog || !privateFog.Running || rules == null) return;
            int viewerId = rules.Find(HumanId).Eliminated ? SpectatedAlly() : HumanId;
            privateFog.UpdateView(rules, viewerId, GetPosition);
        }

        void TryGarenSkill(NativeSkillSlot slot)
        {
            if (garenSkills.TryCast(slot, humanLoadout, rules, hextech, HumanId, false))
            {
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

        Bot NearestVisibleEnemy(int observer)
        {
            Bot found = null;
            float nearest = float.MaxValue;
            foreach (var bot in bots)
            {
                var p = rules.Find(bot.Id);
                if (p.Eliminated || p.Role == rules.Find(observer).Role ||
                    !VisibleTo(observer, bot.Id)) continue;
                float distance = Vector2.Distance(GetPosition(observer), bot.Position);
                if (distance < nearest) { nearest = distance; found = bot; }
            }
            return found;
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

        void UpdateBot(Bot bot, float dt)
        {
            var person = rules.Find(bot.Id);
            if (person.Eliminated || rules.Phase == MatchPhase.Finished) return;
            if (!rules.CanMove(bot.Id) || hextech.Stunned(bot.Id))
            {
                bot.Path.Clear();
                bot.Waypoint = 0;
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

                // AI can use control augments on enemies within its own
                // perception. Its hextech selection/cooldown is independent.
                if (enemyId >= 0 && hextech.CanUseActiveHextech(bot.Id))
                {
                    var skill = hextech.Selected(bot.Id);
                    if (skill == NativeHextech.FreezeGold ||
                        skill == NativeHextech.AstralSilenceSilver)
                    {
                        float tenacity = enemyId == HumanId ?
                            garenSkills.CrowdControlDurationFactor : 1f;
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

            if (bot.Waypoint < bot.Path.Count)
            {
                float allowance = (bot.Role == MatchRole.Hunter ? HunterSpeed : HiderSpeed)
                    * hextech.MoveMultiplier(bot.Id) * dt;
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

            bot.AttackCooldown = Mathf.Max(0f, bot.AttackCooldown - dt);
            if (bot.Role == MatchRole.Hunter && rules.CanAttack(bot.Id) &&
                bot.AttackCooldown <= 0)
            {
                int targetId = GetTargetForActor(bot.Id);
                if (targetId >= 0)
                {
                    float distance = Vector2.Distance(bot.Position, GetPosition(targetId));
                    if (distance <= NativeMatchRules.BasicAttackReach)
                    {
                        bot.AttackCooldown = AiBasicCooldown;
                        bool captured = hextech.TryBasicAttackCapture(bot.Id,
                            targetId, true, VisibleTo(bot.Id, targetId), distance);
                        if (captured)
                            Debug.Log("MATCH_AI_CAPTURE: " + bot.Hero +
                                      " caught " + rules.Find(targetId).HeroKey);
                    }
                }
            }
        }

        void CreateMarker(Bot bot)
        {
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
            foreach (var bot in bots)
                if (bot.Role == humanRole && !rules.Find(bot.Id).Eliminated)
                    return bot.Id;
            return -1;
        }

        void UpdateMarkers()
        {
            if (rules == null) return;
            bool humanEliminated = rules.Find(HumanId).Eliminated;
            int observerId = humanEliminated ? SpectatedAlly() : HumanId;
            // With no surviving ally there is no legal active vision source.
            // Never leak enemies through the eliminated player's last position.
            foreach (var bot in bots)
                if (bot.Ring)
                    bot.Ring.enabled = observerId >= 0 &&
                        !rules.Find(bot.Id).Eliminated &&
                        bot.Id != observerId &&
                        VisibleTo(observerId, bot.Id);
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
            CleanupBots();
            menu = Menu.Role;
            rules = null;
            hextech = null;
            offered = null;
            rift.StopMovement();
            rift.ExternalGameplayControl = false;
            rift.ExternalMoveEnabled = true;
            rift.ExternalMoveMultiplier = 1f;
            rift.CameraFollowOverride = null;
            rift.TryTeleportTo(HiderStart);
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
            float boxHeight = rules != null && rules.Phase == MatchPhase.Finished
                ? 330f + rosterSize * 24f : (tall ? 310f : 218f);
            GUI.Box(new Rect(12, 12, width, boxHeight), "");
            GUI.Label(new Rect(25, 19, 495, 26), "HEXTECH SEEKERS | NATIVE 3D | GAMEPLAY ALPHA");
            if (menu == Menu.Role)
            {
                GUI.Label(new Rect(25, 52, 470, 25),
                    "Player: original Garen | AI slots: unimported tactical markers");
                GUI.Label(new Rect(25, 83, 460, 25), "Total participants: " + rosterSize +
                    " (one hunter, remainder hiders)");
                if (GUI.Button(new Rect(295, 78, 45, 30), "-"))
                    rosterSize = Mathf.Max(2, rosterSize - 1);
                if (GUI.Button(new Rect(350, 78, 45, 30), "+"))
                    rosterSize = Mathf.Min(4, rosterSize + 1);
                if (GUI.Button(new Rect(25, 128, 223, 44), "Play as HUNTER"))
                    Configure(MatchRole.Hunter);
                if (GUI.Button(new Rect(260, 128, 225, 44), "Play as HIDER"))
                    Configure(MatchRole.Hider);
            }
            else if (menu == Menu.Skill)
            {
                GUI.Label(new Rect(25, 52, 478, 30),
                    "Garen: choose your first legal Q/W. E is pure damage.");
                if (GUI.Button(new Rect(25, 94, 215, 47), "Q | movement / cleanse"))
                    SelectSkill(NativeSkillSlot.Q);
                if (GUI.Button(new Rect(252, 94, 230, 47), "W | tenacity"))
                    SelectSkill(NativeSkillSlot.W);
                GUI.Label(new Rect(25, 156, 470, 25),
                    "No normal summoner spells, runes or damage-only abilities.");
            }
            else if (rules != null)
            {
                string phase = rules.Phase == MatchPhase.Preparation ? "PREPARATION" :
                               rules.Phase == MatchPhase.Pursuit ? "PURSUIT" : "FINISHED";
                GUI.Label(new Rect(25, 47, 482, 24),
                    "Role " + humanRole + " | " + phase + " | " +
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
                    "RMB move | S stop | A basic | Q/W chosen skills | Tab target | F hextech");
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
                    var secondary = humanLoadout.Primary == NativeSkillSlot.Q ?
                        NativeSkillSlot.W : NativeSkillSlot.Q;
                    if (GUI.Button(new Rect(25, 195, 250, 32), "Level 2: learn " + secondary))
                        humanLoadout.Choose(secondary);
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
                    privateFog && privateFog.Running ?
                    "Fog: private 3D terrain vision + explored darkness" :
                    "Fog unavailable: no full-map sight protection");
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
            public bool caughtHiderEliminated;
            public bool hunterEarlyBonusApplied;
            public bool hiderCanMoveDuringPreparation;
            public bool hiderWinsBySurviving;
            public int numberOfActors;
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
                rift.PerformBasicAttack();
                result.hunterCapturedUsingBasicAttack = rules.TryCapture(HumanId, 2,
                    true, visible, Vector2.Distance(rift.PlayerPosition, bots[0].Position));
                result.caughtHiderEliminated = rules.Find(2).Eliminated &&
                                               rules.Phase == MatchPhase.Finished;
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
                    !result.caughtHiderEliminated || !result.hunterEarlyBonusApplied ||
                    !result.hiderCanMoveDuringPreparation || !result.hiderWinsBySurviving ||
                    result.numberOfActors != 2)
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
            CleanupBots();
        }
    }
}
