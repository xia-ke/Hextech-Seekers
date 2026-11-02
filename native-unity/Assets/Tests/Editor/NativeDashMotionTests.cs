using System;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeDashMotionTests
    {
        static RiftGrid Walkable()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) raw[i] = 1;
            return new RiftGrid(raw);
        }

        [Test]
        public void DashProgressesThroughTheWorldRatherThanTeleporting()
        {
            var nav = Walkable();
            Vector2 origin = nav.Center(200, 200);
            Vector2 end = nav.Center(205, 200);
            var dash = new NativeDashMotion();
            Assert.IsTrue(dash.Begin(nav, origin, end));
            Assert.IsTrue(dash.Active);
            Assert.AreEqual(end, dash.Destination);
            Vector2 middle = dash.Step(.1f);
            Assert.Greater(middle.x, origin.x);
            Assert.Less(middle.x, end.x);
            Assert.IsTrue(nav.CanWalk(middle));
            Assert.IsTrue(dash.Active);
            Vector2 landed = dash.Step(.1f);
            Assert.AreEqual(end.x, landed.x, .0001f);
            Assert.IsFalse(dash.Active);
        }

        [Test]
        public void DashNeverCrossesObstacleEvenWhenEndpointIsWalkable()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) raw[i] = 1;
            for (int y = 190; y <= 210; y++) raw[y * RiftGrid.Width + 204] = 0;
            var nav = new RiftGrid(raw);
            var dash = new NativeDashMotion();
            Assert.IsFalse(dash.Begin(nav, nav.Center(200, 200),
                                      nav.Center(207, 200)));
            Assert.IsFalse(dash.Active);
        }

        [Test]
        public void CompletedDashNeverOvershootsItsLegalLanding()
        {
            var nav = Walkable();
            var start = nav.Center(200, 200);
            var finish = nav.Center(207, 200);
            var dash = new NativeDashMotion();
            Assert.IsTrue(dash.Begin(nav, start, finish, .15f));
            Assert.AreEqual(finish, dash.Step(10f));
            Assert.IsFalse(dash.Active);
        }

        [Test]
        public void ActiveDashCannotBeRearmedUntilFinishedOrCancelled()
        {
            var nav = Walkable();
            var a = nav.Center(200, 200);
            var b = nav.Center(203, 200);
            var c = nav.Center(206, 200);
            var dash = new NativeDashMotion();
            Assert.IsTrue(dash.Begin(nav, a, b));
            Assert.IsFalse(dash.Begin(nav, a, c));
            dash.Cancel();
            Assert.IsFalse(dash.Active);
            Assert.IsTrue(dash.Begin(nav, a, c));
            Assert.AreEqual(c, dash.Step(1f));
        }

        [Test]
        public void UnsafeParametersAndNonFiniteTickAreRejected()
        {
            var nav = Walkable();
            var a = nav.Center(200, 200);
            var b = nav.Center(205, 200);
            var dash = new NativeDashMotion();
            Assert.IsFalse(dash.Begin(nav, a, b, 0));
            Assert.IsFalse(dash.Begin(nav, a, b, float.NaN));
            Assert.IsFalse(dash.Begin(nav, a, a));
            Assert.IsFalse(dash.Begin(nav, a, new Vector2(999999, 999999)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => dash.Step(float.PositiveInfinity));
        }
    }
}
