using System;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeSkillProjectileTests
    {
        static byte[] OpenGrid()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) raw[i] = 1;
            return raw;
        }

        [Test]
        public void YasuoProjectileHasFlightTimeAndHitsAtPhysicalContact()
        {
            var nav = new RiftGrid(OpenGrid());
            var start = nav.Center(200, 200);
            var victim = nav.Center(204, 200);
            var projectile = new NativeDirectionalProjectile();
            Assert.IsTrue(projectile.Begin(2, MatchRole.Hunter, "Yasuo Q",
                start, Vector2.right, 400f, 1000f, 24f, nav));
            Assert.IsTrue(projectile.Active);
            Assert.IsNull(projectile.Step(.05f, nav,
                new[] { new NativeSkillTarget(1, victim, MatchRole.Hider, false, true) },
                null, 0f));
            Assert.IsTrue(projectile.Active);
            Assert.Greater(projectile.Position.x, start.x);
            var impact = projectile.Step(.2f, nav,
                new[] { new NativeSkillTarget(1, victim, MatchRole.Hider, false, true) },
                null, 0f);
            Assert.IsNotNull(impact);
            Assert.AreEqual(1, impact.TargetId);
            Assert.IsFalse(projectile.Active);
            Assert.GreaterOrEqual(projectile.Position.x, victim.x - 24f);
        }

        [Test]
        public void PhysicalProjectileCanBlindHitConcealedEnemyWithoutCapturing()
        {
            var nav = new RiftGrid(OpenGrid());
            var start = nav.Center(200, 200);
            var victim = nav.Center(203, 200);
            var projectile = new NativeDirectionalProjectile();
            Assert.IsTrue(projectile.Begin(2, MatchRole.Hunter, "Yasuo Q Tornado",
                start, Vector2.right, 500f, 1600f, 90f, nav));
            var impact = projectile.Step(.3f, nav,
                new[] { new NativeSkillTarget(1, victim, MatchRole.Hider, false, false) },
                null, 0f);
            Assert.IsNotNull(impact);
            Assert.AreEqual(1, impact.TargetId);
            Assert.AreEqual(0, impact.ProjectileBlocked ? 1 : 0);
        }

        [Test]
        public void ProjectileStopsAtNavigationWallBeforeReachingTarget()
        {
            var raw = OpenGrid();
            for (int y = 190; y <= 210; y++) raw[y * RiftGrid.Width + 202] = 0;
            var nav = new RiftGrid(raw);
            var start = nav.Center(200, 200);
            var victim = nav.Center(206, 200);
            var projectile = new NativeDirectionalProjectile();
            Assert.IsTrue(projectile.Begin(2, MatchRole.Hunter, "Yasuo Q",
                start, Vector2.right, 500f, 1800f, 60f, nav));
            var impact = projectile.Step(.5f, nav,
                new[] { new NativeSkillTarget(1, victim, MatchRole.Hider, false, true) },
                null, 0f);
            Assert.IsNotNull(impact);
            Assert.IsTrue(impact.ProjectileBlocked);
            Assert.AreEqual(-1, impact.TargetId);
        }

        [Test]
        public void ProjectileStopsAtActiveYasuoWindwall()
        {
            var nav = new RiftGrid(OpenGrid());
            var start = nav.Center(200, 200);
            var victim = nav.Center(207, 200);
            var wall = new NativeWindWall(nav.Center(203, 200), Vector2.up, 300f, 3f);
            var projectile = new NativeDirectionalProjectile();
            Assert.IsTrue(projectile.Begin(2, MatchRole.Hunter, "Yasuo Q",
                start, Vector2.right, 500f, 1800f, 60f, nav));
            var impact = projectile.Step(.5f, nav,
                new[] { new NativeSkillTarget(1, victim, MatchRole.Hider, false, true) },
                wall, 1f);
            Assert.IsNotNull(impact);
            Assert.IsTrue(impact.ProjectileBlocked);
            Assert.AreEqual(-1, impact.TargetId);
        }

        [Test]
        public void ProjectileRejectsInvalidParametersAndCanBeCancelled()
        {
            var nav = new RiftGrid(OpenGrid());
            var start = nav.Center(200, 200);
            var projectile = new NativeDirectionalProjectile();
            Assert.IsFalse(projectile.Begin(2, MatchRole.Hunter, "Q", start,
                Vector2.zero, 200f, 100f, 20f, nav));
            Assert.IsFalse(projectile.Begin(2, MatchRole.Hunter, "Q", start,
                Vector2.right, 0f, 100f, 20f, nav));
            Assert.IsTrue(projectile.Begin(2, MatchRole.Hunter, "Q", start,
                Vector2.right, 200f, 100f, 20f, nav));
            projectile.Cancel();
            Assert.IsFalse(projectile.Active);
            Assert.IsNull(projectile.Step(1f, nav, null, null, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                projectile.Step(float.NaN, nav, null, null, 0f));
        }
    }
}
