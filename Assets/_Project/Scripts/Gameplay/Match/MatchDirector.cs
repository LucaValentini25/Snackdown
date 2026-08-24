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
                NetworkManager.OnClientConnectedCallback += OnClientJoined;
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

                NetworkManager.OnClientConnectedCallback -= OnClientJoined;
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
            if (UnloadCurrentSceneThen(_arenas.Get(_arenaIndex.Value).SceneName)) return;

            // The load was refused, so nothing will ever report having finished it. Going back to
            // the lobby is the only honest state: the phase said Loading a line ago, and leaving it
            // there is a loading screen that never ends with nothing in the console to explain it.
            _expectedCount.Value = 0;
            _phase.Value = MatchPhase.Lobby;
        }

        /// <summary>
        /// Swaps the loaded gameplay scene for another, additively. Returns whether the load was
        /// accepted.
        /// </summary>
        /// <remarks>
        /// <para>The unload is fire-and-forget on purpose: NGO reports the new scene through
        /// <see cref="OnLoadComplete"/> regardless, and gating the load on the unload finishing
        /// would add a round trip to a transition players already perceive as slow.</para>
        /// <para>The load is not. <c>LoadScene</c> answers with a
        /// <see cref="SceneEventProgressStatus"/> and refuses outright when another scene event is
        /// still in flight — silently, with no log and no exception. Discarding that answer is what
        /// left the phase sitting in <c>Loading</c> forever, waiting on load reports for a scene
        /// nobody had been asked to load.</para>
        /// </remarks>
        bool UnloadCurrentSceneThen(string sceneName)
        {
            UnloadCurrentScene();

            SceneEventProgressStatus status =
                NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[Snackdown] Netcode refused to load {sceneName}: {status}.", this);
                return false;
            }

            // Only recorded once the load is under way. Remembering a scene that was refused would
            // have the next unload go looking for something that never arrived.
            _loadedSceneName = sceneName;
            return true;
        }

        void UnloadCurrentScene()
        {
            if (string.IsNullOrEmpty(_loadedSceneName)) return;

            Scene previous = SceneManager.GetSceneByName(_loadedSceneName);

            if (previous.IsValid() && previous.isLoaded)
            {
                SceneEventProgressStatus status = NetworkManager.SceneManager.UnloadScene(previous);

                // Reported and not acted on. Nothing downstream waits for an unload, so a refusal
                // costs an arena left standing under the next one rather than a stuck phase — worth
                // knowing about, not worth stopping for.
                if (status != SceneEventProgressStatus.Started)
                    Debug.LogError($"[Snackdown] Netcode refused to unload {previous.name}: {status}.", this);
            }

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

        /// <summary>True while a round is being played out, from the countdown to the end screen.</summary>
        /// <remarks>
        /// <c>Loading</c> is not one of them, and that is the interesting boundary rather than an
        /// oversight: bodies are handed out when the countdown starts, so somebody who arrives while
        /// the arena is still loading is in time for this round. Somebody who arrives a moment later
        /// is not.
        /// </remarks>
        bool RoundIsUnderWay =>
            _phase.Value == MatchPhase.Countdown
            || _phase.Value == MatchPhase.Playing
            || _phase.Value == MatchPhase.Ended;

        /// <summary>
        /// Sits a player out of a round that had already started before they arrived.
        /// </summary>
        /// <remarks>
        /// <para>Otherwise a late joiner is admitted alive with a full life and no character. The
        /// referee counts them among the living, so the round cannot end by last-one-standing while
        /// they are connected, and when the clock runs out they win it — they have more life left
        /// than anyone who has been playing, having spent none of it. Neither failure looks like a
        /// bug from the outside; the match simply picks the wrong winner.</para>
        /// <para>Decided here rather than by the session itself, because the phase is the match's to
        /// read. Asking <c>MatchDirector.Current</c> from the session would be asking a static that
        /// several peers share inside one test process, and NGO has already spawned the player
        /// object by the time this callback runs — so the session is there to be told.</para>
        /// <para>Nothing else is needed to make them a spectator. The camera already follows the
        /// alive flag, and the next round hands them a body along with everyone else.</para>
        /// </remarks>
        void OnClientJoined(ulong clientId)
        {
            if (!IsServer || !RoundIsUnderWay) return;

            PlayerSession player = PlayerSession.Of(NetworkManager, clientId);
            if (player == null || player.Life == null) return;

            player.Life.ServerEndRound();
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
