using System;
using System.Collections.Generic;
using Snackdown.Gameplay.Player;
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

        [Tooltip("The rules this match runs under. Everything that reads them should read them here.")]
        [SerializeField] MatchConfig _rules;

        /// <summary>The numbers this match is being played with.</summary>
        /// <remarks>
        /// Held once, by the match, instead of separately by every player and by the referee. Three
        /// copies of the same reference is three chances for one of them to point at a different
        /// asset, and no way to tell from the Inspector that they had. It is also what lets the
        /// sandbox scene run under its own rules without a second player prefab.
        /// </remarks>
        public MatchConfig Rules => _rules;

        readonly NetworkVariable<MatchPhase> _phase = new NetworkVariable<MatchPhase>(MatchPhase.Lobby);
        readonly NetworkVariable<int> _arenaIndex = new NetworkVariable<int>(0);

        /// <summary>
        /// Server time at which play begins. Sent once, not counted down over the wire.
        /// </summary>
        /// <remarks>
        /// The first version replicated the remaining seconds and rewrote them every frame — sixty
        /// messages a second to display "3, 2, 1", which is the same mistake <c>docs/00</c> records
        /// against the original's life timer. Worse, it meant every peer's number came from
        /// whenever the last message happened to arrive, so they could disagree and would need
        /// correcting.
        /// <para>Sending the deadline instead removes the problem rather than managing it. Every
        /// peer derives the number from <c>NetworkManager.ServerTime</c>, a clock NGO already keeps
        /// synchronized, so they agree because they are reading the same clock — not because
        /// someone keeps telling them what to think.</para>
        /// </remarks>
        readonly NetworkVariable<double> _playStartsAtServerTime = new NetworkVariable<double>(0d);

        /// <summary>Clients that have finished loading the arena. Server-side only.</summary>
        readonly HashSet<ulong> _loaded = new HashSet<ulong>();

        /// <summary>Gameplay scene currently loaded on top of bootstrap, if any. Server-side only.</summary>
        string _loadedSceneName;

        public MatchPhase Phase => _phase.Value;
        public int ArenaIndex => _arenaIndex.Value;

        /// <summary>
        /// Seconds until play begins, computed locally from the shared clock rather than received.
        /// </summary>
        public float CountdownRemaining
        {
            get
            {
                if (_playStartsAtServerTime.Value <= 0d || NetworkManager == null) return 0f;
                return Mathf.Max(0f, (float)(_playStartsAtServerTime.Value - NetworkManager.ServerTime.Time));
            }
        }

        /// <summary>True while the simulation should accept input and run match rules.</summary>
        public bool IsPlaying => _phase.Value == MatchPhase.Playing;

        /// <summary>How many peers have finished loading, and how many are expected.</summary>
        /// <remarks>
        /// Replicated rather than computed locally: only the server sees the reports coming in, and
        /// a loading screen that showed each client its own progress would sit at 1 of 1 while
        /// waiting for someone else — which looks like a freeze rather than like waiting.
        /// </remarks>
        readonly NetworkVariable<int> _loadedCount = new NetworkVariable<int>(0);
        readonly NetworkVariable<int> _expectedCount = new NetworkVariable<int>(0);

        public int LoadedPeers => _loadedCount.Value;
        public int ExpectedPeers => _expectedCount.Value;

        /// <summary>Load progress from 0 to 1, for a bar.</summary>
        public float LoadProgress => _expectedCount.Value <= 0
            ? 0f
            : Mathf.Clamp01(_loadedCount.Value / (float)_expectedCount.Value);

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
            _loadedCount.Value = 0;
            _expectedCount.Value = NetworkManager.ConnectedClientsIds.Count;
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
            UnloadCurrentScene();

            _loadedSceneName = sceneName;
            NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
        }

        void UnloadCurrentScene()
        {
            if (string.IsNullOrEmpty(_loadedSceneName)) return;

            Scene previous = SceneManager.GetSceneByName(_loadedSceneName);
            if (previous.IsValid() && previous.isLoaded)
                NetworkManager.SceneManager.UnloadScene(previous);

            _loadedSceneName = null;
        }

        void OnLoadComplete(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (!IsServer || _phase.Value != MatchPhase.Loading) return;

            _loaded.Add(clientId);
            _loadedCount.Value = _loaded.Count;

            // Everyone, not just the server. See the type remarks.
            foreach (ulong connected in NetworkManager.ConnectedClientsIds)
                if (!_loaded.Contains(connected)) return;

            BeginCountdown();
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

            BeginCountdown();
        }

        /// <summary>Publishes the deadline once; every peer counts down against the shared clock.</summary>
        void BeginCountdown()
        {
            _playStartsAtServerTime.Value = NetworkManager.ServerTime.Time + _countdownSeconds;
            _phase.Value = MatchPhase.Countdown;
        }

        void Update()
        {
            if (!IsServer || _phase.Value != MatchPhase.Countdown) return;

            // The server watches the same clock everyone else is reading, so play starts when the
            // deadline passes rather than when a replicated counter happens to reach zero.
            if (NetworkManager.ServerTime.Time < _playStartsAtServerTime.Value) return;

            _phase.Value = MatchPhase.Playing;
        }

        /// <summary>Ends the match. The winner is decided elsewhere; this only moves the phase.</summary>
        public void ServerEndMatch()
        {
            if (!IsServer || _phase.Value != MatchPhase.Playing) return;
            _phase.Value = MatchPhase.Ended;
        }

        /// <summary>Drops the arena and puts the session back in the lobby phase.</summary>
        /// <remarks>
        /// It unloads the arena and stops there — it does not load the lobby scene back. The lobby
        /// has exactly one owner, the reconciler in the UI layer that already brings it up whenever
        /// the phase says it should be there, including before any session exists. Loading it here
        /// as well would give it two, and two owners of an additive load is what produced two lobby
        /// scenes stacked on top of each other the first time around: a networked load takes frames
        /// to land, so the second owner looks and correctly concludes the scene is not there yet.
        /// </remarks>
        public void ServerReturnToLobby()
        {
            if (!IsServer) return;

            _loaded.Clear();
            _loadedCount.Value = 0;
            _phase.Value = MatchPhase.Lobby;

            SessionRoster roster = FindFirstObjectByType<SessionRoster>();
            if (roster != null) roster.ServerClearReady();

            // The bodies go with the arena they were standing in. They are not part of it — they
            // are spawned objects and would outlive the unload — and a character left standing in
            // no scene at all is the state that used to be avoided by never despawning one.
            // Resetting the life is not done here: since ps-4 that travels with the next round's
            // body, so a rematch started from the end screen cannot skip it.
            foreach (PlayerSession player in PlayerSession.All)
            {
                if (player.NetworkManager == NetworkManager) player.ServerDespawnAvatar();
            }

            UnloadCurrentScene();
        }
    }
}
