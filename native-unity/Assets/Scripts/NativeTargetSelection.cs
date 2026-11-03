using System;
using System.Collections.Generic;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Non-rendering target arbitration for basic contact. Tab/card-sense
    /// selection can remember an unseen target, but a basic attack may only
    /// acquire a living, visible opposing actor already within melee reach.
    /// Final visibility, range and protection are checked again at hit time.
    /// </summary>
    public static class NativeTargetSelection
    {
        public readonly struct Candidate
        {
            public readonly int Id;
            public readonly bool Opponent;
            public readonly bool Alive;
            public readonly bool Visible;
            public readonly float Distance;

            public Candidate(int id, bool opponent, bool alive, bool visible, float distance)
            {
                Id = id;
                Opponent = opponent;
                Alive = alive;
                Visible = visible;
                Distance = distance;
            }
        }

        public static int ChooseBasicTarget(int preferredId,
                                            IEnumerable<Candidate> candidates,
                                            float maxDistance)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (float.IsNaN(maxDistance) || float.IsInfinity(maxDistance) ||
                maxDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDistance));

            int nearestId = -1;
            float nearestDistance = float.PositiveInfinity;
            foreach (Candidate candidate in candidates)
            {
                if (candidate.Id < 0 || !candidate.Opponent ||
                    !candidate.Alive || !candidate.Visible ||
                    float.IsNaN(candidate.Distance) ||
                    float.IsInfinity(candidate.Distance) ||
                    candidate.Distance < 0f || candidate.Distance > maxDistance)
                    continue;
                if (candidate.Id == preferredId)
                    return preferredId;
                if (candidate.Distance < nearestDistance)
                {
                    nearestDistance = candidate.Distance;
                    nearestId = candidate.Id;
                }
            }
            return nearestId;
        }

        /// <summary>
        /// Display a selected hero name only while target is visible to the
        /// current active observer. Hidden card-sense targets retain the ID,
        /// not a world marker or a live position on the HUD.
        /// </summary>
        public static bool CanDisplaySelectedEnemy(bool spectator, bool opponent,
                                                    bool alive, bool currentlyVisible) =>
            !spectator && opponent && alive && currentlyVisible;
    }
}
