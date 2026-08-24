using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// The character skins a player can pick, in the order their indices refer to.
    /// </summary>
    /// <remarks>
    /// <para>Order is the contract. A <see cref="PlayerSession"/> carries an integer, and that
    /// integer means "the entry at this position" — so reordering this asset silently changes what
    /// everyone already picked. Add at the end.</para>
    /// <para>Skins are cosmetic by design: <c>docs/01</c> settles that the four characters are
    /// mechanically identical. Nothing here reaches the simulation, which is why a wrong or hostile
    /// index is a visual mistake and never an advantage.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "CharacterCatalog", menuName = "Snackdown/Character Catalog")]
    public class CharacterCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Shown in the lobby and character select.")]
            public string DisplayName;

            [Tooltip("Idle frame used until animation arrives in a later phase.")]
            public Sprite Portrait;
        }

        [SerializeField] Entry[] _entries = new Entry[0];

        public int Count => _entries.Length;

        /// <summary>
        /// The entry for an index, clamped. Never throws: the index arrives from the network, and
        /// approval clamps it too — but a catalog that shrinks between builds would otherwise turn
        /// a stale save into an exception on spawn.
        /// </summary>
        public Entry Get(int index)
        {
            if (_entries.Length == 0) return default;
            return _entries[Mathf.Clamp(index, 0, _entries.Length - 1)];
        }
    }
}
