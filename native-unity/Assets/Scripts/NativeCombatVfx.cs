using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Non-licensed 3D diagnostic FX for already-authoritative combat events.
    /// This is NOT original League particle art. World-space traces use the
    /// same Map11 x/z/height coordinates as navigation and champion actors.
    ///
    /// Visuals are strictly audience-gated: an observer cannot see an enemy
    /// caster outside that observer's personal LOS, nor an unseen victim's
    /// contact location. All visual objects are cleaned up between rounds.
    /// </summary>
    public sealed class NativeCombatVfx : MonoBehaviour
    {
        sealed class Cue
        {
            public int OwnerId;
            public GameObject Root;
            public LineRenderer Line;
            public float ExpiresAt;
        }

        const int MaxTransientCues = 32;
        readonly Dictionary<int, Cue> hooks = new Dictionary<int, Cue>();
        readonly Dictionary<int, Cue> walls = new Dictionary<int, Cue>();
        readonly List<Cue> transients = new List<Cue>();
        readonly List<Material> ownedMaterials = new List<Material>();
        RiftGrid nav;
        Material baseMaterial;
        int sequence;

        public bool Ready => nav != null && baseMaterial;
        public int TransientCount => transients.Count;

        public void Begin(RiftGrid map)
        {
            End();
            nav = map ?? throw new ArgumentNullException(nameof(map));
            baseMaterial = Resources.Load<Material>("Native/GarenLit");
            if (!baseMaterial)
            {
                nav = null;
                throw new InvalidOperationException("URP material resource required for skill diagnostics");
            }
            Debug.Log("NATIVE_COMBAT_FX_READY: source=DIAGNOSTIC_NOT_ORIGINAL_ART");
        }

        Vector3 World(Vector2 position, float lift)
        {
            return new Vector3(position.x, nav.HeightAt(position) + lift, position.y);
        }

        Cue Create(int owner, string label, Color color, float width, int count, bool loop)
        {
            var go = new GameObject("DEBUG_SKILL_FX_" + label + "_" + sequence++);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = count;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 4;
            Material material = new Material(baseMaterial);
            material.name = "Nonoriginal diagnostic skill effect";
            material.SetColor("_BaseColor", color);
            ownedMaterials.Add(material);
            line.sharedMaterial = material;
            return new Cue { OwnerId = owner, Root = go, Line = line };
        }

        void SetTrace(Dictionary<int, Cue> store, int owner, bool active,
                      Vector2 a, Vector2 b, Color color, string label)
        {
            if (!Ready) return;
            if (!store.TryGetValue(owner, out Cue cue))
            {
                if (!active) return;
                cue = Create(owner, label, color, 17f, 2, false);
                store.Add(owner, cue);
            }
            cue.Root.SetActive(active);
            if (!active) return;
            cue.Line.SetPosition(0, World(a, 70f));
            cue.Line.SetPosition(1, World(b, 70f));
        }

        public void SetHook(int actor, bool active, Vector2 origin, Vector2 tip)
        {
            SetTrace(hooks, actor, active, origin, tip,
                     new Color(.16f, .76f, 1f), "BLITZ_Q_PROJECTILE");
        }

        public void SetWindWall(int actor, NativeWindWall barrier)
        {
            if (barrier == null)
            {
                SetTrace(walls, actor, false, default, default, Color.clear, "YASUO_W_WALL");
                return;
            }
            SetTrace(walls, actor, true, barrier.EndpointA, barrier.EndpointB,
                     new Color(.56f, .8f, 1f), "YASUO_W_SEGMENT");
        }

        /// <summary>Event visual is at the *caster* anchor only. An unseen
        /// target hit in brush must not leak its precise world position.</summary>
        public void Pulse(int owner, Vector2 casterPosition, string skill)
        {
            if (!Ready) return;
            while (transients.Count >= MaxTransientCues)
                DestroyCue(transients[0], transients);
            Color color = skill.StartsWith("Riven", StringComparison.OrdinalIgnoreCase)
                ? new Color(1f, .67f, .2f)
                : skill.StartsWith("Yasuo", StringComparison.OrdinalIgnoreCase)
                    ? new Color(.4f, .94f, .9f)
                    : skill.StartsWith("Blitz", StringComparison.OrdinalIgnoreCase)
                        ? new Color(.2f, .7f, 1f) : new Color(.85f, .8f, 1f);
            var ring = Create(owner, skill.Replace(' ', '_'), color, 12f, 28, true);
            Vector3 at = World(casterPosition, 15f);
            float radius = skill.Contains(" W") ? 145f : 83f;
            for (int i = 0; i < 28; i++)
            {
                float angle = Mathf.PI * 2 * i / 28f;
                ring.Line.SetPosition(i, at +
                    new Vector3(Mathf.Cos(angle) * radius, 0,
                                Mathf.Sin(angle) * radius));
            }
            ring.ExpiresAt = Time.unscaledTime + .55f;
            transients.Add(ring);
        }

        void DestroyCue(Cue cue, List<Cue> collection)
        {
            collection.Remove(cue);
            if (cue.Root) Destroy(cue.Root);
        }

        public void Tick(Func<int, bool> maySeeCaster)
        {
            if (!Ready || maySeeCaster == null) return;
            for (int i = transients.Count - 1; i >= 0; i--)
            {
                var cue = transients[i];
                if (cue.ExpiresAt <= Time.unscaledTime)
                {
                    DestroyCue(cue, transients);
                    continue;
                }
                if (cue.Root) cue.Root.SetActive(maySeeCaster(cue.OwnerId));
            }
            foreach (var cue in hooks.Values)
                if (cue.Line) cue.Line.enabled =
                    cue.Root.activeSelf && maySeeCaster(cue.OwnerId);
            foreach (var cue in walls.Values)
                if (cue.Line) cue.Line.enabled =
                    cue.Root.activeSelf && maySeeCaster(cue.OwnerId);
        }

        public void End()
        {
            foreach (var cue in hooks.Values)
                if (cue.Root) Destroy(cue.Root);
            foreach (var cue in walls.Values)
                if (cue.Root) Destroy(cue.Root);
            foreach (var cue in transients)
                if (cue.Root) Destroy(cue.Root);
            hooks.Clear();
            walls.Clear();
            transients.Clear();
            foreach (var material in ownedMaterials)
                if (material) Destroy(material);
            ownedMaterials.Clear();
            baseMaterial = null;
            nav = null;
        }

        void OnDestroy() { End(); }
    }
}
