using Snackdown.Gameplay.Match;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Hands every player a character at a free spawn point when a round begins, and takes it back
    /// when the arena goes. Server-only, like every decision that affects the match.
    /// </summary>
    /// <remarks>
    /// <para>Triggered by the round starting rather than by a player connecting, and that ordering
    /// is the whole point. Players connect in the lobby, where this component does not exist — it
    /// lives in the arena, which has not been loaded yet. Placing on connect meant nobody placed
    /// them at all: the characters kept the position they spawned at and fell, and by the time
    /// anyone looked they were hundreds of units below the level.</para>
    /// <para>It used to <i>move</i> characters that already existed, which is why
    /// <c>PlayerSnapshot</c> carried a teleport flag: the owner had to be told that an enormous
    /// disagreement about position was deliberate and not a prediction failure. Since <c>ps-4</c> a
    /// character is created here instead, at the point, so there is no disagreement to explain. The
    /// flag left the wire with the reposition.</para>
    /// <para>This is also the only place that starts a player's round, so the life reset travels
    /// with the body — see <see cref="PlayerSession.ServerBeginRound"/>.</para>
    /// </remarks>
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [Tooltip("Spawn positions, used in order. Wraps around if there are more players than points.")]
        [SerializeField] Transform[] _points;

        MatchDirector _director;
        bool _placedForThisRound;

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
            if (_director.Phase == MatchPhase.Countdown && !_placedForThisRound)
            {
                BeginRoundForEveryone();
                _placedForThisRound = true;
            }
            else if (_director.Phase == MatchPhase.Lobby)
            {
                _placedForThisRound = false;
            }
        }

        /// <remarks>
        /// The connected clients rather than the sessions, and asked of NGO's own lookup: since
        /// <c>ps-4</c> the object it returns for a client <i>is</i> that player's session, which is
        /// what repointing <c>NetworkConfig.PlayerPrefab</c> bought (ADR D-004). One list, kept by
        /// the transport, instead of a registry that has to agree with it.
        /// </remarks>
        void BeginRoundForEveryone()
        {
            int index = 0;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                NetworkObject playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);

                if (playerObject == null || !playerObject.TryGetComponent(out PlayerSession session))
                {
                    Debug.LogWarning($"[Snackdown] No session for client {clientId}; they start this round without a body.", this);
                    continue;
                }

                session.ServerBeginRound(GetSpawnPosition(index++));
            }
        }

        public Vector2 GetSpawnPosition(int index)
        {
            if (_points == null || _points.Length == 0) return Vector2.zero;
            return _points[index % _points.Length].position;
        }
    }
}
