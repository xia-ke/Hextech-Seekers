using System;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Provisional temporal interpolation for an already-authorized champion
    /// dash landing. Navigation is authoritative for the entire swept line,
    /// including each intermediate position; this never grants wall phasing.
    ///
    /// This is independent of original hero animation root motion. Imported
    /// clips can later supply exact duration curves without changing the
    /// legal dash path or capture rules.
    /// </summary>
    public sealed class NativeDashMotion
    {
        public const float ProvisionalDashSeconds = .20f;
        Vector2 start;
        Vector2 end;
        float elapsed;
        float duration;
        public bool Active { get; private set; }
        public Vector2 Destination => end;

        public bool Begin(RiftGrid navigation, Vector2 origin,
                          Vector2 destination,
                          float seconds = ProvisionalDashSeconds)
        {
            if (navigation == null || Active ||
                float.IsNaN(seconds) || float.IsInfinity(seconds) ||
                seconds <= 0 || !navigation.CanWalk(origin) ||
                !navigation.CanWalk(destination) ||
                !navigation.LineClear(origin, destination) ||
                (destination - origin).sqrMagnitude <= .001f)
                return false;
            start = origin;
            end = destination;
            elapsed = 0;
            duration = seconds;
            Active = true;
            return true;
        }

        public Vector2 Step(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0)
                throw new ArgumentOutOfRangeException(nameof(delta));
            if (!Active) return end;
            elapsed = Mathf.Min(duration, elapsed + delta);
            float t = elapsed / duration;
            var position = Vector2.Lerp(start, end, t);
            if (elapsed >= duration) Active = false;
            return position;
        }

        public void Cancel() { Active = false; elapsed = 0f; }
    }
}
