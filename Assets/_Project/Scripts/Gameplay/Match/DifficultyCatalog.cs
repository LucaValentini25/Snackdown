using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// The difficulty presets a host can start from, in the order their indices refer to.
    /// </summary>
    /// <remarks>
    /// <para>An asset rather than a hardcoded list, the same shape as <see cref="ArenaCatalog"/> and
    /// for the same reason: a preset is content, and adding one should be authoring. It carries the
    /// same warning too — the index is what crosses the wire when a host picks one, so entries are
    /// appended and never reordered.</para>
    /// <para>A preset <i>is</i> a <see cref="MatchConfig"/>, not a second description of one. The
    /// project already had that asset and the numbers already lived in it; inventing a parallel
    /// shape would mean two places to add a field to and one of them being forgotten.</para>
    /// <para>Presets are a starting point and not a cage. The host can change any number afterwards
    /// — see <see cref="MatchDirector.ServerSetSettings"/> — which is why this holds no notion of a
    /// preset being "in force". Once a match starts, what is in force is the numbers.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "DifficultyCatalog", menuName = "Snackdown/Difficulty Catalog")]
    public class DifficultyCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Preset
        {
            [Tooltip("Shown in the lobby when picking. Free text; never crosses the wire.")]
            public string DisplayName;

            [Tooltip("The numbers this preset starts the host from.")]
            public MatchConfig Rules;
        }

        [SerializeField] Preset[] _presets = new Preset[0];

        public int Count => _presets.Length;

        public Preset Get(int index)
        {
            if (_presets.Length == 0) return default;
            return _presets[Mathf.Clamp(index, 0, _presets.Length - 1)];
        }

        /// <summary>The numbers a preset stands for, already in the form that travels.</summary>
        public MatchSettings SettingsFor(int index) => MatchSettings.From(Get(index).Rules);

        /// <summary>
        /// Reports the first problem that would otherwise surface as a match with no rules, or null.
        /// </summary>
        /// <remarks>
        /// Checked while authoring rather than trusted at runtime, the same as the arena catalog. A
        /// preset with no config assigned would silently hand the session
        /// <see cref="MatchSettings.Fallback"/>, and a host who picked "Relaxed" and got the default
        /// numbers has no way to tell that from the preset simply being similar.
        /// </remarks>
        public string Validate()
        {
            if (_presets.Length == 0) return $"{name} has no presets.";

            for (int i = 0; i < _presets.Length; i++)
            {
                if (_presets[i].Rules == null)
                    return $"Preset {i} ({Describe(_presets[i].DisplayName)}) in {name} has no MatchConfig assigned.";
            }

            return null;
        }

        static string Describe(string displayName)
            => string.IsNullOrWhiteSpace(displayName) ? "unnamed" : displayName;
    }
}
