using UnityEngine;

namespace Snackdown.Gameplay.Fruits
{
    /// <summary>
    /// Which fruit can spawn, how likely each is, and how much life it is worth.
    /// </summary>
    /// <remarks>
    /// <para>Weights rather than percentages. Percentages have to sum to 100, so adding a fruit
    /// means re-balancing every other number by hand — which is how a table ends up summing to 97
    /// and nobody notices. Weights are relative and normalized on use.</para>
    /// <para>The server is the only thing that ever rolls against this table. A client that could
    /// choose which fruit spawned would spawn legendaries.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "FruitTable", menuName = "Snackdown/Fruit Table")]
    public class FruitTable : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string DisplayName;

            [Tooltip("Relative likelihood. Higher is more common; these do not need to sum to anything.")]
            [Min(0f)] public float Weight;

            [Tooltip("Seconds of life this fruit restores.")]
            [Min(0f)] public float LifeSeconds;

            [Tooltip("Single frame, for anywhere a fruit is shown standing still.")]
            public Sprite Sprite;

            [Tooltip("Frames of the idle spin, in order, at the rate PixelAnimation states.")]
            public Sprite[] Frames;
        }

        [SerializeField] Entry[] _entries = new Entry[0];

        [Tooltip("Frames of the burst played where a fruit was picked up. Shared by every kind.")]
        [SerializeField] Sprite[] _collected = new Sprite[0];

        /// <summary>
        /// The burst played where a fruit was collected, the same for every kind.
        /// </summary>
        /// <remarks>
        /// On the table rather than on each entry because the pack draws it once and it reads as
        /// the act of collecting, not as a property of the apple. Per-entry it would be eight
        /// references to one sheet and eight chances to leave one of them empty.
        /// </remarks>
        public Sprite[] Collected => _collected;

        public int Count => _entries.Length;
        public Entry Get(int index) => _entries[Mathf.Clamp(index, 0, _entries.Length - 1)];

        /// <summary>
        /// Picks an entry index at random, weighted. Returns -1 when the table cannot produce one.
        /// </summary>
        /// <remarks>
        /// Takes the roll as an argument rather than calling <c>Random</c> itself, so the choice is
        /// testable without a seed and the caller stays in control of where randomness enters — the
        /// server, and only the server.
        /// </remarks>
        public int Roll(float roll01)
        {
            float total = 0f;
            for (int i = 0; i < _entries.Length; i++) total += Mathf.Max(0f, _entries[i].Weight);

            if (total <= 0f) return -1;

            float target = Mathf.Clamp01(roll01) * total;
            float running = 0f;

            for (int i = 0; i < _entries.Length; i++)
            {
                running += Mathf.Max(0f, _entries[i].Weight);
                if (target <= running) return i;
            }

            // Only reachable when roll01 is exactly 1 and floating point lands short of the total.
            return _entries.Length - 1;
        }

        /// <summary>The chance of rolling a given entry, as a percentage. For tuning and docs.</summary>
        public float ChanceOf(int index)
        {
            float total = 0f;
            for (int i = 0; i < _entries.Length; i++) total += Mathf.Max(0f, _entries[i].Weight);

            if (total <= 0f || index < 0 || index >= _entries.Length) return 0f;
            return 100f * Mathf.Max(0f, _entries[index].Weight) / total;
        }

        /// <summary>Reports the first problem that would only show up as a fruit that never spawns.</summary>
        public string Validate()
        {
            if (_entries.Length == 0) return "No fruit configured.";

            float total = 0f;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Sprite == null) return $"Fruit {i} ('{_entries[i].DisplayName}') has no sprite.";
                total += Mathf.Max(0f, _entries[i].Weight);
            }

            return total > 0f ? null : "Every fruit has zero weight, so none can ever spawn.";
        }
    }
}
