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

        /// <summary>
        /// Set when this state is the result of the server repositioning the character rather than
        /// simulating it — a spawn placement, and from Phase 3 on, a respawn.
        /// </summary>
        /// <remarks>
        /// Without this the client cannot tell the two apart, because they look identical on the
        /// wire: a state far from what was predicted. It would treat a deliberate teleport as a
        /// prediction failure, replay inputs across it, and count a correction — which quietly
        /// poisons the one statistic the whole layer is judged by. A spawn placement alone
        /// contributed an error of 3.8 units against a real correction of 0.29.
        /// <para>The server keeps setting it for a few consecutive snapshots. Snapshots travel
        /// unreliably, so a flag sent once is a flag that can be lost — the same reasoning that
        /// makes input redundant rather than retransmitted.</para>
        /// </remarks>
        public bool IsTeleport;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetworkObjectId);
            State.NetworkSerialize(serializer);
            serializer.SerializeValue(ref LastProcessedInputTick);
            serializer.SerializeValue(ref IsTeleport);
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
        /// <summary>Largest player count a frame is allowed to claim on read.</summary>
        /// <remarks>
        /// Deliberately its own constant rather than a reference to
        /// <see cref="WorldSnapshotBuffer.MaxBodies"/>: that one bounds how many <i>peers</i> a tick
        /// remembers for collision, this one bounds how many <i>entries</i> a datagram may describe.
        /// They happen to be the same number today and mean different things, so tying them together
        /// would make the next change to either one wrong in a way that is hard to see.
        /// </remarks>
        public const int MaxPlayers = 8;

        public uint Tick;
        public PlayerSnapshot[] Players;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);

            int count = Players?.Length ?? 0;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                // A length read off the wire is a length someone else chose. Allocating first and
                // sanity-checking afterwards turns four bytes into any array size an int can
                // express, so the bound comes first and the frame is dropped rather than trusted.
                if (count < 0 || count > MaxPlayers)
                {
                    Players = null;
                    return;
                }

                Players = new PlayerSnapshot[count];
            }

            for (int i = 0; i < count; i++)
                Players[i].NetworkSerialize(serializer);
        }
    }
}
