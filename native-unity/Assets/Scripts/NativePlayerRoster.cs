using System;
using System.Collections.Generic;

namespace HextechSeekers.Native
{
    /// <summary>
    /// First-release four-hero local roster: one human and 1–3 AI opponents,
    /// no duplicate hero slots. Rendering/provenance is handled separately.
    /// </summary>
    public static class NativePlayerRoster
    {
        public static readonly string[] Heroes = {
            "Garen", "Blitzcrank", "Riven", "Yasuo"
        };

        public static bool IsSelectable(string hero)
        {
            if (string.IsNullOrEmpty(hero)) return false;
            foreach (string candidate in Heroes)
                if (string.Equals(candidate, hero, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static string CanonicalHero(string hero)
        {
            foreach (string candidate in Heroes)
                if (string.Equals(candidate, hero, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            throw new ArgumentException("Unknown native player champion", nameof(hero));
        }

        public static MatchParticipant[] Build(string selectedHero, MatchRole role, int playerCount)
        {
            if (playerCount < 2 || playerCount > 4)
                throw new ArgumentOutOfRangeException(nameof(playerCount));
            string selected = CanonicalHero(selectedHero);
            var participants = new MatchParticipant[playerCount];
            participants[0] = new MatchParticipant(1, selected, role, true);
            int next = 1;
            foreach (string hero in Heroes)
            {
                if (next == playerCount) break;
                if (string.Equals(hero, selected, StringComparison.Ordinal)) continue;
                MatchRole aiRole = role == MatchRole.Hunter ? MatchRole.Hider :
                    next == 1 ? MatchRole.Hunter : MatchRole.Hider;
                participants[next] = new MatchParticipant(next + 1, hero, aiRole, false);
                next++;
            }
            return participants;
        }
    }
}
