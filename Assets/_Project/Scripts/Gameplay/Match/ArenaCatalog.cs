using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// The arenas a match can be played in.
    /// </summary>
    /// <remarks>
    /// <para>An asset rather than a hardcoded list because arenas are content: adding one should be
    /// authoring, not editing code. It is the same shape as the character catalog for the same
    /// reason, and carries the same warning — the index is what the network sends, so entries are
    /// appended, never reordered.</para>
    /// <para>Scenes are referenced by name rather than by <c>SceneAsset</c>, which only exists in
    /// the editor. A name that is not in Build Settings fails at runtime with a message nobody can
    /// act on, so <see cref="Validate"/> is what turns that into a problem you find while authoring.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "ArenaCatalog", menuName = "Snackdown/Arena Catalog")]
    public class ArenaCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Arena
        {
            [Tooltip("Shown when picking or announcing the arena.")]
            public string DisplayName;

            [Tooltip("Scene name, exactly as it appears in Build Settings.")]
            public string SceneName;
        }

        [SerializeField] Arena[] _arenas = new Arena[0];

        public int Count => _arenas.Length;

        public Arena Get(int index)
        {
            if (_arenas.Length == 0) return default;
            return _arenas[Mathf.Clamp(index, 0, _arenas.Length - 1)];
        }

        /// <summary>
        /// Reports the first problem that would only surface as a failed load, or null if there is
        /// none. Called before a match starts rather than trusted.
        /// </summary>
        public string Validate()
        {
            if (_arenas.Length == 0) return "No arenas configured.";

            for (int i = 0; i < _arenas.Length; i++)
                if (string.IsNullOrWhiteSpace(_arenas[i].SceneName))
                    return $"Arena {i} ('{_arenas[i].DisplayName}') has no scene name.";

            return null;
        }
    }
}
