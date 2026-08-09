using UnityEngine;

namespace Snackdown.Simulation
{
    /// <summary>One other character, as the simulation needs to see it: a box at a position.</summary>
    /// <remarks>
    /// Deliberately not a reference to the other player. The motor must be replayable, and a replay
    /// runs against <i>past</i> ticks — asking a live object where it is would give its position
    /// now, not where it was, and the replay would produce an answer the server never computed.
    /// </remarks>
    public struct PeerBody
    {
        public ulong Id;
        public Vector2 Position;
        public Vector2 Size;
    }

    /// <summary>
    /// Everything outside a character that its simulation is allowed to look at, for one tick.
    /// </summary>
    /// <remarks>
    /// <para>The extension point for mechanics that need more than the character's own state.
    /// Traps, moving platforms and hazards belong here as they arrive: passed in as data, never
    /// read from the scene, so a replay of tick N sees the world as it was at tick N.</para>
    /// <para>Passing a struct with a borrowed array rather than a list keeps this allocation-free
    /// on the hot path — reconciliation can build one of these per tick while replaying.</para>
    /// </remarks>
    public readonly struct SimulationContext
    {
        /// <summary>Other characters this tick. May be empty; never null once <see cref="Count"/> is 0.</summary>
        readonly PeerBody[] _peers;

        /// <summary>How many entries of <see cref="_peers"/> are valid.</summary>
        public readonly int Count;

        /// <summary>The character being simulated, so it does not collide with itself.</summary>
        public readonly ulong SelfId;

        public SimulationContext(PeerBody[] peers, int count, ulong selfId)
        {
            _peers = peers;
            Count = count;
            SelfId = selfId;
        }

        public PeerBody Get(int index) => _peers[index];

        /// <summary>A context with no other characters — a solo test, or a match of one.</summary>
        public static SimulationContext Empty => new SimulationContext(System.Array.Empty<PeerBody>(), 0, 0);
    }
}
