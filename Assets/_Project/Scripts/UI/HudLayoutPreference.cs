using UnityEngine;

namespace Snackdown.UI
{
    /// <summary>
    /// This machine's own choice of life-bar layout, if it has made one.
    /// </summary>
    /// <remarks>
    /// <para>Separate from the room default on purpose. The host picks what a session opens with,
    /// because a demo should look the way whoever is running it wants; the person watching gets to
    /// disagree, because it changes nothing about the match — see ADR D-006. Replicating the
    /// override instead would mean a player whose taste differs from the host's plays all night with
    /// the layout they did not want, for no rule-level reason.</para>
    /// <para>"No choice" is a state, not a value. Somebody who has never pressed the key follows the
    /// room, and follows it again when the host changes it; somebody who has pressed it stops
    /// following. Storing a default rather than an absence would make the first player to open the
    /// game silently opinionated.</para>
    /// </remarks>
    public static class HudLayoutPreference
    {
        const string Key = "snackdown.hud.placement";

        /// <summary>True once this machine has chosen for itself.</summary>
        public static bool HasChoice => PlayerPrefs.HasKey(Key);

        /// <summary>What this machine chose. Meaningless unless <see cref="HasChoice"/>.</summary>
        public static LifeBarPlacement Choice =>
            (LifeBarPlacement)PlayerPrefs.GetInt(Key, (int)LifeBarPlacement.AlongTheBottom);

        /// <summary>Records a choice, and stops this machine following the room.</summary>
        public static void Choose(LifeBarPlacement placement)
        {
            PlayerPrefs.SetInt(Key, (int)placement);

            // Written now rather than at some point before quitting: losing a preference silently
            // is the kind of thing that gets blamed on the feature not existing.
            PlayerPrefs.Save();
        }

        /// <summary>Goes back to following whatever the room is set to.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
