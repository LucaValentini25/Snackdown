using Snackdown.Gameplay.Match;

namespace Snackdown.UI
{
    /// <summary>
    /// When the escape menu is allowed to be on screen.
    /// </summary>
    /// <remarks>
    /// <para>Split from the component for the reason the spectator ring was: the interesting part is
    /// a rule, the rule has five phases and two connection states to get right, and a rule reachable
    /// only by pressing Escape in a running match is a rule nothing tests.</para>
    /// <para>It answers one question and is asked it twice — once when Escape is pressed, and once
    /// every frame while the menu is up. That second call is the point. A match starting, a host
    /// leaving or a round ending can all happen while somebody is reading the menu, and a menu that
    /// only checked on the way in would sit over a loading screen it has no business covering.</para>
    /// </remarks>
    public static class EscapeMenu
    {
        /// <summary>
        /// Whether the menu belongs on screen right now.
        /// </summary>
        /// <param name="inSession">Whether this machine is connected to a match at all.</param>
        /// <remarks>
        /// <para>Only inside a session. With no session the main menu is what is being looked at,
        /// and it carries its own way out — a second one layered over it would be two quit buttons
        /// on one screen, and the one underneath would still be clickable.</para>
        /// <para>Not while loading or counting down. The loading screen covers those phases whole,
        /// so a menu opened there would either be hidden behind it or drawn over a screen the player
        /// cannot act on; and leaving a session mid-load is the one moment that would interrupt a
        /// networked scene load that has already started.</para>
        /// </remarks>
        public static bool MayBeOpen(bool inSession, MatchPhase phase)
        {
            if (!inSession) return false;

            return phase == MatchPhase.Lobby
                || phase == MatchPhase.Playing
                || phase == MatchPhase.Ended;
        }
    }
}
