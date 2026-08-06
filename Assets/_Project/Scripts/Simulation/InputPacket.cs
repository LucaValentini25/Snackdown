using Unity.Netcode;

namespace Snackdown.Simulation
{
    /// <summary>
    /// The redundancy window sent to the server every tick: the newest command plus the two
    /// before it. Input travels unreliably, so instead of asking for retransmission we simply
    /// send each command three times, in three consecutive packets.
    /// </summary>
    /// <remarks>
    /// Losing one packet costs nothing. Losing three in a row is the case the server covers by
    /// repeating the last input it did receive. Retransmission would be worse: it would add a
    /// round trip to data that is already stale by the time it arrives.
    /// </remarks>
    public struct InputPacket : INetworkSerializable
    {
        public InputCommand Newest;
        public InputCommand Previous;
        public InputCommand Oldest;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Newest.NetworkSerialize(serializer);
            Previous.NetworkSerialize(serializer);
            Oldest.NetworkSerialize(serializer);
        }
    }
}
