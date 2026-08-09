using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// The numbers that decide how a match feels: how fast life drains, how long it can get, how
    /// long a round lasts.
    /// </summary>
    /// <remarks>
    /// An asset rather than constants because these are the values that get changed dozens of times
    /// while a game is being tuned, and recompiling between each one is how tuning stops happening.
    /// Same reasoning as <c>MovementConfig</c>.
    /// </remarks>
    [CreateAssetMenu(fileName = "MatchConfig", menuName = "Snackdown/Match Config")]
    public class MatchConfig : ScriptableObject
    {
        [Header("Life")]
        [Tooltip("Seconds a player starts with.")]
        public float StartingLife = 60f;

        [Tooltip("Ceiling on banked life, so a hoarder cannot become unkillable.")]
        public float MaxLife = 90f;

        [Tooltip("Seconds of life lost per second of play.")]
        public float DrainPerSecond = 1f;

        [Header("Round")]
        [Tooltip("Hard stop. At zero the player with the most life left wins.")]
        public float RoundSeconds = 180f;

        [Tooltip("How often life is replicated, in times per second.")]
        [Range(0.5f, 10f)]
        public float LifeReplicationHz = 1f;

        /// <summary>Seconds between replicated life updates.</summary>
        /// <remarks>
        /// The original replicated life on every frame, because it subtracted <c>Time.deltaTime</c>
        /// from a <c>NetworkVariable</c> — sixty writes a second, per player, for a number that
        /// changes by a hundredth each time and is rendered as whole seconds. Replicating once a
        /// second is roughly a sixtieth of that traffic and looks identical on screen.
        /// </remarks>
        public float ReplicationInterval => 1f / Mathf.Max(0.5f, LifeReplicationHz);
    }
}
