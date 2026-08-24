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
    /// <para>This is the only place a player's name, skin, ready state, life and fruit count exist.
    /// The roster used to replicate a second copy of the first three in a <c>NetworkList</c>, and two
    /// objects claiming to be the source of the same fact is exactly the failure this epic exists to
    /// remove — so the list went and <see cref="SessionRoster"/> became an index over these.</para>
    /// <para>The life itself is a component of its own, <see cref="PlayerLife"/>, sitting on this
    /// same object. It moved here off the avatar: if the avatar has to survive death to keep the
    /// number, it stays the owner of identity and this whole separation achieves nothing. See ADR
    /// D-003.</para>
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

        /// <summary>Raised when a session's name, skin, ready state or fruit count changes.</summary>
        /// <remarks>
        /// Not raised for life. That number moves every frame on the server and about once a second
        /// on everyone else, and the views that show it already read it per frame — routing it
        /// through an event would be a redraw notification arriving at the rate of a redraw.
        /// </remarks>
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

        /// <summary>How much fruit this player has collected since they connected.</summary>
        /// <remarks>
        /// Counted for the whole connection rather than reset with the round, because a player's
        /// total is the only version of this number that survives what the avatar does — which is
        /// the property the epic is buying. A per-round figure is a subtraction away if a scoreboard
        /// ever wants one.
        /// </remarks>
        private readonly NetworkVariable<int> _fruitEaten = new NetworkVariable<int>();

        /// <summary>The life clock sitting beside this component. Cached; never null on the prefab.</summary>
        private PlayerLife _life;

        /// <summary>The name this player was admitted under. Never the raw string they sent.</summary>
        public string Nickname => _nickname.Value.ToString();

        /// <summary>The skin this player was admitted with, already clamped by approval.</summary>
        public int CharacterIndex => _characterIndex.Value;

        /// <summary>Whether this player has said they are ready to start.</summary>
        public bool IsReady => _isReady.Value;

        /// <summary>Pieces of fruit collected since this player connected.</summary>
        public int FruitEaten => _fruitEaten.Value;

        /// <summary>This player's life clock, which lives on this object rather than on the avatar.</summary>
        public PlayerLife Life => _life;

        /// <summary>
        /// The session a client has on one peer, or null if it has not arrived there yet.
        /// </summary>
        /// <remarks>
        /// The peer is a parameter and not implied, because <see cref="All"/> is a static and the
        /// Play mode harness runs a host and its clients inside one process — so it holds several
        /// peers' copies of the same player, and the first match is not reliably the caller's. Asking
        /// for the wrong one is not a wrong read but a wrong <i>write</i>: a server calling
        /// <see cref="ServerCollectFruit"/> on a client's copy gets a write-permission error from
        /// NGO and the fruit is silently never banked. In a shipped build every copy belongs to the
        /// only peer there is and the argument costs nothing.
        /// </remarks>
        public static PlayerSession Of(NetworkManager peer, ulong clientId)
        {
            foreach (PlayerSession session in _all)
            {
                if (session.OwnerClientId == clientId && session.NetworkManager == peer) return session;
            }

            return null;
        }

        /// <remarks>
        /// A sibling component rather than a <c>RequireComponent</c> attribute, which was tried and
        /// reverted: Unity acts on that attribute in the editor, so declaring it while
        /// <see cref="PlayerLife"/> was still on the avatar prefab had the editor silently add a
        /// second <see cref="PlayerSession"/> there. An attribute that edits assets is not a
        /// constraint, it is a migration nobody asked for. The check below is the same guarantee
        /// without the side effect.
        /// </remarks>
        private void Awake() => _life = GetComponent<PlayerLife>();

        public override void OnNetworkSpawn()
        {
            _all.Add(this);

            _nickname.OnValueChanged += OnNicknameChanged;
            _characterIndex.OnValueChanged += OnCharacterIndexChanged;
            _isReady.OnValueChanged += OnReadyChanged;
            _fruitEaten.OnValueChanged += OnFruitEatenChanged;

            if (_life == null)
            {
                Debug.LogError(
                    $"[Snackdown] {name} has no PlayerLife beside it; this player has no life and "
                    + "can neither run out nor collect fruit.", this);
            }

            if (IsServer) AdoptApprovedIdentity();

            MembershipChanged?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _all.Remove(this);

            _nickname.OnValueChanged -= OnNicknameChanged;
            _characterIndex.OnValueChanged -= OnCharacterIndexChanged;
            _isReady.OnValueChanged -= OnReadyChanged;
            _fruitEaten.OnValueChanged -= OnFruitEatenChanged;

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
        /// roster has since moved to this side of that line, so handing the values in would now
        /// build — and there is no longer any reason to.</para>
        /// <para><b>Here is already early enough.</b> <c>NetworkObject.SpawnInternal</c> runs the
        /// server's own spawn — this method with it — and only then calls
        /// <c>SendSpawnCallForObject</c>, which serializes each variable's <i>current</i> value. So
        /// these two are inside the spawn message every client receives, not deltas trailing it. The
        /// note left on <c>ps-1</c> claiming otherwise, and D-015 which reasoned from it, were both
        /// wrong about the cost — writing before <c>Spawn</c> would buy nothing and add the
        /// <i>"doesn't know its NetworkBehaviour yet"</i> warning on every join, because NGO does not
        /// attach a variable to its behaviour until the object spawns.</para>
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
        //  Fruit
        // ==================================================================================

        /// <summary>Banks a piece of fruit: the life it is worth, and one on the counter.</summary>
        /// <remarks>
        /// <para>One call rather than two, so that a caller cannot credit the life and forget the
        /// count or the other way round. <c>Fruit</c> is the only caller and it does not need
        /// to know that a player is two components.</para>
        /// <para>Returns whether the fruit was taken. A dead player is not a pickup, and the fruit
        /// has to know that before it despawns itself — otherwise walking a corpse over an apple
        /// removes the apple and gives nobody anything.</para>
        /// </remarks>
        public bool ServerCollectFruit(float lifeSeconds)
        {
            if (!IsServer) return false;
            if (_life == null || !_life.IsAlive) return false;

            _life.ServerAdd(lifeSeconds);
            _fruitEaten.Value++;

            return true;
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

        private void OnFruitEatenChanged(int previous, int current) => DetailsChanged?.Invoke(this);
    }
}
