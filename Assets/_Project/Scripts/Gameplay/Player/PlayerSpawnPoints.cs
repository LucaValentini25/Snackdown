using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Places each connecting player at a free spawn point. Server-only, like every decision that
    /// affects the match.
    /// </summary>
    /// <remarks>
    /// The placement is applied through <see cref="PredictedPlayer.ServerTeleport"/> and reaches
    /// clients the same way everything else does — in the next snapshot. No RPC, no "force position
    /// for five frames" routine like the original project needed: with a single authority over
    /// position, a teleport is just a state change.
    /// </remarks>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [Tooltip("Spawn positions, used in order. Wraps around if there are more players than points.")]
        [SerializeField] Transform[] _points;

        int _nextIndex;

        void Start()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        void OnClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            StartCoroutine(PlaceWhenSpawned(clientId, _nextIndex++));
        }

        IEnumerator PlaceWhenSpawned(ulong clientId, int index)
        {
            // The player object usually exists by now, but the spawn is not guaranteed to have
            // completed on the very frame the connection callback fires.
            for (int attempt = 0; attempt < 30; attempt++)
            {
                NetworkObject player = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
                if (player != null && player.TryGetComponent(out PredictedPlayer predicted))
                {
                    predicted.ServerTeleport(GetSpawnPosition(index));
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"[Snackdown] No player object for client {clientId}; spawn point not applied.");
        }

        public Vector2 GetSpawnPosition(int index)
        {
            if (_points == null || _points.Length == 0) return Vector2.zero;
            return _points[index % _points.Length].position;
        }
    }
}
