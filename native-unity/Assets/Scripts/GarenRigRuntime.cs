using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Phase 2: a real skinned Garen mesh reconstructed from the repository's
    /// verified native geometry/animation buffers. No 2D atlases or stand-ins.
    /// The source bones are row-major world-bind matrices, including some older
    /// meshes without explicit local matrices.
    /// </summary>
    public sealed class GarenRigRuntime : MonoBehaviour
    {
        [Serializable] public sealed class BufferView { public int offset; public int count; public string type; }
        [Serializable] public sealed class Geometry {
            public BufferView position, normal, uv, skinIndex, skinWeight, index;
        }
        [Serializable] public sealed class Bone {
            public string name; public int parent; public float scale;
            public float[] bind, local;
        }
        [Serializable] public sealed class MeshGroup {
            public string name; public int vertexStart, vertexCount, start, count;
        }
        [Serializable] public sealed class Track {
            public int bone;
            public BufferView times, position, quaternion, scale;
        }
        [Serializable] public sealed class MotionClip {
            public string name; public float duration; public int fps;
            public Track[] tracks;
        }
        [Serializable] public sealed class Manifest {
            public int version; public string hero, texture, binary;
            public Geometry geometry; public MeshGroup[] groups;
            public Bone[] bones; public MotionClip[] clips;
        }

        Manifest source;
        byte[] raw;
        Transform[] skeleton;
        Vector3[] defaultPositions;
        Quaternion[] defaultRotations;
        Vector3[] defaultScales;
        Dictionary<string, MotionClip> motions;
        MotionClip current;
        float time;
        bool attacking;
        Mesh builtMesh;
        Material builtMaterial;
        Texture2D builtTexture;

        public string CurrentClip => current != null ? current.name : string.Empty;
        public bool IsAttacking => attacking;

        static float[] ReadFloats(byte[] data, BufferView view)
        {
            if (view == null || view.type != "f" || view.count < 0 ||
                view.offset < 0 || (long)view.offset + (long)view.count * 4 > data.Length)
                throw new InvalidDataException("Invalid float buffer view");
            var values = new float[view.count];
            for (int i = 0; i < values.Length; i++)
                values[i] = BitConverter.ToSingle(data, view.offset + i * 4);
            return values;
        }
        static ushort[] ReadU16(byte[] data, BufferView view)
        {
            if (view == null || view.type != "H" || view.count < 0 ||
                view.offset < 0 || (long)view.offset + (long)view.count * 2 > data.Length)
                throw new InvalidDataException("Invalid joint/index buffer view");
            var values = new ushort[view.count];
            for (int i = 0; i < values.Length; i++)
                values[i] = BitConverter.ToUInt16(data, view.offset + i * 2);
            return values;
        }
        static Vector3[] Vec3(float[] source)
        {
            if (source.Length % 3 != 0) throw new InvalidDataException("Invalid vec3 count");
            var value = new Vector3[source.Length / 3];
            for (int i = 0; i < value.Length; i++)
                value[i] = new Vector3(source[3 * i], source[3 * i + 1], source[3 * i + 2]);
            return value;
        }
        static Matrix4x4 Matrix(float[] data)
        {
            if (data == null || data.Length != 16) throw new InvalidDataException("Invalid bone bind matrix");
            var matrix = new Matrix4x4();
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    matrix[row, col] = data[4 * row + col];
            return matrix;
        }
        static Quaternion Rotation(Matrix4x4 matrix)
        {
            return Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
        }
        static string ChooseClip(Dictionary<string, MotionClip> clips, params string[] names)
        {
            foreach (string name in names) if (clips.ContainsKey(name)) return name;
            throw new InvalidDataException("Missing native animation clip: " + string.Join("/", names));
        }

        public static GarenRigRuntime Load(Transform parent, string nativeFolder)
        {
            string manifestPath = Path.Combine(nativeFolder, "garen.json");
            if (!File.Exists(manifestPath)) throw new FileNotFoundException("Garen mesh manifest missing", manifestPath);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.hero != "Garen" ||
                manifest.geometry == null || manifest.bones == null || manifest.bones.Length == 0)
                throw new InvalidDataException("Invalid verified original Garen mesh manifest");
            string geometryPath = Path.Combine(nativeFolder, "garen.bin");
            string texturePath = Path.Combine(nativeFolder, manifest.texture);
            if (!File.Exists(geometryPath) || !File.Exists(texturePath))
                throw new FileNotFoundException("Garen mesh buffer/texture absent. Run prepare_assets.py");
            var go = new GameObject("ORIGINAL_GAREN_SKINNED_3D");
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<GarenRigRuntime>();
            renderer.Build(manifest, File.ReadAllBytes(geometryPath), File.ReadAllBytes(texturePath));
            return renderer;
        }

        void Build(Manifest model, byte[] geometryBuffer, byte[] png)
        {
            source = model; raw = geometryBuffer;
            var geo = model.geometry;
            Vector3[] positions = Vec3(ReadFloats(raw, geo.position));
            Vector3[] normals = Vec3(ReadFloats(raw, geo.normal));
            float[] uvFlat = ReadFloats(raw, geo.uv);
            var joints = ReadU16(raw, geo.skinIndex);
            float[] weights = ReadFloats(raw, geo.skinWeight);
            var indices = ReadU16(raw, geo.index);
            int count = positions.Length;
            if (uvFlat.Length != count * 2 || normals.Length != count ||
                joints.Length != count * 4 || weights.Length != count * 4 ||
                model.groups == null || model.groups.Length == 0)
                throw new InvalidDataException("Garen geometry attribute count mismatch");

            var boneWorld = new Matrix4x4[model.bones.Length];
            skeleton = new Transform[model.bones.Length];
            defaultPositions = new Vector3[skeleton.Length];
            defaultRotations = new Quaternion[skeleton.Length];
            defaultScales = new Vector3[skeleton.Length];
            for (int i = 0; i < skeleton.Length; i++)
            {
                var b = model.bones[i];
                if (b.parent >= i || b.parent < -1) throw new InvalidDataException("Invalid bone tree");
                boneWorld[i] = Matrix(b.bind);
                Matrix4x4 rest = b.local != null && b.local.Length == 16
                    ? Matrix(b.local)
                    : b.parent < 0 ? boneWorld[i] : boneWorld[b.parent].inverse * boneWorld[i];
                var bone = new GameObject("bone_" + i + "_" + b.name).transform;
                bone.SetParent(b.parent < 0 ? transform : skeleton[b.parent], false);
                bone.localPosition = rest.GetColumn(3);
                bone.localRotation = Rotation(rest);
                bone.localScale = rest.lossyScale;
                skeleton[i] = bone;
                defaultPositions[i] = bone.localPosition;
                defaultRotations[i] = bone.localRotation;
                defaultScales[i] = bone.localScale;
            }
            var boneWeights = new BoneWeight[count];
            for (int i = 0; i < count; i++)
            {
                int offset = 4 * i;
                for (int k = 0; k < 4; k++)
                    if (joints[offset + k] >= skeleton.Length)
                        throw new InvalidDataException("Vertex references missing joint");
                boneWeights[i] = new BoneWeight {
                    boneIndex0 = joints[offset], boneIndex1 = joints[offset + 1],
                    boneIndex2 = joints[offset + 2], boneIndex3 = joints[offset + 3],
                    weight0 = weights[offset], weight1 = weights[offset + 1],
                    weight2 = weights[offset + 2], weight3 = weights[offset + 3]
                };
            }
            var uv = new Vector2[count];
            for (int i = 0; i < count; i++) uv[i] = new Vector2(uvFlat[2 * i], 1f - uvFlat[2 * i + 1]);
            builtMesh = new Mesh { name = "Original Garen skinned mesh" };
            builtMesh.indexFormat = count > 65535 ?
                UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            builtMesh.vertices = positions;
            builtMesh.normals = normals;
            builtMesh.uv = uv;
            builtMesh.boneWeights = boneWeights;
            var bindposes = new Matrix4x4[skeleton.Length];
            for (int i = 0; i < skeleton.Length; i++)
                bindposes[i] = skeleton[i].worldToLocalMatrix * transform.localToWorldMatrix;
            builtMesh.bindposes = bindposes;
            builtMesh.subMeshCount = model.groups.Length;
            for (int i = 0; i < model.groups.Length; i++)
            {
                var group = model.groups[i];
                if (group.start < 0 || group.count <= 0 || group.count % 3 != 0 ||
                    (long)group.start + group.count > indices.Length)
                    throw new InvalidDataException("Invalid mesh index group");
                var tri = new int[group.count];
                for (int k = 0; k < tri.Length; k++) tri[k] = indices[group.start + k];
                builtMesh.SetTriangles(tri, i, false);
            }
            builtMesh.RecalculateBounds();
            builtTexture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!builtTexture.LoadImage(png)) throw new InvalidDataException("Garen original texture decode failed");
            builtMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            builtMaterial.name = "Original Garen diffuse";
            builtMaterial.SetTexture("_BaseMap", builtTexture);
            builtMaterial.SetFloat("_Cull", 0f); // initial handedness diagnostic; needs Unity visual confirmation
            var skinned = gameObject.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = builtMesh;
            skinned.sharedMaterial = builtMaterial;
            skinned.bones = skeleton;
            skinned.rootBone = skeleton[0];
            skinned.localBounds = new Bounds(new Vector3(0, 110, 0), new Vector3(500, 440, 500));
            skinned.updateWhenOffscreen = false;
            motions = new Dictionary<string, MotionClip>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in model.clips)
                if (clip != null && !string.IsNullOrEmpty(clip.name)) motions[clip.name] = clip;
            ChangeClip(ChooseClip(motions, "idle1", "idle"));
            ApplyFrame();
            Debug.Log($"Native Garen rig: {count} vertices, {indices.Length / 3} triangles, {skeleton.Length} bones, {motions.Count} clips.");
        }

        void ChangeClip(string name)
        {
            if (current != null && string.Equals(current.name, name, StringComparison.OrdinalIgnoreCase)) return;
            if (!motions.TryGetValue(name, out current)) throw new InvalidDataException("Missing Garen clip: " + name);
            time = 0;
        }

        public void PlayAttack()
        {
            if (attacking) return;
            string name = ChooseClip(motions, "attack1");
            ChangeClip(name);
            attacking = true;
        }

        public void Tick(float seconds, bool moving)
        {
            if (current == null) return;
            time += Mathf.Max(0, seconds);
            if (attacking && time >= current.duration)
            {
                attacking = false;
                ChangeClip(ChooseClip(motions, moving ? "run" : "idle1", "idle", "run"));
            }
            else if (!attacking)
            {
                ChangeClip(ChooseClip(motions, moving ? "run" : "idle1", "idle", "run"));
            }
            ApplyFrame();
        }

        void ApplyFrame()
        {
            for (int i = 0; i < skeleton.Length; i++)
            {
                skeleton[i].localPosition = defaultPositions[i];
                skeleton[i].localRotation = defaultRotations[i];
                skeleton[i].localScale = defaultScales[i];
            }
            if (current.tracks == null || current.fps <= 0 || current.duration <= 0) return;
            int fps = current.fps;
            foreach (var track in current.tracks)
            {
                if (track.bone < 0 || track.bone >= skeleton.Length) continue;
                float[] p = ReadFloats(raw, track.position);
                float[] q = ReadFloats(raw, track.quaternion);
                if (p.Length == 0 || p.Length % 3 != 0 || q.Length != p.Length / 3 * 4) continue;
                int frames = p.Length / 3;
                int at = Mathf.FloorToInt((time % current.duration) * fps) % frames;
                int i = 3 * at, j = 4 * at;
                skeleton[track.bone].localPosition = new Vector3(p[i], p[i + 1], p[i + 2]);
                skeleton[track.bone].localRotation = new Quaternion(q[j], q[j + 1], q[j + 2], q[j + 3]);
                if (track.scale != null && track.scale.count == frames * 3)
                {
                    float[] scale = ReadFloats(raw, track.scale);
                    skeleton[track.bone].localScale = new Vector3(scale[i], scale[i + 1], scale[i + 2]);
                }
            }
        }

        void OnDestroy()
        {
            if (builtMesh) Destroy(builtMesh);
            if (builtMaterial) Destroy(builtMaterial);
            if (builtTexture) Destroy(builtTexture);
        }
    }
}
