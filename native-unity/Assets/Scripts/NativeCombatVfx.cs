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
            public Material EffectMaterial;
            public GameObject Head;
            public readonly List<LineRenderer> ExtraLines = new List<LineRenderer>();
            public readonly List<Vector3> TrailPoints = new List<Vector3>();
            public float ExpiresAt;
        }

        const int MaxTransientCues = 32;
        readonly Dictionary<int, Cue> hooks = new Dictionary<int, Cue>();
        readonly Dictionary<int, Cue> walls = new Dictionary<int, Cue>();
        readonly Dictionary<int, Cue> dashes = new Dictionary<int, Cue>();
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
            return new Cue {
                OwnerId = owner, Root = go, Line = line, EffectMaterial = material
            };
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
            if (!hooks.TryGetValue(actor, out Cue cue)) return;
            if (!cue.Head)
            {
                cue.Head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cue.Head.name = "DEBUG_BLITZ_Q_HOOK_HEAD";
                cue.Head.transform.SetParent(cue.Root.transform, true);
                cue.Head.transform.localScale = Vector3.one * 42f;
                var collider = cue.Head.GetComponent<Collider>();
                if (collider) Destroy(collider);
                var renderer = cue.Head.GetComponent<Renderer>();
                renderer.sharedMaterial = cue.EffectMaterial;
                var trail = cue.Head.AddComponent<TrailRenderer>();
                trail.time = .16f;
                trail.startWidth = 24f;
                trail.endWidth = 2f;
                trail.sharedMaterial = cue.EffectMaterial;
                trail.numCapVertices = 4;
            }
            cue.Head.SetActive(active);
            if (active) cue.Head.transform.position = World(tip, 70f);
        }

        public void SetDash(int actor, bool active, Vector2 from, Vector2 to, string hero)
        {
            if (!Ready) return;
            if (!dashes.TryGetValue(actor, out Cue cue))
            {
                if (!active) return;
                Color color = hero == "Riven" ? new Color(1f, .55f, .16f) :
                    hero == "Yasuo" ? new Color(.35f, .9f, 1f) :
                    new Color(.2f, .75f, 1f);
                cue = Create(actor, hero + "_DASH_TRAIL", color, 34f, 2, false);
                dashes.Add(actor, cue);
            }
            cue.Root.SetActive(active);
            if (!active)
            {
                cue.TrailPoints.Clear();
                return;
            }
            Vector3 a = World(from, 26f);
            Vector3 b = World(to, 26f);
            if (cue.TrailPoints.Count == 0 ||
                Vector3.Distance(cue.TrailPoints[cue.TrailPoints.Count - 1], a) > 1f)
                cue.TrailPoints.Add(a);
            cue.TrailPoints.Add(b);
            while (cue.TrailPoints.Count > 14) cue.TrailPoints.RemoveAt(0);
            cue.Line.positionCount = cue.TrailPoints.Count;
            cue.Line.SetPositions(cue.TrailPoints.ToArray());
        }

        public void SetWindWall(int actor, NativeWindWall barrier)
        {
            if (!Ready) return;
            if (!walls.TryGetValue(actor, out Cue cue))
            {
                if (barrier == null) return;
                cue = Create(actor, "YASUO_W_WALL_FRAME",
                    new Color(.56f, .8f, 1f), 13f, 5, false);
                walls.Add(actor, cue);
            }
            cue.Root.SetActive(barrier != null);
            if (barrier == null) return;
            // Outline a vertical 3D energy plane from the exact collision
            // endpoints, then add five vertical ribs to give it real volume.
            Vector2 a = barrier.EndpointA, b = barrier.EndpointB;
            cue.Line.SetPosition(0, World(a, 14f));
            cue.Line.SetPosition(1, World(b, 14f));
            cue.Line.SetPosition(2, World(b, 170f));
            cue.Line.SetPosition(3, World(a, 170f));
            cue.Line.SetPosition(4, World(a, 14f));
            while (cue.ExtraLines.Count < 5)
            {
                var go = new GameObject("DEBUG_YASUO_W_WALL_RIB");
                go.transform.SetParent(cue.Root.transform, false);
                var rib = go.AddComponent<LineRenderer>();
                rib.useWorldSpace = true;
                rib.positionCount = 2;
                rib.startWidth = 4f;
                rib.endWidth = 4f;
                rib.sharedMaterial = cue.EffectMaterial;
                cue.ExtraLines.Add(rib);
            }
            for (int i = 0; i < cue.ExtraLines.Count; i++)
            {
                float t = i / (float)(cue.ExtraLines.Count - 1);
                Vector2 point = Vector2.Lerp(a, b, t);
                cue.ExtraLines[i].SetPosition(0, World(point, 14f));
                cue.ExtraLines[i].SetPosition(1, World(point, 170f));
            }
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
            bool tornado = skill.IndexOf("Tornado", StringComparison.OrdinalIgnoreCase) >= 0;
            var ring = Create(owner, skill.Replace(' ', '_'), color,
                              tornado ? 20f : 12f, tornado ? 56 : 28, !tornado);
            Vector3 at = World(casterPosition, 15f);
            if (tornado)
            {
                // A visible 3D spiral for Yasuo's third Q; diagnostic geometry,
                // deliberately not represented as licensed original particles.
                for (int i = 0; i < 56; i++)
                {
                    float t = i / 55f;
                    float angle = t * Mathf.PI * 7f;
                    float radius = 24f + 76f * t;
                    ring.Line.SetPosition(i, at + new Vector3(
                        Mathf.Cos(angle) * radius, 230f * t,
                        Mathf.Sin(angle) * radius));
                }
            }
            else
            {
                float radius = skill.Contains(" W") ? 145f : 83f;
                for (int i = 0; i < 28; i++)
                {
                    float angle = Mathf.PI * 2 * i / 28f;
                    ring.Line.SetPosition(i, at +
                        new Vector3(Mathf.Cos(angle) * radius, 0,
                                    Mathf.Sin(angle) * radius));
                }
            }
            ring.ExpiresAt = Time.unscaledTime + (tornado ? .8f : .55f);
            transients.Add(ring);
        }

        /// <summary>
        /// A contact burst is rendered at a victim only when the current
        /// observer independently has vision of that victim. Hidden brush
        /// contacts still affect gameplay but never disclose their location.
        /// </summary>
        public void Impact(int owner, Vector2 targetPosition, string skill,
                           bool targetVisibleToObserver)
        {
            if (!Ready || !targetVisibleToObserver) return;
            while (transients.Count >= MaxTransientCues)
                DestroyCue(transients[0], transients);
            Color color = skill.StartsWith("Riven", StringComparison.OrdinalIgnoreCase)
                ? new Color(1f, .67f, .2f)
                : skill.StartsWith("Yasuo", StringComparison.OrdinalIgnoreCase)
                    ? new Color(.4f, .94f, .9f)
                    : new Color(.2f, .7f, 1f);
            var cue = Create(owner, skill.Replace(' ', '_') + "_VISIBLE_IMPACT",
                             color, 10f, 24, true);
            Vector3 center = World(targetPosition, 22f);
            for (int i = 0; i < 24; i++)
            {
                float angle = Mathf.PI * 2f * i / 24f;
                cue.Line.SetPosition(i, center + new Vector3(
                    Mathf.Cos(angle) * 96f, 0, Mathf.Sin(angle) * 96f));
            }
            var verticalObject = new GameObject("DEBUG_SKILL_IMPACT_VERTICAL_RING");
            verticalObject.transform.SetParent(cue.Root.transform, false);
            var vertical = verticalObject.AddComponent<LineRenderer>();
            vertical.useWorldSpace = true;
            vertical.loop = true;
            vertical.positionCount = 24;
            vertical.startWidth = 7f;
            vertical.endWidth = 7f;
            vertical.sharedMaterial = cue.EffectMaterial;
            cue.ExtraLines.Add(vertical);
            for (int i = 0; i < 24; i++)
            {
                float angle = Mathf.PI * 2f * i / 24f;
                vertical.SetPosition(i, center + new Vector3(
                    0, Mathf.Cos(angle) * 84f, Mathf.Sin(angle) * 84f));
            }
            cue.ExpiresAt = Time.unscaledTime + .42f;
            transients.Add(cue);
        }

        void DestroyCue(Cue cue, List<Cue> collection)
        {
            collection.Remove(cue);
            if (cue.Root) Destroy(cue.Root);
            if (cue.EffectMaterial)
            {
                ownedMaterials.Remove(cue.EffectMaterial);
                Destroy(cue.EffectMaterial);
            }
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
            {
                bool visible = cue.Root.activeSelf && maySeeCaster(cue.OwnerId);
                if (cue.Line) cue.Line.enabled = visible;
                if (cue.Head) cue.Head.SetActive(visible);
            }
            foreach (var cue in walls.Values)
            {
                bool visible = cue.Root.activeSelf && maySeeCaster(cue.OwnerId);
                if (cue.Line) cue.Line.enabled = visible;
                foreach (var extra in cue.ExtraLines)
                    if (extra) extra.enabled = visible;
            }
            foreach (var cue in dashes.Values)
                if (cue.Line) cue.Line.enabled =
                    cue.Root.activeSelf && maySeeCaster(cue.OwnerId);
        }

        public void End()
        {
            foreach (var cue in hooks.Values)
                if (cue.Root) Destroy(cue.Root);
            foreach (var cue in walls.Values)
                if (cue.Root) Destroy(cue.Root);
            foreach (var cue in dashes.Values)
                if (cue.Root) Destroy(cue.Root);
            foreach (var cue in transients)
                if (cue.Root) Destroy(cue.Root);
            hooks.Clear();
            walls.Clear();
            dashes.Clear();
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
