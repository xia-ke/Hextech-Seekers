using System;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Optional explicitly measured spawn/fountain positions on the native Rift.
    /// A local staging fallback is not called a verified League fountain.
    /// During the 120s hiding phase, MatchRules disallows every hunter move
    /// regardless of any UI or skill input.
    /// </summary>
    public sealed class NativeSpawnCalibration
    {
        [Serializable]
        sealed class Spawn
        {
            public float x;
            public float z;
        }

        [Serializable]
        sealed class Data
        {
            public int version;
            public bool manuallyVerified;
            public string source;
            public Spawn hunter;
            public Spawn hider;
            public float fountainRadius;
        }

        public Vector2 HunterSpawn { get; private set; }
        public Vector2 HiderSpawn { get; private set; }
        public float FountainRadius { get; private set; }
        public string VerificationSource { get; private set; }

        NativeSpawnCalibration() {}

        public static NativeSpawnCalibration LoadVerified(string path, RiftGrid nav)
        {
            if (string.IsNullOrEmpty(path) || nav == null || !File.Exists(path))
                return null;
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
            if (data == null || data.version != 1 || !data.manuallyVerified ||
                string.IsNullOrWhiteSpace(data.source) || data.hunter == null ||
                data.hider == null ||
                !Valid(data.hunter.x) || !Valid(data.hunter.z) ||
                !Valid(data.hider.x) || !Valid(data.hider.z) ||
                !Valid(data.fountainRadius) || data.fountainRadius < 50f ||
                data.fountainRadius > 1000f)
                throw new InvalidDataException("Spawn coordinates require verified Map11 provenance");
            Vector2 hunter = new Vector2(data.hunter.x, data.hunter.z);
            Vector2 hider = new Vector2(data.hider.x, data.hider.z);
            if (!nav.TryNearest(hunter, out var actualHunter, 4) ||
                !nav.TryNearest(hider, out var actualHider, 4) ||
                Vector2.Distance(hunter, actualHunter) > 220f ||
                Vector2.Distance(hider, actualHider) > 220f)
                throw new InvalidDataException("Verified fountain/entry coordinates are outside the navigable Rift");
            return new NativeSpawnCalibration {
                HunterSpawn = actualHunter,
                HiderSpawn = actualHider,
                FountainRadius = data.fountainRadius,
                VerificationSource = data.source
            };
        }

        static bool Valid(float n) => !float.IsNaN(n) && !float.IsInfinity(n);

        public bool IsWithinHunterFountain(Vector2 position)
        {
            return (position - HunterSpawn).sqrMagnitude <= FountainRadius * FountainRadius;
        }
    }
}
