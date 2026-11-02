using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Playable Phase 4 vertical slice: 1 human Garen vs. one tactical AI marker
    /// on the original 3D Rift. The AI marker is NOT a completed champion model;
    /// all four distinct original champion assets/skills remain later milestones.
    ///
    /// Right mouse: move via the same validated Rift A*; S: stop; A: basic attack
    /// when hunter. During preparation, hunter cannot leave the staging point.
    /// </summary>
    public sealed class NativeMatchController : MonoBehaviour
    {
        const int HumanId = 1;
        const int AiId = 2;
        const float AiDecisionInterval = .65f;
        const float HunterMoveSpeed = 365f; // modest speed advantage, subject to tuning
        const float HiderMoveSpeed = 340f;
        const float AiAttackCooldown = 1.1f;
        const float HumanAttackCooldown = .8f;

        static readonly Vector2 HiderStart = new Vector2(7765f, -4020f);
        // Interim valid staging zone. Verified fountain world coordinates are
        // not yet established on this lower-jungle navigation prototype.
        static readonly Vector2 HunterStaging = new Vector2(13000f, -1100f);

        readonly List<Vector2> aiRoute = new List<Vector2>();
        readonly List<Vector2> candidatePath = new List<Vector2>();
        readonly System.Random random = new System.Random(1439);

        RiftBootstrap rift;
        NativeMatchRules rules;
        MatchRole humanRole, aiRole;
        GameObject aiIndicator;
        LineRenderer aiRing;
        Vector2 aiPosition;
        Vector2 aiDestination;
        int aiWaypoint;
        float decisionCooldown;
        float aiAttackCooldown;
        float humanAttackCooldown;
        bool resultLogged;

        void Awake()
        {
            // Preserve the already accepted Player GPU test: the acceptance
            // flag must never launch the match GUI, AI, or change QA camera.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechVisualQA") >= 0)
            {
                enabled = false;
                return;
            }
            rift = GetComponent<RiftBootstrap>();
            if (!rift) { Debug.LogError("MATCH: RiftBootstrap is required"); enabled = false; }
        }

        [Serializable]
        sealed class GameplayQaReport
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

        void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hextechGameplayQA") >= 0)
            {
                Debug.Log("MATCH_QA_START: native gameplay integration smoke");
                StartCoroutine(VerifyGameplay());
            }
        }

        IEnumerator VerifyGameplay()
        {
            var result = new GameplayQaReport { status = "FAIL" };
            string output = Path.Combine(Application.persistentDataPath, "HextechGameplayQA");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-hextechEvidenceDir") output = Path.GetFullPath(args[i + 1]);
            Directory.CreateDirectory(output);
            float started = Time.realtimeSinceStartup;
            while (rift != null && !rift.IsReady && !rift.HasLoadFailed &&
                   Time.realtimeSinceStartup - started < 180f)
                yield return null;
            try
            {
                if (!rift || !rift.IsReady)
                    throw new InvalidOperationException("Rift failed to load: " + (rift ? rift.LoadProblem : "no bootstrap"));
                result.loadedRift = true;

                BeginMatch(MatchRole.Hunter);
                if (rules == null) throw new InvalidOperationException("Hunter round did not start");
                result.numberOfActors = rules.Players.Count;
                result.hunterLockedDuringPreparation = !rules.CanMove(HumanId) &&
                    rules.CanMove(AiId);
                rules.Advance(120f);
                result.hunterReleasedAfterPreparation = rules.CanMove(HumanId);
                if (!rift.TryTeleportTo(aiPosition))
                    throw new InvalidOperationException("Cannot position human on AI's navigable cell");
                bool visible = NativeSight.Visible(rift.Navigation, rift.PlayerPosition, aiPosition);
                rift.PerformBasicAttack();
                result.hunterCapturedUsingBasicAttack = rules.TryCapture(HumanId, AiId,
                    true, visible, Vector2.Distance(rift.PlayerPosition, aiPosition));
                result.caughtHiderEliminated = rules.Find(AiId).Eliminated &&
                    rules.Phase == MatchPhase.Finished;
                result.hunterEarlyBonusApplied = rules.Find(HumanId).Score == 2;
                ReturnToMenu();

                BeginMatch(MatchRole.Hider);
                if (rules == null) throw new InvalidOperationException("Hider round did not start");
                result.hiderCanMoveDuringPreparation = rules.CanMove(HumanId) &&
                    !rules.CanMove(AiId);
                rules.Advance(120f + NativeMatchRules.ChaseSeconds(1));
                result.hiderWinsBySurviving = rules.Phase == MatchPhase.Finished &&
                    !rules.HunterWon && rules.Find(HumanId).Score == 1;
                if (!result.loadedRift || !result.hunterLockedDuringPreparation ||
                    !result.hunterReleasedAfterPreparation ||
                    !result.hunterCapturedUsingBasicAttack ||
                    !result.caughtHiderEliminated || !result.hunterEarlyBonusApplied ||
                    !result.hiderCanMoveDuringPreparation || !result.hiderWinsBySurviving ||
                    result.numberOfActors != 2)
                    throw new InvalidOperationException("Native 3D gameplay smoke assertions failed");
                result.status = "PASS";
            }
            catch (Exception ex)
            {
                result.status = "FAIL";
                result.reason = ex.ToString();
                Debug.LogException(ex);
            }
            File.WriteAllText(Path.Combine(output, "gameplay-result.json"), JsonUtility.ToJson(result, true));
            Debug.Log("MATCH_QA_RESULT: " + result.status + " at " + output);
            if (!Application.isEditor) Application.Quit(result.status == "PASS" ? 0 : 1);
        }

        void Update()
        {
            if (!rift || !rift.IsReady) return;
            if (rules == null) return;

            if (rules.Phase != MatchPhase.Finished)
                rules.Advance(Time.deltaTime);

            rift.ExternalMoveEnabled = rules.CanMove(HumanId);
            rift.ExternalMoveMultiplier = humanRole == MatchRole.Hunter ?
                HunterMoveSpeed / HiderMoveSpeed : 1f;
            if (!rift.ExternalMoveEnabled) rift.StopMovement();

            if (rules.Phase == MatchPhase.Finished)
            {
                rift.StopMovement();
                if (!resultLogged)
                {
                    resultLogged = true;
                    Debug.Log("MATCH_FINISHED: winner=" + (rules.HunterWon ? "Hunter" : "Hiders") +
                              " humanScore=" + rules.Find(HumanId).Score +
                              " aiScore=" + rules.Find(AiId).Score);
                }
                RenderAiVisibility();
                return;
            }

            if (rift.ExternalMoveEnabled)
            {
                if (Input.GetKeyDown(KeyCode.S)) rift.StopMovement();
                if (Input.GetMouseButtonDown(1) && !OverHud(Input.mousePosition))
                    rift.TryNavigateFromScreen(Input.mousePosition);
            }

            humanAttackCooldown = Mathf.Max(0, humanAttackCooldown - Time.deltaTime);
            aiAttackCooldown = Mathf.Max(0, aiAttackCooldown - Time.deltaTime);
            if (Input.GetKeyDown(KeyCode.A) && rules.CanAttack(HumanId) && humanAttackCooldown <= 0)
            {
                humanAttackCooldown = HumanAttackCooldown;
                rift.PerformBasicAttack();
                bool visible = NativeSight.Visible(rift.Navigation, rift.PlayerPosition, aiPosition);
                bool caught = rules.TryCapture(HumanId, AiId, true, visible,
                    Vector2.Distance(rift.PlayerPosition, aiPosition));
                Debug.Log("MATCH_BASIC_ATTACK: role=human hunter; hit=" + caught +
                          " visible=" + visible);
            }

            if (rules.Phase != MatchPhase.Finished && !rules.Find(AiId).Eliminated)
                UpdateAi(Time.deltaTime);

            RenderAiVisibility();
        }

        static bool OverHud(Vector2 mouse)
        {
            // IMGUI coordinates are top-left, Unity input coordinates bottom-left.
            return mouse.x <= 475 && mouse.y >= Screen.height - 235;
        }

        void CreateTrainingMarker()
        {
            if (aiIndicator) Destroy(aiIndicator);
            aiIndicator = new GameObject("AI_TACTICAL_MARKER_NOT_CHAMPION");
            aiRing = aiIndicator.AddComponent<LineRenderer>();
            aiRing.useWorldSpace = false;
            aiRing.loop = true;
            aiRing.positionCount = 32;
            aiRing.startWidth = 12f;
            aiRing.endWidth = 12f;
            aiRing.numCapVertices = 2;
            aiRing.numCornerVertices = 2;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2 / 32f;
                aiRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * 55f, 14f,
                                                  Mathf.Sin(angle) * 55f));
            }
            Material baseMaterial = Resources.Load<Material>("Native/GarenLit");
            if (!baseMaterial)
                throw new InvalidOperationException("Native/GarenLit material missing from built Player");
            var material = new Material(baseMaterial);
            material.name = "Tactical AI position marker";
            material.SetColor("_BaseColor", aiRole == MatchRole.Hunter ?
                new Color(1f, .25f, .2f) : new Color(1f, .74f, .2f));
            aiRing.sharedMaterial = material;
            UpdateMarkerLocation();
        }

        void UpdateMarkerLocation()
        {
            if (aiIndicator && rift && rift.Navigation != null)
                aiIndicator.transform.position = new Vector3(aiPosition.x,
                    rift.Navigation.HeightAt(aiPosition), aiPosition.y);
        }

        void BeginMatch(MatchRole selectedRole)
        {
            if (!rift || !rift.IsReady) return;
            if (aiIndicator) { Destroy(aiRing.sharedMaterial); Destroy(aiIndicator); }
            humanRole = selectedRole;
            aiRole = humanRole == MatchRole.Hunter ? MatchRole.Hider : MatchRole.Hunter;

            // Garen is the only fully reconstructed native champion. This
            // opponent uses a distinct, explicitly labeled *training* slot.
            rules = new NativeMatchRules(
                new MatchParticipant(HumanId, "Garen", humanRole, true),
                new MatchParticipant(AiId, "TrainingAI", aiRole, false));
            rift.ExternalGameplayControl = true;
            rift.ExternalMoveEnabled = true;
            if (!rift.TryTeleportTo(humanRole == MatchRole.Hunter ? HunterStaging : HiderStart))
            {
                Debug.LogError("MATCH_START_ABORTED: player staging position unavailable");
                rift.ExternalGameplayControl = false;
                rules = null;
                return;
            }
            if (!rift.Navigation.TryNearest(aiRole == MatchRole.Hunter ?
                         HunterStaging : HiderStart + new Vector2(420f, 180f),
                         out aiPosition, 65))
            {
                Debug.LogError("MATCH_START_ABORTED: AI spawn unavailable");
                rift.ExternalGameplayControl = false;
                rules = null;
                return;
            }
            aiRoute.Clear(); aiWaypoint = 0;
            decisionCooldown = aiAttackCooldown = humanAttackCooldown = 0;
            resultLogged = false;
            CreateTrainingMarker();
            rules.Begin();
            rift.ExternalMoveEnabled = rules.CanMove(HumanId);
            Debug.Log("MATCH_BEGIN: mode=1v1 native 3D; player=" + humanRole +
                      " 120s preparation; 180s chase; no champion placeholders claimed");
        }

        void ChooseAiRoute(Vector2 destination)
        {
            candidatePath.Clear();
            if (rift.Navigation.TryPath(aiPosition, destination, candidatePath) &&
                candidatePath.Count > 0)
            {
                aiRoute.Clear();
                aiRoute.AddRange(candidatePath);
                aiWaypoint = 0;
                aiDestination = destination;
            }
        }

        void Wander()
        {
            for (int attempt = 0; attempt < 7; attempt++)
            {
                double direction = random.NextDouble() * Math.PI * 2;
                float distance = 500f + (float)random.NextDouble() * 650f;
                Vector2 candidate = aiPosition +
                    new Vector2((float)Math.Cos(direction), (float)Math.Sin(direction)) * distance;
                if (!rift.Navigation.TryNearest(candidate, out var snapped, 13)) continue;
                ChooseAiRoute(snapped);
                if (aiRoute.Count > 0) return;
            }
        }

        void UpdateAi(float dt)
        {
            var nav = rift.Navigation;
            bool canMove = rules.CanMove(AiId);
            bool seesHuman = NativeSight.Visible(nav, aiPosition, rift.PlayerPosition);
            if (!canMove) { aiRoute.Clear(); aiWaypoint = 0; return; }

            decisionCooldown -= dt;
            if (decisionCooldown <= 0f)
            {
                decisionCooldown = AiDecisionInterval;
                if (aiRole == MatchRole.Hunter && seesHuman)
                {
                    if (Vector2.Distance(aiDestination, rift.PlayerPosition) > 125f ||
                        aiWaypoint >= aiRoute.Count)
                        ChooseAiRoute(rift.PlayerPosition);
                }
                else if (aiRole == MatchRole.Hider && seesHuman &&
                         rules.Phase == MatchPhase.Pursuit)
                {
                    Vector2 flee = (aiPosition - rift.PlayerPosition).normalized;
                    if (flee.sqrMagnitude < .01f) flee = Vector2.right;
                    Vector2 candidate = aiPosition + flee * 900f;
                    if (nav.TryNearest(candidate, out var snap, 18))
                        ChooseAiRoute(snap);
                }
                else if (aiWaypoint >= aiRoute.Count)
                    Wander();
            }

            if (aiWaypoint < aiRoute.Count)
            {
                float available = (aiRole == MatchRole.Hunter ? HunterMoveSpeed : HiderMoveSpeed) * dt;
                while (available > 0 && aiWaypoint < aiRoute.Count)
                {
                    Vector2 next = aiRoute[aiWaypoint];
                    Vector2 delta = next - aiPosition;
                    if (delta.magnitude <= available)
                    {
                        aiPosition = next;
                        available -= delta.magnitude;
                        aiWaypoint++;
                    }
                    else
                    {
                        Vector2 step = aiPosition + delta.normalized * available;
                        if (nav.CanWalk(step)) aiPosition = step;
                        available = 0;
                    }
                }
            }

            UpdateMarkerLocation();
            if (aiRole == MatchRole.Hunter && rules.CanAttack(AiId) &&
                aiAttackCooldown <= 0f)
            {
                float distance = Vector2.Distance(aiPosition, rift.PlayerPosition);
                if (distance <= NativeMatchRules.BasicAttackReach && seesHuman)
                {
                    aiAttackCooldown = AiAttackCooldown;
                    bool captured = rules.TryCapture(AiId, HumanId, true,
                        NativeSight.Visible(nav, aiPosition, rift.PlayerPosition), distance);
                    Debug.Log("MATCH_AI_BASIC_ATTACK: hit=" + captured + " distance=" + distance);
                }
            }
        }

        void RenderAiVisibility()
        {
            if (!aiRing || rules == null) return;
            // Individual perception only: no global wallhack. The tactical
            // ring is explicitly not an original champion model.
            bool allowed = !rules.Find(AiId).Eliminated &&
                rules.Phase != MatchPhase.Lobby &&
                NativeSight.Visible(rift.Navigation, rift.PlayerPosition, aiPosition);
            aiRing.enabled = allowed;
        }

        void ReturnToMenu()
        {
            if (aiIndicator) { Destroy(aiRing.sharedMaterial); Destroy(aiIndicator); }
            aiIndicator = null;
            aiRing = null;
            rules = null;
            rift.StopMovement();
            rift.ExternalGameplayControl = false;
            rift.ExternalMoveEnabled = true;
            rift.ExternalMoveMultiplier = 1f;
            rift.TryTeleportTo(HiderStart);
        }

        void OnGUI()
        {
            if (!rift || !rift.IsReady) return;
            GUI.Box(new Rect(12, 12, 465, rules == null ? 180 : 210), "");
            GUI.Label(new Rect(28, 20, 440, 26), "HEXTECH SEEKERS | NATIVE 3D | PLAYABLE MATCH ALPHA");
            if (rules == null)
            {
                GUI.Label(new Rect(28, 51, 420, 26), "Garen vs tactical AI marker (not a finished champion).");
                GUI.Label(new Rect(28, 74, 420, 26), "Choose your role. 120s hiding preparation.");
                if (GUI.Button(new Rect(28, 110, 194, 38), "Play as HUNTER"))
                    BeginMatch(MatchRole.Hunter);
                if (GUI.Button(new Rect(234, 110, 194, 38), "Play as HIDER"))
                    BeginMatch(MatchRole.Hider);
                return;
            }
            string phase = rules.Phase == MatchPhase.Preparation ? "HIDING PREPARATION" :
                           rules.Phase == MatchPhase.Pursuit ? "PURSUIT" : "ROUND OVER";
            GUI.Label(new Rect(28, 49, 435, 24),
                "Role: " + humanRole + " | " + phase +
                " | " + Mathf.CeilToInt(rules.RemainingSeconds) + "s");
            GUI.Label(new Rect(28, 73, 435, 24),
                "Captured: " + rules.CapturedCount + "/" + rules.HiderCount +
                " | Scores: you " + rules.Find(HumanId).Score +
                ", AI " + rules.Find(AiId).Score);
            if (rules.Phase == MatchPhase.Finished)
            {
                GUI.Label(new Rect(28, 102, 435, 26),
                    (rules.HunterWon ? "Hunter wins" : "Hiders survive") +
                    " | " + (rules.Find(HumanId).Eliminated ? "SPECTATING" : "ALIVE"));
                if (GUI.Button(new Rect(28, 140, 200, 42), "Return to role select"))
                    ReturnToMenu();
            }
            else
            {
                GUI.Label(new Rect(28, 105, 430, 26),
                    rules.Phase == MatchPhase.Preparation && humanRole == MatchRole.Hunter ?
                    "Hunter locked in staging until preparation ends." :
                    "RMB: move  |  S: stop  |  A: hunter basic attack");
                GUI.Label(new Rect(28, 133, 432, 24),
                    "Sight: local LOS + bush detection (full fog pending).");
            }
        }

        void OnDestroy()
        {
            if (aiRing && aiRing.sharedMaterial) Destroy(aiRing.sharedMaterial);
            if (aiIndicator) Destroy(aiIndicator);
        }
    }
}
