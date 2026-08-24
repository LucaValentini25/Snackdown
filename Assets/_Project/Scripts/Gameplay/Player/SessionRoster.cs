using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Who is in the session right now: an ordered index over the live
    /// <see cref="PlayerSession"/> objects.
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
    /// <para>It used to spawn the sessions as well. Since <c>ps-4</c> they are NGO's own player
    /// object — <c>NetworkConfig.PlayerPrefab</c> points at the session prefab — so the connection
    /// that creates a player also creates their session, and this is left with nothing to do but
    /// order them and say when the list changed. See ADR D-004.</para>
    /// <para>Deliberately free of any opinion about how it is drawn. A lobby screen, a HUD and an
    /// end-of-match scoreboard all need the same list, so this exposes it and raises an event when
    /// it changes, and nothing more.</para>
    /// </remarks>
    public class SessionRoster : NetworkBehaviour
    {
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

            Rebuild();
        }

        public override void OnNetworkDespawn()
        {
            PlayerSession.MembershipChanged -= OnMembershipChanged;
            PlayerSession.DetailsChanged -= OnDetailsChanged;

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

        /// <summary>Clears every ready flag. Server-only; used when returning to the lobby.</summary>
        public void ServerClearReady()
        {
            if (!IsServer) return;

            foreach (PlayerSession player in _players) player.ServerSetReady(false);
        }

        // ==================================================================================
        //  Kicking
        // ==================================================================================

        /// <summary>What a removed player is told, in their own words rather than the log's.</summary>
        /// <remarks>
        /// Fixed rather than typed by the host. A free-text reason would be a second player-supplied
        /// string crossing the wire, and it would need everything the nickname needs — stripping,
        /// trimming, a length cap — to stop a host writing control characters into somebody else's
        /// screen. A host who wants to say more has a voice; this only has to say who did it, so
        /// being dropped does not read as the connection failing.
        /// </remarks>
        public const string KickReason = "The host removed you from the session.";

        /// <summary>Asks the server to remove a player. Only the host is obeyed.</summary>
        /// <remarks>
        /// The lobby only offers this to the host, but that is a button and buttons are not
        /// security. The request travels as an Rpc any client could send, and the server is where it
        /// is refused — see <see cref="KickRpc"/>.
        /// </remarks>
        public void RequestKick(ulong clientId) => KickRpc(clientId);

        /// <remarks>
        /// <para>The sender is compared against the server's own id rather than trusted from the
        /// body, the same way readying up is. NGO overwrites
        /// <c>RpcParams.Receive.SenderClientId</c> with the transport's id on the server, so it is
        /// the one identity a client cannot forge — and without this check any client could clear
        /// the lobby.</para>
        /// <para>Nothing here removes the player from this roster. NGO despawns everything a
        /// departing client owned, the session among it, and the roster is rebuilt from what is
        /// spawned — so the list is right because the objects are gone, not because two places were
        /// kept in step.</para>
        /// </remarks>
        [Rpc(SendTo.Server)]
        private void KickRpc(ulong clientId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;

            // A host kicking itself would shut the session down for everyone, which is a way to end
            // a game and not a way to remove a player.
            if (clientId == NetworkManager.ServerClientId) return;

            if (Of(clientId) == null) return;

            NetworkManager.DisconnectClient(clientId, KickReason);
        }
    }
}
