using System;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// First playable Garen Q/W mechanics on the actual Garen controller.
    /// Timings and movement amounts are provisional pending reference-version
    /// verification. E is deliberately illegal under the pure-damage rule.
    /// This is gameplay logic, not a claim that source VFX/audio are complete.
    /// </summary>
    public sealed class NativeGarenSkills
    {
        public const float QDurationProvisional = 1.5f;
        public const float QSpeedFactorProvisional = 1.25f;
        public const float QCooldownProvisional = 8f;
        public const float WDurationProvisional = 2f;
        public const float WCooldownProvisional = 20f;
        public const float WControlDurationFactorProvisional = .65f;

        float elapsed;
        float qEndsAt;
        float wEndsAt;
        float nextQ;
        float nextW;
        public bool SlowClearedByQ { get; private set; }
        public bool QActive => qEndsAt > elapsed;
        public bool WActive => wEndsAt > elapsed;
        public float MovementMultiplier => QActive ? QSpeedFactorProvisional : 1f;
        public float CrowdControlDurationFactor => WActive ? WControlDurationFactorProvisional : 1f;
        public float CooldownQ => Mathf.Max(0f, nextQ - elapsed);
        public float CooldownW => Mathf.Max(0f, nextW - elapsed);

        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds < 0 || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            elapsed += deltaSeconds;
        }

        /// <summary>No skill damage. Q cleanses slow status, accelerates movement,
        /// and any subsequent empowered basic attack is still a basic attack.</summary>
        public bool TryCast(NativeSkillSlot slot, NativeHeroLoadout loadout,
                            NativeMatchRules match, NativeHextechRuntime hextech,
                            int playerId, bool slowed)
        {
            if (loadout == null || !loadout.Knows(slot) ||
                !string.Equals(loadout.Hero, "Garen", StringComparison.OrdinalIgnoreCase) ||
                match == null || hextech == null ||
                !match.CanMove(playerId) || !hextech.CanUseHeroSkill(playerId))
                return false;
            if (slot == NativeSkillSlot.Q && elapsed >= nextQ)
            {
                SlowClearedByQ = slowed;
                qEndsAt = elapsed + QDurationProvisional;
                nextQ = elapsed + QCooldownProvisional;
                return true;
            }
            if (slot == NativeSkillSlot.W && elapsed >= nextW)
            {
                wEndsAt = elapsed + WDurationProvisional;
                nextW = elapsed + WCooldownProvisional;
                return true;
            }
            return false;
        }
    }
}
