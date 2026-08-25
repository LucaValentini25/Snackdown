using NUnit.Framework;
using Snackdown.Netcode;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// The project's headline mechanism, finally reachable: rewind to what the server said and
    /// replay everything predicted since.
    /// </summary>
    /// <remarks>
    /// <para>Ninety lines and eight decisions sat at zero coverage for four phases, because they
    /// lived inside a <c>MonoBehaviour</c> that needed a scene, a <c>NetworkManager</c> and Play
    /// mode to exist. Nothing here needs any of the three. The half that does — moving a transform
    /// and telling the smoother — stayed with the character and is still only visible in a session.
    /// </para>
    /// <para>The tests drive the real <see cref="PlayerMotor"/> against a real
    /// <see cref="MovementConfig"/>, so a replay here is the same arithmetic the server ran. A
    /// fake step would prove the plumbing and nothing about whether the two sides converge.</para>
    /// </remarks>
    public class ReconcilerTests
    {
        const float TickDelta = 1f / 30f;
        const float Tolerance = 0.03f;
        const ulong SelfId = 1;

        MovementConfig _config;
        PredictionBuffer _buffer;
        WorldSnapshotBuffer _world;
        Reconciler _reconciler;
        PeerBody[] _scratch;

        [SetUp]
        public void Build()
        {
            // Built rather than loaded: these are about the mechanism, and a config somebody
            // retunes for feel should not be able to fail them.
            _config = ScriptableObject.CreateInstance<MovementConfig>();

            _buffer = new PredictionBuffer();
            _world = new WorldSnapshotBuffer();
            _reconciler = new Reconciler(_buffer, _world);
            _scratch = new PeerBody[WorldSnapshotBuffer.MaxBodies];
        }

        [TearDown]
        public void Drop() => Object.DestroyImmediate(_config);

        // ── the first snapshot ─────────────────────────────────────────────────────────────

        [Test]
        public void TheFirstSnapshot_EstablishesABaselineRatherThanCorrectingOne()
        {
            var server = PlayerState.AtPosition(new Vector2(3f, 0f));

            ReconcileResult result = Apply(SnapshotAt(server, ackTick: 10), latestPredictedTick: 10);

            // Nothing was predicted yet, so there is nothing to compare against and no correction
            // to count. Reporting one here is what used to poison the statistic the whole layer is
            // judged by.
            Assert.AreEqual(ReconcileOutcome.Snapped, result.Outcome);
            Assert.AreEqual(0f, result.PredictionError, 0.0001f, "a correction was counted for a baseline");
            Assert.IsTrue(result.IsHardSnap, "a baseline should not be smoothed into");
        }

        // ── out of order and duplicate delivery ────────────────────────────────────────────

        [Test]
        public void AnOlderSnapshot_CannotUndoANewerOne()
        {
            Sync(atTick: 20);

            Assert.AreEqual(ReconcileOutcome.Ignored,
                Apply(SnapshotAt(PlayerState.AtPosition(Vector2.zero), ackTick: 12), 25).Outcome,
                "Snapshots travel unreliably and arrive out of order; an old one carries nothing new.");
        }

        [Test]
        public void TheSameSnapshotTwice_IsAppliedOnce()
        {
            Sync(atTick: 20);

            PlayerSnapshot again = SnapshotAt(PlayerState.AtPosition(new Vector2(9f, 0f)), ackTick: 20);

            Assert.AreEqual(ReconcileOutcome.Ignored, Apply(again, 25).Outcome);
        }

        // ── agreeing ──────────────────────────────────────────────────────────────────────

        [Test]
        public void APredictionInsideTolerance_IsLeftAlone()
        {
            Sync(atTick: 0);

            var predicted = PlayerState.AtPosition(new Vector2(5f, 0f));
            _buffer.Store(5, default, predicted);

            // A hair off, well inside the tolerance. Correcting here would yank the character for
            // a disagreement nobody could see.
            var server = PlayerState.AtPosition(new Vector2(5.01f, 0f));

            ReconcileResult result = Apply(SnapshotAt(server, ackTick: 5), latestPredictedTick: 6);

            Assert.AreEqual(ReconcileOutcome.Agreed, result.Outcome);
            Assert.IsFalse(result.Moved, "the character was moved for a prediction that was right");
        }

        // ── the one the epic is named after ────────────────────────────────────────────────

        [Test]
        public void AfterAForcedDesync_ReplayConvergesOnTheServersAnswer()
        {
            Sync(atTick: 0);

            // The client predicts ten ticks of holding right, from a position the server will
            // disagree with.
            var input = new InputCommand { MoveX = 1 };
            PlayerState clientState = PlayerState.AtPosition(new Vector2(0.5f, 0f));

            for (uint tick = 1; tick <= 10; tick++)
            {
                clientState = PlayerMotor.Simulate(clientState, input, _config, Empty(tick), TickDelta);
                _buffer.Store(tick, input, clientState);
            }

            // The server answers for tick 4 from somewhere else entirely.
            var server = PlayerState.AtPosition(Vector2.zero);
            ReconcileResult result = Apply(SnapshotAt(server, ackTick: 4), latestPredictedTick: 10);

            Assert.AreEqual(ReconcileOutcome.Replayed, result.Outcome);
            Assert.AreEqual(6u, result.ReplayedTicks, "ticks 5 through 10 should have been replayed");

            // What convergence means: the same arithmetic, from the server's state, over the same
            // inputs. Not "close to where the client was" — the client was wrong.
            PlayerState expected = server;
            for (uint tick = 5; tick <= 10; tick++)
                expected = PlayerMotor.Simulate(expected, input, _config, Empty(tick), TickDelta);

            Assert.AreEqual(expected.Position.x, result.State.Position.x, 0.0001f, "x after replay");
            Assert.AreEqual(expected.Position.y, result.State.Position.y, 0.0001f, "y after replay");
        }

        [Test]
        public void ADroppedSnapshot_LeavesNoPermanentOffset()
        {
            Sync(atTick: 0);

            var input = new InputCommand { MoveX = 1 };

            // The client is wrong from the start — half a unit ahead of where the server has it —
            // so every snapshot is a real correction rather than an agreement.
            PlayerState clientState = PlayerState.AtPosition(new Vector2(0.5f, 0f));

            for (uint tick = 1; tick <= 12; tick++)
            {
                clientState = PlayerMotor.Simulate(clientState, input, _config, Empty(tick), TickDelta);
                _buffer.Store(tick, input, clientState);
            }

            // The server's own history over the same inputs, from where it actually had the player.
            PlayerState serverState = PlayerState.AtPosition(Vector2.zero);
            var serverAt = new PlayerState[13];
            serverAt[0] = serverState;

            for (uint tick = 1; tick <= 12; tick++)
            {
                serverState = PlayerMotor.Simulate(serverState, input, _config, Empty(tick), TickDelta);
                serverAt[tick] = serverState;
            }

            // Tick 5's snapshot never arrives. This is the case the whole unreliable-delivery
            // argument rests on: losing one has to cost nothing permanent, because losing them is
            // normal and retransmitting would cost more than it saves.
            ReconcileResult afterTheGap = Apply(SnapshotAt(serverAt[7], ackTick: 7), latestPredictedTick: 12);

            Assert.AreEqual(ReconcileOutcome.Replayed, afterTheGap.Outcome,
                "the snapshot after a dropped one did not correct anything");

            // Where the server would have the player at tick 12. The next snapshot to arrive after
            // a gap carries a complete correction, so the client lands exactly there rather than
            // keeping half a unit of the error it started with.
            float offset = Vector2.Distance(afterTheGap.State.Position, serverAt[12].Position);

            Assert.Less(offset, Tolerance,
                $"A dropped snapshot left the client {offset:0.0000} units from the server's answer.");
        }

        [Test]
        public void AGapLongerThanTheBuffer_TakesTheServersWordRatherThanReplaying()
        {
            Sync(atTick: 0);

            // Further behind than the ring can remember. Replaying would skip the inputs it no
            // longer holds and land on a state neither side ever computed — wrong, and silently.
            ReconcileResult result = Apply(
                SnapshotAt(PlayerState.AtPosition(new Vector2(2f, 0f)), ackTick: 1),
                latestPredictedTick: PredictionBuffer.Capacity + 50);

            Assert.AreEqual(ReconcileOutcome.Snapped, result.Outcome);
        }

        [Test]
        public void WithNoMemoryOfTheAcknowledgedTick_TheServerIsBelieved()
        {
            Sync(atTick: 0);

            // Nothing was stored for tick 7 — the buffer wrapped, or the character just spawned.
            ReconcileResult result = Apply(
                SnapshotAt(PlayerState.AtPosition(new Vector2(4f, 0f)), ackTick: 7),
                latestPredictedTick: 9);

            Assert.AreEqual(ReconcileOutcome.Snapped, result.Outcome);
        }

        // ── prediction switched off ────────────────────────────────────────────────────────

        [Test]
        public void WithPredictionOff_TheServerIsFollowedWithoutReplaying()
        {
            Sync(atTick: 0);

            var server = PlayerState.AtPosition(new Vector2(6f, 0f));

            ReconcileResult result = _reconciler.Apply(
                SnapshotAt(server, ackTick: 5), 8, SelfId, _config, TickDelta, Tolerance,
                predictionEnabled: false, worldScratch: _scratch);

            Assert.AreEqual(ReconcileOutcome.Followed, result.Outcome);
            Assert.AreEqual(server.Position.x, result.State.Position.x, 0.0001f);

            // Smoothed rather than snapped: the overlay is meant to show the latency, not a stutter
            // laid on top of it.
            Assert.IsFalse(result.IsHardSnap);
        }

        [Test]
        public void AfterADesync_TheNextSnapshotRebuildsTheBaseline()
        {
            Sync(atTick: 20);
            _reconciler.Desync();

            // Not Ignored, even though this tick is older than the one already acknowledged: a
            // desync means the prediction it was compared against no longer means anything.
            ReconcileResult result = Apply(
                SnapshotAt(PlayerState.AtPosition(new Vector2(1f, 0f)), ackTick: 25), 25);

            Assert.AreEqual(ReconcileOutcome.Snapped, result.Outcome);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────

        /// <summary>Gets past the first-snapshot branch, so a test can be about something else.</summary>
        void Sync(uint atTick)
            => Apply(SnapshotAt(PlayerState.AtPosition(Vector2.zero), atTick), atTick);

        ReconcileResult Apply(in PlayerSnapshot snapshot, uint latestPredictedTick)
            => _reconciler.Apply(
                snapshot, latestPredictedTick, SelfId, _config, TickDelta, Tolerance,
                predictionEnabled: true, worldScratch: _scratch);

        static PlayerSnapshot SnapshotAt(in PlayerState state, uint ackTick) => new PlayerSnapshot
        {
            NetworkObjectId = SelfId,
            State = state,
            LastProcessedInputTick = ackTick
        };

        /// <summary>A world with nobody else in it, so these test the replay and not peer contact.</summary>
        SimulationContext Empty(uint tick) => _world.ContextFor(tick, SelfId, _scratch);
    }
}
