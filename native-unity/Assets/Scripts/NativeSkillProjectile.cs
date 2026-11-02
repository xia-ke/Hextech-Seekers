using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Frame-stepped, navigation-authoritative linear skill projectile.
    /// Collision is physical (a blind shot can hit brush concealment), while
    /// AI target selection remains separately gated by personal sight.
    /// </summary>
    public sealed class NativeDirectionalProjectile
    {
        Vector2 origin;
        Vector2 direction;
        float travelled;
        float range;
        float speed;
        float radius;
        int ownerId;
        MatchRole ownerRole;
        string skill;
        bool active;

        public bool Active => active;
        public Vector2 Position => origin + direction * travelled;
        public float Travelled => travelled;
        public float RemainingRange => Mathf.Max(0f, range - travelled);
        public string Skill => skill;

        public bool Begin(int casterId, MatchRole casterRole, string skillName,
                          Vector2 start, Vector2 facing, float maxRange,
                          float projectileSpeed, float collisionRadius,
                          RiftGrid navigation)
        {
            if (active || navigation == null || casterId < 0 ||
                string.IsNullOrWhiteSpace(skillName) || !navigation.CanWalk(start) ||
                facing.sqrMagnitude < .001f || float.IsNaN(maxRange) ||
                float.IsInfinity(maxRange) || maxRange <= 0f ||
                float.IsNaN(projectileSpeed) || float.IsInfinity(projectileSpeed) ||
                projectileSpeed <= 0f || float.IsNaN(collisionRadius) ||
                float.IsInfinity(collisionRadius) || collisionRadius <= 0f)
                return false;
            ownerId = casterId;
            ownerRole = casterRole;
            skill = skillName;
            origin = start;
            direction = facing.normalized;
            range = maxRange;
            speed = projectileSpeed;
            radius = collisionRadius;
            travelled = 0f;
            active = true;
            return true;
        }

        public NativeSkillImpact Step(float deltaSeconds, RiftGrid navigation,
                                      IReadOnlyList<NativeSkillTarget> targets,
                                      NativeWindWall windWall, float worldTime)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!active || navigation == null) return null;
            float nextTravel = Mathf.Min(range, travelled + speed * deltaSeconds);
            Vector2 previous = Position;
            Vector2 next = origin + direction * nextTravel;
            int steps = Mathf.Max(1, Mathf.CeilToInt(
                Vector2.Distance(previous, next) / (RiftGrid.CellSize * .2f)));
            for (int i = 1; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(previous, next, i / (float)steps);
                if (!navigation.CanWalk(point) ||
                    (windWall != null && windWall.Crosses(previous, point, worldTime)))
                {
                    active = false;
                    return new NativeSkillImpact {
                        Skill = skill, ProjectileBlocked = true
                    };
                }
                if (targets == null) continue;
                foreach (var target in targets)
                {
                    if (target.Id == ownerId || target.Eliminated ||
                        target.Role == ownerRole ||
                        Vector2.Distance(point, target.Position) > radius)
                        continue;
                    if (!navigation.LineClear(origin, target.Position)) continue;
                    active = false;
                    return new NativeSkillImpact {
                        Skill = skill, TargetId = target.Id
                    };
                }
            }
            travelled = nextTravel;
            if (travelled >= range) active = false;
            return null;
        }

        public void Cancel() { active = false; travelled = 0f; }
    }
}
