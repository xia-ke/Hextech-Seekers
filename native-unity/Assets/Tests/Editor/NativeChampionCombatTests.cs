using System;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeChampionCombatTests
    {
        static byte[] RawGrid()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) raw[i] = 1;
            return raw;
        }

        static NativeMatchRules Started(string hero, MatchRole role,
                                        out NativeHextechRuntime hextech,
                                        out NativeHeroLoadout loadout)
        {
            var actors = new[] {
                new MatchParticipant(1, "Garen",
                    role == MatchRole.Hunter ? MatchRole.Hider : MatchRole.Hunter, true),
                new MatchParticipant(2, hero, role, false)
            };
            var match = new NativeMatchRules(actors);
            hextech = new NativeHextechRuntime(match);
            loadout = new NativeHeroLoadout(hero, role);
            match.Begin();
            match.Advance(120f);
            return match;
        }

        [Test]
        public void BlitzHookHasFlightTimeAndPullsOnlyFirstVisibleEnemy()
        {
            var nav = new RiftGrid(RawGrid());
            var m = Started("Blitzcrank", MatchRole.Hunter,
                out var effects, out var skills);
            Assert.IsTrue(skills.Choose(NativeSkillSlot.Q));
            var caster = nav.Center(200, 200);
            var target = nav.Center(210, 200);
            var state = new NativeChampionCombat(2, "Blitzcrank", skills, m,
                                                 effects, nav);
            var candidates = new[] {
                new NativeSkillTarget(1, target, MatchRole.Hider, false, true)
            };
            Assert.IsTrue(state.TryBlitzHook(caster, Vector2.right));
            Assert.IsTrue(state.HookActive);
            Assert.IsNull(state.StepBlitzHook(.08f, candidates));
            Assert.IsTrue(state.HookActive);
            var collision = state.StepBlitzHook(.5f, candidates);
            Assert.IsNotNull(collision);
            Assert.AreEqual(1, collision.TargetId);
            Assert.AreEqual("Blitzcrank Q", collision.Skill);
            Assert.IsTrue(collision.TargetLanding.HasValue);
            Assert.Less(Vector2.Distance(caster, collision.TargetLanding.Value), 160f);
            Assert.AreEqual(0, m.CapturedCount,
                "Hook may pull but skill hit never directly captures");
            Assert.IsFalse(state.HookActive);
        }

        [Test]
        public void BlitzHookCannotTargetHiddenEnemyEvenInsideProjectileRadius()
        {
            var nav = new RiftGrid(RawGrid());
            var m = Started("Blitzcrank", MatchRole.Hunter,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.Q);
            var state = new NativeChampionCombat(2, "Blitzcrank", skills, m,
                                                 effects, nav);
            var victim = new[] {
                new NativeSkillTarget(1, nav.Center(206, 200),
                                      MatchRole.Hider, false, false)
            };
            Assert.IsTrue(state.TryBlitzHook(nav.Center(200, 200), Vector2.right));
            Assert.IsNull(state.StepBlitzHook(1f, victim));
            Assert.IsFalse(state.HookActive);
        }

        [Test]
        public void HookIsBlockedByRealNavigationWall()
        {
            var raw = RawGrid();
            for (int y = 194; y <= 206; y++) raw[y * RiftGrid.Width + 205] = 0;
            var nav = new RiftGrid(raw);
            var m = Started("Blitzcrank", MatchRole.Hunter,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.Q);
            var state = new NativeChampionCombat(2, "Blitzcrank", skills, m,
                                                 effects, nav);
            var candidates = new[] {
                new NativeSkillTarget(1, nav.Center(210, 200),
                    MatchRole.Hider, false, true)
            };
            Assert.IsTrue(state.TryBlitzHook(nav.Center(200, 200), Vector2.right));
            var hit = state.StepBlitzHook(.6f, candidates);
            Assert.IsNotNull(hit);
            Assert.IsTrue(hit.ProjectileBlocked);
            Assert.AreEqual(-1, hit.TargetId);
            Assert.AreEqual(0, m.CapturedCount);
        }

        [Test]
        public void YasuoWallStopsBlitzProjectileBeforeItTouchesEnemy()
        {
            var nav = new RiftGrid(RawGrid());
            var m = Started("Blitzcrank", MatchRole.Hunter,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.Q);
            var combat = new NativeChampionCombat(2, "Blitzcrank", skills, m, effects, nav);
            var wall = new NativeWindWall(nav.Center(204, 200),
                Vector2.up, 300f, 2f);
            var candidates = new[] {
                new NativeSkillTarget(1, nav.Center(210, 200),
                    MatchRole.Hider, false, true)
            };
            Assert.IsTrue(combat.TryBlitzHook(nav.Center(200, 200), Vector2.right));
            var hit = combat.StepBlitzHook(.5f, candidates, wall);
            Assert.IsNotNull(hit);
            Assert.IsTrue(hit.ProjectileBlocked);
            Assert.AreEqual(0, m.CapturedCount);
        }

        [Test]
        public void RivenQHasThreeDistinctDashesAndOnlyThirdKnocksUp()
        {
            var nav = new RiftGrid(RawGrid());
            var match = Started("Riven", MatchRole.Hider,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.Q);
            var state = new NativeChampionCombat(2, "Riven", skills, match,
                                                 effects, nav);
            var at = nav.Center(200, 200);
            var enemies = new[] {
                new NativeSkillTarget(1, nav.Center(213, 200),
                    MatchRole.Hunter, false, true)
            };
            for (int i = 1; i <= 3; i++)
            {
                var cast = state.TryRivenQ(at, Vector2.right, enemies);
                Assert.IsNotNull(cast, "Q cast " + i);
                Assert.AreEqual("Riven Q" + i, cast.Skill);
                Assert.IsTrue(cast.CasterLanding.HasValue);
                Assert.Greater(cast.CasterLanding.Value.x, at.x);
                if (i < 3) Assert.AreEqual(-1, cast.TargetId);
                else
                {
                    Assert.AreEqual(1, cast.TargetId);
                    Assert.Greater(cast.LockSeconds, 0f);
                }
                at = cast.CasterLanding.Value;
            }
            Assert.AreEqual(0, state.RivenQStage);
            Assert.IsNull(state.TryRivenQ(at, Vector2.right, enemies),
                "Q's cooldown begins on the first cast");
            Assert.AreEqual(0, match.CapturedCount);
        }

        [Test]
        public void RivenDashNeverCrossesAWall()
        {
            var raw = RawGrid();
            for (int y = 190; y < 220; y++) raw[y * RiftGrid.Width + 204] = 0;
            var nav = new RiftGrid(raw);
            var origin = nav.Center(200, 200);
            var landing = NativeSkillGeometry.WalkableDash(
                nav, origin, Vector2.right, 600f);
            Assert.IsTrue(nav.CanWalk(landing));
            Assert.Less(landing.x, nav.Center(203, 200).x);
        }

        [Test]
        public void RivenWStunAndEDashRequireIndividuallyLearnedSkills()
        {
            var nav = new RiftGrid(RawGrid());
            var match = Started("Riven", MatchRole.Hider,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.W);
            var state = new NativeChampionCombat(2, "Riven", skills, match,
                                                 effects, nav);
            var at = nav.Center(200, 200);
            var actors = new[] {
                new NativeSkillTarget(1, nav.Center(202, 200),
                                      MatchRole.Hunter, false, true)
            };
            var impact = state.TryRivenW(at, actors);
            Assert.IsNotNull(impact);
            Assert.AreEqual(1, impact.TargetId);
            Assert.AreEqual(NativeChampionCombat.RivenWStunSecondsProvisional,
                            impact.LockSeconds);
            Assert.IsNull(state.TryRivenE(at, Vector2.right),
                "E is not a free skill when W was chosen");
            Assert.AreEqual(0, match.CapturedCount);
        }

        [Test]
        public void BlitzWBoostThenSelfSlowAndBlitzERequiresHunterSkillSlot()
        {
            var nav = new RiftGrid(RawGrid());
            var match = Started("Blitzcrank", MatchRole.Hunter,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.W);
            var state = new NativeChampionCombat(2, "Blitzcrank", skills,
                                                 match, effects, nav);
            Assert.IsTrue(state.TryBlitzW());
            Assert.Greater(state.MoveMultiplier, 1);
            Assert.IsFalse(state.TryBlitzE());
            state.Advance(4.1f);
            Assert.Less(state.MoveMultiplier, 1);
            state.Advance(2f);
            Assert.AreEqual(1f, state.MoveMultiplier);
        }

        [Test]
        public void BlitzEmpoweredAttackIsNotTheSkillHitOrAutomaticCapture()
        {
            var nav = new RiftGrid(RawGrid());
            var match = Started("Blitzcrank", MatchRole.Hunter,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.E);
            var state = new NativeChampionCombat(2, "Blitzcrank", skills,
                                                 match, effects, nav);
            Assert.IsTrue(state.TryBlitzE());
            Assert.IsTrue(state.BlitzEArmed);
            Assert.IsTrue(state.ConsumeEmpoweredBasicAttack(out float knockup));
            Assert.Greater(knockup, 0);
            Assert.IsFalse(state.ConsumeEmpoweredBasicAttack(out _));
            Assert.AreEqual(0, match.CapturedCount,
                "Ability arm and control do not perform a basic hit automatically");
        }

        [Test]
        public void HeroStunPreventsMovesAndExpiresWithoutDamage()
        {
            var state = new NativeHeroControl();
            state.Apply(2, 1.5f);
            Assert.IsTrue(state.Locked(2));
            Assert.IsFalse(state.Locked(3));
            state.Advance(1.4f);
            Assert.IsTrue(state.Locked(2));
            state.Advance(.2f);
            Assert.IsFalse(state.Locked(2));
        }

        [Test]
        public void RivenWControlsAllVisibleEnemiesInsideRadiusButNeverCaptures()
        {
            var nav = new RiftGrid(RawGrid());
            var match = new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hider, true),
                new MatchParticipant(2, "Riven", MatchRole.Hunter, false),
                new MatchParticipant(3, "Yasuo", MatchRole.Hider, false));
            var effects = new NativeHextechRuntime(match);
            var skills = new NativeHeroLoadout("Riven", MatchRole.Hunter);
            Assert.IsTrue(skills.Choose(NativeSkillSlot.W));
            match.Begin();
            match.Advance(120f);
            var at = nav.Center(200, 200);
            var enemies = new[] {
                new NativeSkillTarget(1, nav.Center(201, 200),
                                      MatchRole.Hider, false, true),
                new NativeSkillTarget(3, nav.Center(200, 202),
                                      MatchRole.Hider, false, true)
            };
            var state = new NativeChampionCombat(2, "Riven", skills, match,
                                                 effects, nav);
            var impact = state.TryRivenW(at, enemies);
            Assert.IsNotNull(impact);
            Assert.AreEqual(1, impact.TargetId);
            CollectionAssert.Contains(impact.AdditionalHitTargets, 3);
            Assert.AreEqual(0, match.CapturedCount);
        }

        [Test]
        public void RigLoaderNeverCallsAnUnverifiedHeroSourceAnOriginalModel()
        {
            Assert.IsTrue(GarenRigRuntime.IsSupportedHero("Riven"));
            Assert.IsTrue(GarenRigRuntime.IsSupportedHero("Blitzcrank"));
            Assert.IsTrue(GarenRigRuntime.IsSupportedHero("Yasuo"));
            Assert.IsFalse(GarenRigRuntime.IsSupportedHero("FakeChampion"));
            string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "no-hextech-hero-" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(GarenRigRuntime.SourceFilesPresent(missing, "Riven"));
            Assert.IsFalse(GarenRigRuntime.SourceFilesPresent(missing, "FakeChampion"));
        }

        [Test]
        public void RivenComboWindowExpiresIfNotRepeated()
        {
            var nav = new RiftGrid(RawGrid());
            var match = Started("Riven", MatchRole.Hider,
                out var effects, out var skills);
            skills.Choose(NativeSkillSlot.Q);
            var state = new NativeChampionCombat(2, "Riven", skills,
                                                 match, effects, nav);
            var first = state.TryRivenQ(nav.Center(200, 200), Vector2.right, null);
            Assert.IsNotNull(first);
            Assert.AreEqual(1, state.RivenQStage);
            state.Advance(NativeChampionCombat.RivenQComboWindowProvisional + .01f);
            Assert.AreEqual(0, state.RivenQStage);
            Assert.IsNull(state.TryRivenQ(nav.Center(205, 200), Vector2.right, null),
                "The full Q cooldown still applies after combo expiration");
        }
    }
}
