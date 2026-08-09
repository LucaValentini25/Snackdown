using System;
using Unity.Collections;
using Unity.Netcode;

namespace Snackdown.Connection
{
    /// <summary>One player's place in the session, as everyone else sees them.</summary>
    /// <remarks>
    /// Every field here has already been through <see cref="ConnectionApproval"/>. The nickname is
    /// the sanitized one and the character index is the clamped one, so anything reading a slot is
    /// reading a checked value rather than whatever that client asked for. Nothing a client sends
    /// reaches this struct unexamined.
    /// </remarks>
    public struct PlayerSlot : INetworkSerializable, IEquatable<PlayerSlot>
    {
        public ulong ClientId;

        /// <summary>Sanitized display name. Never the raw string the client sent.</summary>
        public FixedString32Bytes Nickname;

        /// <summary>Clamped skin index.</summary>
        public int CharacterIndex;

        /// <summary>Whether this player has said they are ready to start.</summary>
        public bool IsReady;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Nickname);
            serializer.SerializeValue(ref CharacterIndex);
            serializer.SerializeValue(ref IsReady);
        }

        /// <remarks>
        /// <c>NetworkList</c> requires this to work out which entries changed. Comparing every field
        /// rather than just the id is deliberate: an equality that ignored <see cref="IsReady"/>
        /// would make a ready-up look like no change at all, and the list would never replicate it.
        /// </remarks>
        public bool Equals(PlayerSlot other)
            => ClientId == other.ClientId
               && Nickname.Equals(other.Nickname)
               && CharacterIndex == other.CharacterIndex
               && IsReady == other.IsReady;

        public override bool Equals(object obj) => obj is PlayerSlot other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ClientId, Nickname, CharacterIndex, IsReady);

        public override string ToString() => $"{Nickname} (#{ClientId}){(IsReady ? " ready" : "")}";
    }
}
