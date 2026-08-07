using System;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Connection
{
    /// <summary>
    /// Who is in the session right now, replicated to everyone: names, chosen skins, ready state.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately free of any opinion about how it is drawn. A lobby screen, a HUD and an
    /// end-of-match scoreboard all need the same list, and the tech used to render it is still an
    /// open question — so this replicates the data and raises an event when it changes, and nothing
    /// more.</para>
    /// <para>A <see cref="NetworkList{T}"/> rather than a broadcast RPC, because a player joining
    /// midway needs the <i>current</i> roster and not the change they missed. NGO sends the whole
    /// list on spawn and deltas afterwards, which is exactly the difference between a late joiner
    /// seeing an empty lobby and seeing the three people already in it.</para>
    /// <para>Server-authoritative like everything else: clients ask to change their own ready state
    /// through an Rpc and never write to the list. The server owns membership.</para>
    /// </remarks>
    public class SessionRoster : NetworkBehaviour
    {
        readonly NetworkList<PlayerSlot> _slots = new NetworkList<PlayerSlot>();

        /// <summary>Raised on every peer whenever the roster changes, after the change is applied.</summary>
        public event Action Changed;

        public int Count => _slots.Count;

        public PlayerSlot this[int index] => _slots[index];

        /// <summary>True when there is at least one player and all of them are ready.</summary>
        /// <remarks>
        /// The emptiness check is not pedantry: an empty lobby trivially satisfies "everyone is
        /// ready", and without it a session with nobody in it would start a match.
        /// </remarks>
        public bool EveryoneReady
        {
            get
            {
                if (_slots.Count == 0) return false;

                foreach (PlayerSlot slot in _slots)
                    if (!slot.IsReady) return false;

                return true;
            }
        }

        public override void OnNetworkSpawn()
        {
            _slots.OnListChanged += OnSlotsChanged;

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += AddPlayer;
                NetworkManager.OnClientDisconnectCallback += RemovePlayer;

                // Clients already connected when this spawned would otherwise be missing — the host
                // itself is always one of them, since it connects before the scene objects exist.
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds) AddPlayer(clientId);
            }

            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _slots.OnListChanged -= OnSlotsChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= AddPlayer;
                NetworkManager.OnClientDisconnectCallback -= RemovePlayer;
            }
        }

        void OnSlotsChanged(NetworkListEvent<PlayerSlot> _) => Changed?.Invoke();

        void AddPlayer(ulong clientId)
        {
            if (!IsServer) return;
            if (IndexOf(clientId) >= 0) return;

            ConnectionApproval approval = ConnectionApproval.Current;

            _slots.Add(new PlayerSlot
            {
                ClientId = clientId,
                // Falls back only when approval is not in use at all; a client that connected
                // through it always has a checked name waiting here.
                Nickname = approval?.NicknameOf(clientId) ?? $"Player {clientId}",
                CharacterIndex = approval?.CharacterOf(clientId) ?? 0,
                IsReady = false
            });
        }

        void RemovePlayer(ulong clientId)
        {
            if (!IsServer) return;

            int index = IndexOf(clientId);
            if (index >= 0) _slots.RemoveAt(index);
        }

        int IndexOf(ulong clientId)
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].ClientId == clientId) return i;

            return -1;
        }

        // ==================================================================================
        //  Ready state
        // ==================================================================================

        /// <summary>Asks the server to flip this client's ready flag.</summary>
        public void ToggleReady() => SetReadyRpc(!IsReadyLocally());

        bool IsReadyLocally()
        {
            int index = IndexOf(NetworkManager.LocalClientId);
            return index >= 0 && _slots[index].IsReady;
        }

        /// <remarks>
        /// The client id comes from <c>RpcParams</c>, never from the message body. A sender-supplied
        /// id would let any client mark anyone else ready — the cheapest possible way to start a
        /// match nobody agreed to.
        /// </remarks>
        [Rpc(SendTo.Server)]
        void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;

            int index = IndexOf(senderId);
            if (index < 0) return;

            PlayerSlot slot = _slots[index];
            if (slot.IsReady == ready) return;

            slot.IsReady = ready;
            _slots[index] = slot;
        }

        /// <summary>Clears every ready flag. Server-only; used when returning to the lobby.</summary>
        public void ServerClearReady()
        {
            if (!IsServer) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                PlayerSlot slot = _slots[i];
                if (!slot.IsReady) continue;

                slot.IsReady = false;
                _slots[i] = slot;
            }
        }

        /// <remarks>
        /// <c>NetworkBehaviour</c> defines its own <c>OnDestroy</c> and does real teardown in it, so
        /// this overrides rather than hides — declaring a plain method would silently skip NGO's
        /// own cleanup. The list holds native memory and leaks if it goes out with the managed
        /// object.
        /// </remarks>
        public override void OnDestroy()
        {
            _slots?.Dispose();
            base.OnDestroy();
        }
    }
}
