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
    /// <para>This is now the only place a player's name, skin and ready state exist. The roster used
    /// to replicate a second copy of all three in a <c>NetworkList</c>, and two objects claiming to
    /// be the source of the same fact is exactly the failure this epic exists to remove — so the
    /// list went and <see cref="SessionRoster"/> became an index over these.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerSession : NetworkBehaviour
    {
        private static readonly List<PlayerSession> _all = new List<PlayerSession>();

        /// <summary>Every player currently in the session, in no particular order.</summary>
        /// <remarks>
        /// Kept by spawn and despawn rather than found by a scene search, the same way
        /// <see cref="PlayerLife.All"/> is, and for the same reason: this is read every frame by
        /// anything that draws a player list, and <c>FindObjectsByType</c> at that rate is a cost
        /// that only surfaces once a profiler is open.
        /// </remarks>
        public static IReadOnlyList<PlayerSession> All => _all;

        /// <summary>Raised when a session spawns or despawns — the set of players changed.</summary>
        /// <remarks>
        /// <para>Static because sessions come and go on their own schedule: there is no moment at
        /// which an interested object could subscribe to each of them individually, since the one
        /// that arrives next does not exist yet.</para>
        /// <para>Kept separate from <see cref="DetailsChanged"/> even though most subscribers redraw
        /// the same thing for both. Only this one invalidates an index built over
        /// <see cref="All"/> — and a subscriber that rebuilt its list on a field write would be
        /// doing it re-entrantly from inside its own loop over that list, the moment anything walks
        /// the roster setting a value on each entry.</para>
        /// </remarks>
        public static event Action<PlayerSession> MembershipChanged;

        /// <summary>Raised when a session's name, skin or ready state arrives or changes.</summary>
        public static event Action<PlayerSession> DetailsChanged;

        /// <summary>The sanitized display name, written by the server and read by everyone.</summary>
        /// <remarks>
        /// A <see cref="FixedString32Bytes"/> rather than a string because it crosses the wire and
        /// NGO will not serialize a managed string in a <see cref="NetworkVariable{T}"/> without
        /// one. The cap it imposes is larger than the sixteen characters
        /// <see cref="ConnectionApproval.MaxNicknameLength"/> already allows through.
        /// </remarks>
        private readonly NetworkVariable<FixedString32Bytes> _nickname =
            new NetworkVariable<FixedString32Bytes>();

        private readonly NetworkVariable<int> _characterIndex = new NetworkVariable<int>();

        private readonly NetworkVariable<bool> _isReady = new NetworkVariable<bool>();

        /// <summary>The name this player was admitted under. Never the raw string they sent.</summary>
        public string Nickname => _nickname.Value.ToString();

        /// <summary>The skin this player was admitted with, already clamped by approval.</summary>
        public int CharacterIndex => _characterIndex.Value;

        /// <summary>Whether this player has said they are ready to start.</summary>
        public bool IsReady => _isReady.Value;

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

            _nickname.OnValueChanged += OnNicknameChanged;
            _characterIndex.OnValueChanged += OnCharacterIndexChanged;
            _isReady.OnValueChanged += OnReadyChanged;

            if (IsServer) AdoptApprovedIdentity();

            MembershipChanged?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _all.Remove(this);

            _nickname.OnValueChanged -= OnNicknameChanged;
            _characterIndex.OnValueChanged -= OnCharacterIndexChanged;
            _isReady.OnValueChanged -= OnReadyChanged;

            // Raised after the removal, so a subscriber rebuilding a list from All during this call
            // gets one without this player rather than one it has to remember to skip.
            MembershipChanged?.Invoke(this);
        }

        /// <summary>
        /// Reads this player's checked name and skin out of approval, on the server, once it exists.
        /// </summary>
        /// <remarks>
        /// <para>The session asks approval who it belongs to rather than being handed it by whoever
        /// spawned it. That was originally forced: the spawner lived in <c>Snackdown.Connection</c>,
        /// which this assembly depends on, so naming this type there would not have compiled. The
        /// roster has since moved to this side of that line, and handing the values in would now
        /// build.</para>
        /// <para>It still is not done, and the reason is the console rather than the compiler.
        /// Writing these before <c>Spawn</c> is what would fold them into the spawn message instead
        /// of sending them as deltas immediately after it — but a <see cref="NetworkVariable{T}"/>
        /// written before its behaviour is initialised logs <i>"NetworkVariable is written to, but
        /// doesn't know its NetworkBehaviour yet"</i> on every single join, and the flag that
        /// suppresses that warning is internal to the package. Two deltas per join is the cheaper of
        /// the two.</para>
        /// </remarks>
        private void AdoptApprovedIdentity()
        {
            ConnectionApproval approval = ConnectionApproval.Current;

            // Falls back only when approval is not vetting connections at all. A client that came
            // through it always has a sanitized name and a clamped index waiting here.
            string admitted = approval?.NicknameOf(OwnerClientId)
                              ?? ConnectionApproval.SanitizeNickname(null, OwnerClientId);

            _nickname.Value = new FixedString32Bytes(admitted);
            _characterIndex.Value = approval?.CharacterOf(OwnerClientId) ?? 0;
        }

        // ==================================================================================
        //  Ready state
        // ==================================================================================

        /// <summary>Asks the server to flip this player's ready flag. Does nothing for anyone else.</summary>
        public void ToggleReady()
        {
            if (!IsOwner) return;

            SetReadyRpc(!IsReady);
        }

        /// <summary>Sets the flag without asking. Server-only; used when returning to the lobby.</summary>
        public void ServerSetReady(bool ready)
        {
            if (!IsServer) return;

            _isReady.Value = ready;
        }

        /// <remarks>
        /// The sender is checked against the owner rather than taken from the message body. On the
        /// server NGO overwrites <c>RpcParams.Receive.SenderClientId</c> with the transport's id, so
        /// it is the one identity a client cannot forge — and without the check any client could
        /// ready anyone else up, which is the cheapest possible way to start a match nobody agreed
        /// to.
        /// </remarks>
        [Rpc(SendTo.Server)]
        private void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            _isReady.Value = ready;
        }

        private void OnNicknameChanged(FixedString32Bytes previous, FixedString32Bytes current)
            => DetailsChanged?.Invoke(this);

        private void OnCharacterIndexChanged(int previous, int current) => DetailsChanged?.Invoke(this);

        private void OnReadyChanged(bool previous, bool current) => DetailsChanged?.Invoke(this);
    }
}
