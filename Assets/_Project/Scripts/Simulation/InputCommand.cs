using Unity.Netcode;

namespace Snackdown.Simulation
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

        /// <summary>
        /// The same command with every field forced back into its declared range.
        /// </summary>
        /// <remarks>
        /// The declared range and the wire range are not the same thing: <see cref="MoveX"/> is
        /// documented as -1/0/1 but travels as a signed byte, and <see cref="Buttons"/> defines two
        /// bits out of eight. A server that trusts the declaration hands <see cref="PlayerMotor"/> a
        /// horizontal target of <c>MoveX * MoveSpeed</c>, where <c>MoveSpeed</c> stops being a
        /// ceiling the moment <c>MoveX</c> is not ±1.
        /// <para>This lives on the struct rather than at the call site because it is a property of
        /// the type: anything that receives an <see cref="InputCommand"/> from outside this process
        /// needs it, and the next such caller should not have to rediscover why.</para>
        /// </remarks>
        public static InputCommand Sanitized(in InputCommand command) => new InputCommand
        {
            Tick = command.Tick,
            MoveX = (sbyte)(command.MoveX > 0 ? 1 : command.MoveX < 0 ? -1 : 0),
            Buttons = (byte)(command.Buttons & (JumpHeldBit | JumpPressedBit))
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref Buttons);
        }

        public override string ToString() => $"[t{Tick} x{MoveX} b{Buttons}]";
    }
}
