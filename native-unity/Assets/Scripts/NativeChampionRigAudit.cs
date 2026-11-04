using System;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Mechanical integrity checks for imported ORIGINAL skinned meshes. These
    /// are deliberately distinct from visual/artistic acceptance: an actor can
    /// be technically valid yet have the wrong scale, yaw or appearance.
    /// </summary>
    public static class NativeChampionRigAudit
    {
        [Serializable]
        public sealed class GeometryReport
        {
            public int vertices, bones, triangles;
            public float localWidth, localHeight, localDepth;
            public string materialShader;
        }

        public static bool UsableBounds(Bounds bounds)
        {
            Vector3 c = bounds.center, s = bounds.size;
            return Finite(c.x) && Finite(c.y) && Finite(c.z) &&
                   Finite(s.x) && Finite(s.y) && Finite(s.z) &&
                   s.x > .0001f && s.y > .0001f && s.z > .0001f;
        }

        static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        public static GeometryReport Inspect(GarenRigRuntime rig, string expectedHero)
        {
            if (!rig || !string.Equals(rig.HeroName, expectedHero,
                                        StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Original rig identity mismatch: " + expectedHero);
            var skin = rig.GetComponent<SkinnedMeshRenderer>();
            if (!skin || !skin.enabled || !skin.gameObject.activeInHierarchy ||
                !skin.sharedMesh || !UsableBounds(skin.sharedMesh.bounds))
                throw new InvalidOperationException("Original skinned mesh absent/degenerate: " + expectedHero);
            Mesh mesh = skin.sharedMesh;
            if (mesh.vertexCount < 3 || mesh.subMeshCount < 1 ||
                mesh.boneWeights.Length != mesh.vertexCount ||
                mesh.bindposes.Length == 0 || skin.bones == null ||
                skin.bones.Length != mesh.bindposes.Length || !skin.rootBone)
                throw new InvalidOperationException("Original skin weights/bones invalid: " + expectedHero);

            int triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                triangles += (int)mesh.GetIndexCount(i) / 3;
            if (triangles < 1)
                throw new InvalidOperationException("Original mesh has no triangles: " + expectedHero);

            Material material = skin.sharedMaterial;
            if (!material || !material.shader || !material.shader.isSupported ||
                material.shader.name == "Hidden/InternalErrorShader" ||
                !material.GetTexture("_BaseMap"))
                throw new InvalidOperationException("Original hero texture/shader invalid: " + expectedHero);
            if (!rig.HasClip("run") || !rig.HasClip("attack1") ||
                !(rig.HasClip("idle1") || rig.HasClip("idle")))
                throw new InvalidOperationException("Original hero motion clips missing: " + expectedHero);

            Vector3 size = mesh.bounds.size;
            return new GeometryReport {
                vertices = mesh.vertexCount, bones = skin.bones.Length,
                triangles = triangles,
                localWidth = size.x, localHeight = size.y,
                localDepth = size.z,
                materialShader = material.shader.name
            };
        }

        public static string AssetState(bool hasSource, bool hasRig, bool hasTacticalMarker)
        {
            // A source that was staged but failed to load is not a benign skip:
            // it must block the original-art acceptance pipeline.
            if (hasSource && hasRig && !hasTacticalMarker) return "ORIGINAL_RIG";
            if (!hasSource && !hasRig && hasTacticalMarker) return "SOURCE_MISSING_MARKER";
            throw new InvalidOperationException(
                "Source/rig/marker identity inconsistent: source=" + hasSource +
                " rig=" + hasRig + " marker=" + hasTacticalMarker);
        }
    }
}
