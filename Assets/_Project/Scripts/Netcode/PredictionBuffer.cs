using Snackdown.Gameplay.Player;

namespace Snackdown.Netcode
{
    /// <summary>
    /// The owning client's memory of what it did and what it expected: for every recent tick, the
    /// input that was sent and the state that input produced locally.
    /// </summary>
    /// <remarks>
    /// Reconciliation is only possible because of this buffer. When the server says "at tick N you
    /// were here", the client needs two things this holds: the state it <i>predicted</i> for tick N
    /// (to know whether it was wrong at all) and every input <i>after</i> N (to replay them on top
    /// of the correction, so the player doesn't lose the moves they've made since).
    /// <para>A ring indexed by <c>tick % Capacity</c> — no allocation, no shifting, O(1) lookup.
    /// Each slot stores its own tick so a stale wrapped-around entry can't masquerade as a fresh
    /// one.</para>
    /// </remarks>
    public class PredictionBuffer
    {
        /// <summary>~34 seconds at 30 Hz. Far more than any recoverable desync needs.</summary>
        public const int Capacity = 1024;

        struct Entry
        {
            public uint Tick;
            public bool Occupied;
            public InputCommand Input;
            public PlayerState State;
        }

        readonly Entry[] _entries = new Entry[Capacity];

        static int IndexOf(uint tick) => (int)(tick % Capacity);

        public void Store(uint tick, in InputCommand input, in PlayerState state)
        {
            _entries[IndexOf(tick)] = new Entry
            {
                Tick = tick,
                Occupied = true,
                Input = input,
                State = state
            };
        }

        /// <summary>Rewrites the predicted state of a tick after a replay produced a better answer.</summary>
        public void OverwriteState(uint tick, in PlayerState state)
        {
            int i = IndexOf(tick);
            if (_entries[i].Occupied && _entries[i].Tick == tick)
                _entries[i].State = state;
        }

        public bool TryGetState(uint tick, out PlayerState state)
        {
            int i = IndexOf(tick);
            if (_entries[i].Occupied && _entries[i].Tick == tick)
            {
                state = _entries[i].State;
                return true;
            }

            state = default;
            return false;
        }

        public bool TryGetInput(uint tick, out InputCommand input)
        {
            int i = IndexOf(tick);
            if (_entries[i].Occupied && _entries[i].Tick == tick)
            {
                input = _entries[i].Input;
                return true;
            }

            input = default;
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _entries.Length; i++)
                _entries[i] = default;
        }
    }
}
