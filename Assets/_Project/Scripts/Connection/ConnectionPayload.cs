using Unity.Collections;
using Unity.Netcode;

namespace Snackdown.Connection
{
    /// <summary>
    /// What a client sends about itself while connecting, before it is admitted to anything.
    /// </summary>
    /// <remarks>
    /// <para>This travels in NGO's connection data, which means it arrives <i>with</i> the handshake
    /// rather than after it. That timing is the entire point. The original project set the nickname
    /// once the character already existed, which meant racing the spawn — a retry loop and a
    /// <c>Task.Delay(100)</c> waiting for a value that had no reason to be there yet. Carrying it in
    /// the payload makes it available at the one moment it can still be used to refuse the
    /// connection.</para>
    /// <para>Every field here is <b>attacker-controlled</b>. Nothing in this struct is trusted; it
    /// is raw input that <see cref="ConnectionApproval"/> validates before anything else sees it.
    /// </para>
    /// </remarks>
    public struct ConnectionPayload : INetworkSerializable
    {
        /// <summary>
        /// Build identifier. Mismatched builds disagree about the simulation, so they are refused at
        /// the door rather than left to desync in confusing ways ten minutes later.
        /// </summary>
        public FixedString32Bytes GameVersion;

        /// <summary>Requested display name. Sanitized on arrival — never used as sent.</summary>
        public FixedString32Bytes Nickname;

        /// <summary>Requested character skin. Clamped on arrival.</summary>
        public int CharacterIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref GameVersion);
            serializer.SerializeValue(ref Nickname);
            serializer.SerializeValue(ref CharacterIndex);
        }

        /// <summary>Serializes to the byte array NGO expects in <c>NetworkConfig.ConnectionData</c>.</summary>
        public byte[] ToBytes()
        {
            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(this);
            return writer.ToArray();
        }

        /// <summary>
        /// Reads a payload sent by a client. Returns false on anything malformed — a truncated or
        /// garbage payload is a connection to refuse, not an exception to propagate out of the
        /// approval callback.
        /// </summary>
        public static bool TryRead(byte[] bytes, out ConnectionPayload payload)
        {
            payload = default;
            if (bytes == null || bytes.Length == 0) return false;

            try
            {
                using var reader = new FastBufferReader(bytes, Allocator.Temp);
                reader.ReadNetworkSerializable(out payload);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
