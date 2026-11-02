using System;
using System.Collections.Generic;
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
        public NativeNeutralMonsterWorld World { get; private set; }
        public int EntityCount => spawned.Count;

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

        public void Clear()
        {
            World = null;
            foreach (var item in spawned) if (item) Destroy(item);
            foreach (var material in materials) if (material) Destroy(material);
            spawned.Clear();
            materials.Clear();
        }

        void OnDestroy() { Clear(); }
    }
}
