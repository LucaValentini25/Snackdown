using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Netcode
{
    /// <summary>What reconciling one snapshot turned out to mean.</summary>
    /// <remarks>
    /// Returned rather than acted on, because the acting is the half that needs a
    /// <c>MonoBehaviour</c> — a transform to move and a smoother to tell. Naming the outcomes is
    /// also what makes the branch a test can assert on: "took the server's word for it" and
    /// "replayed twelve ticks" are different events that both end with the character somewhere new.
    /// </remarks>
    public enum ReconcileOutcome
    {
        /// <summary>Older than one already applied, or the same one twice. Nothing happened.</summary>
        Ignored,

        /// <summary>No baseline to compare against, so the server's state was taken whole.</summary>
        Snapped,

        /// <summary>Prediction is off, so the state is followed directly rather than replayed.</summary>
        Followed,

        /// <summary>Prediction was inside tolerance. The client was right and kept what it had.</summary>
        Agreed,

        /// <summary>Rewound to the server's state and replayed every input since.</summary>
        Replayed
    }

    /// <summary>Everything one reconciliation produced, for the caller to apply and report.</summary>
    public readonly struct ReconcileResult
    {
        public readonly ReconcileOutcome Outcome;

        /// <summary>
        /// Where the character ends up. Only meaningful when <see cref="Moved"/> is true — an
        /// agreement reports the state at the acknowledged tick, which is behind where the client
        /// has predicted to and is not somewhere it should be put.
        /// </summary>
        public readonly PlayerState State;

        /// <summary>How far the prediction for the acknowledged tick was from the server's answer.</summary>
        public readonly float PredictionError;

        /// <summary>Ticks re-simulated. Zero unless the outcome is Replayed.</summary>
        public readonly uint ReplayedTicks;

        /// <summary>True when the caller should move without smoothing, because nothing led here.</summary>
        public bool IsHardSnap => Outcome == ReconcileOutcome.Snapped;

        /// <summary>True when the caller should move at all.</summary>
        public bool Moved => Outcome != ReconcileOutcome.Ignored && Outcome != ReconcileOutcome.Agreed;

        public ReconcileResult(ReconcileOutcome outcome, in PlayerState state, float predictionError, uint replayedTicks)
        {
            Outcome = outcome;
            State = state;
            PredictionError = predictionError;
            ReplayedTicks = replayedTicks;
        }

        public static ReconcileResult Ignored => new ReconcileResult(ReconcileOutcome.Ignored, default, 0f, 0);
    }

    /// <summary>
    /// Decides what a client should do with the server's answer: accept it, follow it, or rewind to
    /// it and replay everything predicted since.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the project's headline mechanism, and until now nothing could test it.</b>
    /// It lived inside a <c>MonoBehaviour</c> that needed a scene, a <c>NetworkManager</c> and Play
    /// mode to exist at all — ninety lines and eight decisions, at zero coverage, in the one place
    /// the whole netcode argument rests on. <c>docs/01</c> already prescribed the fix for cases like
    /// this: take the logic worth testing out of the object that owns the statics.</para>
    /// <para>It holds the two pieces of state that exist only for reconciling — the newest tick
    /// acknowledged, and whether a baseline has ever been established — and nothing else. Position,
    /// smoothing and telemetry stay with the character, because those are the parts that need a
    /// transform.</para>
    /// <para>Replaying is what makes a correction survivable. Snapping to the server's state alone
    /// would also erase the half second of movement the player has performed since that tick, and
    /// they would feel yanked backwards every time one landed.</para>
    /// </remarks>
    public class Reconciler
    {
        readonly PredictionBuffer _buffer;
        readonly WorldSnapshotBuffer _world;

        uint _lastAckedTick;
        bool _hasSyncedOnce;

        /// <summary>The newest tick the server has acknowledged having seen input for.</summary>
        public uint LastAckedTick => _lastAckedTick;

        /// <summary>False until a first snapshot has established where this character actually is.</summary>
        public bool HasSyncedOnce => _hasSyncedOnce;

        public Reconciler(PredictionBuffer buffer, WorldSnapshotBuffer world)
        {
            _buffer = buffer;
            _world = world;
        }

        /// <summary>
        /// Forgets the baseline, so the next snapshot re-establishes one instead of being compared
        /// against a prediction that no longer means anything.
        /// </summary>
        public void Desync() => _hasSyncedOnce = false;

        /// <summary>
        /// Works out what the server's answer means for a client that predicted up to
        /// <paramref name="latestPredictedTick"/>.
        /// </summary>
        /// <remarks>
        /// Pure with respect to everything except its own two fields and the prediction buffer,
        /// whose replayed states it overwrites — that buffer is the client's memory of what it
        /// predicted, and a replay that left it holding the old answers would compare the next
        /// snapshot against states it had already corrected.
        /// </remarks>
        /// <param name="snapshot">The server's state and the newest input tick behind it.</param>
        /// <param name="latestPredictedTick">The newest tick this client has predicted.</param>
        /// <param name="selfId">Which body in the world snapshot is this character.</param>
        /// <param name="config">Movement numbers, identical on both sides of the wire.</param>
        /// <param name="tickDelta">Seconds per tick.</param>
        /// <param name="tolerance">Position error below which the prediction counts as right.</param>
        /// <param name="predictionEnabled">False while prediction is switched off from the overlay.</param>
        /// <param name="worldScratch">Reused buffer for the peer bodies of a replayed tick.</param>
        public ReconcileResult Apply(
            in PlayerSnapshot snapshot,
            uint latestPredictedTick,
            ulong selfId,
            MovementConfig config,
            float tickDelta,
            float tolerance,
            bool predictionEnabled,
            PeerBody[] worldScratch)
        {
            uint ackTick = snapshot.LastProcessedInputTick;

            // Unreliable delivery means frames arrive out of order sometimes. An older one carries
            // no new information and must not be allowed to undo a newer correction.
            if (ackTick < _lastAckedTick) return ReconcileResult.Ignored;

            if (!_hasSyncedOnce)
            {
                _hasSyncedOnce = true;
                _lastAckedTick = ackTick;
                return new ReconcileResult(ReconcileOutcome.Snapped, snapshot.State, 0f, 0);
            }

            if (!predictionEnabled)
            {
                // Unpredicted: just follow the server. The caller still smooths it, so what the
                // overlay shows is the latency itself and not a stutter on top of it.
                _lastAckedTick = ackTick;
                return new ReconcileResult(ReconcileOutcome.Followed, snapshot.State, 0f, 0);
            }

            if (ackTick == _lastAckedTick) return ReconcileResult.Ignored;
            _lastAckedTick = ackTick;

            uint pendingTicks = latestPredictedTick > ackTick ? latestPredictedTick - ackTick : 0;

            if (pendingTicks > PredictionBuffer.Capacity)
            {
                // More pending ticks than the ring can hold: the oldest inputs in the range have
                // already been overwritten. Replaying anyway would skip them one by one and land on
                // a state neither side ever computed — wrong, and silently so. Taking the server's
                // word for it is the honest answer, and at that gap the player was disconnected in
                // every sense that matters.
                return new ReconcileResult(ReconcileOutcome.Snapped, snapshot.State, 0f, 0);
            }

            if (!_buffer.TryGetState(ackTick, out PlayerState predicted))
            {
                // No memory of that tick — the buffer wrapped, or this character just spawned.
                // Nothing to compare against, so take the server's word for it.
                return new ReconcileResult(ReconcileOutcome.Snapped, snapshot.State, 0f, 0);
            }

            float error = predicted.PositionErrorTo(snapshot.State);
            if (error <= tolerance)
            {
                return new ReconcileResult(ReconcileOutcome.Agreed, predicted, error, 0);
            }

            PlayerState replayed = snapshot.State;

            for (uint tick = ackTick + 1; tick <= latestPredictedTick; tick++)
            {
                // A genuine hole — a tick this client never predicted, as after a spawn or a
                // toggle. Overflow can no longer reach here; the capacity check above caught it.
                if (!_buffer.TryGetInput(tick, out InputCommand input)) continue;

                replayed = PlayerMotor.Simulate(
                    replayed, input, config, _world.ContextFor(tick, selfId, worldScratch), tickDelta);

                _buffer.OverwriteState(tick, replayed);
            }

            return new ReconcileResult(ReconcileOutcome.Replayed, replayed, error, pendingTicks);
        }
    }
}
