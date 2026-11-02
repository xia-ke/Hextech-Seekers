using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    public enum NativeHextech
    {
        FreezeGold,
        AstralSilenceSilver,
        CardSenseSilver,
        CardSenseGold,
        TimeClockSilver,
        MirrorSilver,
        ReadyShoesSilver,
        LevelUpGold
    }

    public sealed class NativeHextechDefinition
    {
        public readonly NativeHextech Id;
        public readonly string Name;
        public readonly bool Active;
        public readonly bool HiderOnly;
        public NativeHextechDefinition(NativeHextech id, string name, bool active, bool hiderOnly = false)
        {
            Id = id; Name = name; Active = active; HiderOnly = hiderOnly;
        }
        public bool Eligible(MatchRole role) => !HiderOnly || role == MatchRole.Hider;
    }

    /// <summary>
    /// Exactly the eight confirmed V1 augments, no rune pages or summoner spells.
    /// Three distinct choices per character; independent rolls may repeat
    /// the same augment on different characters.
    /// </summary>
    public static class NativeHextechPool
    {
        public static readonly NativeHextechDefinition[] All =
        {
            new NativeHextechDefinition(NativeHextech.FreezeGold, "冻结·金", true),
            new NativeHextechDefinition(NativeHextech.AstralSilenceSilver, "星体结界·银", true),
            new NativeHextechDefinition(NativeHextech.CardSenseSilver, "卡牌感应·银", true),
            new NativeHextechDefinition(NativeHextech.CardSenseGold, "卡牌感应·金", true),
            new NativeHextechDefinition(NativeHextech.TimeClockSilver, "时光发条·银", true),
            new NativeHextechDefinition(NativeHextech.MirrorSilver, "镜花水月·银", false, true),
            new NativeHextechDefinition(NativeHextech.ReadyShoesSilver, "准备动手·银", false),
            new NativeHextechDefinition(NativeHextech.LevelUpGold, "升级咯·金", false)
        };

        public static NativeHextechDefinition Find(NativeHextech id)
        {
            foreach (var definition in All) if (definition.Id == id) return definition;
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        public static NativeHextechDefinition[] Deal(MatchRole role, System.Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var candidates = new List<NativeHextechDefinition>();
            foreach (var definition in All)
                if (definition.Eligible(role)) candidates.Add(definition);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var swap = candidates[i]; candidates[i] = candidates[j]; candidates[j] = swap;
            }
            return candidates.GetRange(0, 3).ToArray();
        }
    }

    public enum NativeSkillSlot { Q, W, E }

    /// <summary>
    /// Whitelist derived from the agreed adapted skills, NOT a claim that all
    /// original champion physics, models or hit effects are implemented.
    /// </summary>
    public static class NativeSkillCatalog
    {
        public static bool Allowed(string hero, MatchRole role, NativeSkillSlot slot)
        {
            if (string.IsNullOrEmpty(hero)) return false;
            switch (hero.ToLowerInvariant())
            {
                case "garen": return slot == NativeSkillSlot.Q || slot == NativeSkillSlot.W;
                case "blitzcrank":
                    return slot == NativeSkillSlot.Q || slot == NativeSkillSlot.W ||
                           (role == MatchRole.Hunter && slot == NativeSkillSlot.E);
                case "riven": return true;
                case "yasuo": return true;
                default: return false;
            }
        }
    }

    public sealed class NativeHeroLoadout
    {
        public readonly string Hero;
        public readonly MatchRole Role;
        public NativeSkillSlot? Primary { get; private set; }
        public NativeSkillSlot? Secondary { get; private set; }
        public int Level { get; private set; } = 1;

        public NativeHeroLoadout(string hero, MatchRole role)
        {
            Hero = hero ?? throw new ArgumentNullException(nameof(hero));
            Role = role;
        }

        public bool Choose(NativeSkillSlot slot)
        {
            if (!NativeSkillCatalog.Allowed(Hero, Role, slot)) return false;
            if (!Primary.HasValue) { Primary = slot; return true; }
            if (Level < 2 || Secondary.HasValue || Primary.Value == slot) return false;
            Secondary = slot;
            return true;
        }

        public bool UpgradeDuringPreparation(MatchPhase phase)
        {
            if (phase != MatchPhase.Preparation || Level >= 2) return false;
            Level = 2;
            return true;
        }

        public bool Knows(NativeSkillSlot slot) =>
            Primary == slot || Secondary == slot;
    }

    /// <summary>
    /// Game-time status and cooldown clock. Immunity is deliberately distinct
    /// from ordinary shields: only Mirror can absorb a capture.
    /// Provisional balance values are named constants pending gameplay tuning.
    /// </summary>
    public sealed class NativeHextechRuntime
    {
        public const float CooldownSeconds = 120f;
        public const float FreezeSeconds = 2f;
        public const float SilenceSeconds = 5f;
        public const float MirrorGraceSecondsProvisional = 2.5f;
        public const float ShoesSpeedMultiplierProvisional = 1.08f;
        public const float TimeShiftSeconds = 60f;

        sealed class Holder
        {
            public MatchParticipant Participant;
            public NativeHextech? Chosen;
            public float NextUseAt;
            public float StunUntil;
            public float SilenceUntil;
            public float ImmuneUntil;
            public bool UsedOneShot;
        }

        readonly NativeMatchRules match;
        readonly Dictionary<int, Holder> holders = new Dictionary<int, Holder>();
        float now;
        public float Elapsed => now;

        public NativeHextechRuntime(NativeMatchRules match)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            foreach (var p in match.Players)
                holders.Add(p.Id, new Holder { Participant = p });
        }

        public bool Select(int playerId, NativeHextech id)
        {
            if (!holders.TryGetValue(playerId, out var h) || h.Chosen.HasValue ||
                match.Phase != MatchPhase.Lobby) return false;
            var definition = NativeHextechPool.Find(id);
            if (!definition.Eligible(h.Participant.Role)) return false;
            h.Chosen = id;
            return true;
        }

        public NativeHextech? Selected(int playerId) =>
            holders.TryGetValue(playerId, out var h) ? h.Chosen : null;

        /// <summary>Passive level-up is applied during preparation, after Begin.</summary>
        public bool ApplyPreparationPassives(int playerId, NativeHeroLoadout loadout)
        {
            if (loadout == null || !holders.TryGetValue(playerId, out var h) ||
                match.Phase != MatchPhase.Preparation) return false;
            if (h.Chosen == NativeHextech.LevelUpGold)
                return loadout.UpgradeDuringPreparation(match.Phase);
            return false;
        }

        public float MoveMultiplier(int playerId) =>
            Selected(playerId) == NativeHextech.ReadyShoesSilver ?
                ShoesSpeedMultiplierProvisional : 1f;

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (match.Phase == MatchPhase.Finished || match.Phase == MatchPhase.Lobby) return;
            now += deltaSeconds;
        }

        public bool Stunned(int playerId) =>
            holders.TryGetValue(playerId, out var h) && h.StunUntil > now;
        public bool Silenced(int playerId) =>
            holders.TryGetValue(playerId, out var h) && h.SilenceUntil > now;
        public bool CanUseHeroSkill(int playerId) =>
            !Stunned(playerId) && !Silenced(playerId) &&
            match.CanMove(playerId) &&
            holders.TryGetValue(playerId, out var h) && !h.Participant.Eliminated;
        public bool CanUseActiveHextech(int playerId) =>
            !Stunned(playerId) &&
            holders.TryGetValue(playerId, out var h) &&
            !h.Participant.Eliminated && match.Phase == MatchPhase.Pursuit &&
            h.Chosen.HasValue && NativeHextechPool.Find(h.Chosen.Value).Active &&
            h.NextUseAt <= now && !h.UsedOneShot;
        public float RemainingCooldown(int playerId) =>
            holders.TryGetValue(playerId, out var h) ? Mathf.Max(0f, h.NextUseAt - now) : 0f;

        public bool TryActive(int actorId, int targetId, bool targetVisible,
                              int clockDirection, string campInformation,
                              out string result, float targetControlDurationMultiplier = 1f)
        {
            result = "Active hextech is unavailable";
            if (!CanUseActiveHextech(actorId)) return false;
            var actor = holders[actorId];
            var id = actor.Chosen.Value;
            bool needsTarget = id == NativeHextech.FreezeGold ||
                id == NativeHextech.AstralSilenceSilver ||
                id == NativeHextech.CardSenseSilver;
            if (needsTarget)
            {
                if (!holders.TryGetValue(targetId, out var target) ||
                    target.Participant.Eliminated ||
                    target.Participant.Role == actor.Participant.Role)
                {
                    result = "No eligible enemy target";
                    return false;
                }
                // Card Sense is an explicit information-gathering exception:
                // it must be able to locate an unseen enemy. A stun/silence
                // still requires the enemy to be in current personal vision.
                if (id != NativeHextech.CardSenseSilver && !targetVisible)
                {
                    result = "Enemy is outside your vision";
                    return false;
                }
            }
            if (id == NativeHextech.FreezeGold)
            {
                var target = holders[targetId];
                target.StunUntil = Mathf.Max(target.StunUntil,
                    now + FreezeSeconds * Mathf.Clamp(targetControlDurationMultiplier, .1f, 1f));
                result = "冻结·金: 2 second stun";
            }
            else if (id == NativeHextech.AstralSilenceSilver)
            {
                var target = holders[targetId];
                target.SilenceUntil = Mathf.Max(target.SilenceUntil,
                    now + SilenceSeconds * Mathf.Clamp(targetControlDurationMultiplier, .1f, 1f));
                result = "星体结界·银: 5 second hero-skill silence; active hextech remains enabled";
            }
            else if (id == NativeHextech.CardSenseSilver || id == NativeHextech.CardSenseGold)
            {
                if (string.IsNullOrWhiteSpace(campInformation))
                {
                    result = "No verified jungle camp location data configured";
                    return false;
                }
                result = campInformation;
            }
            else if (id == NativeHextech.TimeClockSilver)
            {
                if (clockDirection != -1 && clockDirection != 1)
                {
                    result = "Select +1 minute or -1 minute";
                    return false;
                }
                if (!match.ShiftPursuitEndTime(clockDirection * TimeShiftSeconds))
                    return false;
                actor.UsedOneShot = true;
                result = "时光发条·银: " + (clockDirection > 0 ? "+60s" : "-60s") +
                         " remaining; early-capture bonus threshold unchanged";
            }
            else return false;

            if (!actor.UsedOneShot) actor.NextUseAt = now + CooldownSeconds;
            return true;
        }

        /// <summary>
        /// Must be called only after a *valid* hunter basic-attack hit.
        /// Invalid attacks never consume the one-time shield.
        /// </summary>
        public bool AbsorbValidCapture(int targetId)
        {
            if (!holders.TryGetValue(targetId, out var h) ||
                h.Participant.Eliminated || h.Participant.Role != MatchRole.Hider)
                return false;
            if (now < h.ImmuneUntil) return true;
            if (h.Chosen != NativeHextech.MirrorSilver || h.UsedOneShot) return false;
            h.UsedOneShot = true;
            h.ImmuneUntil = now + MirrorGraceSecondsProvisional;
            return true;
        }

        public bool TryBasicAttackCapture(int attackerId, int targetId, bool hit,
                                          bool visible, float distance)
        {
            if (Stunned(attackerId) || !match.IsValidCaptureHit(
                    attackerId, targetId, hit, visible, distance))
                return false;
            if (AbsorbValidCapture(targetId)) return false;
            return match.TryCapture(attackerId, targetId, hit, visible, distance);
        }
    }
}
