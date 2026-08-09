using Snackdown.Simulation;

namespace Snackdown.Netcode
{
    /// <summary>
    /// Remembers where every other character was on each recent tick, so a replay can re-run
    /// collisions against the world as it was rather than as it is now.
    /// </summary>
    /// <remarks>
    /// <para>This is what makes peer collision predictable rather than merely server-decided.
    /// Reconciliation re-simulates the owner across every tick since the server's last word, and
    /// each of those ticks needs the other players' positions <i>at that tick</i>. Reading them
    /// live would replay tick 40 against everyone's position at tick 52 and produce a state the
    /// server never computed — the replay would disagree with the very authority it is correcting
    /// toward.</para>
    /// <para>A ring keyed by <c>tick % Capacity</c>, like <see cref="PredictionBuffer"/>, and for
    /// the same reasons: no allocation, O(1) lookup, and each slot carries its own tick so a
    /// wrapped entry cannot masquerade as a fresh one.</para>
    /// <para>Capacity is deliberately smaller than the prediction buffer's. Peer positions are only
    /// needed for the ticks a replay actually covers — a fraction of a second — where predicted
    /// states are kept far longer for diagnostics.</para>
    /// </remarks>
    public class WorldSnapshotBuffer
    {
        /// <summary>~4 seconds at 30 Hz. Far more than any replay spans.</summary>
        public const int Capacity = 128;

        /// <summary>Bodies stored per tick. Matches the four-player ceiling in docs/01.</summary>
        public const int MaxBodies = 8;

        struct Frame
        {
            public uint Tick;
            public bool Occupied;
            public int Count;
        }

        readonly Frame[] _frames = new Frame[Capacity];
        readonly PeerBody[] _bodies = new PeerBody[Capacity * MaxBodies];

        static int IndexOf(uint tick) => (int)(tick % Capacity);

        /// <summary>Records the bodies present on a tick. Extra bodies past <see cref="MaxBodies"/> are dropped.</summary>
        public void Store(uint tick, PeerBody[] bodies, int count)
        {
            int frame = IndexOf(tick);
            int stored = count < MaxBodies ? count : MaxBodies;

            for (int i = 0; i < stored; i++)
                _bodies[frame * MaxBodies + i] = bodies[i];

            _frames[frame] = new Frame { Tick = tick, Occupied = true, Count = stored };
        }

        /// <summary>
        /// Builds the context for a tick. Falls back to an empty world when that tick is not held,
        /// which is the honest answer: replaying against a guess would be worse than replaying
        /// against nobody, since the server will correct either way.
        /// </summary>
        public SimulationContext ContextFor(uint tick, ulong selfId, PeerBody[] scratch)
        {
            int frame = IndexOf(tick);
            if (!_frames[frame].Occupied || _frames[frame].Tick != tick) return SimulationContext.Empty;

            int count = _frames[frame].Count;
            for (int i = 0; i < count; i++)
                scratch[i] = _bodies[frame * MaxBodies + i];

            return new SimulationContext(scratch, count, selfId);
        }

        public void Clear()
        {
            for (int i = 0; i < _frames.Length; i++) _frames[i] = default;
        }
    }
}
