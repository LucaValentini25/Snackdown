using Snackdown.Connection;
using UnityEngine;

namespace Snackdown.UI
{
    /// <summary>
    /// The two bits of formatting every life display needs, in one place.
    /// </summary>
    /// <remarks>
    /// Shared by three views — the clock, the bar over each character and the strip along the
    /// bottom. Duplicating it would let them disagree about what "one minute left" looks like, and
    /// two clocks on screen showing the same moment differently is the kind of detail that reads as
    /// a bug even when nothing is wrong.
    /// </remarks>
    public static class LifeText
    {
        /// <summary>Seconds as <c>m:ss</c>, because a bare count stops being readable past a minute.</summary>
        public static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }

        /// <summary>The display name for a client, falling back to their id if the roster has not arrived.</summary>
        public static string NameOf(ulong clientId)
        {
            SessionRoster roster = SessionRoster.Current;
            if (roster == null) return $"P{clientId}";

            for (int i = 0; i < roster.Count; i++)
                if (roster[i].ClientId == clientId)
                    return roster[i].Nickname.ToString();

            return $"P{clientId}";
        }

        /// <summary>The skin index a client picked, for showing their portrait next to their name.</summary>
        public static int CharacterIndexOf(ulong clientId)
        {
            SessionRoster roster = SessionRoster.Current;
            if (roster == null) return 0;

            for (int i = 0; i < roster.Count; i++)
                if (roster[i].ClientId == clientId)
                    return roster[i].CharacterIndex;

            return 0;
        }
    }
}
