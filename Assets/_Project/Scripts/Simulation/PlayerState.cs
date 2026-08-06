using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Simulation
{
    /// <summary>
    /// The complete simulation state of one character at one tick.
    /// </summary>
    /// <remarks>
    /// "Complete" is the load-bearing word: this holds EVERYTHING <see cref="PlayerMotor"/> reads.
    /// If a field the motor uses were missing here, a client that rewound to a server state would
    /// resume with a stale value for it, and the replay would produce a different answer than the
    /// server got — a desync that looks random and is miserable to debug. That's why the coyote
    /// and jump-buffer timers are in here and on the wire, even though they cost bytes.
    /// </remarks>
    public struct PlayerState : INetworkSerializable
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public bool Grounded;

        /// <summary>Time left to still jump after leaving a ledge.</summary>
        public float CoyoteTimer;

        /// <summary>Time left for a jump pressed in mid-air to fire on landing.</summary>
        public float JumpBufferTimer;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref Grounded);
            serializer.SerializeValue(ref CoyoteTimer);
            serializer.SerializeValue(ref JumpBufferTimer);
        }

        /// <summary>
        /// How far this state sits from an authoritative one. Only position is compared:
        /// a small velocity difference that hasn't moved anything yet is not worth a correction,
        /// and it would be corrected implicitly on the next tick anyway.
        /// </summary>
        public float PositionErrorTo(in PlayerState authoritative)
            => Vector2.Distance(Position, authoritative.Position);

        public static PlayerState AtPosition(Vector2 position) => new PlayerState { Position = position };

        public override string ToString() => $"pos{Position} vel{Velocity} g{(Grounded ? 1 : 0)}";
    }
}
