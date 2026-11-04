using System;
using HextechSeekers.Native;
using NUnit.Framework;
using UnityEngine;

namespace HextechSeekers.Native.Tests
{
    public sealed class NativeChampionRigAuditTests
    {
        [Test]
        public void SourceAndActualRigMustAgreeBeforeOriginalArtClaim()
        {
            Assert.AreEqual("ORIGINAL_RIG",
                NativeChampionRigAudit.AssetState(true, true, false));
            Assert.AreEqual("SOURCE_MISSING_MARKER",
                NativeChampionRigAudit.AssetState(false, false, true));
        }

        [Test]
        public void MissingStagedRigCannotSilentlyBecomeTacticalPass()
        {
            Assert.Throws<InvalidOperationException>(() =>
                NativeChampionRigAudit.AssetState(true, false, true));
            Assert.Throws<InvalidOperationException>(() =>
                NativeChampionRigAudit.AssetState(true, false, false));
            Assert.Throws<InvalidOperationException>(() =>
                NativeChampionRigAudit.AssetState(true, true, true));
        }

        [Test]
        public void MissingSourceCannotMasqueradeAsOriginalHero()
        {
            Assert.Throws<InvalidOperationException>(() =>
                NativeChampionRigAudit.AssetState(false, true, false));
            Assert.Throws<InvalidOperationException>(() =>
                NativeChampionRigAudit.AssetState(false, true, true));
            Assert.Throws<InvalidOperationException>(() =>
                NativeChampionRigAudit.AssetState(false, false, false));
        }

        [Test]
        public void ZeroOrFlatMeshBoundsAreNotAcceptableForOriginalSkinnedModels()
        {
            Assert.IsFalse(NativeChampionRigAudit.UsableBounds(
                new Bounds(Vector3.zero, Vector3.zero)));
            Assert.IsFalse(NativeChampionRigAudit.UsableBounds(
                new Bounds(Vector3.zero, new Vector3(10, 20, 0))));
            Assert.IsFalse(NativeChampionRigAudit.UsableBounds(
                new Bounds(Vector3.zero, new Vector3(.00001f, 10, 10))));
        }

        [Test]
        public void PositiveFiniteMeshBoundsAreAcceptedWithoutInventedChampionScale()
        {
            Assert.IsTrue(NativeChampionRigAudit.UsableBounds(
                new Bounds(new Vector3(1000, 10, -800), new Vector3(30, 120, 40))));
            Assert.IsTrue(NativeChampionRigAudit.UsableBounds(
                new Bounds(Vector3.zero, new Vector3(4, 12, 4))));
            // Relative hero scale is recorded for a visual reviewer, not
            // automatically normalized to some fictitious universal height.
        }

        [Test]
        public void NonFiniteGeometryIsRejected()
        {
            var invalid = new Bounds(Vector3.zero, Vector3.one);
            invalid.size = new Vector3(float.PositiveInfinity, 10, 10);
            Assert.IsFalse(NativeChampionRigAudit.UsableBounds(invalid));
            invalid.size = new Vector3(float.NaN, 10, 10);
            Assert.IsFalse(NativeChampionRigAudit.UsableBounds(invalid));
        }

        [Test]
        public void UninitializedHeroComponentIsNotAnOriginalMesh()
        {
            var root = new GameObject("Phase14 Synthetic Rig Test");
            try
            {
                var rig = root.AddComponent<GarenRigRuntime>();
                Assert.Throws<InvalidOperationException>(() =>
                    NativeChampionRigAudit.Inspect(rig, "Garen"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
