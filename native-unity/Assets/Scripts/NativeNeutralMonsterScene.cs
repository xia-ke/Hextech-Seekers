using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// One physical, trigger-targetable Unity GameObject per verified camp.
    /// The light-blue diagnostic ring is NOT monster artwork. Monsters neither
    /// pursue players nor have attack/health/death systems. Their stable camp
    /// name and position come solely from NativeCampCatalog.LoadVerified().
    /// </summary>
    public sealed class NativeNeutralMonsterScene : MonoBehaviour
    {
        readonly List<GameObject> spawned = new List<GameObject>();
        readonly List<Material> materials = new List<Material>();
        readonly List<GltfImport> originalImports = new List<GltfImport>();
        SkinnedMeshRenderer blueRenderer;
        Animation blueIdle;
        string blueIdleClip;
        public NativeNeutralMonsterWorld World { get; private set; }
        public int EntityCount => spawned.Count;
        public int OriginalArtCount { get; private set; }
        public int OriginalBlueVertices => blueRenderer && blueRenderer.sharedMesh ?
            blueRenderer.sharedMesh.vertexCount : 0;
        public bool OriginalBlueIdlePlaying => blueIdle &&
            !string.IsNullOrEmpty(blueIdleClip) && blueIdle.IsPlaying(blueIdleClip);
        public SkinnedMeshRenderer OriginalBlueRenderer => blueRenderer;

        [Serializable]
        sealed class OriginalArtReceipt
        {
            public int version;
            public string kind;
            public string sourceSha256;
            public string convertedSha256;
            public string idleClip;
            public bool sourceVerified;
            public int skinnedMeshCount;
        }

        const string BlueSourceSha256 =
            "6d3181d21998e13783f0bd05ff571217b86714d0ec0f3bbaab424d5979fee1c9";
        const string BlueIdleClip = "sru_blue_idle_normal";

        public void Initialize(NativeNeutralMonsterWorld world, RiftGrid nav)
        {
            Clear();
            if (world == null || nav == null)
                throw new ArgumentNullException("Verified neutral world and navigation are required");
            World = world;
            if (!world.HasVerifiedCamps)
            {
                Debug.Log("NATIVE_NEUTRALS_DISABLED: no manually verified Map11 camps; no invented monsters");
                return;
            }

            var baseMaterial = Resources.Load<Material>("Native/GarenLit");
            if (!baseMaterial)
                throw new InvalidOperationException("Neutral diagnostic markers require existing native URP material");
            foreach (var neutral in world.Monsters)
            {
                // Explicitly named diagnostic; do not claim original monster
                // skin, hitbox dimensions or game animation fidelity.
                var entity = new GameObject(
                    "NEUTRAL_DEBUG_TARGET_" + neutral.Id + "_" + neutral.CampName);
                entity.transform.SetParent(transform, false);
                var loc = neutral.WalkableAnchor;
                entity.transform.position = new Vector3(
                    loc.x, nav.HeightAt(loc), loc.y);
                var collider = entity.AddComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.radius = 60f;
                collider.center = new Vector3(0, 38f, 0);
                // A kinematic rigidbody makes this static-position neutral a
                // valid trigger counterpart for future animated skill hitboxes.
                var body = entity.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                var ring = entity.AddComponent<LineRenderer>();
                ring.positionCount = 24;
                ring.loop = true;
                ring.useWorldSpace = false;
                ring.startWidth = 11f;
                ring.endWidth = 11f;
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * Mathf.PI * 2f / 24f;
                    ring.SetPosition(i, new Vector3(
                        Mathf.Cos(angle) * 55f, 12f,
                        Mathf.Sin(angle) * 55f));
                }
                Material material = new Material(baseMaterial);
                material.SetColor("_BaseColor", new Color(.12f, .84f, 1f));
                material.name = "Verified-camp neutral diagnostic target";
                ring.sharedMaterial = material;
                spawned.Add(entity);
                materials.Add(material);
            }
            Debug.Log("NATIVE_NEUTRALS_CREATED: verifiedCamps=" + spawned.Count +
                      " passive=true indestructible=true attackDamage=0");
        }

        /// <summary>
        /// Add real upstream skinned geometry only after checking its exact
        /// pinned source and locally converted GLB hashes. The one-model
        /// Phase18A acceptance requires blue sentinel idle animation; missing
        /// optional files in normal local play leave diagnostic rings intact.
        /// </summary>
        public async Task LoadOriginalBlueAsync(string neutralArtFolder, bool required)
        {
            if (World == null || !World.HasVerifiedCamps)
            {
                if (required) throw new InvalidDataException(
                    "Original neutral art requires verified Map11 camps");
                return;
            }
            var modelFile = Path.Combine(neutralArtFolder, "buff_blue.glb");
            var receiptFile = Path.Combine(neutralArtFolder, "buff_blue.json");
            if (!File.Exists(modelFile) && !File.Exists(receiptFile))
            {
                if (required)
                    throw new FileNotFoundException(
                        "Phase18A original blue sentinel not staged", modelFile);
                Debug.Log("NATIVE_ORIGINAL_BLUE_ABSENT: diagnostic marker retained");
                return;
            }
            if (!File.Exists(modelFile) || !File.Exists(receiptFile))
                throw new InvalidDataException(
                    "Partial original blue sentinel source/receipt is forbidden");
            var receipt = JsonUtility.FromJson<OriginalArtReceipt>(
                File.ReadAllText(receiptFile));
            if (receipt == null || receipt.version != 1 ||
                receipt.kind != "buff_blue" || !receipt.sourceVerified ||
                receipt.sourceSha256 != BlueSourceSha256 ||
                receipt.idleClip != BlueIdleClip ||
                receipt.skinnedMeshCount < 1 ||
                string.IsNullOrWhiteSpace(receipt.convertedSha256))
                throw new InvalidDataException(
                    "Original blue sentinel provenance/idle source invalid");
            byte[] source = File.ReadAllBytes(modelFile);
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(source))
                    .Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, receipt.convertedSha256,
                                   StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Original blue sentinel local GLB hash mismatch");
            }
            NativeNeutralMonster selected = null;
            int index = -1;
            for (int i = 0; i < World.Monsters.Count; i++)
                if (World.Monsters[i].CampName == "Chaos Blue")
                {
                    selected = World.Monsters[i];
                    index = i;
                    break;
                }
            if (selected == null || index < 0 || index >= spawned.Count)
                throw new InvalidDataException(
                    "Original blue sentinel needs source-verified Chaos Blue camp");
            var artRoot = new GameObject("ORIGINAL_MAP11_BLUE_SENTINEL_SKINNED");
            artRoot.transform.SetParent(spawned[index].transform, false);
            // Original Map11 imported glTF uses the same conversion as terrain:
            // avoid provisional scale changes that would fabricate geometry.
            artRoot.transform.localPosition = Vector3.zero;
            artRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            artRoot.transform.localScale = Vector3.one;
            var importer = new GltfImport(logger: new ConsoleLogger());
            try
            {
                var url = new Uri(Path.GetFullPath(modelFile)).AbsoluteUri;
                if (!await importer.Load(new Uri(url)))
                    throw new InvalidDataException(
                        "Verified original blue sentinel GLB failed glTFast loading");
                var scene = new GameObjectInstantiator(
                    importer, artRoot.transform, logger: new ConsoleLogger());
                if (!await importer.InstantiateMainSceneAsync(scene))
                    throw new InvalidDataException(
                        "Verified original blue sentinel GLB instantiation failed");
                var renderers = artRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                SkinnedMeshRenderer first = null;
                int vertices = 0;
                foreach (var renderer in renderers)
                {
                    if (!renderer || !renderer.sharedMesh) continue;
                    if (first == null) first = renderer;
                    vertices += renderer.sharedMesh.vertexCount;
                }
                var animation = scene.SceneInstance?.LegacyAnimation;
                if (first == null || vertices <= 0 || animation == null ||
                    animation.GetClip(receipt.idleClip) == null)
                    throw new InvalidDataException(
                        "Original blue sentinel requires skinned mesh and authentic idle clip");
                var idle = animation[receipt.idleClip];
                if (idle == null)
                    throw new InvalidDataException("Blue sentinel idle animation state missing");
                idle.wrapMode = WrapMode.Loop;
                if (!animation.Play(receipt.idleClip))
                    throw new InvalidDataException(
                        "Original blue sentinel idle animation cannot play");
                // Hide cyan diagnostic ring ONLY when the real model and its
                // original idle state have both been positively validated.
                var diagnostic = spawned[index].GetComponent<LineRenderer>();
                if (diagnostic) diagnostic.enabled = false;
                blueRenderer = first;
                blueIdle = animation;
                blueIdleClip = receipt.idleClip;
                OriginalArtCount = 1;
                originalImports.Add(importer);
                Debug.Log("NATIVE_ORIGINAL_NEUTRAL_MODEL_READY: camp=Chaos Blue" +
                          " kind=buff_blue skinnedRenderers=" + renderers.Length +
                          " vertices=" + vertices + " idle=" + receipt.idleClip +
                          " originalMesh=true diagnosticRing=false");
            }
            catch
            {
                Destroy(artRoot);
                importer.Dispose();
                throw;
            }
        }

        public void Clear()
        {
            World = null;
            blueRenderer = null;
            blueIdle = null;
            blueIdleClip = null;
            OriginalArtCount = 0;
            foreach (var importer in originalImports) importer.Dispose();
            originalImports.Clear();
            foreach (var item in spawned) if (item) Destroy(item);
            foreach (var material in materials) if (material) Destroy(material);
            spawned.Clear();
            materials.Clear();
        }

        void OnDestroy() { Clear(); }
    }
}
