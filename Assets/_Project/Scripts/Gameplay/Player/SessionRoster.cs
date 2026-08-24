using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Who is in the session right now: an ordered index over the live
    /// <see cref="PlayerSession"/> objects, plus the server-side job of creating them.
    /// </summary>
    /// <remarks>
    /// <para>It used to replicate the list itself, as a <c>NetworkList</c> of a <c>PlayerSlot</c>
    /// struct carrying each player's name, skin and ready flag. That was a second copy of facts the
    /// session object already owns, kept in step by hand — and a second copy is a copy that can
    /// disagree. Both are gone: the fields live on the session, this holds no networked state at
    /// all, and there is exactly one answer to "what is that player called".</para>
    /// <para>Nothing was lost on the wire by dropping the list. A late joiner still sees everyone
    /// already present, because NGO synchronizes the spawned session objects on join for the same
    /// reason it sent the whole list before — the difference is that the identity now arrives with
    /// the object that owns it instead of alongside it.</para>
    /// <para>Deliberately free of any opinion about how it is drawn. A lobby screen, a HUD and an
    /// end-of-match scoreboard all need the same list, so this exposes it and raises an event when
    /// it changes, and nothing more.</para>
    /// </remarks>
    public class SessionRoster : NetworkBehaviour
    {
        /// <summary>The per-connection session object spawned for every player who joins.</summary>
        /// <remarks>
        /// Still a plain <see cref="GameObject"/> rather than the <see cref="PlayerSession"/> it
        /// carries, even though this file now lives in the same assembly and could name the type.
        /// The field has one task left: <c>ps-4</c> makes the session NGO's own
        /// <c>NetworkConfig.PlayerPrefab</c>, which is a <see cref="GameObject"/> and is assigned in
        /// the Inspector — retyping this one to lose it two tasks later would mean re-authoring the
        /// prefab reference twice for nothing.
        /// </remarks>
        [SerializeField] private GameObject _sessionPrefab;

        /// <summary>Ordered by owner id, so every peer draws the lobby in the same order.</summary>
        /// <remarks>
        /// <see cref="PlayerSession.All"/> is in spawn order, which is not something a client can
        /// rely on matching anyone else's. Sorting by the id NGO assigned is the cheapest ordering
        /// that every peer agrees on, and it is stable across a rebuild — a lobby list whose rows
        /// swap places when somebody readies up reads as a bug.
        /// </remarks>
        private readonly List<PlayerSession> _players = new List<PlayerSession>();

        private static readonly Comparison<PlayerSession> ByOwnerId =
            (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId);

        /// <summary>Raised on every peer whenever the roster changes, after the change is applied.</summary>
        public event Action Changed;

        public int Count => _players.Count;

        public PlayerSession this[int index] => _players[index];

        /// <summary>This peer's own session, or null before it has arrived.</summary>
        /// <remarks>
        /// Null-checks the manager because the lobby holds a reference to this object from the
        /// moment the scene loads, which is before anyone has hosted or joined and therefore before
        /// there is a local client id to ask about.
        /// </remarks>
        public PlayerSession Local
            => NetworkManager == null ? null : Of(NetworkManager.LocalClientId);

        /// <summary>True when there is at least one player and all of them are ready.</summary>
        /// <remarks>
        /// The emptiness check is not pedantry: an empty lobby trivially satisfies "everyone is
        /// ready", and without it a session with nobody in it would start a match.
        /// </remarks>
        public bool EveryoneReady
        {
            get
            {
                if (_players.Count == 0) return false;

                foreach (PlayerSession player in _players)
                    if (!player.IsReady) return false;

                return true;
            }
        }

        /// <summary>The roster for the running session, if there is one.</summary>
        /// <remarks>
        /// The same ambient-static pattern as <c>MatchDirector.Current</c>, and here for a concrete
        /// reason: the in-match HUD reads names every frame, and a scene search at that rate is a
        /// cost that only shows up once a profiler is open.
        /// </remarks>
        public static SessionRoster Current { get; private set; }

        /// <summary>The session of a connected client, or null if there is none.</summary>
        public PlayerSession Of(ulong clientId)
        {
            foreach (PlayerSession player in _players)
                if (player.OwnerClientId == clientId) return player;

            return null;
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            PlayerSession.MembershipChanged += OnMembershipChanged;
            PlayerSession.DetailsChanged += OnDetailsChanged;

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += SpawnSession;

                // Clients already connected when this spawned would otherwise never get a session —
                // the host itself is always one of them, since it connects before the scene objects
                // exist. Nothing removes them: NGO destroys an owned object when its owner
                // disconnects, which is exactly the lifetime wanted.
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds) SpawnSession(clientId);
            }

            Rebuild();
        }

        public override void OnNetworkDespawn()
        {
            PlayerSession.MembershipChanged -= OnMembershipChanged;
            PlayerSession.DetailsChanged -= OnDetailsChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= SpawnSession;
            }

            if (ReferenceEquals(Current, this)) Current = null;
        }

        private void OnMembershipChanged(PlayerSession _) => Rebuild();

        private void OnDetailsChanged(PlayerSession _) => Changed?.Invoke();

        /// <remarks>
        /// Rebuilt wholesale rather than patched. The list is capped at four entries by approval, so
        /// the sort costs less than the bookkeeping that would work out which single row moved — and
        /// a full rebuild cannot drift out of step with what is actually spawned, which patching
        /// can.
        /// </remarks>
        private void Rebuild()
        {
            _players.Clear();

            foreach (PlayerSession session in PlayerSession.All)
            {
                // PlayerSession.All is a static, and the two-peer Play mode harness runs a host and
                // its clients inside one process — so it holds sessions belonging to peers other
                // than this one. A NetworkBehaviour knows which NetworkManager it was spawned by.
                if (session.NetworkManager == NetworkManager) _players.Add(session);
            }

            _players.Sort(ByOwnerId);

            Changed?.Invoke();
        }

        /// <summary>
        /// Gives a newly admitted client its own session object, owned by that client.
        /// </summary>
        /// <remarks>
        /// Spawned with ownership rather than left on the server so that the client can ask things
        /// of its own session — readying up now, changing skin once the wardrobe lands — through
        /// Rpcs the server still validates. Ownership is who may ask, not who decides.
        /// </remarks>
        private void SpawnSession(ulong clientId)
        {
            if (!IsServer) return;
            if (Of(clientId) != null) return;

            if (_sessionPrefab == null)
            {
                Debug.LogError($"[Snackdown] {name} has no session prefab; players will have no identity.", this);
                return;
            }

            GameObject session = Instantiate(_sessionPrefab);
            session.GetComponent<NetworkObject>().SpawnWithOwnership(clientId);
        }

        /// <summary>Clears every ready flag. Server-only; used when returning to the lobby.</summary>
        public void ServerClearReady()
        {
            if (!IsServer) return;

            foreach (PlayerSession player in _players) player.ServerSetReady(false);
        }
    }
}
