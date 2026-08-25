using UnityEngine;

namespace Snackdown.Netcode
{
    /// <summary>
    /// Carrying a known state forward to a tick nobody has been told about yet.
    /// </summary>
    /// <remarks>
    /// <para>The arithmetic behind <see cref="PeerContactSource.Authoritative"/>. A client's newest
    /// authoritative state for a rival is always some ticks behind the one it is predicting, and
    /// this is the guess that closes the gap: keep going at the velocity you last saw.</para>
    /// <para>A static rather than a method on the character, so the two cases that matter — a tick
    /// that has already been described, and a gap long enough to send a rival through a wall — are
    /// tests rather than something you would have to reproduce with a lossy connection and two
    /// running players.</para>
    /// </remarks>
    public static class PeerExtrapolation
    {
        /// <summary>How far a last known velocity may be carried forward, in seconds.</summary>
        /// <remarks>
        /// A quarter of a second: longer than any round trip this project has measured and shorter
        /// than the time a running character takes to cross the arena. A rival whose snapshots have
        /// stopped arriving keeps the velocity they had when they vanished, and without a bound a
        /// player who lost half a second of traffic would predict contact against somebody three
        /// metres inside the scenery. Holding still is wrong in a way that stops getting worse.
        /// </remarks>
        public const float MaxSeconds = 0.25f;

        /// <summary>
        /// Where a body moving at <paramref name="velocity"/> would be at <paramref name="tick"/>.
        /// </summary>
        /// <param name="knownTick">The tick <paramref name="position"/> is true for.</param>
        /// <remarks>
        /// A tick at or before the known one is answered with the known position rather than by
        /// running the velocity backwards. Snapshots arrive out of order and a client's tick can be
        /// corrected backwards; inventing a past from a present velocity would be a guess about
        /// something the server has already stated.
        /// </remarks>
        public static Vector2 PositionAt(
            Vector2 position, Vector2 velocity, uint knownTick, uint tick, float tickDelta)
        {
            long gap = (long)tick - knownTick;
            if (gap <= 0) return position;

            float seconds = Mathf.Min(gap * tickDelta, MaxSeconds);
            return position + velocity * seconds;
        }
    }
}
