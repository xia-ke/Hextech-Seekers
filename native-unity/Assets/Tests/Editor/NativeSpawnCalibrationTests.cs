using System;
using System.Globalization;
using System.IO;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeSpawnCalibrationTests
    {
        static RiftGrid Navigation()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++) raw[i] = 1;
            return new RiftGrid(raw);
        }

        static string WriteTemp(Vector2 hunter, Vector2 hider, bool verified, string source)
        {
            string file = Path.Combine(Path.GetTempPath(),
                "hextech-spawns-" + Guid.NewGuid().ToString("N") + ".json");
            string fmt(float n) => n.ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(file,
                "{\"version\":1,\"manuallyVerified\":" + (verified ? "true" : "false") +
                ",\"source\":\"" + source + "\",\"fountainRadius\":250," +
                "\"hunter\":{\"x\":" + fmt(hunter.x) + ",\"z\":" + fmt(hunter.y) + "}," +
                "\"hider\":{\"x\":" + fmt(hider.x) + ",\"z\":" + fmt(hider.y) + "}}");
            return file;
        }

        [Test]
        public void MissingCalibrationIsAllowedButNeverCalledVerified()
        {
            Assert.IsNull(NativeSpawnCalibration.LoadVerified(
                Path.Combine(Path.GetTempPath(), "nonexistent-map11-spawns-" +
                Guid.NewGuid().ToString("N") + ".json"), Navigation()));
        }

        [Test]
        public void VerifiedSpawnsHaveWalkableCentersAndFountainBoundary()
        {
            var nav = Navigation();
            var fountain = nav.Center(210, 210);
            var spawn = nav.Center(230, 220);
            string file = WriteTemp(fountain, spawn, true, "unit-test verified synthetic map");
            try
            {
                var actual = NativeSpawnCalibration.LoadVerified(file, nav);
                Assert.IsNotNull(actual);
                Assert.IsTrue(nav.CanWalk(actual.HunterSpawn));
                Assert.IsTrue(nav.CanWalk(actual.HiderSpawn));
                Assert.IsTrue(actual.IsWithinHunterFountain(fountain));
                Assert.IsFalse(actual.IsWithinHunterFountain(spawn));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void UnverifiedProvenanceIsRejected()
        {
            var nav = Navigation();
            string file = WriteTemp(nav.Center(210, 210),
                nav.Center(230, 220), false, "unverified");
            try
            {
                Assert.Throws<InvalidDataException>(
                    () => NativeSpawnCalibration.LoadVerified(file, nav));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void EmptySourceIsRejected()
        {
            var nav = Navigation();
            string file = WriteTemp(nav.Center(210, 210),
                nav.Center(230, 220), true, "");
            try
            {
                Assert.Throws<InvalidDataException>(
                    () => NativeSpawnCalibration.LoadVerified(file, nav));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void UnrealWorldCoordinatesAreRejected()
        {
            var nav = Navigation();
            string file = WriteTemp(new Vector2(999999f, 999999f),
                nav.Center(230, 220), true, "invalid synthetic");
            try
            {
                Assert.Throws<InvalidDataException>(
                    () => NativeSpawnCalibration.LoadVerified(file, nav));
            }
            finally { File.Delete(file); }
        }
    }
}
