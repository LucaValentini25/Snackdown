using System;
using System.Collections.Generic;
using Snackdown.Connection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// One player, for as long as they are connected — separate from the character they are
    /// currently wearing.
    /// </summary>
    /// <remarks>
    /// <para>Netcode for GameObjects gives every connection exactly one object, and the whole
    /// project has been treating that object as the character. That works right up to the moment a
    /// player has to outlive their body: dying should despawn the avatar and leave the player in the
    /// session as a spectator, a new round should hand them a fresh one, and changing skin in the
    /// lobby should not mean tearing down and rebuilding the thing that holds their name. Each of
    /// those is awkward on its own and stops being a special case once identity and avatar have
    /// separate lifetimes. See ADR D-001 on the board.</para>
    /// <para>Nothing reads this yet. It is spawned alongside the avatar and carries only the name,
    /// deliberately: the roster still owns identity until the task that turns it into an index, and
    /// two objects claiming to be the source of a player's nickname at the same time is precisely
    /// the failure that refactor exists to remove.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerSession : NetworkBehaviour
    {
        private static readonly List<PlayerSession> _all = new List<PlayerSession>();

        /// <summary>Every player currently in the session, in no particular order.</summary>
        /// <remarks>
        /// Kept by spawn and despawn rather than found by a scene search, the same way
        /// <see cref="PlayerLife.All"/> is, and for the same reason: this is about to be read every
        /// frame by anything that draws a player list, and <c>FindObjectsByType</c> at that rate is
        /// a cost that only surfaces once a profiler is open.
        /// </remarks>
        public static IReadOnlyList<PlayerSession> All => _all;

        /// <summary>The sanitized display name, written by the server and read by everyone.</summary>
        /// <remarks>
        /// A <see cref="FixedString32Bytes"/> rather than a string because it crosses the wire and
        /// NGO will not serialize a managed string in a <see cref="NetworkVariable{T}"/> without
        /// one. The cap it imposes is larger than the sixteen characters
        /// <see cref="ConnectionApproval.MaxNicknameLength"/> already allows through.
        /// </remarks>
        private readonly NetworkVariable<FixedString32Bytes> _nickname =
            new NetworkVariable<FixedString32Bytes>();

        /// <summary>The name this player was admitted under. Never the raw string they sent.</summary>
        public string Nickname => _nickname.Value.ToString();

        /// <summary>Raised on every peer when this player's name arrives or changes.</summary>
        public event Action<PlayerSession> NicknameChanged;

        /// <summary>The session belonging to a connected client, or null if there is none.</summary>
        public static PlayerSession Of(ulong clientId)
        {
            foreach (PlayerSession session in _all)
            {
                if (session.OwnerClientId == clientId) return session;
            }

            return null;
        }

        public override void OnNetworkSpawn()
        {
            _all.Add(this);
            _nickname.OnValueChanged += OnNicknamePublished;

            if (IsServer) AdoptApprovedIdentity();

            NicknameChanged?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _all.Remove(this);
            _nickname.OnValueChanged -= OnNicknamePublished;
        }

        /// <summary>
        /// Reads this player's checked name out of approval, on the server, once it exists.
        /// </summary>
        /// <remarks>
        /// <para>The session asks approval who it belongs to rather than being told by whoever
        /// spawned it. That is what keeps the spawner from needing to know this type at all — and
        /// it has to, because the object that spawns sessions today lives in
        /// <c>Snackdown.Connection</c>, which this assembly depends on. Handing the name in would
        /// mean a reference back the other way, and the two assemblies would stop compiling.</para>
        /// <para>The cost is one delta: the spawn message carries an empty name and the real one
        /// follows immediately after. Writing it before the spawn would avoid that and is what the
        /// task after this one can do, once the roster is an index over sessions and lives on this
        /// side of the assembly line.</para>
        /// </remarks>
        private void AdoptApprovedIdentity()
        {
            ConnectionApproval approval = ConnectionApproval.Current;

            // Falls back only when approval is not vetting connections at all. A client that came
            // through it always has a sanitized name waiting here.
            string admitted = approval?.NicknameOf(OwnerClientId)
                              ?? ConnectionApproval.SanitizeNickname(null, OwnerClientId);

            _nickname.Value = new FixedString32Bytes(admitted);
        }

        private void OnNicknamePublished(FixedString32Bytes previous, FixedString32Bytes current)
            => NicknameChanged?.Invoke(this);
    }
}
