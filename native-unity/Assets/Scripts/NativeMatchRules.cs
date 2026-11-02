using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    public enum MatchPhase { Lobby, Preparation, Pursuit, Finished }
    public enum MatchRole { Hunter, Hider }

    /// <summary>
    /// One participant, with its own identity, hero slot and visibility.
    /// A training opponent must never be presented as a completed original hero.
    /// </summary>
    public sealed class MatchParticipant
    {
        public int Id { get; private set; }
        public string HeroKey { get; private set; }
        public MatchRole Role { get; private set; }
        public bool Human { get; private set; }
        public bool Eliminated { get; internal set; }
        public int Score { get; internal set; }

        public MatchParticipant(int id, string heroKey, MatchRole role, bool human)
        {
            if (string.IsNullOrWhiteSpace(heroKey)) throw new ArgumentException("Hero key is required", nameof(heroKey));
            Id = id;
            HeroKey = heroKey;
            Role = role;
            Human = human;
        }
    }

    /// <summary>
    /// Authoritative single-round hide-and-seek rules, independent of rendering,
    /// animation, input, frame rate, and AI. One hunter; 1-3 hiders; no duplicate
    /// hero slots. A successful basic attack alone can capture.
    /// </summary>
    public sealed class NativeMatchRules
    {
        public const float PreparationSeconds = 120f;
        public const float BasicAttackReach = 130f;
        readonly MatchParticipant[] players;
        readonly int hiders;
        readonly MatchParticipant hunter;
        public MatchPhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }
        public float ChaseElapsedSeconds { get; private set; }
        public int CapturedCount { get; private set; }
        public bool HunterWon { get; private set; }
        public IReadOnlyList<MatchParticipant> Players => players;
        public int HiderCount => hiders;

        public static float ChaseSeconds(int hiderCount)
        {
            if (hiderCount < 1 || hiderCount > 3)
                throw new ArgumentOutOfRangeException(nameof(hiderCount), "First release supports 1-3 hiders");
            return (3 + 2 * (hiderCount - 1)) * 60f;
        }

        public static float EarlyCaptureBonusSeconds(int hiderCount)
        {
            if (hiderCount < 1 || hiderCount > 3)
                throw new ArgumentOutOfRangeException(nameof(hiderCount));
            return (3 + 2 * (hiderCount - 2)) * 60f;
        }

        public NativeMatchRules(params MatchParticipant[] participants)
        {
            if (participants == null || participants.Length < 2 || participants.Length > 4)
                throw new ArgumentException("A round requires 2-4 participants", nameof(participants));
            var ids = new HashSet<int>();
            var heroKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int hunters = 0, humans = 0;
            foreach (var p in participants)
            {
                if (p == null || !ids.Add(p.Id) || !heroKeys.Add(p.HeroKey))
                    throw new ArgumentException("Participants must have unique ids and hero slots");
                if (p.Role == MatchRole.Hunter) { hunters++; hunter = p; }
                else hiders++;
                if (p.Human) humans++;
            }
            if (hunters != 1 || humans != 1)
                throw new ArgumentException("First release requires one hunter and one human");
            players = (MatchParticipant[])participants.Clone();
            Phase = MatchPhase.Lobby;
        }

        public void Begin()
        {
            if (Phase != MatchPhase.Lobby) throw new InvalidOperationException("Match already started");
            foreach (var p in players) { p.Eliminated = false; p.Score = 0; }
            CapturedCount = 0;
            ChaseElapsedSeconds = 0;
            HunterWon = false;
            Phase = MatchPhase.Preparation;
            RemainingSeconds = PreparationSeconds;
        }

        public MatchParticipant Find(int id)
        {
            foreach (var p in players) if (p.Id == id) return p;
            return null;
        }

        public bool CanMove(int id)
        {
            var p = Find(id);
            return p != null && !p.Eliminated &&
                (Phase == MatchPhase.Preparation && p.Role == MatchRole.Hider ||
                 Phase == MatchPhase.Pursuit);
        }

        public bool CanAttack(int id)
        {
            var p = Find(id);
            return Phase == MatchPhase.Pursuit && p != null &&
                   p.Role == MatchRole.Hunter && !p.Eliminated;
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (Phase == MatchPhase.Lobby || Phase == MatchPhase.Finished) return;

            if (Phase == MatchPhase.Preparation)
            {
                if (deltaSeconds < RemainingSeconds)
                {
                    RemainingSeconds -= deltaSeconds;
                    return;
                }
                deltaSeconds -= RemainingSeconds;
                Phase = MatchPhase.Pursuit;
                RemainingSeconds = ChaseSeconds(hiders);
            }
            if (Phase != MatchPhase.Pursuit) return;
            float elapsed = Mathf.Min(RemainingSeconds, deltaSeconds);
            ChaseElapsedSeconds += elapsed;
            RemainingSeconds -= elapsed;
            if (RemainingSeconds <= 0f)
                Complete();
        }

        /// <summary>Validate the real attack BEFORE allowing passive protection to consume.</summary>
        public bool IsValidCaptureHit(int attackerId, int targetId, bool basicAttackHit,
                                      bool visible, float worldDistance)
        {
            if (!basicAttackHit || !visible || !CanAttack(attackerId) ||
                float.IsNaN(worldDistance) || float.IsInfinity(worldDistance) ||
                worldDistance < 0 || worldDistance > BasicAttackReach) return false;
            var target = Find(targetId);
            return target != null && target.Role == MatchRole.Hider && !target.Eliminated;
        }

        /// <summary>
        /// Time Clock changes the chase end time only. ChaseElapsedSeconds and
        /// EarlyCaptureBonusSeconds() are deliberately unchanged.
        /// </summary>
        public bool ShiftPursuitEndTime(float deltaSeconds)
        {
            if (Phase != MatchPhase.Pursuit || float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) || Mathf.Abs(deltaSeconds) != 60f)
                return false;
            RemainingSeconds = Mathf.Max(0f, RemainingSeconds + deltaSeconds);
            if (RemainingSeconds <= 0) Complete();
            return true;
        }

        public bool TryCapture(int attackerId, int targetId, bool basicAttackHit,
                               bool visible, float worldDistance)
        {
            if (!IsValidCaptureHit(attackerId, targetId, basicAttackHit, visible, worldDistance))
                return false;
            var target = Find(targetId);
            target.Eliminated = true;
            CapturedCount++;
            if (CapturedCount == hiders) Complete();
            return true;
        }

        void Complete()
        {
            if (Phase == MatchPhase.Finished) return;
            Phase = MatchPhase.Finished;
            RemainingSeconds = 0;
            int surviving = hiders - CapturedCount;
            HunterWon = surviving == 0;
            hunter.Score = CapturedCount +
                (HunterWon && ChaseElapsedSeconds <= EarlyCaptureBonusSeconds(hiders) ? 1 : 0);
            foreach (var p in players)
            {
                if (p.Role != MatchRole.Hider) continue;
                if (surviving == 0) p.Score = 0;
                else if (surviving == 1)
                    p.Score = p.Eliminated ? 1 : Mathf.CeilToInt(hiders / 2f);
                else
                    p.Score = p.Eliminated ? 0 : surviving;
            }
        }
    }

    /// <summary>
    /// Tactical LOS only; map-wide shader fog and full League vision occlusion
    /// are not yet implemented. Used for each AI's *own* perception.
    /// </summary>
    public static class NativeSight
    {
        public const float BasicSightDistance = 1250f;
        public const float BushRevealDistance = 120f;

        public static bool Visible(RiftGrid nav, Vector2 observer, Vector2 target)
        {
            if (nav == null || !nav.CanWalk(observer) || !nav.CanWalk(target)) return false;
            float distance = Vector2.Distance(observer, target);
            if (distance > BasicSightDistance || !nav.LineClear(observer, target)) return false;
            byte targetBush = nav.BushAt(target);
            if (targetBush == 0) return true;
            byte observerBush = nav.BushAt(observer);
            return distance <= BushRevealDistance || observerBush == targetBush;
        }
    }
}
