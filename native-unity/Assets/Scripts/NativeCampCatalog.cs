using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Optional, explicitly calibrated map-specific wild-monster camp coordinates
    /// for Card Sense. Never fabricate names/positions from player coordinates.
    /// Read-only camp locators are not attackable/mortal monster implementations.
    /// </summary>
    public sealed class NativeCampCatalog
    {
        [Serializable]
        sealed class Definition
        {
            public int version;
            public bool manuallyVerified;
            public Entry[] camps;
        }
        [Serializable]
        sealed class Entry
        {
            public string name;
            public float x;
            public float z;
        }

        public sealed class Camp
        {
            public string Name { get; internal set; }
            public Vector2 Location { get; internal set; }
        }

        readonly List<Camp> camps = new List<Camp>();
        public IReadOnlyList<Camp> Camps => camps;

        NativeCampCatalog() { }

        public static NativeCampCatalog LoadVerified(string path, RiftGrid navigation)
        {
            if (!File.Exists(path) || navigation == null) return null;
            var model = JsonUtility.FromJson<Definition>(File.ReadAllText(path));
            if (model == null || model.version != 1 || !model.manuallyVerified ||
                model.camps == null || model.camps.Length == 0)
                throw new InvalidDataException("Camp locations require manually verified Map11 coordinates");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new NativeCampCatalog();
            foreach (var entry in model.camps)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name) ||
                    float.IsNaN(entry.x) || float.IsNaN(entry.z) ||
                    float.IsInfinity(entry.x) || float.IsInfinity(entry.z) ||
                    !seen.Add(entry.name))
                    throw new InvalidDataException("Invalid or duplicate camp entry");
                // The monster itself may occupy a non-walkable part of the camp,
                // but the nearby navigation surface must be within our game region.
                if (!navigation.TryNearest(new Vector2(entry.x, entry.z), out _, 15))
                    throw new InvalidDataException("Camp outside the native playable navigation region: " + entry.name);
                result.camps.Add(new Camp {
                    Name = entry.name,
                    Location = new Vector2(entry.x, entry.z)
                });
            }
            return result;
        }

        public string NearestName(Vector2 position)
        {
            if (camps.Count == 0) return null;
            string name = null;
            float best = float.PositiveInfinity;
            foreach (var camp in camps)
            {
                float distance = (camp.Location - position).sqrMagnitude;
                if (distance < best) { best = distance; name = camp.Name; }
            }
            return name;
        }
    }
}
