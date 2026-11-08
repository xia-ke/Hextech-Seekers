using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeRiftCameraProfileTests
    {
        [Test]
        public void DefaultSummonersRiftProjectionMatchesReferencePitchFovAndOffset()
        {
            Assert.AreEqual(40f, NativeRiftCameraProfile.FieldOfView, .001f);
            Assert.AreEqual(56f, NativeRiftCameraProfile.DefaultPitch, .001f);
            Vector3 focus = new Vector3(5300f, 0f, -7550f);
            Vector3 position = NativeRiftCameraProfile.CameraPosition(
                focus, NativeRiftCameraProfile.DefaultPitch,
                NativeRiftCameraProfile.DefaultYaw,
                NativeRiftCameraProfile.DefaultDistance);
            // The original client references approx 1289.558 units back
            // and 1911.849 units up when zoomed out. No orthographic math.
            Assert.AreEqual(0f, position.x - focus.x, 1f);
            Assert.AreEqual(1911.849f, position.y - focus.y, 3f);
            Assert.AreEqual(-1289.558f, position.z - focus.z, 3f);
            Vector3 forward = NativeRiftCameraProfile.Rotation(
                NativeRiftCameraProfile.DefaultPitch, 0f) * Vector3.forward;
            Assert.AreEqual((focus - position).normalized.x, forward.x, .001f);
            Assert.AreEqual((focus - position).normalized.y, forward.y, .001f);
            Assert.AreEqual((focus - position).normalized.z, forward.z, .001f);
        }

        [Test]
        public void CameraFocusIsFixedToWorldZeroHeightInsteadOfHeroElevation()
        {
            var navigation = new Vector2(7765f, -4020f);
            var focus = NativeRiftCameraProfile.FocusOnMapPlane(navigation);
            Assert.AreEqual(7765f, focus.x);
            Assert.AreEqual(0f, focus.y);
            Assert.AreEqual(-4020f, focus.z);
        }

        [Test]
        public void ZoomAndTiltLimitsAreFiniteAndNavigable()
        {
            Assert.AreEqual(NativeRiftCameraProfile.MinDistance,
                NativeRiftCameraProfile.ClampDistance(-100000f));
            Assert.AreEqual(NativeRiftCameraProfile.DefaultDistance,
                NativeRiftCameraProfile.ClampDistance(float.MaxValue));
            Assert.AreEqual(NativeRiftCameraProfile.MinPitch,
                NativeRiftCameraProfile.ClampPitch(-50f));
            Assert.AreEqual(NativeRiftCameraProfile.MaxPitch,
                NativeRiftCameraProfile.ClampPitch(190f));
        }

        [Test]
        public void CameraYawCalibrationRotatesScreenPanNotNavigationCoordinates()
        {
            var nav = new Vector2(7765f, -4020f);
            var world = NativeRiftCameraProfile.FocusOnMapPlane(nav);
            Assert.AreEqual(nav.x, world.x);
            Assert.AreEqual(nav.y, world.z);
            var defaultRight = NativeRiftCameraProfile.PanPlane(0f, Vector2.right);
            var rotatedRight = NativeRiftCameraProfile.PanPlane(90f, Vector2.right);
            Assert.AreEqual(1f, defaultRight.x, .001f);
            Assert.AreEqual(0f, defaultRight.y, .001f);
            Assert.AreEqual(0f, rotatedRight.x, .001f);
            Assert.AreEqual(-1f, rotatedRight.y, .001f);
            Assert.AreEqual(-45f, NativeRiftCameraProfile.WrapYaw(315f), .001f);
            Assert.AreEqual(45f, NativeRiftCameraProfile.WrapYaw(405f), .001f);
        }
    }
}
