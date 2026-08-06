using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Netcode
{
    /// <summary>
    /// Drives the fixed network tick and imposes a deterministic order on it: everyone predicts,
    /// then the server simulates, then the server publishes one snapshot for the whole world.
    /// </summary>
    /// <remarks>
    /// <para>Every character could subscribe to the tick individually, but then the order in which
    /// they ran — and whether the snapshot went out before or after a given player had moved —
    /// would depend on spawn order. Ordering the phases explicitly in one place removes an entire
    /// category of "it desyncs sometimes" bugs, and makes the tick readable as a sequence.</para>
    /// <para>Note the two different clocks. Owners predict on <c>LocalTime</c>, which NGO runs
    /// deliberately <i>ahead</i> of the server so that inputs land just before the server needs
    /// them. The server simulates on <c>ServerTime</c>. Mixing the two up is a classic source of
    /// input that is perpetually one tick late.</para>
    /// </remarks>
    public class NetworkSimulationLoop : NetworkBehaviour
    {
        /// <summary>
        /// Registered characters. Static because each peer is its own process (and its own
        /// NetworkManager) — including under Multiplayer Play Mode.
        /// </summary>
        static readonly List<IPredictedPeer> Players = new List<IPredictedPeer>();

        public static IReadOnlyList<IPredictedPeer> ActivePlayers => Players;
        public static NetworkSimulationLoop Instance { get; private set; }

        PlayerSnapshot[] _snapshotScratch;

        /// <summary>Snapshots sent since the loop started. Read by the debug overlay.</summary>
        public uint SnapshotsSent { get; private set; }

        public static void Register(IPredictedPeer player)
        {
            if (!Players.Contains(player)) Players.Add(player);
        }

        public static void Unregister(IPredictedPeer player) => Players.Remove(player);

        public override void OnNetworkSpawn()
        {
            Instance = this;
            NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
                NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;

            Players.Clear();
            Instance = null;
        }

        void OnNetworkTick()
        {
            // --- phase 1: owners predict ----------------------------------------------------
            // Skipped on the host, whose owned character is simulated authoritatively below.
            uint localTick = (uint)NetworkManager.LocalTime.Tick;
            for (int i = 0; i < Players.Count; i++)
            {
                IPredictedPeer player = Players[i];
                if (player.IsOwner && !player.IsServer)
                    player.OwnerPredictTick(localTick);
            }

            if (!IsServer) return;

            // --- phase 2: the server simulates everyone ------------------------------------
            uint serverTick = (uint)NetworkManager.ServerTime.Tick;
            for (int i = 0; i < Players.Count; i++)
                Players[i].ServerSimulateTick(serverTick);

            // --- phase 3: publish, once, after every character has moved -------------------
            BroadcastSnapshot(serverTick);
        }

        void BroadcastSnapshot(uint tick)
        {
            if (Players.Count == 0) return;

            if (_snapshotScratch == null || _snapshotScratch.Length != Players.Count)
                _snapshotScratch = new PlayerSnapshot[Players.Count];

            for (int i = 0; i < Players.Count; i++)
                _snapshotScratch[i] = Players[i].BuildSnapshot();

            SnapshotRpc(new SnapshotFrame { Tick = tick, Players = _snapshotScratch });
            SnapshotsSent++;
        }

        /// <summary>
        /// The world, once per tick, unreliably. Reliability would be actively harmful here: a
        /// re-sent snapshot arrives describing a moment that has already been superseded, so the
        /// only thing waiting for it buys is latency.
        /// </summary>
        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void SnapshotRpc(SnapshotFrame frame)
        {
            double snapshotTime = frame.Tick / (double)NetworkManager.NetworkConfig.TickRate;
            if (frame.Players == null) return;

            for (int i = 0; i < frame.Players.Length; i++)
            {
                PlayerSnapshot snapshot = frame.Players[i];

                if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
                        snapshot.NetworkObjectId, out NetworkObject networkObject))
                    continue;   // spawn message hasn't landed yet; the next snapshot will do

                if (networkObject.TryGetComponent(out IPredictedPeer player))
                    player.ApplySnapshot(snapshot, snapshotTime);
            }
        }
    }
}
