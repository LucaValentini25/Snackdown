using Snackdown.Simulation;
using Unity.Netcode;

namespace Snackdown.Netcode
{
    /// <summary>One player's authoritative state at one tick.</summary>
    public struct PlayerSnapshot : INetworkSerializable
    {
        /// <summary>Identifies which character this belongs to, across every peer.</summary>
        public ulong NetworkObjectId;

        public PlayerState State;

        /// <summary>
        /// The tick of the newest input the server had consumed when it produced this state.
        /// </summary>
        /// <remarks>
        /// This is the hinge of the whole reconciliation model. Without it the client would know
        /// <i>what</i> the server thinks but not <i>which of my inputs it had seen</i> — so it
        /// couldn't tell an honest disagreement from input that simply hadn't arrived yet, and it
        /// wouldn't know where to restart the replay.
        /// </remarks>
        public uint LastProcessedInputTick;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetworkObjectId);
            State.NetworkSerialize(serializer);
            serializer.SerializeValue(ref LastProcessedInputTick);
        }
    }

    /// <summary>
    /// Everything the server has to say about one tick: every player, in one packet.
    /// </summary>
    /// <remarks>
    /// One frame per tick rather than one message per player — with four players that is a single
    /// <b>172-byte payload</b> instead of four messages, and every state inside it shares a timestamp,
    /// which is what lets the interpolator line remote characters up against a common clock.
    /// <para>The size is <c>8 + 41N</c>: a 4-byte tick, a 4-byte count, and 41 bytes per player
    /// (8 for the object id, 29 for <see cref="PlayerState"/>, 4 for the acknowledged input tick).
    /// On the wire that becomes roughly 236 bytes direct or 276 relayed once NGO's RPC metadata and
    /// batch header, the transport framing and IP/UDP are added.</para>
    /// <para><b>Measured on 2026-08-25</b>, and the arithmetic above was short. At two players the
    /// profiler counts 3 001 B/s of RPC traffic leaving the host against the 2 700 B/s this payload
    /// predicts at 30 Hz — about eleven bytes a snapshot of NGO's own framing, which counting the
    /// serializers could not see. See <c>docs/05</c>.</para>
    /// <para>It was 42 bytes per player until <c>ps-4</c>. The forty-second was a teleport flag,
    /// there because the server repositioned an existing character at the start of a round and the
    /// owner had to be told not to count that as a prediction failure. The round now hands out a new
    /// character instead of moving the old one, so there is no reposition left to announce.</para>
    /// </remarks>
    public struct SnapshotFrame : INetworkSerializable
    {
        public uint Tick;
        public PlayerSnapshot[] Players;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);

            int count = Players?.Length ?? 0;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader) Players = new PlayerSnapshot[count];
            for (int i = 0; i < count; i++)
                Players[i].NetworkSerialize(serializer);
        }
    }
}
