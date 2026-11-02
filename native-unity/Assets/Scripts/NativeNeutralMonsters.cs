using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Non-combat neutral skill target anchored to a verified Map11 camp.
    /// There is deliberately no health, damage, attack, death or respawn loop.
    /// A skill contact can trigger an effect but must never capture a hider.
    /// </summary>
    public sealed class NativeNeutralMonster
    {
        public readonly int Id;
        public readonly string CampName;
        public readonly Vector2 CampPosition;
        public readonly Vector2 WalkableAnchor;
        public bool Alive => true;
        public bool Aggressive => false;

        internal NativeNeutralMonster(int id, string campName, Vector2 position, Vector2 anchor)
        {
            Id = id;
            CampName = campName;
            CampPosition = position;
            WalkableAnchor = anchor;
        }
    }

    /// <summary>
    /// Physical targeting/data model. Creates no monsters unless a manually
    /// verified camp catalog is supplied. A camp is currently one targetable
    /// test entity, NOT a complete population of jungle monsters.
    /// </summary>
    public sealed class NativeNeutralMonsterWorld
    {
        readonly List<NativeNeutralMonster> targets = new List<NativeNeutralMonster>();
        readonly RiftGrid navigation;
        public IReadOnlyList<NativeNeutralMonster> Monsters => targets;
        public bool HasVerifiedCamps => targets.Count > 0;

        public NativeNeutralMonsterWorld(RiftGrid navigation, NativeCampCatalog camps)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            if (camps == null) return;
            int id = 1;
            foreach (var camp in camps.Camps)
            {
                if (!navigation.TryNearest(camp.Location, out var anchor, 15))
                    throw new InvalidOperationException("Verified camp is not accessible: " + camp.Name);
                targets.Add(new NativeNeutralMonster(id++, camp.Name, camp.Location, anchor));
            }
        }

        public NativeNeutralMonster Find(int id)
        {
            foreach (var monster in targets)
                if (monster.Id == id) return monster;
            return null;
        }

        /// <summary>
        /// Skill contact query for a provisional linear skill footprint:
        /// a positive distance along normalized direction, <= reach, and
        /// transverse offset <= halfWidth. No hit through navigation walls.
        /// Always returns a real verified camp entity or null.
        /// </summary>
        public NativeNeutralMonster FirstLineContact(
            Vector2 origin, Vector2 direction, float reach, float halfWidth)
        {
            if (float.IsNaN(reach) || float.IsInfinity(reach) || reach <= 0 ||
                float.IsNaN(halfWidth) || float.IsInfinity(halfWidth) || halfWidth <= 0 ||
                direction.sqrMagnitude < .0001f || !navigation.CanWalk(origin))
                return null;
            direction.Normalize();
            float nearest = float.PositiveInfinity;
            NativeNeutralMonster found = null;
            foreach (var monster in targets)
            {
                Vector2 delta = monster.WalkableAnchor - origin;
                float forward = Vector2.Dot(delta, direction);
                if (forward < 0 || forward > reach || forward >= nearest) continue;
                Vector2 projected = origin + direction * forward;
                if ((monster.WalkableAnchor - projected).sqrMagnitude > halfWidth * halfWidth)
                    continue;
                if (!navigation.LineClear(origin, monster.WalkableAnchor)) continue;
                found = monster;
                nearest = forward;
            }
            return found;
        }

        /// <summary>
        /// Only applicable to target-requiring dashes. Returns a walkable
        /// destination beyond the entity. Does not physically move the monster.
        /// </summary>
        public bool TryDashLanding(int monsterId, Vector2 caster,
                                   float maxRange, float beyond, out Vector2 landing)
        {
            landing = default;
            var monster = Find(monsterId);
            if (monster == null || !navigation.CanWalk(caster)) return false;
            Vector2 to = monster.WalkableAnchor - caster;
            if (to.sqrMagnitude < 1 || to.sqrMagnitude > maxRange * maxRange ||
                !navigation.LineClear(caster, monster.WalkableAnchor))
                return false;
            Vector2 proposed = monster.WalkableAnchor + to.normalized * beyond;
            if (!navigation.TryNearest(proposed, out landing, 3))
                return false;
            return navigation.LineClear(monster.WalkableAnchor, landing) &&
                   navigation.LineClear(caster, landing);
        }
    }

    /// <summary>
    /// Yasuo Q skill state and monster stack interactions. Damage is ignored.
    /// Two landed Qs prepare a tornado on the third cast; third cast consumes
    /// state even on miss. All timing and footprint numbers are provisional.
    /// No Yasuo artwork, projectile or hero controller is claimed here.
    /// </summary>
    public sealed class NativeYasuoSkillState
    {
        public const float QCooldownProvisional = 3.5f;
        public const float QStackLifetimeProvisional = 6f;
        public const float QReachProvisional = 260f;
        public const float QHalfWidthProvisional = 62f;
        public const float EDashRangeProvisional = 440f;
        public const float EDashBeyondProvisional = 125f;
        public const float ERepeatTargetLockProvisional = 8f;

        readonly Dictionary<int, float> targetDashLocks = new Dictionary<int, float>();
        float clock;
        float eAvailableAt;
        float qAvailableAt;
        float stackExpiresAt;
        int stacks;

        public int QStacks => stacks;
        public float CooldownQ => Mathf.Max(0, qAvailableAt - clock);
        public float CooldownE => Mathf.Max(0, eAvailableAt - clock);

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            clock += deltaSeconds;
            if (stacks > 0 && clock > stackExpiresAt) stacks = 0;
        }

        bool CanCast(NativeHeroLoadout loadout, NativeMatchRules match,
                     NativeHextechRuntime effects, int casterId,
                     NativeSkillSlot slot)
        {
            return loadout != null &&
                   string.Equals(loadout.Hero, "Yasuo", StringComparison.OrdinalIgnoreCase) &&
                   loadout.Knows(slot) && match != null && effects != null &&
                   effects.CanUseHeroSkill(casterId) && match.CanMove(casterId);
        }

        public bool TryQ(NativeHeroLoadout loadout, NativeMatchRules match,
                         NativeHextechRuntime effects, int casterId,
                         bool landedOnLegalTarget, out bool tornado)
        {
            tornado = false;
            if (!CanCast(loadout, match, effects, casterId, NativeSkillSlot.Q) ||
                clock < qAvailableAt) return false;
            tornado = stacks >= 2;
            // A third Q fires the tornado but does not auto-capture anything.
            if (tornado) stacks = 0;
            else if (landedOnLegalTarget)
                RegisterQHit(false);
            qAvailableAt = clock + QCooldownProvisional;
            return true;
        }

        /// <summary>
        /// Called only after a normal Q projectile physically contacts an
        /// eligible enemy. A tornado cast consumes its two prepared stacks
        /// when launched and does not re-stack on its own hit.
        /// </summary>
        public void RegisterQHit(bool tornado)
        {
            if (tornado) return;
            stacks = Mathf.Min(2, stacks + 1);
            stackExpiresAt = clock + QStackLifetimeProvisional;
        }

        /// <summary>Monsters count as dash targets but never die. The actor's
        /// caller must apply the new position; using E cannot capture.</summary>
        public bool TryEDashToMonster(NativeHeroLoadout loadout,
                                     NativeMatchRules match,
                                     NativeHextechRuntime effects,
                                     int casterId, Vector2 casterPosition,
                                     int monsterId, NativeNeutralMonsterWorld world,
                                     out Vector2 destination)
        {
            destination = default;
            if (!CanCast(loadout, match, effects, casterId, NativeSkillSlot.E) ||
                world == null || clock < eAvailableAt) return false;
            if (targetDashLocks.TryGetValue(monsterId, out float lockedUntil) &&
                lockedUntil > clock) return false;
            if (!world.TryDashLanding(monsterId, casterPosition,
                                      EDashRangeProvisional, EDashBeyondProvisional,
                                      out destination)) return false;
            targetDashLocks[monsterId] = clock + ERepeatTargetLockProvisional;
            eAvailableAt = clock + .5f;
            return true;
        }

        /// <summary>
        /// E can pass through a currently perceived opposing champion as well
        /// as a neutral camp target. The caster lands beyond the target only
        /// when both the approach and exit path are navigable. No damage,
        /// displacement, capture or target-state mutation is performed.
        /// </summary>
        public bool TryEDashThroughHero(
            NativeHeroLoadout loadout, NativeMatchRules match,
            NativeHextechRuntime effects, int casterId, Vector2 casterPosition,
            NativeSkillTarget target, RiftGrid navigation, out Vector2 destination)
        {
            destination = default;
            if (!CanCast(loadout, match, effects, casterId, NativeSkillSlot.E) ||
                navigation == null || clock < eAvailableAt ||
                target.Id == casterId || target.Eliminated ||
                !target.VisibleToCaster ||
                target.Role == match.Find(casterId).Role)
                return false;
            if (targetDashLocks.TryGetValue(target.Id, out float lockedUntil) &&
                lockedUntil > clock) return false;
            Vector2 to = target.Position - casterPosition;
            if (to.sqrMagnitude < 1f ||
                to.sqrMagnitude > EDashRangeProvisional * EDashRangeProvisional ||
                !navigation.LineClear(casterPosition, target.Position))
                return false;
            Vector2 proposed = target.Position + to.normalized * EDashBeyondProvisional;
            if (!navigation.TryNearest(proposed, out destination, 3) ||
                !navigation.LineClear(target.Position, destination) ||
                !navigation.LineClear(casterPosition, destination))
            {
                destination = default;
                return false;
            }
            targetDashLocks[target.Id] = clock + ERepeatTargetLockProvisional;
            eAvailableAt = clock + .5f;
            return true;
        }
    }
}
