using System;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeVisionGridTests
    {
        static byte[] AllWalkable()
        {
            var raw = new byte[RiftGrid.BinaryBytes];
            for (int i = 0; i < RiftGrid.Width * RiftGrid.Height; i++)
            {
                raw[i] = 1;
                raw[RiftGrid.BushOffset + i] = byte.MaxValue;
            }
            return raw;
        }

        [Test]
        public void FogBeginsUnknownAndRevealsOnlyCurrentPersonalRadius()
        {
            var nav = new RiftGrid(AllWalkable());
            var sight = new NativeVisionGrid(nav);
            Vector2 focus = nav.Center(210, 210);
            Vector2 far = nav.Center(255, 210);
            Assert.AreEqual(NativeVisionGrid.UnknownShade, sight.ShadeAt(210, 210));
            Assert.IsFalse(sight.WasExplored(far));
            sight.Reveal(focus);
            Assert.AreEqual(NativeVisionGrid.VisibleShade, sight.ShadeAt(210, 210));
            Assert.IsFalse(sight.IsCurrentlyVisible(far));
            Assert.AreEqual(NativeVisionGrid.UnknownShade, sight.ShadeAt(255, 210));
            Assert.Greater(sight.VisibleCells, 0);
        }

        [Test]
        public void ExploredTerrainRemainsDimAfterObserverMoves()
        {
            var nav = new RiftGrid(AllWalkable());
            var sight = new NativeVisionGrid(nav);
            var start = nav.Center(210, 210);
            var later = nav.Center(255, 210);
            sight.Reveal(start);
            Assert.IsTrue(sight.WasExplored(start));
            sight.Reveal(later);
            Assert.AreEqual(NativeVisionGrid.ExploredShade, sight.ShadeAt(210, 210));
            Assert.IsTrue(sight.WasExplored(start));
            Assert.IsTrue(sight.IsCurrentlyVisible(later));
            sight.ClearActiveSight();
            Assert.AreEqual(NativeVisionGrid.ExploredShade, sight.ShadeAt(255, 210));
            sight.Reset();
            Assert.IsFalse(sight.WasExplored(start));
            Assert.AreEqual(NativeVisionGrid.UnknownShade, sight.ShadeAt(255, 210));
        }

        [Test]
        public void StoneWallBlocksFogRayButItsNearFaceCanBeSeen()
        {
            var raw = AllWalkable();
            for (int y = 190; y <= 230; y++)
                raw[y * RiftGrid.Width + 213] = 0;
            var nav = new RiftGrid(raw);
            var sight = new NativeVisionGrid(nav);
            sight.Reveal(nav.Center(207, 210));
            Assert.AreEqual(NativeVisionGrid.VisibleShade, sight.ShadeAt(211, 210));
            Assert.AreEqual(NativeVisionGrid.UnknownShade, sight.ShadeAt(224, 210));
        }

        [Test]
        public void BrushConcealsChampionButNotTheTerrainItself()
        {
            var raw = AllWalkable();
            int target = 210 * RiftGrid.Width + 219;
            raw[RiftGrid.BushOffset + target] = 5;
            var nav = new RiftGrid(raw);
            var observer = nav.Center(210, 210);
            var insideGrass = nav.Center(219, 210);
            var fog = new NativeVisionGrid(nav);
            fog.Reveal(observer);
            Assert.IsTrue(fog.IsCurrentlyVisible(insideGrass),
                "The bush floor remains visible; only enemy characters are hidden");
            Assert.IsFalse(NativeSight.Visible(nav, observer, insideGrass),
                "An enemy in grass outside reveal proximity remains concealed");
        }

        [Test]
        public void TwoHidersNeverSharePreviouslyExploredOrCurrentVision()
        {
            var nav = new RiftGrid(AllWalkable());
            var first = new NativeVisionGrid(nav);
            var second = new NativeVisionGrid(nav);
            var a = nav.Center(210, 210);
            var b = nav.Center(250, 210);
            first.Reveal(a);
            second.Reveal(b);
            Assert.IsTrue(first.IsCurrentlyVisible(a));
            Assert.IsFalse(second.WasExplored(a));
            Assert.IsTrue(second.IsCurrentlyVisible(b));
            Assert.IsFalse(first.WasExplored(b));
        }

        [Test]
        public void FogPixelsMatchGridAndRejectWrongTextureBuffer()
        {
            var nav = new RiftGrid(AllWalkable());
            var sight = new NativeVisionGrid(nav);
            var pixels = new Color32[RiftGrid.Width * RiftGrid.Height];
            Assert.Throws<ArgumentException>(() => sight.CopyFogColors(new Color32[1]));
            sight.Reveal(nav.Center(210, 210));
            sight.CopyFogColors(pixels);
            Assert.AreEqual(0, pixels[210 * RiftGrid.Width + 210].r);
            Assert.AreEqual(238, pixels[210 * RiftGrid.Width + 265].r);
            Assert.AreEqual(255, pixels[210 * RiftGrid.Width + 210].a);
        }
    }
}
