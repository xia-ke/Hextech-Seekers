using System;
using System.Collections.Generic;
using System.IO;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeJunglePlayAreaTests
    {
        static RiftGrid SourceCompatibleWalkableFixture()
        {
            byte[] raw = new byte[RiftGrid.BinaryBytes];
            int cells = RiftGrid.Width * RiftGrid.Height;
            for (int i = 0; i < cells; i++)
            {
                raw[i] = 1;
                // Preserve the actual Map11 semantics: 255 means no grass.
                raw[RiftGrid.BushOffset + i] = 255;
            }
            var nav = new RiftGrid(raw);
            var center = nav.Cell(8650f, -6420f);
            for (int y = center.y - 4; y <= center.y + 4; y++)
            for (int x = center.x - 4; x <= center.x + 4; x++)
                raw[RiftGrid.BushOffset + y * RiftGrid.Width + x] = 22;
            return new RiftGrid(raw);
        }

        static NativeCampCatalog OriginalCampFixture(RiftGrid nav)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "hextech-jungle-camps-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path,
                    "{\"version\":1,\"manuallyVerified\":true,\"camps\":[" +
                    "{\"name\":\"Chaos Blue\",\"x\":11031.729,\"z\":-6990.844}," +
                    "{\"name\":\"Order Red\",\"x\":7765.244,\"z\":-4020.187}," +
                    "{\"name\":\"Chaos OwlBear\",\"x\":12625.562,\"z\":-6434.189}," +
                    "{\"name\":\"Chaos Wolves\",\"x\":10979.8,\"z\":-8356.89}," +
                    "{\"name\":\"Order Wraiths\",\"x\":6969,\"z\":-5422}," +
                    "{\"name\":\"Order Small Golems\",\"x\":8390,\"z\":-2675}," +
                    "{\"name\":\"Dragon Crab\",\"x\":10500,\"z\":-5170}," +
                    "{\"name\":\"Dragon\",\"x\":9837.674,\"z\":-4397.761}]}");
                return NativeCampCatalog.LoadVerified(path, nav);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void TrueBrushCellIsUsedAsHunterWaitingPointNotFountain()
        {
            var nav = SourceCompatibleWalkableFixture();
            var area = NativeJunglePlayArea.Build(nav, OriginalCampFixture(nav));
            Assert.AreEqual(8, area.CampCount);
            Assert.Greater(area.AllowedCellCount, 1000);
            Assert.Less(area.AllowedCellCount,
                RiftGrid.Width * RiftGrid.Height);
            Assert.IsTrue(nav.CanWalk(area.HunterRiverBrush));
            Assert.AreEqual(23, nav.BushAt(area.HunterRiverBrush),
                "The source raw grass ID 22 must map to bush group 23");
            Assert.AreEqual(23, area.HunterBrushGroup);
            Assert.Less(Vector2.Distance(area.HunterRiverBrush,
                new Vector2(7500f, -7500f)), 2500f);
        }

        [Test]
        public void JungleOnlyRegionBlocksOutsideAndRetainsCampPath()
        {
            var nav = SourceCompatibleWalkableFixture();
            var area = NativeJunglePlayArea.Build(nav, OriginalCampFixture(nav));
            var outside = area.ExcludedWalkable;
            var redCamp = new Vector2(7765f, -4020f);
            Assert.IsTrue(nav.CanWalk(outside),
                "The outside point must be on the ORIGINAL navigable grid");
            nav.SetPlayArea(area.SnapshotMask());
            Assert.IsTrue(nav.IsPlayAreaRestricted);
            Assert.IsFalse(nav.CanWalk(outside));
            Assert.IsTrue(nav.CanWalk(redCamp));
            var path = new List<Vector2>();
            Assert.IsTrue(nav.TryPath(area.HunterRiverBrush, redCamp, path));
            Assert.Greater(path.Count, 0);
            foreach (Vector2 node in path) Assert.IsTrue(nav.CanWalk(node));
            nav.SetPlayArea(null);
            Assert.IsFalse(nav.IsPlayAreaRestricted);
            Assert.IsTrue(nav.CanWalk(outside),
                "The original unrestricted navigation must be recoverable");
        }

        [Test]
        public void InvisibleAirWallsSealEveryExteriorWalkableExit()
        {
            var nav = SourceCompatibleWalkableFixture();
            var area = NativeJunglePlayArea.Build(nav, OriginalCampFixture(nav));
            GameObject root = null;
            try
            {
                root = NativeJungleAirWalls.Create(nav, area, null,
                                                   out int segments, out int exits);
                var walls = root.GetComponentsInChildren<BoxCollider>();
                Assert.Greater(segments, 0);
                Assert.AreEqual(segments, walls.Length);
                Assert.AreEqual(0, root.GetComponentsInChildren<Renderer>().Length);
                var mask = area.SnapshotMask();
                int counted = 0;
                for (int y = 0; y < RiftGrid.Height; y++)
                for (int x = 0; x < RiftGrid.Width; x++)
                {
                    if (x + 1 < RiftGrid.Width &&
                        nav.OriginalCellWalkable(x, y) &&
                        nav.OriginalCellWalkable(x + 1, y) &&
                        mask[y * RiftGrid.Width + x] != mask[y * RiftGrid.Width + x + 1])
                    {
                        var at = new Vector3(RiftGrid.MinX + (x + 1) * RiftGrid.CellSize,
                                             0f, RiftGrid.MinZ + (y + .5f) * RiftGrid.CellSize);
                        Assert.IsTrue(Array.Exists(walls, wall => wall.bounds.Contains(at)),
                                      "East-west jungle exit has no collider: " + at);
                        counted++;
                    }
                    if (y + 1 < RiftGrid.Height &&
                        nav.OriginalCellWalkable(x, y) &&
                        nav.OriginalCellWalkable(x, y + 1) &&
                        mask[y * RiftGrid.Width + x] != mask[(y + 1) * RiftGrid.Width + x])
                    {
                        var at = new Vector3(RiftGrid.MinX + (x + .5f) * RiftGrid.CellSize,
                                             0f, RiftGrid.MinZ + (y + 1) * RiftGrid.CellSize);
                        Assert.IsTrue(Array.Exists(walls, wall => wall.bounds.Contains(at)),
                                      "North-south jungle exit has no collider: " + at);
                        counted++;
                    }
                }
                Assert.Greater(counted, 0);
                Assert.AreEqual(exits, counted);
                nav.SetPlayArea(area.SnapshotMask());
                Assert.IsFalse(nav.CanWalk(area.ExcludedWalkable));
                nav.SetPlayArea(null);
            }
            finally { if (root) UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void APlayAreaMaskCannotUseInvalidCellCount()
        {
            var nav = SourceCompatibleWalkableFixture();
            Assert.Throws<ArgumentException>(() => nav.SetPlayArea(new bool[4]));
            Assert.IsFalse(nav.IsPlayAreaRestricted);
        }
    }
}
