using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace HextechSeekers.Native.Tests
{
    public sealed class RiftGridTests
    {
        static byte[] Fixture()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++)
            {
                raw[i] = 1;
                raw[RiftGrid.BushOffset + i] = byte.MaxValue; // Real Map11 no-bush sentinel
            }
            return raw;
        }
        static Vector2 Cell(RiftGrid grid, int x, int y) => grid.Center(x, y);
        [Test]
        public void RejectsTruncatedOriginalNavigation()
        {
            Assert.Throws<InvalidDataException>(() => new RiftGrid(new byte[10]));
        }
        [Test]
        public void ReadsLittleEndianNativeHeightAndBushId()
        {
            var b = Fixture();
            int i = 200 * RiftGrid.Width + 200;
            int height = RiftGrid.Width * RiftGrid.Height + i * 2;
            b[height] = 0x34; b[height + 1] = 0x12;
            b[RiftGrid.BushOffset + i] = 9;
            var nav = new RiftGrid(b);
            var p = Cell(nav, 200, 200);
            Assert.AreEqual((short)0x1234, nav.HeightAt(p));
            Assert.AreEqual(10, nav.BushAt(p)); // zero-based stored group 9 -> runtime group 10
        }
        [Test]
        public void OriginalMap11NoBushSentinelAndZeroBasedGroupZeroAreDistinct()
        {
            var raw = Fixture();
            int first = 200 * RiftGrid.Width + 205;
            int last = 200 * RiftGrid.Width + 206;
            raw[RiftGrid.BushOffset + first] = 0;
            raw[RiftGrid.BushOffset + last] = 38;
            var nav = new RiftGrid(raw);
            Vector2 observer = Cell(nav, 200, 200);
            Vector2 bushZero = Cell(nav, 205, 200);
            Vector2 bushThirtyEight = Cell(nav, 206, 200);
            Assert.AreEqual(0, nav.BushAt(observer),
                "Original 255 means outside all bushes");
            Assert.AreEqual(1, nav.BushAt(bushZero),
                "Stored bush group zero is a real bush");
            Assert.AreEqual(39, nav.BushAt(bushThirtyEight));
            Assert.IsFalse(NativeSight.Visible(nav, observer, bushZero),
                "A real bush beyond reveal distance must conceal its target");
            Assert.IsTrue(NativeSight.Visible(nav, Cell(nav, 204, 200), bushZero),
                "A bush target within 120 units must be revealed");
        }

        [Test]
        public void DirectPathRemainsOnWalkableCells()
        {
            var nav = new RiftGrid(Fixture());
            var a = Cell(nav, 200, 200);
            var b = Cell(nav, 215, 210);
            var path = new List<Vector2>();
            Assert.IsTrue(nav.TryPath(a, b, path));
            Assert.IsTrue(path.Count > 0);
            Assert.IsTrue(nav.CanWalk(path[path.Count - 1]));
            Assert.IsTrue(nav.LineClear(a, path[0]));
        }
        [Test]
        public void WallCellsForbidDirectTraversal()
        {
            var b = Fixture();
            b[200 * RiftGrid.Width + 208] = 0;
            var nav = new RiftGrid(b);
            Assert.IsFalse(nav.LineClear(Cell(nav, 200, 200), Cell(nav, 220, 200)));
            var path = new List<Vector2>();
            Assert.IsTrue(nav.TryPath(Cell(nav, 200, 200), Cell(nav, 220, 200), path));
            Assert.IsTrue(path.Count > 1);
        }
    }
}