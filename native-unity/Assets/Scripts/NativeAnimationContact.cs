using System;
using System.IO;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Explicit source-clip bindings for genuine imported hero animation tracks.
    /// We never guess that "spell1" equals Q or silently play an unrelated
    /// animation in the absence of validated original animation metadata.
    /// </summary>
    public sealed class NativeAbilityAnimationBindings
    {
        [Serializable]
        sealed class Data
        {
            public int version;
            public string hero;
            public bool sourceVerified;
            public string q1, q2, q3, w, e;
        }

        readonly string q1, q2, q3, w, e;
        public string Hero { get; private set; }

        NativeAbilityAnimationBindings(Data data)
        {
            Hero = data.hero; q1 = data.q1; q2 = data.q2;
            q3 = data.q3; w = data.w; e = data.e;
        }

        public static NativeAbilityAnimationBindings LoadVerified(
            string filename, string expectedHero, Func<string, bool> existsClip)
        {
            if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename))
                return null; // missing bindings are not guessed
            if (!GarenRigRuntime.IsSupportedHero(expectedHero) || existsClip == null)
                throw new ArgumentException("Supported hero and original rig clip lookup required");
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(filename));
            if (data == null || data.version != 1 || !data.sourceVerified ||
                !string.Equals(data.hero, expectedHero, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Skill animation binding must be verified for this hero");
            int mapped = 0;
            foreach (string clip in new[] { data.q1, data.q2, data.q3, data.w, data.e })
            {
                if (string.IsNullOrWhiteSpace(clip)) continue;
                if (clip.Length > 128 || clip.Contains("/") || clip.Contains("\\") ||
                    !existsClip(clip))
                    throw new InvalidDataException("Skill binding refers to a missing original clip: " + clip);
                mapped++;
            }
            if (mapped == 0)
                throw new InvalidDataException("Skill binding is empty");
            return new NativeAbilityAnimationBindings(data);
        }

        public string Resolve(NativeSkillSlot slot, int stage = 1)
        {
            switch (slot)
            {
                case NativeSkillSlot.Q:
                    return stage == 3 ? q3 : stage == 2 ? q2 : stage == 1 ? q1 : null;
                case NativeSkillSlot.W: return w;
                case NativeSkillSlot.E: return e;
                default: return null;
            }
        }
    }

    /// <summary>
    /// Actor-locked basic hit window. Pressing the attack key does not capture:
    /// the authoritative caller rechecks range, LOS, invulnerability and
    /// hunter role at the contact frame. Attack animations can use this
    /// provisional contact offset until verified frame data is available.
    /// </summary>
    public sealed class NativeBasicContactWindow
    {
        public const float ProvisionalContactSeconds = .23f;
        float remaining;
        int target = -1;

        public bool Pending => target >= 0;
        public int TargetId => target;

        public bool Begin(int targetId, float contactSeconds = ProvisionalContactSeconds)
        {
            if (targetId < 0 || Pending || float.IsNaN(contactSeconds) ||
                float.IsInfinity(contactSeconds) || contactSeconds <= 0f) return false;
            remaining = contactSeconds;
            target = targetId;
            return true;
        }

        public bool Tick(float dt, out int targetId)
        {
            targetId = -1;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f)
                throw new ArgumentOutOfRangeException(nameof(dt));
            if (!Pending) return false;
            remaining -= dt;
            if (remaining > 0f) return false;
            targetId = target;
            Cancel();
            return true;
        }

        public void Cancel() { target = -1; remaining = 0; }
    }
}
