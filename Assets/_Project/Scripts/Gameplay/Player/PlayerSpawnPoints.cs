using Snackdown.Gameplay.Match;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Places every player at a free spawn point when a match begins. Server-only, like every
    /// decision that affects the match.
    /// </summary>
    /// <remarks>
    /// <para>Triggered by the match starting rather than by a player connecting, and that ordering
    /// is the whole point. Players connect in the lobby, where this component does not exist —
    /// it lives in the arena, which has not been loaded yet. Placing on connect meant nobody
    /// placed them at all: the characters kept the position they spawned at and fell, and by the
    /// time anyone looked they were hundreds of units below the level.</para>
    /// <para>The placement is applied through <see cref="PredictedPlayer.ServerTeleport"/> and
    /// reaches clients the same way everything else does — in the next snapshot, flagged as a
    /// teleport so nobody counts it as a prediction failure. No RPC, no "force position for five
    /// frames" routine like the original project needed: with a single authority over position, a
    /// teleport is just a state change.</para>
    /// </remarks>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [Tooltip("Spawn positions, used in order. Wraps around if there are more players than points.")]
        [SerializeField] Transform[] _points;

        MatchDirector _director;
        bool _placedForThisMatch;

        void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            if (_director == null)
            {
                _director = MatchDirector.Current;
                if (_director == null) return;
            }

            // Countdown rather than Playing: players must already be standing on the ground while
            // the numbers count down, not appear when they hit zero.
            if (_director.Phase == MatchPhase.Countdown && !_placedForThisMatch)
            {
                PlaceEveryone();
                _placedForThisMatch = true;
            }
            else if (_director.Phase == MatchPhase.Lobby)
            {
                _placedForThisMatch = false;
            }
        }

        void PlaceEveryone()
        {
            int index = 0;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                NetworkObject player = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);

                if (player == null || !player.TryGetComponent(out PredictedPlayer predicted))
                {
                    Debug.LogWarning($"[Snackdown] No player object for client {clientId}; not placed.", this);
                    continue;
                }

                predicted.ServerTeleport(GetSpawnPosition(index++));
            }
        }

        public Vector2 GetSpawnPosition(int index)
        {
            if (_points == null || _points.Length == 0) return Vector2.zero;
            return _points[index % _points.Length].position;
        }
    }
}
