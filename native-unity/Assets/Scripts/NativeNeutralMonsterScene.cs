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
        readonly List<OriginalNeutralVisual> originalVisuals =
            new List<OriginalNeutralVisual>();
        public NativeNeutralMonsterWorld World { get; private set; }
        public int EntityCount => spawned.Count;
        public int OriginalArtCount => originalVisuals.Count;
        public IReadOnlyList<OriginalNeutralVisual> OriginalVisuals => originalVisuals;
        public int OriginalBlueVertices =>
            FindOriginalVisual("Chaos Blue")?.VertexCount ?? 0;
        public bool OriginalBlueIdlePlaying =>
            FindOriginalVisual("Chaos Blue")?.IdlePlaying ?? false;
        public SkinnedMeshRenderer OriginalBlueRenderer =>
            FindOriginalVisual("Chaos Blue")?.Renderer;

        // The camp names and model types are separate source datasets:
        // monster types are taken from the pinned original Map11 jungle manifest
        // and assigned to the pinned lower-half camp coordinates by geography.
        // Visual placement/facing remains subject to manual screenshot review.
        public sealed class OriginalNeutralVisual
        {
            public readonly string CampName;
            public readonly string Kind;
            public readonly string IdleClip;
            public readonly SkinnedMeshRenderer Renderer;
            public readonly Animation IdleAnimation;
            public readonly int VertexCount;
            public bool IdlePlaying => IdleAnimation &&
                IdleAnimation.IsPlaying(IdleClip);
            internal OriginalNeutralVisual(string camp, string kind, string idle,
                                           SkinnedMeshRenderer renderer,
                                           Animation animation, int vertices)
            {
                CampName = camp;
                Kind = kind;
                IdleClip = idle;
                Renderer = renderer;
                IdleAnimation = animation;
                VertexCount = vertices;
            }
        }

        sealed class NeutralArtSpec
        {
            public readonly string Camp, Kind, SourceSha, IdleClip;
            public readonly float Scale;
            public NeutralArtSpec(string camp, string kind, string sourceSha,
                                  string idle, float scale)
            {
                Camp = camp; Kind = kind; SourceSha = sourceSha;
                IdleClip = idle; Scale = scale;
            }
        }
        static readonly NeutralArtSpec[] RequiredModels = {
            // Blue's previous one-model GPU acceptance used scale=1: preserve it.
            new NeutralArtSpec("Chaos Blue", "buff_blue",
                "6d3181d21998e13783f0bd05ff571217b86714d0ec0f3bbaab424d5979fee1c9",
                "sru_blue_idle_normal", 1f),
            new NeutralArtSpec("Order Red", "buff_red",
                "0a06eed929ba61dec8a4feff2edb44beeb0e6712d573aa4f1c92967f52a31c1f",
                "sru_red_idle1", 2.075f),
            new NeutralArtSpec("Chaos OwlBear", "gromp",
                "ba8070aa03cdda575e33c5a61deff0b1cdf95dfc5471616b6a477c346521ad3a",
                "sru_gromp_idle1", 1f),
            new NeutralArtSpec("Chaos Wolves", "wolves",
                "9b9992a26de40957eb565c9ca6258b51d569d1491a401c04b5403c3ccc6f7ed5",
                "sru_murkwolf_idle1", 1.2f),
            new NeutralArtSpec("Order Wraiths", "raptors",
                "f0464e241c0e8c3c6ebeb04f136b773b8557a3864157815df8fde6c1295d6696",
                "sru_razorbeak_idle_normal1", 1.4f),
            new NeutralArtSpec("Order Small Golems", "krugs",
                "16df469a0c7e68f31cc3850eb6da153d79f20905775cb89e8ae0a8eb9f301b05",
                "krug_idle_normal", 1.15f),
            new NeutralArtSpec("Dragon Crab", "scuttle",
                "40f0563b30d3a454e1d1cabb6bc653ee85889d12a3ea5c5493d73cb919387f26",
                "sru_crab_idle1", .75f),
            new NeutralArtSpec("Dragon", "dragon",
                "ded89570c13274d7faa939e5695a43588ba35c57089a520303dee1b2465b30b3",
                "sru_dragon_idle_normal", 1.25f)
        };

        [Serializable]
        sealed class OriginalArtReceipt
        {
            public int version;
            public string kind, sourceRepo, sourceCommit, sourceSha256;
            public string convertedSha256, idleClip;
            public bool sourceVerified;
            public int skinnedMeshCount;
        }

        public OriginalNeutralVisual FindOriginalVisual(string campName)
        {
            foreach (var visual in originalVisuals)
                if (visual.CampName == campName) return visual;
            return null;
        }

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
        /// Load all locally staged source-pinned original monster meshes.
        /// In normal local play, absent files retain explicit diagnostic rings;
        /// incomplete/tampered bundles always fail instead of impersonating art.
        /// Windows GPU QA requires all eight original models and idle states.
        /// </summary>
        public async Task LoadOriginalNeutralsAsync(string folder,
                                                    bool requireBlue,
                                                    bool requireAll)
        {
            if (World == null || !World.HasVerifiedCamps)
            {
                if (requireAll || requireBlue)
                    throw new InvalidDataException(
                        "Original neutral art requires source-verified Map11 camps");
                return;
            }
            foreach (var spec in RequiredModels)
            {
                bool required = requireAll || (requireBlue && spec.Kind == "buff_blue");
                await LoadOneOriginalAsync(folder, spec, required);
            }
            if (requireAll && originalVisuals.Count != RequiredModels.Length)
                throw new InvalidDataException(
                    "Phase18B requires all eight original skinned neutral rigs");
        }

        async Task LoadOneOriginalAsync(string folder, NeutralArtSpec spec, bool required)
        {
            string modelFile = Path.Combine(folder, spec.Kind + ".glb");
            string receiptFile = Path.Combine(folder, spec.Kind + ".json");
            if (!File.Exists(modelFile) && !File.Exists(receiptFile))
            {
                if (required)
                    throw new FileNotFoundException(
                        "Required original neutral model not staged: " + spec.Camp,
                        modelFile);
                Debug.Log("NATIVE_ORIGINAL_NEUTRAL_ABSENT: camp=" + spec.Camp +
                          " fallback=diagnostic-ring");
                return;
            }
            if (!File.Exists(modelFile) || !File.Exists(receiptFile))
                throw new InvalidDataException(
                    "Partial original neutral model/receipt: " + spec.Kind);
            var receipt = JsonUtility.FromJson<OriginalArtReceipt>(
                File.ReadAllText(receiptFile));
            if (receipt == null || receipt.version != 1 ||
                receipt.kind != spec.Kind || !receipt.sourceVerified ||
                receipt.sourceRepo != "iamywl/Web_game" ||
                receipt.sourceCommit != "557a9d5352af12bee8bf3db879c22d04d0a0954e" ||
                receipt.sourceSha256 != spec.SourceSha ||
                receipt.idleClip != spec.IdleClip ||
                receipt.skinnedMeshCount < 1 ||
                string.IsNullOrWhiteSpace(receipt.convertedSha256))
                throw new InvalidDataException(
                    "Original neutral source provenance or clip mismatch: " + spec.Kind);
            byte[] bytes = File.ReadAllBytes(modelFile);
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, receipt.convertedSha256,
                                   StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Original neutral converted file SHA256 mismatch: " + spec.Kind);
            }
            int index = -1;
            for (int i = 0; i < World.Monsters.Count; i++)
                if (World.Monsters[i].CampName == spec.Camp)
                {
                    index = i;
                    break;
                }
            if (index < 0 || index >= spawned.Count)
                throw new InvalidDataException(
                    "Original neutral kind has no actual verified camp: " + spec.Camp);

            var root = new GameObject("ORIGINAL_MAP11_" + spec.Kind + "_SKINNED");
            root.transform.SetParent(spawned[index].transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            root.transform.localScale = Vector3.one * spec.Scale;
            var importer = new GltfImport(logger: new ConsoleLogger());
            try
            {
                var uri = new Uri(Path.GetFullPath(modelFile)).AbsoluteUri;
                if (!await importer.Load(new Uri(uri)))
                    throw new InvalidDataException(
                        "Original source GLB failed glTFast Load: " + spec.Kind);
                var scene = new GameObjectInstantiator(
                    importer, root.transform, logger: new ConsoleLogger());
                if (!await importer.InstantiateMainSceneAsync(scene))
                    throw new InvalidDataException(
                        "Original source GLB failed instantiation: " + spec.Kind);
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                SkinnedMeshRenderer first = null;
                int vertices = 0;
                foreach (var candidate in renderers)
                {
                    if (!candidate || !candidate.sharedMesh) continue;
                    if (!first) first = candidate;
                    vertices += candidate.sharedMesh.vertexCount;
                }
                var animation = scene.SceneInstance?.LegacyAnimation;
                if (!first || vertices <= 0 || animation == null ||
                    !animation.GetClip(spec.IdleClip))
                    throw new InvalidDataException(
                        "Missing authentic skinned mesh or idle clip: " + spec.Kind);
                var state = animation[spec.IdleClip];
                if (state == null)
                    throw new InvalidDataException(
                        "Original idle animation state missing: " + spec.Kind);
                state.wrapMode = WrapMode.Loop;
                if (!animation.Play(spec.IdleClip))
                    throw new InvalidDataException(
                        "Cannot play authentic original idle clip: " + spec.Kind);
                var diagnostic = spawned[index].GetComponent<LineRenderer>();
                if (diagnostic) diagnostic.enabled = false;
                originalImports.Add(importer);
                var original = new OriginalNeutralVisual(
                    spec.Camp, spec.Kind, spec.IdleClip, first, animation, vertices);
                originalVisuals.Add(original);
                Debug.Log("NATIVE_ORIGINAL_NEUTRAL_MODEL_READY: camp=" + spec.Camp +
                          " kind=" + spec.Kind +
                          " skinnedRenderers=" + renderers.Length +
                          " vertices=" + vertices +
                          " idle=" + spec.IdleClip +
                          " originalMesh=true diagnosticRing=false");
            }
            catch
            {
                Destroy(root);
                importer.Dispose();
                throw;
            }
        }

        public void Clear()
        {
            World = null;
            originalVisuals.Clear();
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
