using Unity.Netcode;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// One tick's worth of player intent. This is the ONLY thing a client is allowed to tell the
    /// server about its character — never a position, never a velocity.
    /// </summary>
    /// <remarks>
    /// Six bytes on the wire. MoveX is deliberately quantized to -1/0/1 instead of a float axis:
    /// prediction only works if the client and the server feed byte-identical numbers into
    /// <see cref="PlayerMotor"/>, and an analog axis would round differently on each side.
    /// </remarks>
    public struct InputCommand : INetworkSerializable
    {
        public const byte JumpHeldBit = 1 << 0;
        public const byte JumpPressedBit = 1 << 1;

        /// <summary>Network tick this input was sampled on. Everything is keyed by this.</summary>
        public uint Tick;

        /// <summary>Horizontal intent: -1, 0 or 1.</summary>
        public sbyte MoveX;

        /// <summary>Packed button state. See the *Bit constants.</summary>
        public byte Buttons;

        public bool JumpHeld => (Buttons & JumpHeldBit) != 0;

        /// <summary>True only on the tick the jump was first pressed — an edge, not a level.</summary>
        public bool JumpPressed => (Buttons & JumpPressedBit) != 0;

        public static byte Pack(bool jumpHeld, bool jumpPressed)
        {
            byte b = 0;
            if (jumpHeld) b |= JumpHeldBit;
            if (jumpPressed) b |= JumpPressedBit;
            return b;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref Buttons);
        }

        public override string ToString() => $"[t{Tick} x{MoveX} b{Buttons}]";
    }

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
