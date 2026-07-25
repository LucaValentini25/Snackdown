using Snackdown.Gameplay.Player;
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
    /// One frame per tick rather than one message per player — with four players that's a single
    /// ~120 byte datagram instead of four, and every state inside it shares a timestamp, which is
    /// what lets the interpolator line remote characters up against a common clock.
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
