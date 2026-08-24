using Snackdown.Gameplay.Player;
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

        /// <summary>The display name for a client, falling back to their id if the session has not arrived.</summary>
        public static string NameOf(ulong clientId)
            => SessionOf(clientId)?.Nickname ?? $"P{clientId}";

        /// <summary>The skin index a client picked, for showing their portrait next to their name.</summary>
        public static int CharacterIndexOf(ulong clientId)
            => SessionOf(clientId)?.CharacterIndex ?? 0;

        /// <remarks>
        /// Asked of the roster rather than of <see cref="PlayerSession.Of"/>, which is a static
        /// registry shared by every peer in the process. That distinction is invisible in a build
        /// and is the difference between reading this peer's players and reading everyone's while
        /// the Play mode harness has a host and its clients running side by side.
        /// </remarks>
        static PlayerSession SessionOf(ulong clientId)
        {
            SessionRoster roster = SessionRoster.Current;
            return roster == null ? null : roster.Of(clientId);
        }
    }
}
