using UnityEngine;

namespace Snackdown.UI
{
    /// <summary>
    /// The name this machine offers in the menu: whatever was last played under, or one picked at
    /// random for somebody who has never played.
    /// </summary>
    /// <remarks>
    /// <para>It used to be <c>SystemInfo.deviceName</c> — the name of the computer. That is a dull
    /// default and a small leak: it travels to every other player through the roster, so a lobby
    /// could be reading somebody's laptop name without them ever having typed it.</para>
    /// <para>The first <c>PlayerPrefs</c> in the project. A name is a preference of the person at
    /// the keyboard rather than state of the game — nothing about a match depends on it, and it
    /// should survive the game being closed the way a volume slider does.</para>
    /// <para>What is remembered is what the player <i>typed</i>, not what they were admitted as.
    /// Approval numbers duplicates, so a player admitted as "Ana (2)" who came back as that would be
    /// admitted as "Ana (2) (2)" the next time somebody else was already Ana, and would drift a
    /// bracket further from their own name every evening.</para>
    /// </remarks>
    public static class NicknamePreference
    {
        const string Key = "snackdown.nickname";

        /// <summary>
        /// Names offered to somebody who has never typed one.
        /// </summary>
        /// <remarks>
        /// In code rather than in a ScriptableObject, unlike the arenas, the fruit and the skins.
        /// Those are tuned while the game is being balanced and are content; this is flavour on a
        /// text field that the player is expected to overwrite. If it ever wants translating or
        /// editing without a recompile, it becomes an asset then.
        /// </remarks>
        static readonly string[] Names =
        {
            "Apple Thief", "Banana Bandit", "Cherry Picker", "Sour Grape",
            "Melon Baller", "Peach Fuzz", "Kiwi Kid", "Plum Trouble",
            "Mango Menace", "Berry Bad", "Fig Newton", "Lime Light"
        };

        /// <summary>What to put in the name field when the menu opens.</summary>
        public static string Offered
        {
            get
            {
                string remembered = PlayerPrefs.GetString(Key, string.Empty);
                return string.IsNullOrWhiteSpace(remembered) ? Random() : remembered;
            }
        }

        /// <summary>
        /// Remembers a name the player actually went into a session with.
        /// </summary>
        /// <remarks>
        /// Saved on a successful host or join rather than on every keystroke. A name typed and then
        /// abandoned at the menu is not a choice, and remembering it would greet the player next
        /// time with something they backed out of.
        /// </remarks>
        public static void Remember(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname)) return;

            PlayerPrefs.SetString(Key, nickname.Trim());

            // Written now rather than at some point before quitting. A crash between here and the
            // next clean exit would otherwise lose it, and losing a preference silently is the kind
            // of thing that gets blamed on the feature not existing.
            PlayerPrefs.Save();
        }

        /// <summary>Forgets it, so the next launch offers a random one again.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        static string Random() => Names[UnityEngine.Random.Range(0, Names.Length)];
    }
}
