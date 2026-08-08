using System;
using System.Collections.Generic;
using Snackdown.Connection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Owns the match: which phase it is in, which arena is loaded, and when play actually starts.
    /// </summary>
    /// <remarks>
    /// <para>Server-authoritative like everything that decides an outcome. Clients read the phase
    /// and react; they never set it. A client that could declare the match started could start one
    /// nobody else is in.</para>
    /// <para>The part worth reading is <see cref="OnLoadComplete"/>. Loading a scene over a network
    /// is not one event but one per player, arriving at different times on different hardware.
    /// Starting the countdown when the <i>server</i> finishes would hand the host a head start of
    /// however long the slowest client takes — seconds, on a cold shader cache. So the countdown
    /// waits for every connected client to report in, which is the difference between a fair start
    /// and one that merely looks synchronized on the machine that tested it.</para>
    /// </remarks>
    public class MatchDirector : NetworkBehaviour
    {
        [Tooltip("Arenas this match can be played in.")]
        [SerializeField] ArenaCatalog _arenas;

        [Tooltip("Seconds between the arena being ready and play starting.")]
        [SerializeField] float _countdownSeconds = 3f;

        [Tooltip("Scene returned to when a match ends. Must be in Build Settings.")]
        [SerializeField] string _lobbySceneName = "Lobby";

        readonly NetworkVariable<MatchPhase> _phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        readonly NetworkVariable<int> _arenaIndex = new NetworkVariable<int>(0);

        /// <summary>Seconds left in the countdown. Only meaningful during <see cref="MatchPhase.Countdown"/>.</summary>
        readonly NetworkVariable<float> _countdownRemaining = new NetworkVariable<float>(0f);

        /// <summary>Clients that have finished loading the arena. Server-side only.</summary>
        readonly HashSet<ulong> _loaded = new HashSet<ulong>();

        /// <summary>Gameplay scene currently loaded on top of bootstrap, if any. Server-side only.</summary>
        string _loadedSceneName;

        public MatchPhase Phase => _phase.Value;
        public int ArenaIndex => _arenaIndex.Value;
        public float CountdownRemaining => _countdownRemaining.Value;

        /// <summary>True while the simulation should accept input and run match rules.</summary>
        public bool IsPlaying => _phase.Value == MatchPhase.Playing;

        /// <summary>Raised on every peer when the phase changes.</summary>
        public event Action<MatchPhase> PhaseChanged;

        public static MatchDirector Current { get; private set; }

        public override void OnNetworkSpawn()
        {
            Current = this;
            _phase.OnValueChanged += OnPhaseChanged;

            if (IsServer)
            {
                NetworkManager.SceneManager.OnLoadComplete += OnLoadComplete;
                NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            }

            PhaseChanged?.Invoke(_phase.Value);
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= OnPhaseChanged;

            if (IsServer && NetworkManager != null)
            {
                if (NetworkManager.SceneManager != null)
                    NetworkManager.SceneManager.OnLoadComplete -= OnLoadComplete;

                NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            }

            if (ReferenceEquals(Current, this)) Current = null;
        }

        void OnPhaseChanged(MatchPhase previous, MatchPhase current) => PhaseChanged?.Invoke(current);

        // ==================================================================================
        //  Server
        // ==================================================================================

        /// <summary>
        /// Starts a match in the given arena. Server-only; the lobby's Start button reaches this
        /// through the host.
        /// </summary>
        public void ServerStartMatch(int arenaIndex)
        {
            if (!IsServer) return;
            if (_phase.Value != MatchPhase.Lobby && _phase.Value != MatchPhase.Ended) return;

            string problem = _arenas != null ? _arenas.Validate() : "No arena catalog assigned.";
            if (problem != null)
            {
                Debug.LogError($"[Snackdown] Cannot start a match: {problem}", this);
                return;
            }

            _arenaIndex.Value = Mathf.Clamp(arenaIndex, 0, _arenas.Count - 1);
            _loaded.Clear();
            _phase.Value = MatchPhase.Loading;

            // Additive, not Single. Single would unload the bootstrap scene along with everything
            // else -- including this director and the roster, which are exactly the things that have
            // to survive a match starting. So bootstrap stays loaded for the whole session and the
            // lobby and arenas come and go on top of it.
            UnloadCurrentSceneThen(_arenas.Get(_arenaIndex.Value).SceneName);
        }

        /// <summary>
        /// Swaps the loaded gameplay scene for another, additively.
        /// </summary>
        /// <remarks>
        /// The unload is fire-and-forget on purpose: NGO reports the new scene through
        /// <see cref="OnLoadComplete"/> regardless, and gating the load on the unload finishing
        /// would add a round trip to a transition players already perceive as slow.
        /// </remarks>
        void UnloadCurrentSceneThen(string sceneName)
        {
            if (!string.IsNullOrEmpty(_loadedSceneName))
            {
                Scene previous = SceneManager.GetSceneByName(_loadedSceneName);
                if (previous.IsValid() && previous.isLoaded)
                    NetworkManager.SceneManager.UnloadScene(previous);
            }

            _loadedSceneName = sceneName;
            NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
        }

        void OnLoadComplete(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (!IsServer || _phase.Value != MatchPhase.Loading) return;

            _loaded.Add(clientId);

            // Everyone, not just the server. See the type remarks.
            foreach (ulong connected in NetworkManager.ConnectedClientsIds)
                if (!_loaded.Contains(connected)) return;

            _countdownRemaining.Value = _countdownSeconds;
            _phase.Value = MatchPhase.Countdown;
        }

        /// <remarks>
        /// A player who disconnects mid-load would otherwise never report in, leaving everyone else
        /// waiting on someone who is gone.
        /// </remarks>
        void OnClientLeft(ulong clientId)
        {
            if (!IsServer) return;

            _loaded.Remove(clientId);
            if (_phase.Value != MatchPhase.Loading) return;

            foreach (ulong connected in NetworkManager.ConnectedClientsIds)
                if (!_loaded.Contains(connected)) return;

            _countdownRemaining.Value = _countdownSeconds;
            _phase.Value = MatchPhase.Countdown;
        }

        void Update()
        {
            if (!IsServer || _phase.Value != MatchPhase.Countdown) return;

            _countdownRemaining.Value -= Time.deltaTime;
            if (_countdownRemaining.Value > 0f) return;

            _countdownRemaining.Value = 0f;
            _phase.Value = MatchPhase.Playing;
        }

        /// <summary>Ends the match. The winner is decided elsewhere; this only moves the phase.</summary>
        public void ServerEndMatch()
        {
            if (!IsServer || _phase.Value != MatchPhase.Playing) return;
            _phase.Value = MatchPhase.Ended;
        }

        /// <summary>Sends everyone back to the lobby scene and resets ready flags.</summary>
        public void ServerReturnToLobby()
        {
            if (!IsServer) return;

            _loaded.Clear();
            _phase.Value = MatchPhase.Lobby;

            SessionRoster roster = FindFirstObjectByType<SessionRoster>();
            if (roster != null) roster.ServerClearReady();

            if (!string.IsNullOrWhiteSpace(_lobbySceneName))
                UnloadCurrentSceneThen(_lobbySceneName);
        }
    }
}
