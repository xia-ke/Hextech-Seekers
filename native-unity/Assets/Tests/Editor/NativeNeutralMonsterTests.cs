using System;
using System.IO;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeNeutralMonsterTests
    {
        static byte[] Walkable()
        {
            var cells = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++)
                cells[i] = 1;
            return cells;
        }

        static NativeCampCatalog MakeVerifiedCatalog(RiftGrid nav, Vector2 point,
                                                     bool verified = true)
        {
            string temporary = Path.Combine(Path.GetTempPath(),
                "hextech-neutral-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string json = "{\"version\":1,\"manuallyVerified\":" +
                    (verified ? "true" : "false") +
                    ",\"camps\":[{\"name\":\"TestCamp\",\"x\":" +
                    point.x.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"z\":" +
                    point.y.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "}]}";
                File.WriteAllText(temporary, json);
                return NativeCampCatalog.LoadVerified(temporary, nav);
            }
            finally { File.Delete(temporary); }
        }

        static NativeMatchRules Round(out NativeHextechRuntime hextech)
        {
            var match = new NativeMatchRules(
                new MatchParticipant(1, "Garen", MatchRole.Hunter, true),
                new MatchParticipant(2, "Yasuo", MatchRole.Hider, false));
            hextech = new NativeHextechRuntime(match);
            match.Begin();
            return match;
        }

        [Test]
        public void MissingCampCatalogNeverInventsMonsters()
        {
            var nav = new RiftGrid(Walkable());
            var world = new NativeNeutralMonsterWorld(nav, null);
            Assert.IsFalse(world.HasVerifiedCamps);
            Assert.AreEqual(0, world.Monsters.Count);
            Assert.IsNull(world.FirstLineContact(nav.Center(200, 200),
                Vector2.right, 300, 60));
            Assert.IsNull(world.Find(1));
        }

        [Test]
        public void UnverifiedCampDataIsRejectedBeforeEntityCreation()
        {
            var nav = new RiftGrid(Walkable());
            var point = nav.Center(205, 200);
            Assert.Throws<InvalidDataException>(
                () => MakeVerifiedCatalog(nav, point, verified: false));
        }

        [Test]
        public void VerifiedCampCreatesStablePassiveIndestructibleTarget()
        {
            var nav = new RiftGrid(Walkable());
            var point = nav.Center(205, 200);
            var world = new NativeNeutralMonsterWorld(nav, MakeVerifiedCatalog(nav, point));
            Assert.AreEqual(1, world.Monsters.Count);
            var monster = world.Find(1);
            Assert.IsNotNull(monster);
            Assert.AreEqual("TestCamp", monster.CampName);
            // Camp anchors are computed through float world/grid transforms; compare with a small world-space tolerance.
            Assert.Less(Vector2.Distance(point, monster.CampPosition), .01f);
            Assert.IsTrue(monster.Alive);
            Assert.IsFalse(monster.Aggressive);
            Assert.IsNull(world.Find(2));
        }

        [Test]
        public void FirstLineContactRespectsRangeDirectionAndWalls()
        {
            var nav = new RiftGrid(Walkable());
            var source = nav.Center(200, 200);
            var destination = nav.Center(205, 200);
            var world = new NativeNeutralMonsterWorld(nav,
                MakeVerifiedCatalog(nav, destination));
            Assert.IsNotNull(world.FirstLineContact(source, Vector2.right, 300, 65));
            Assert.IsNull(world.FirstLineContact(source, Vector2.left, 300, 65));
            Assert.IsNull(world.FirstLineContact(source, Vector2.right, 100, 65));
            Assert.IsNull(world.FirstLineContact(source, Vector2.up, 300, 65));
            var wall = Walkable();
            for (int y = 190; y <= 210; y++)
                wall[y * RiftGrid.Width + 203] = 0;
            var blockedNav = new RiftGrid(wall);
            var blocked = new NativeNeutralMonsterWorld(blockedNav,
                MakeVerifiedCatalog(blockedNav, destination));
            Assert.IsNull(blocked.FirstLineContact(source, Vector2.right, 300, 65));
        }

        [Test]
        public void YasuoTwoLandedQsOnVerifiedMonsterPrepareThirdTornado()
        {
            var nav = new RiftGrid(Walkable());
            var world = new NativeNeutralMonsterWorld(nav,
                MakeVerifiedCatalog(nav, nav.Center(205, 200)));
            var hit = world.FirstLineContact(nav.Center(200, 200),
                Vector2.right, NativeYasuoSkillState.QReachProvisional,
                NativeYasuoSkillState.QHalfWidthProvisional);
            Assert.IsNotNull(hit);
            var match = Round(out var hextech);
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.Q));
            var state = new NativeYasuoSkillState();
            Assert.IsTrue(state.TryQ(loadout, match, hextech, 2, hit != null, out bool tornado));
            Assert.IsFalse(tornado);
            Assert.AreEqual(1, state.QStacks);
            Assert.IsFalse(state.TryQ(loadout, match, hextech, 2, true, out _),
                "Q cannot be spammed during cooldown");
            state.Advance(NativeYasuoSkillState.QCooldownProvisional);
            Assert.IsTrue(state.TryQ(loadout, match, hextech, 2, true, out tornado));
            Assert.IsFalse(tornado);
            Assert.AreEqual(2, state.QStacks);
            state.Advance(NativeYasuoSkillState.QCooldownProvisional);
            Assert.IsTrue(state.TryQ(loadout, match, hextech, 2, false, out tornado));
            Assert.IsTrue(tornado, "Third cast must be tornado regardless of hit");
            Assert.AreEqual(0, state.QStacks);
            Assert.IsTrue(hit.Alive, "Yasuo Q can stack without monster taking damage");
            Assert.AreEqual(0, match.CapturedCount, "Skills never directly capture");
        }

        [Test]
        public void YasuoQStackExpiresAfterItsDuration()
        {
            var match = Round(out var effects);
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            loadout.Choose(NativeSkillSlot.Q);
            var state = new NativeYasuoSkillState();
            Assert.IsTrue(state.TryQ(loadout, match, effects, 2, true, out _));
            state.Advance(NativeYasuoSkillState.QStackLifetimeProvisional + .01f);
            Assert.AreEqual(0, state.QStacks);
        }

        [Test]
        public void YasuoDashUsesRealMonsterTargetAndPreventsImmediateReuse()
        {
            var nav = new RiftGrid(Walkable());
            Vector2 source = nav.Center(200, 200);
            var world = new NativeNeutralMonsterWorld(nav,
                MakeVerifiedCatalog(nav, nav.Center(204, 200)));
            var match = Round(out var effects);
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            loadout.Choose(NativeSkillSlot.E);
            var state = new NativeYasuoSkillState();
            Assert.IsTrue(state.TryEDashToMonster(loadout, match, effects, 2,
                source, 1, world, out Vector2 landing));
            Assert.IsTrue(nav.CanWalk(landing));
            Assert.Greater(landing.x, world.Find(1).WalkableAnchor.x);
            Assert.IsFalse(state.TryEDashToMonster(loadout, match, effects, 2,
                source, 1, world, out _));
            state.Advance(NativeYasuoSkillState.ERepeatTargetLockProvisional);
            Assert.IsTrue(state.TryEDashToMonster(loadout, match, effects, 2,
                source, 1, world, out _));
        }

        [Test]
        public void YasuoHeroDashRejectsHiddenAlliedAndEliminatedTargetsWithoutSpendingCooldown()
        {
            var nav = new RiftGrid(Walkable());
            var match = Round(out var effects);
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.E));
            var state = new NativeYasuoSkillState();
            Vector2 origin = nav.Center(200, 200);
            Vector2 targetPosition = nav.Center(204, 200);
            Assert.IsFalse(state.TryEDashThroughHero(
                loadout, match, effects, 2, origin,
                new NativeSkillTarget(1, targetPosition,
                    MatchRole.Hunter, false, false), nav, out _),
                "Invisible enemy must not be automatically acquired for targeted E");
            Assert.IsFalse(state.TryEDashThroughHero(
                loadout, match, effects, 2, origin,
                new NativeSkillTarget(1, targetPosition,
                    MatchRole.Hider, false, true), nav, out _),
                "A teammate cannot be a targeted dash enemy");
            Assert.IsFalse(state.TryEDashThroughHero(
                loadout, match, effects, 2, origin,
                new NativeSkillTarget(1, targetPosition,
                    MatchRole.Hunter, true, true), nav, out _),
                "Eliminated enemy cannot be a valid skill target");
            Assert.AreEqual(0f, state.CooldownE,
                "Rejected target candidates cannot consume E cooldown");
            var visible = new NativeSkillTarget(
                1, targetPosition, MatchRole.Hunter, false, true);
            Assert.IsTrue(state.TryEDashThroughHero(
                loadout, match, effects, 2, origin, visible, nav,
                out Vector2 destination));
            Assert.IsTrue(nav.CanWalk(destination));
            Assert.IsTrue(nav.LineClear(origin, destination));
            state.Advance(.6f);
            Assert.AreEqual(0f, state.CooldownE);
            Assert.IsFalse(state.TryEDashThroughHero(
                loadout, match, effects, 2, origin, visible, nav, out _),
                "Per-target lock lasts longer than global E cooldown");
            Assert.AreEqual(0, match.CapturedCount);
        }

        [Test]
        public void YasuoNearestLegalHeroSelectionSkipsLockedOrHiddenCloserTargets()
        {
            var nav = new RiftGrid(Walkable());
            var match = new NativeMatchRules(new[] {
                new MatchParticipant(1, "Yasuo", MatchRole.Hunter, true),
                new MatchParticipant(2, "Garen", MatchRole.Hider, false),
                new MatchParticipant(3, "Riven", MatchRole.Hider, false)
            });
            var effects = new NativeHextechRuntime(match);
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hunter);
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.E));
            match.Begin();
            match.Advance(NativeMatchRules.PreparationSeconds);
            var state = new NativeYasuoSkillState();
            Vector2 origin = nav.Center(200, 200);
            var near = new NativeSkillTarget(2, nav.Center(203, 200),
                MatchRole.Hider, false, false);
            var farther = new NativeSkillTarget(3, nav.Center(205, 200),
                MatchRole.Hider, false, true);
            Assert.IsTrue(state.TryEDashThroughNearestHero(
                loadout, match, effects, 1, origin,
                new[] { near, farther }, nav,
                out Vector2 destination, out int selected));
            Assert.AreEqual(3, selected, "Nearest hidden target must not block a visible opponent");
            Assert.IsTrue(nav.LineClear(origin, destination));
            state.Advance(.6f);
            near.VisibleToCaster = true;
            Assert.IsTrue(state.TryEDashThroughNearestHero(
                loadout, match, effects, 1, origin,
                new[] { farther, near }, nav,
                out _, out selected));
            Assert.AreEqual(2, selected,
                "Still-locked farther target must not suppress a newly legal closer target");
            Assert.AreEqual(0, match.CapturedCount);
        }

        [Test]
        public void YasuoEHeroAndMonsterWithSameNumericIdHaveIndependentLocks()
        {
            var nav = new RiftGrid(Walkable());
            Vector2 source = nav.Center(200, 200);
            var world = new NativeNeutralMonsterWorld(nav,
                MakeVerifiedCatalog(nav, nav.Center(204, 200)));
            var match = Round(out var effects);
            var loadout = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            Assert.IsTrue(loadout.Choose(NativeSkillSlot.E));
            var hero = new NativeSkillTarget(1, nav.Center(206, 200),
                MatchRole.Hunter, false, true);
            Assert.AreEqual(1, world.Monsters[0].Id,
                "Regression setup must deliberately collide numeric hero and monster IDs");

            var state = new NativeYasuoSkillState();
            Assert.IsTrue(state.TryEDashToMonster(loadout, match, effects,
                2, source, 1, world, out _));
            state.Advance(.6f);
            Assert.IsTrue(state.TryEDashThroughHero(loadout, match, effects,
                2, source, hero, nav, out _),
                "Monster #1's lock must not prevent a different champion #1 dash");
            state.Advance(.6f);
            Assert.IsFalse(state.TryEDashToMonster(loadout, match, effects,
                2, source, 1, world, out _),
                "Monster target may not be reused until its own lock expires");
            Assert.IsFalse(state.TryEDashThroughHero(loadout, match, effects,
                2, source, hero, nav, out _),
                "Champion target may not be reused until its own lock expires");

            var reverse = new NativeYasuoSkillState();
            Assert.IsTrue(reverse.TryEDashThroughHero(loadout, match, effects,
                2, source, hero, nav, out _));
            reverse.Advance(.6f);
            Assert.IsTrue(reverse.TryEDashToMonster(loadout, match, effects,
                2, source, 1, world, out _),
                "Champion #1's lock must not prevent a different neutral monster #1 dash");
            Assert.AreEqual(0, match.CapturedCount,
                "Neither type of E dash directly captures an opponent");
            Assert.IsTrue(world.Find(1).Alive);
            Assert.IsFalse(world.Find(1).Aggressive);
        }

        [Test]
        public void MissingSkillAndIncorrectHeroCannotCastMonsterAbility()
        {
            var match = Round(out var effects);
            var q = new NativeYasuoSkillState();
            var unlearned = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            Assert.IsFalse(q.TryQ(unlearned, match, effects, 2, true, out _));
            var incorrect = new NativeHeroLoadout("Garen", MatchRole.Hider);
            incorrect.Choose(NativeSkillSlot.Q);
            Assert.IsFalse(q.TryQ(incorrect, match, effects, 2, true, out _));
            // Only legal hider skills may be cast during preparation.
            var legitimate = new NativeHeroLoadout("Yasuo", MatchRole.Hider);
            legitimate.Choose(NativeSkillSlot.Q);
            Assert.IsTrue(q.TryQ(legitimate, match, effects, 2, true, out _));
        }
    }
}
