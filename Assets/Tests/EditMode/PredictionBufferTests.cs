using NUnit.Framework;
using Snackdown.Netcode;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the ring buffer reconciliation reads from.
    /// </summary>
    /// <remarks>
    /// The behaviour worth pinning down is what happens at and past the wrap point. A ring indexed
    /// by <c>tick % Capacity</c> will happily return a stale entry for a tick it never stored unless
    /// each slot verifies its own tick — and a reconciliation that trusts a wrong entry corrects
    /// toward a state that never existed.
    /// </remarks>
    public class PredictionBufferTests
    {
        PredictionBuffer _buffer;

        [SetUp]
        public void SetUp() => _buffer = new PredictionBuffer();

        static PlayerState StateAt(float x) => new PlayerState { Position = new Vector2(x, 0f) };
        static InputCommand InputAt(uint tick, sbyte moveX) => new InputCommand { Tick = tick, MoveX = moveX };

        [Test]
        public void StoredTick_IsReadBack()
        {
            _buffer.Store(42, InputAt(42, 1), StateAt(7f));

            Assert.IsTrue(_buffer.TryGetState(42, out PlayerState state));
            Assert.AreEqual(7f, state.Position.x);

            Assert.IsTrue(_buffer.TryGetInput(42, out InputCommand input));
            Assert.AreEqual(1, input.MoveX);
        }

        [Test]
        public void UnknownTick_IsReportedAsMissing()
        {
            _buffer.Store(42, InputAt(42, 1), StateAt(7f));

            Assert.IsFalse(_buffer.TryGetState(43, out _));
            Assert.IsFalse(_buffer.TryGetInput(43, out _));
        }

        [Test]
        public void EmptyBuffer_ReportsEverythingAsMissing()
        {
            Assert.IsFalse(_buffer.TryGetState(0, out _));
            Assert.IsFalse(_buffer.TryGetInput(1000, out _));
        }

        [Test]
        public void AWrappedSlot_DoesNotMasqueradeAsTheOldTick()
        {
            // Both ticks map to the same index. The old one must be gone, not stale-but-readable.
            const uint older = 5;
            uint newer = older + PredictionBuffer.Capacity;

            _buffer.Store(older, InputAt(older, -1), StateAt(1f));
            _buffer.Store(newer, InputAt(newer, 1), StateAt(2f));

            Assert.IsFalse(_buffer.TryGetState(older, out _), "the overwritten tick must not read back");

            Assert.IsTrue(_buffer.TryGetState(newer, out PlayerState state));
            Assert.AreEqual(2f, state.Position.x);
        }

        [Test]
        public void OverwriteState_UpdatesTheStateButKeepsTheInput()
        {
            // This is what a replay does: the inputs are history and stay put; the predicted states
            // are revised as the replay recomputes them.
            _buffer.Store(10, InputAt(10, 1), StateAt(1f));

            _buffer.OverwriteState(10, StateAt(99f));

            Assert.IsTrue(_buffer.TryGetState(10, out PlayerState state));
            Assert.AreEqual(99f, state.Position.x);

            Assert.IsTrue(_buffer.TryGetInput(10, out InputCommand input));
            Assert.AreEqual(1, input.MoveX, "the input that produced the tick is history, not a prediction");
        }

        [Test]
        public void OverwriteState_OnAnUnknownTick_DoesNothing()
        {
            _buffer.OverwriteState(77, StateAt(5f));

            Assert.IsFalse(_buffer.TryGetState(77, out _), "overwriting must not conjure an entry");
        }

        [Test]
        public void OverwriteState_OnAWrappedTick_DoesNotTouchTheCurrentOccupant()
        {
            const uint older = 3;
            uint newer = older + PredictionBuffer.Capacity;

            _buffer.Store(newer, InputAt(newer, 1), StateAt(2f));
            _buffer.OverwriteState(older, StateAt(999f));

            Assert.IsTrue(_buffer.TryGetState(newer, out PlayerState state));
            Assert.AreEqual(2f, state.Position.x, "a stale tick must not overwrite the slot's new owner");
        }

        [Test]
        public void AFullLapOfTicks_ReadsBackCorrectly()
        {
            for (uint t = 0; t < PredictionBuffer.Capacity; t++)
                _buffer.Store(t, InputAt(t, 1), StateAt(t));

            for (uint t = 0; t < PredictionBuffer.Capacity; t++)
            {
                Assert.IsTrue(_buffer.TryGetState(t, out PlayerState state), $"tick {t} should still be held");
                Assert.AreEqual(t, state.Position.x, 0.0001f);
            }
        }

        [Test]
        public void Clear_DropsEverything()
        {
            _buffer.Store(1, InputAt(1, 1), StateAt(1f));
            _buffer.Store(2, InputAt(2, 1), StateAt(2f));

            _buffer.Clear();

            Assert.IsFalse(_buffer.TryGetState(1, out _));
            Assert.IsFalse(_buffer.TryGetState(2, out _));
        }

        [Test]
        public void TickZero_IsStorableLikeAnyOther()
        {
            // Tick 0 is discarded on the wire as a side effect of the staleness check, but the
            // buffer itself has no such rule and must not grow one.
            _buffer.Store(0, InputAt(0, -1), StateAt(4f));

            Assert.IsTrue(_buffer.TryGetState(0, out PlayerState state));
            Assert.AreEqual(4f, state.Position.x);
        }
    }
}
