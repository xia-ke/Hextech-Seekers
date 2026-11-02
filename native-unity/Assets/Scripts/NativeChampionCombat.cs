using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Minimal native skill contact report; an ability never directly captures.
    /// All lengths/timings here are provisional gameplay tuning values, not
    /// verified patch-specific original champion data.
    /// </summary>
    public sealed class NativeSkillImpact
    {
        public string Skill;
        public int TargetId = -1;
        public Vector2? CasterLanding;
        public Vector2? TargetLanding;
        public float LockSeconds;
        public readonly List<int> AdditionalHitTargets = new List<int>();
        public bool NeutralHit;
        public bool ProjectileBlocked;
    }

    public struct NativeSkillTarget
    {
        public int Id;
        public Vector2 Position;
        public MatchRole Role;
        public bool Eliminated;
        public bool VisibleToCaster;

        public NativeSkillTarget(int id, Vector2 position, MatchRole role,
                                 bool eliminated, bool visible)
        {
            Id = id; Position = position; Role = role;
            Eliminated = eliminated; VisibleToCaster = visible;
        }
    }

    /// <summary>
    /// World-authoritative ability crowd control. An ordinary shield does not
    /// negate a capture; only NativeHextechRuntime.Mirror may do that.
    /// </summary>
    public sealed class NativeHeroControl
    {
        readonly Dictionary<int, float> lockedUntil = new Dictionary<int, float>();
        float now;
        public void Advance(float delta)
        {
            if (delta < 0 || float.IsNaN(delta) || float.IsInfinity(delta))
                throw new ArgumentOutOfRangeException(nameof(delta));
            now += delta;
        }
        public bool Locked(int id) =>
            lockedUntil.TryGetValue(id, out float ends) && ends > now;

        public void Apply(int id, float duration)
        {
            if (duration <= 0 || float.IsNaN(duration) || float.IsInfinity(duration))
                return;
            float until = now + duration;
            if (!lockedUntil.TryGetValue(id, out float old) || old < until)
                lockedUntil[id] = until;
        }
    }

    /// <summary>
    /// Skill collision/targeting for *one* actor. The actor controller is
    /// responsible for committing destinations and playing original animations.
    /// Never consume a basic attack or score a capture from these skill events.
    /// </summary>
    public sealed class NativeChampionCombat
    {
        public const float BlitzHookRangeProvisional = 870f;
        public const float BlitzHookSpeedProvisional = 1500f;
        public const float BlitzHookRadiusProvisional = 72f;
        public const float BlitzHookCooldownProvisional = 18f;
        public const float BlitzWBoostSecondsProvisional = 4f;
        public const float BlitzWSelfSlowSecondsProvisional = 1.5f;
        public const float BlitzEReadySecondsProvisional = 5f;
        public const float RivenQDashProvisional = 210f;
        public const float RivenQComboWindowProvisional = 4f;
        public const float RivenQRecastMinimumProvisional = .25f;
        public const float RivenQCooldownProvisional = 12f;
        public const float RivenWRadiusProvisional = 260f;
        public const float RivenWStunSecondsProvisional = .75f;
        public const float RivenEDashProvisional = 240f;
        public const float YasuoWallDurationProvisional = 3.5f;
        public const float YasuoWallHalfWidthProvisional = 240f;

        readonly string hero;
        readonly int actorId;
        readonly NativeHeroLoadout loadout;
        readonly NativeMatchRules match;
        readonly NativeHextechRuntime hextech;
        readonly RiftGrid nav;
        float now, nextQ, nextW, nextE, comboExpires, rivenRecastAt;
        float blitzBoostUntil, blitzSlowUntil, blitzEExpires;
        int rivenQStage;
        bool blitzEArmed;
        bool hookActive;
        Vector2 hookStart, hookDirection;
        float hookTravel;
        public NativeWindWall WindWall { get; private set; }

        public float Elapsed => now;
        public bool HookActive => hookActive;
        public float CooldownQ => Mathf.Max(0f, nextQ - now);
        public float CooldownW => Mathf.Max(0f, nextW - now);
        public float CooldownE => Mathf.Max(0f, nextE - now);
        public float BlitzBoostRemaining => Mathf.Max(0f, blitzBoostUntil - now);
        public float BlitzSlowRemaining => Mathf.Max(0f, blitzSlowUntil - now);
        public float BlitzEArmRemaining => blitzEArmed ? Mathf.Max(0f, blitzEExpires - now) : 0f;
        public float WindWallRemaining => WindWall != null ? WindWall.Remaining(now) : 0f;
        public Vector2 HookLaunchPosition => hookStart;
        public Vector2 HookTipPosition => hookStart + hookDirection * hookTravel;
        public int RivenQStage => rivenQStage;
        public bool BlitzEArmed => blitzEArmed && blitzEExpires > now;
        public float MoveMultiplier =>
            blitzBoostUntil > now ? 1.25f :
            blitzSlowUntil > now ? .75f : 1f;

        public NativeChampionCombat(int actor, string heroKey, NativeHeroLoadout skills,
                                    NativeMatchRules rules, NativeHextechRuntime effects,
                                    RiftGrid navigation)
        {
            if (string.IsNullOrEmpty(heroKey) || skills == null || rules == null ||
                effects == null || navigation == null)
                throw new ArgumentException("A real actor, skill loadout and Rift are required");
            actorId = actor; hero = heroKey;
            loadout = skills; match = rules; hextech = effects; nav = navigation;
        }

        bool Can(NativeSkillSlot slot) =>
            loadout.Knows(slot) && match.CanMove(actorId) &&
            hextech.CanUseHeroSkill(actorId);

        public void Advance(float delta)
        {
            if (delta < 0 || float.IsNaN(delta) || float.IsInfinity(delta))
                throw new ArgumentOutOfRangeException(nameof(delta));
            now += delta;
            if (rivenQStage != 0 && now > comboExpires) rivenQStage = 0;
            if (blitzEArmed && now > blitzEExpires) blitzEArmed = false;
            if (WindWall != null && !WindWall.Active(now)) WindWall = null;
        }

        /// <summary>Only a *landed basic attack* consumes Blitz E and knocks up.
        /// The attack itself is scored by the separate capture system.</summary>
        public bool ConsumeEmpoweredBasicAttack(out float knockupSeconds)
        {
            knockupSeconds = 0;
            if (!blitzEArmed || now > blitzEExpires) return false;
            blitzEArmed = false;
            knockupSeconds = .75f;
            return true;
        }

        public bool TryBlitzHook(Vector2 origin, Vector2 facing)
        {
            if (!hero.Equals("Blitzcrank", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.Q) || now < nextQ ||
                !nav.CanWalk(origin) || facing.sqrMagnitude < .001f)
                return false;
            hookStart = origin;
            hookDirection = facing.normalized;
            hookTravel = 0;
            hookActive = true;
            nextQ = now + BlitzHookCooldownProvisional;
            return true;
        }

        /// <summary>Advance a real time-of-flight hook. The earliest reached
        /// legal enemy is hit, not an omniscient target. Walls and an active
        /// enemy Yasuo windwall stop the projectile before it can pull.</summary>
        public NativeSkillImpact StepBlitzHook(
            float delta, IReadOnlyList<NativeSkillTarget> actors, NativeWindWall wall = null)
        {
            if (!hookActive || actors == null || delta < 0) return null;
            float travelTo = Mathf.Min(BlitzHookRangeProvisional,
                                      hookTravel + BlitzHookSpeedProvisional * delta);
            Vector2 from = hookStart + hookDirection * hookTravel;
            Vector2 to = hookStart + hookDirection * travelTo;
            float distance = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (RiftGrid.CellSize * .25f)));

            for (int step = 1; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(from, to, step / (float)steps);
                if (!nav.CanWalk(point))
                {
                    hookActive = false;
                    return new NativeSkillImpact { Skill = "Blitzcrank Q", ProjectileBlocked = true };
                }
                if (wall != null && wall.Crosses(from, point, now))
                {
                    hookActive = false;
                    return new NativeSkillImpact { Skill = "Blitzcrank Q", ProjectileBlocked = true };
                }
                foreach (var actor in actors)
                {
                    if (actor.Id == actorId || actor.Eliminated ||
                        actor.Role == match.Find(actorId).Role) continue;
                    // A skillshot already in flight can collide with a hidden
                    // unit in brush. Visibility gates AI *aiming*, not physical
                    // collision. No enemy is revealed merely by this scan.
                    if (Vector2.Distance(point, actor.Position) > BlitzHookRadiusProvisional)
                        continue;
                    // Check the whole path from the initial cast. Never hook
                    // through a wall even if the enemy target was stale.
                    if (!nav.LineClear(hookStart, actor.Position)) continue;
                    hookActive = false;
                    Vector2 towardsCaster = (hookStart - actor.Position).normalized;
                    Vector2 destination = hookStart - towardsCaster * 100f;
                    if (!nav.TryNearest(destination, out var snapped, 4) ||
                        !nav.LineClear(actor.Position, snapped))
                        snapped = actor.Position;
                    return new NativeSkillImpact {
                        Skill = "Blitzcrank Q", TargetId = actor.Id,
                        TargetLanding = snapped, LockSeconds = .35f
                    };
                }
            }
            hookTravel = travelTo;
            if (hookTravel >= BlitzHookRangeProvisional) hookActive = false;
            return null;
        }

        public bool TryBlitzW()
        {
            if (!hero.Equals("Blitzcrank", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.W) || now < nextW) return false;
            nextW = now + 15f;
            blitzBoostUntil = now + BlitzWBoostSecondsProvisional;
            blitzSlowUntil = blitzBoostUntil + BlitzWSelfSlowSecondsProvisional;
            return true;
        }

        public bool TryBlitzE()
        {
            if (!hero.Equals("Blitzcrank", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.E) || now < nextE) return false;
            nextE = now + 9f;
            blitzEArmed = true;
            blitzEExpires = now + BlitzEReadySecondsProvisional;
            return true;
        }

        /// <summary>Three sequential directional Q dashes. Stage 3 may knock
        /// up enemies within the finishing radius; never captures. No generic
        /// one-button multi-dash, no movement through a Rift obstacle.</summary>
        public NativeSkillImpact TryRivenQ(Vector2 origin, Vector2 direction,
                                           IReadOnlyList<NativeSkillTarget> actors)
        {
            if (!hero.Equals("Riven", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.Q) || direction.sqrMagnitude < .001f ||
                (rivenQStage == 0 && now < nextQ) ||
                (rivenQStage > 0 && now < rivenRecastAt) ||
                !nav.CanWalk(origin)) return null;
            int upcoming = rivenQStage + 1;
            Vector2 destination = NativeSkillGeometry.WalkableDash(
                nav, origin, direction, RivenQDashProvisional);
            if ((destination - origin).sqrMagnitude < 25f) return null;
            if (rivenQStage == 0)
            {
                nextQ = now + RivenQCooldownProvisional;
                comboExpires = now + RivenQComboWindowProvisional;
            }
            rivenRecastAt = now + RivenQRecastMinimumProvisional;
            rivenQStage = upcoming == 3 ? 0 : upcoming;
            var result = new NativeSkillImpact {
                Skill = "Riven Q" + upcoming, CasterLanding = destination
            };
            if (upcoming != 3 || actors == null) return result;
            foreach (var target in actors)
            {
                if (target.Id == actorId || target.Eliminated ||
                    target.Role == match.Find(actorId).Role)
                    continue;
                if (Vector2.Distance(destination, target.Position) <= 175f &&
                    nav.LineClear(destination, target.Position))
                {
                    if (result.TargetId < 0) result.TargetId = target.Id;
                    else result.AdditionalHitTargets.Add(target.Id);
                    result.LockSeconds = .75f;
                }
            }
            return result;
        }

        public NativeSkillImpact TryRivenW(Vector2 origin, IReadOnlyList<NativeSkillTarget> actors)
        {
            if (!hero.Equals("Riven", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.W) || now < nextW || actors == null)
                return null;
            var result = new NativeSkillImpact { Skill = "Riven W" };
            foreach (var target in actors)
                if (target.Id != actorId && !target.Eliminated &&
                    target.Role != match.Find(actorId).Role &&
                    Vector2.Distance(origin, target.Position) <= RivenWRadiusProvisional &&
                    nav.LineClear(origin, target.Position))
                {
                    if (result.TargetId < 0) result.TargetId = target.Id;
                    else result.AdditionalHitTargets.Add(target.Id);
                    result.LockSeconds = RivenWStunSecondsProvisional;
                }
            // A cast with no physical target is not an impact and must not
            // burn the cooldown; the AI may still use a learned E to escape.
            if (result.TargetId < 0) return null;
            nextW = now + 11f;
            return result;
        }

        /// <summary>Riven E dash preserves ordinary shield effects only.
        /// The shield never provides immunity to a valid capture.</summary>
        public NativeSkillImpact TryRivenE(Vector2 origin, Vector2 direction)
        {
            if (!hero.Equals("Riven", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.E) || now < nextE ||
                direction.sqrMagnitude < .001f || !nav.CanWalk(origin)) return null;
            Vector2 destination = NativeSkillGeometry.WalkableDash(
                nav, origin, direction, RivenEDashProvisional);
            if ((destination - origin).sqrMagnitude < 25f) return null;
            nextE = now + 10f;
            return new NativeSkillImpact {
                Skill = "Riven E", CasterLanding = destination
            };
        }

        public bool TryYasuoWindWall(Vector2 position, Vector2 facing)
        {
            if (!hero.Equals("Yasuo", StringComparison.OrdinalIgnoreCase) ||
                !Can(NativeSkillSlot.W) || now < nextW || facing.sqrMagnitude < .001f)
                return false;
            nextW = now + 24f;
            WindWall = new NativeWindWall(position + facing.normalized * 100f,
                new Vector2(-facing.y, facing.x), YasuoWallHalfWidthProvisional,
                now + YasuoWallDurationProvisional);
            return true;
        }
    }

    public sealed class NativeWindWall
    {
        readonly Vector2 a, b;
        readonly float expireAt;
        public NativeWindWall(Vector2 center, Vector2 tangent, float halfLength, float expireTime)
        {
            Vector2 along = tangent.normalized * halfLength;
            a = center - along; b = center + along; expireAt = expireTime;
        }
        public Vector2 EndpointA => a;
        public Vector2 EndpointB => b;
        public bool Active(float at) => at <= expireAt;
        public float Remaining(float at) => Mathf.Max(0f, expireAt - at);
        public bool Crosses(Vector2 segmentA, Vector2 segmentB, float at) =>
            Active(at) && NativeSkillGeometry.SegmentsIntersect(segmentA, segmentB, a, b);
    }

    public static class NativeSkillGeometry
    {
        /// <summary>Conservative dash path stops at the first unsafe nav cell,
        /// never snaps over the other side of a wall.</summary>
        public static Vector2 WalkableDash(RiftGrid grid, Vector2 origin,
                                           Vector2 direction, float length)
        {
            if (grid == null || direction.sqrMagnitude < .001f ||
                length <= 0 || !grid.CanWalk(origin)) return origin;
            direction.Normalize();
            int count = Mathf.Max(1, Mathf.CeilToInt(length / (RiftGrid.CellSize * .2f)));
            Vector2 last = origin;
            for (int i = 1; i <= count; i++)
            {
                Vector2 next = origin + direction * (length * i / count);
                if (!grid.CanWalk(next)) break;
                last = next;
            }
            return last;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 r = b - a, s = d - c;
            float den = Cross(r, s);
            if (Mathf.Abs(den) < .00001f) return false;
            Vector2 delta = c - a;
            float t = Cross(delta, s) / den;
            float u = Cross(delta, r) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }
    }
}
