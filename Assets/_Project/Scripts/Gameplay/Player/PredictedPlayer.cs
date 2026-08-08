using System.Collections.Generic;
using Snackdown.Gameplay.Match;
using Snackdown.Input;
using Snackdown.Netcode;
using Snackdown.Simulation;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// One networked character, seen from whichever side of the wire this peer happens to be on.
    /// Prediction, reconciliation and interpolation all live here.
    /// </summary>
    /// <remarks>
    /// <para>The same component behaves as three different things depending on its role, and keeping
    /// those roles straight is most of the job:</para>
    /// <list type="bullet">
    /// <item><b>Owner on a client</b> — samples input, simulates immediately without waiting for the
    /// server, remembers what it did, and corrects itself when the server disagrees.</item>
    /// <item><b>Server</b> — the only authority. Consumes the inputs it received, simulates, and
    /// publishes the result. Never trusts a position it was told.</item>
    /// <item><b>Remote on a client</b> — no simulation at all, just interpolation between
    /// authoritative snapshots.</item>
    /// </list>
    /// <para>The host is owner <i>and</i> server at once. It takes the server path exclusively:
    /// its input never crosses a wire, so there is nothing to predict and nothing to reconcile.
    /// Predicting there would be pure overhead and would invent errors that cannot exist.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class PredictedPlayer : NetworkBehaviour, IPredictedPeer
    {
        [Header("Simulation")]
        [SerializeField] MovementConfig _config;

        [Tooltip("Position error above which the client rewinds and replays. Below it, prediction is considered correct.")]
        [SerializeField] float _reconciliationTolerance = 0.03f;

        [Header("Presentation")]
        [Tooltip("Child that carries the sprite and absorbs correction jumps.")]
        [SerializeField] VisualSmoother _smoother;

        [Tooltip("Optional ghost showing where the server says we are. Owner-only, for the demo.")]
        [SerializeField] Transform _authoritativeGhost;

        [Tooltip("How far in the past remote players are rendered, in seconds.")]
        [SerializeField] float _interpolationDelay = 0.1f;

        // --- shared -----------------------------------------------------------------------
        PlayerState _state;
        InputReader _inputReader;
        float _tickDelta = 1f / 30f;

        // --- owner ------------------------------------------------------------------------
        readonly PredictionBuffer _buffer = new PredictionBuffer();
        InputCommand _previous1, _previous2;
        uint _latestPredictedTick;
        uint _lastAckedTick;
        bool _hasSyncedOnce;
        bool _predictionWasEnabled = true;

        // --- server -----------------------------------------------------------------------
        readonly Queue<InputCommand> _incomingInputs = new Queue<InputCommand>();
        InputCommand _lastConsumedInput;
        uint _highestReceivedInputTick;
        uint _lastProcessedInputTick;

        /// <summary>Inputs are buffered to absorb jitter; past this depth we're just adding latency.</summary>
        const int MaxQueueDepth = 8;

        /// <summary>Hard ceiling on the input queue, regardless of what the client sends.</summary>
        /// <remarks>
        /// <see cref="MaxQueueDepth"/> only makes the server drain one extra command per tick, so
        /// it bounds latency, not memory: a client sending faster than the tick rate adds entries
        /// quicker than two-per-tick removes them, and the queue grows without limit. Nothing here
        /// is protected by "no real client would do that" — a ServerRpc assumes the caller is
        /// hostile. At 30 Hz this is still a second of buffered intent, far beyond anything a
        /// legitimate connection needs.
        /// </remarks>
        const int MaxQueueCapacity = 32;

        /// <summary>
        /// How far ahead of the server's own tick an input may claim to be before it is discarded.
        /// </summary>
        /// <remarks>
        /// NGO deliberately runs a client's local tick ahead of the server so input lands just in
        /// time, and the lead grows with RTT — so some lead is correct and must be accepted. What
        /// must not be accepted is an unbounded one: a client claiming a tick far in the future
        /// would push <see cref="_highestReceivedInputTick"/> beyond anything it could legitimately
        /// send afterwards, and every honest input behind it would then be rejected as stale.
        /// ~2 seconds of lead is generous for any real connection.
        /// </remarks>
        const uint MaxInputTickLead = 64;

        /// <summary>
        /// How many consecutive snapshots keep announcing a teleport after one happens.
        /// </summary>
        /// <remarks>
        /// Snapshots are unreliable, so a flag announced once is a flag that can be dropped — and
        /// a dropped teleport flag turns a deliberate reposition back into a counted prediction
        /// failure. Three is the same redundancy the input window uses, for the same reason.
        /// </remarks>
        const int TeleportAnnounceCount = 3;

        int _teleportAnnouncesLeft;

        // --- remote -----------------------------------------------------------------------
        readonly SnapshotInterpolator _interpolator = new SnapshotInterpolator();

        /// <summary>
        /// True once there is an arena to stand in.
        /// </summary>
        /// <remarks>
        /// Gravity does not care that the level has not loaded. Simulating in the lobby — where
        /// this character exists but the ground does not — meant players fell continuously from
        /// the moment they connected; by the time the arena appeared they were hundreds of units
        /// below it, still falling. Countdown counts as simulating so they settle onto the floor
        /// before the match begins rather than snapping down at zero.
        /// </remarks>
        bool ShouldSimulate
        {
            get
            {
                MatchDirector director = MatchDirector.Current;

                // No director means no match system at all — a bare test scene, where simulating
                // is the only sensible default.
                if (director == null) return true;

                return director.Phase == MatchPhase.Countdown || director.Phase == MatchPhase.Playing;
            }
        }

        /// <summary>
        /// Global kill switch for client-side prediction, flipped from the debug overlay.
        /// Turning it off under simulated latency is the most direct demonstration of what
        /// prediction actually buys — the character stops responding to the player and starts
        /// responding to the network.
        /// </summary>
        public static bool PredictionEnabled = true;

        // --- debug telemetry (read by NetDebugOverlay) ------------------------------------
        readonly ReconciliationStats _stats = new ReconciliationStats();
        readonly RunRecorder _recorder = new RunRecorder();

        public int ReconciliationCount => _stats.TotalCorrections;
        public float LastPredictionError { get; private set; }
        public uint LastReplayedTicks { get; private set; }
        public Vector2 LastAuthoritativePosition { get; private set; }
        public int ServerQueueDepth => _incomingInputs.Count;
        public int StarvedTicks { get; private set; }
        public PlayerState State => _state;

        /// <summary>Correction rate and typical error over the last few seconds, or false if there
        /// were none — which is the outcome the whole layer is aiming for.</summary>
        public bool TryGetReconciliationWindow(out ReconciliationWindow window)
            => _stats.TryGetWindow(Time.time, out window);

        /// <summary>How far the sprite currently trails the simulated position, in units.</summary>
        /// <remarks>
        /// The one number that shows a correction was absorbed rather than hidden: it spikes on
        /// a correction and decays to zero, while the logical position never moved off the truth.
        /// </remarks>
        public float VisualError => _smoother != null ? _smoother.CurrentError : 0f;

        /// <summary>True while a run is being recorded for export.</summary>
        public bool IsRecording => _recorder.IsRunning;

        public int RecordedCorrections => _recorder.SampleCount;
        public float RecordedDuration => _recorder.Duration(Time.time);

        /// <summary>
        /// Round trip measured on this project's own traffic, in milliseconds.
        /// </summary>
        /// <remarks>
        /// <c>UnityTransport.GetCurrentRtt</c> cannot answer this. It reads
        /// <c>RttInfo.LastRtt</c> off the <i>reliable sequenced</i> pipeline, and every packet this
        /// layer sends — input and snapshots alike — is deliberately unreliable. What it reports is
        /// NGO's handshake and time-sync traffic, not the game's.
        /// <para>The honest measurement was already in the data: the owner predicts up to tick N
        /// while the server acknowledges tick M, so <c>(N - M)</c> ticks is exactly how long a
        /// command takes to go out and come back. Multiplied by the tick interval, that is the
        /// round trip of the traffic we actually care about.</para>
        /// </remarks>
        public float LastMeasuredRttMs { get; private set; }

        /// <summary>What the transport claims, kept only to be reported next to the real one.</summary>
        ulong TransportRtt()
        {
            if (IsServer) return 0UL;
            return NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        }

        /// <summary>
        /// Writes the recorded run to disk and returns the path, or null if this peer has nothing
        /// to report — which is every peer except a predicting client.
        /// </summary>
        /// <remarks>
        /// Only the owning client reconciles, so only the owning client has a run worth writing.
        /// The host and remote peers deliberately return null rather than an empty file: an empty
        /// measurement file is indistinguishable from a run where nothing went wrong, and those
        /// two mean opposite things.
        /// </remarks>
        /// <remarks>
        /// Exporting also starts a fresh run. Comparing behaviour across network conditions means
        /// several runs in one session, and a recorder that only ever accumulates would blend the
        /// calm minute and the hostile one into a single average describing neither.
        /// </remarks>
        public string WriteRunMetrics(string directory, string fileName, string conditions)
        {
            if (!IsOwner || IsServer || !_recorder.IsRunning) return null;

            string path = _recorder.Write(directory, fileName, Time.time, conditions);
            _recorder.Begin(Time.time);
            return path;
        }

        public override void OnNetworkSpawn()
        {
            if (_config == null)
            {
                // Without a config every Simulate call dereferences null, thirty times a second,
                // inside the one code path that has to be trustworthy. Fail once and loudly here
                // instead, and stay out of the loop entirely.
                Debug.LogError($"[Snackdown] {name} has no MovementConfig assigned; simulation disabled.", this);
                enabled = false;
                return;
            }

            _tickDelta = 1f / NetworkManager.NetworkConfig.TickRate;
            _state = PlayerState.AtPosition(transform.position);
            _inputReader = GetComponent<InputReader>();

            // Only the owner's device drives this character. On every other peer the reader would
            // enable its InputActions and latch the local keyboard's jumps into a character that
            // never reads them. The host counts as owner: its input skips the wire, but it is
            // still sampled — in ServerSimulateTick rather than in prediction.
            if (_inputReader != null) _inputReader.enabled = IsOwner;

            if (_smoother != null) _smoother.Snap();
            if (_authoritativeGhost != null)
                _authoritativeGhost.gameObject.SetActive(IsOwner && !IsServer);

            // Recording starts with the character, not with a keypress: the interesting corrections
            // are the ones right after joining, and a run that only begins once someone remembers
            // to press a key is missing exactly them.
            if (IsOwner && !IsServer) _recorder.Begin(Time.time);

            NetworkSimulationLoop.Register(this);
        }

        public override void OnNetworkDespawn() => NetworkSimulationLoop.Unregister(this);

        // ==================================================================================
        //  Owner — predict
        // ==================================================================================

        /// <summary>
        /// Runs on the owning client only. Simulate now, ask permission never — the server's
        /// verdict arrives a round trip later and is dealt with in <see cref="Reconcile"/>.
        /// </summary>
        public void OwnerPredictTick(uint tick)
        {
            if (!ShouldSimulate) return;
            if (PredictionEnabled != _predictionWasEnabled) OnPredictionToggled();

            InputCommand input = SampleInput(tick);

            // With prediction switched off the owner still samples and still sends — it just
            // refuses to act before the server answers. That is the demo: the same character,
            // now moving a full round trip after the button was pressed.
            if (PredictionEnabled)
                _state = PlayerMotor.Simulate(_state, input, _config, _tickDelta);

            _buffer.Store(tick, input, _state);
            _latestPredictedTick = tick;

            SubmitInputRpc(new InputPacket
            {
                Newest = input,
                Previous = _previous1,
                Oldest = _previous2
            });

            _previous2 = _previous1;
            _previous1 = input;

            if (PredictionEnabled) ApplyLogicalPosition(_state.Position, smooth: true);
        }

        /// <summary>
        /// Discards everything the buffer holds when the F1 switch is flipped, in either direction.
        /// </summary>
        /// <remarks>
        /// While prediction is off the owner still records a buffer entry per tick, but the state
        /// in it was never simulated — it is whatever the last snapshot left behind. Turning
        /// prediction back on and reconciling against those entries would compare the server's
        /// answer to a state nobody ever predicted, and report a correction that says more about
        /// the switch than about the network. Since this exists to make the demo legible, a lying
        /// correction counter is the one thing it cannot afford.
        /// </remarks>
        void OnPredictionToggled()
        {
            _predictionWasEnabled = PredictionEnabled;
            _buffer.Clear();
            _hasSyncedOnce = false;   // the next snapshot re-establishes a known-good baseline

            // The rolling window describes current conditions, and the switch just changed them.
            // Averaging across the flip would blend two different regimes into one meaningless
            // number, in the exact moment the demo is trying to contrast them.
            _stats.Clear();
        }

        InputCommand SampleInput(uint tick)
        {
            if (_inputReader == null) return new InputCommand { Tick = tick };

            return new InputCommand
            {
                Tick = tick,
                MoveX = _inputReader.MoveX,
                Buttons = InputCommand.Pack(_inputReader.JumpHeld, _inputReader.ConsumeJumpPressed())
            };
        }

        // ==================================================================================
        //  Server — authority
        // ==================================================================================

        /// <summary>
        /// Sent every tick, unreliable, carrying a three-command window. See <see cref="InputPacket"/>
        /// for why redundancy beats retransmission here.
        /// </summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void SubmitInputRpc(InputPacket packet)
        {
            uint serverTick = (uint)NetworkManager.ServerTime.Tick;

            // Oldest first, so the queue stays ordered.
            EnqueueIfNew(packet.Oldest, serverTick);
            EnqueueIfNew(packet.Previous, serverTick);
            EnqueueIfNew(packet.Newest, serverTick);
        }

        /// <summary>
        /// Admits one command from a redundancy window, if it is genuinely new and plausible.
        /// </summary>
        /// <remarks>
        /// Tick 0 needs no special case: <see cref="_highestReceivedInputTick"/> starts at zero, so
        /// the staleness test below already rejects it. The cost is one discarded input on the very
        /// first tick of a session, before the character can have moved — which buys the absence of
        /// a sentinel value that would have to stay true forever.
        /// </remarks>
        void EnqueueIfNew(in InputCommand input, uint serverTick)
        {
            // A command we've already seen — either an earlier copy of this redundant window, or a
            // duplicate from the network. Either way, applying it twice would double the movement.
            if (input.Tick <= _highestReceivedInputTick) return;

            // Further into the future than any real clock skew explains.
            if (input.Tick > serverTick + MaxInputTickLead) return;

            _highestReceivedInputTick = input.Tick;

            // Drop the oldest rather than refuse the newest: what the player is doing now matters
            // more than a backlog they have already moved past.
            if (_incomingInputs.Count >= MaxQueueCapacity) _incomingInputs.Dequeue();

            _incomingInputs.Enqueue(input);
        }

        /// <summary>
        /// The authoritative step. Everything this produces is truth by definition; everyone else
        /// either predicted it correctly or is about to be corrected.
        /// </summary>
        public void ServerSimulateTick(uint serverTick)
        {
            if (!ShouldSimulate) return;

            InputCommand input;

            if (IsOwner)
            {
                // The host: its input never travelled, so there is nothing to buffer.
                input = SampleInput(serverTick);
                _lastProcessedInputTick = serverTick;
            }
            else if (_incomingInputs.Count > 0)
            {
                // Drain an extra command when the queue has run long, so a client whose packets
                // arrived in a burst catches up instead of permanently lagging by that burst.
                if (_incomingInputs.Count > MaxQueueDepth) _incomingInputs.Dequeue();

                input = _incomingInputs.Dequeue();
                _lastProcessedInputTick = input.Tick;
            }
            else
            {
                // Nothing arrived in time. Repeating the last input keeps a running player running
                // through a hiccup; freezing them would look like a stutter and then a snap.
                input = _lastConsumedInput;
                StarvedTicks++;
            }

            _lastConsumedInput = input;
            _state = PlayerMotor.Simulate(_state, input, _config, _tickDelta);
            ApplyLogicalPosition(_state.Position, smooth: true);
        }

        public PlayerSnapshot BuildSnapshot()
        {
            var snapshot = new PlayerSnapshot
            {
                NetworkObjectId = NetworkObjectId,
                State = _state,
                LastProcessedInputTick = _lastProcessedInputTick,
                IsTeleport = _teleportAnnouncesLeft > 0
            };

            if (_teleportAnnouncesLeft > 0) _teleportAnnouncesLeft--;
            return snapshot;
        }

        // ==================================================================================
        //  Client — reconcile (owner) / interpolate (remote)
        // ==================================================================================

        public void ApplySnapshot(in PlayerSnapshot snapshot, double snapshotTime)
        {
            if (IsServer) return;

            if (IsOwner) Reconcile(snapshot);
            else _interpolator.Push(snapshotTime, snapshot.State);
        }

        /// <summary>
        /// Compares the server's answer against what we predicted for the same tick and, when they
        /// disagree, rewinds to the server state and replays every input made since.
        /// </summary>
        /// <remarks>
        /// Replaying is what makes a correction survivable. Snapping to the server's state alone
        /// would also erase the half second of movement the player has performed since that tick —
        /// they'd feel yanked backwards every time a correction landed.
        /// </remarks>
        void Reconcile(in PlayerSnapshot snapshot)
        {
            uint ackTick = snapshot.LastProcessedInputTick;
            LastAuthoritativePosition = snapshot.State.Position;

            // Unreliable delivery means frames arrive out of order sometimes. An older one carries
            // no new information and must not be allowed to undo a newer correction.
            if (ackTick < _lastAckedTick) return;

            // The round trip of our OWN traffic, in ticks: how far our prediction has run past the
            // newest input the server had seen. See LastMeasuredRttMs for why the transport's
            // number cannot answer this.
            if (_latestPredictedTick > ackTick)
                LastMeasuredRttMs = (_latestPredictedTick - ackTick) * _tickDelta * 1000f;

            if (snapshot.IsTeleport)
            {
                // The server moved us on purpose. Replaying inputs across a teleport would carry
                // over momentum from a place we are no longer in, and counting it as a correction
                // would blame prediction for something it never got to predict.
                HardSnapTo(snapshot.State);
                _hasSyncedOnce = true;
                _lastAckedTick = ackTick;
                _buffer.Clear();
                return;
            }

            if (!_hasSyncedOnce)
            {
                HardSnapTo(snapshot.State);
                _hasSyncedOnce = true;
                _lastAckedTick = ackTick;
                return;
            }

            if (!PredictionEnabled)
            {
                // Unpredicted: just follow the server. Still smoothed, so what the overlay shows
                // is the latency itself and not a stutter on top of it.
                _state = snapshot.State;
                _lastAckedTick = ackTick;
                ApplyLogicalPosition(_state.Position, smooth: true);
                return;
            }

            if (ackTick == _lastAckedTick) return;
            _lastAckedTick = ackTick;

            uint pendingTicks = _latestPredictedTick > ackTick ? _latestPredictedTick - ackTick : 0;

            if (pendingTicks > PredictionBuffer.Capacity)
            {
                // More pending ticks than the ring can hold: the oldest inputs in the range have
                // already been overwritten. Replaying anyway would skip them one by one and land
                // on a state neither side ever computed — wrong, and silently so. Taking the
                // server's word for it is the honest answer, and at 34 seconds of gap the player
                // was disconnected in every sense that matters.
                HardSnapTo(snapshot.State);
                return;
            }

            if (!_buffer.TryGetState(ackTick, out PlayerState predicted))
            {
                // We have no memory of that tick (buffer wrapped, or we just spawned).
                // Nothing to compare against, so take the server's word for it.
                HardSnapTo(snapshot.State);
                return;
            }

            LastPredictionError = predicted.PositionErrorTo(snapshot.State);
            if (LastPredictionError <= _reconciliationTolerance) return;   // prediction was right

            PlayerState replayed = snapshot.State;
            for (uint t = ackTick + 1; t <= _latestPredictedTick; t++)
            {
                // A genuine hole — a tick this client never predicted, as after a spawn or a
                // toggle. Overflow can no longer reach here; the capacity check above caught it.
                if (!_buffer.TryGetInput(t, out InputCommand input)) continue;

                replayed = PlayerMotor.Simulate(replayed, input, _config, _tickDelta);
                _buffer.OverwriteState(t, replayed);
            }

            _state = replayed;
            LastReplayedTicks = pendingTicks;
            _stats.Record(Time.time, LastPredictionError, pendingTicks);
            _recorder.Record(Time.time, LastPredictionError, pendingTicks, LastMeasuredRttMs, TransportRtt());

            // Logically instant, visually smooth: the smoother eats the jump.
            ApplyLogicalPosition(_state.Position, smooth: true);
        }

        void HardSnapTo(in PlayerState state)
        {
            _state = state;
            ApplyLogicalPosition(_state.Position, smooth: false);
            if (_smoother != null) _smoother.Snap();
        }

        void Update()
        {
            // Remote characters are driven entirely by the render clock, not by the tick.
            if (!IsServer && !IsOwner)
            {
                double renderTime = NetworkManager.ServerTime.Time - _interpolationDelay;
                if (_interpolator.TryEvaluate(renderTime, out PlayerState interpolated))
                {
                    _state = interpolated;
                    ApplyLogicalPosition(_state.Position, smooth: false);
                }
            }

            if (_authoritativeGhost != null && _authoritativeGhost.gameObject.activeSelf)
                _authoritativeGhost.position = LastAuthoritativePosition;
        }

        void ApplyLogicalPosition(Vector2 position, bool smooth)
        {
            Vector3 previous = transform.position;
            transform.position = new Vector3(position.x, position.y, previous.z);

            if (smooth && _smoother != null)
                _smoother.AbsorbMovement(previous - transform.position);
        }

        /// <summary>
        /// Takes control away from this character for a while. Server-only.
        /// </summary>
        /// <remarks>
        /// Written into the simulation state rather than sent as its own message, so it travels in
        /// the snapshot the owner already reconciles against. The owner never predicted the stun —
        /// it could not, since it depends on where another player was — so it arrives as an
        /// ordinary correction and the replay carries it forward across the ticks since.
        /// </remarks>
        public void ServerApplyStun(float seconds)
        {
            if (!IsServer || seconds <= 0f) return;
            _state.StunTimer = Mathf.Max(_state.StunTimer, seconds);
        }

        /// <summary>Launches this character upward, for the player who landed the stomp. Server-only.</summary>
        public void ServerBounce(float upwardVelocity)
        {
            if (!IsServer) return;

            _state.Velocity.y = upwardVelocity;
            _state.Grounded = false;
        }

        /// <summary>Server-side teleport that every peer will follow through the next snapshot.</summary>
        /// <remarks>
        /// The next few snapshots announce this as a teleport rather than letting owners discover
        /// it as an enormous prediction error. Without that, a spawn placement is indistinguishable
        /// on the wire from the simulation having gone badly wrong.
        /// </remarks>
        public void ServerTeleport(Vector2 position)
        {
            if (!IsServer) return;
            _state = PlayerState.AtPosition(position);
            _teleportAnnouncesLeft = TeleportAnnounceCount;
            ApplyLogicalPosition(position, smooth: false);
            if (_smoother != null) _smoother.Snap();
        }
    }
}
