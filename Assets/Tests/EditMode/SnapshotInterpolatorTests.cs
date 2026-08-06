using NUnit.Framework;
using Snackdown.Netcode;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for how remote characters are rendered between authoritative samples.
    /// </summary>
    /// <remarks>
    /// The rules that matter here are the refusals: never extrapolate past the newest sample, never
    /// accept a sample that arrived out of order, and never blend <c>Grounded</c> into something
    /// halfway. A boolean averaged across two samples is how a remote character ends up
    /// half-airborne, and it is the kind of bug that only shows as an animation glitch.
    /// </remarks>
    public class SnapshotInterpolatorTests
    {
        SnapshotInterpolator _interpolator;

        [SetUp]
        public void SetUp() => _interpolator = new SnapshotInterpolator();

        static PlayerState At(float x, bool grounded = false)
            => new PlayerState { Position = new Vector2(x, 0f), Velocity = new Vector2(1f, 0f), Grounded = grounded };

        [Test]
        public void EmptyBuffer_HasNothingToEvaluate()
        {
            Assert.IsFalse(_interpolator.TryEvaluate(1.0, out _));
        }

        [Test]
        public void ASingleSample_IsHeldRatherThanExtrapolated()
        {
            _interpolator.Push(1.0, At(5f));

            Assert.IsTrue(_interpolator.TryEvaluate(99.0, out PlayerState state));
            Assert.AreEqual(5f, state.Position.x, "one sample means hold it, never guess forward");
        }

        [Test]
        public void RenderTimeBetweenTwoSamples_InterpolatesLinearly()
        {
            _interpolator.Push(1.0, At(0f));
            _interpolator.Push(2.0, At(10f));

            Assert.IsTrue(_interpolator.TryEvaluate(1.5, out PlayerState state));
            Assert.AreEqual(5f, state.Position.x, 0.0001f);
        }

        [Test]
        public void RenderTimeBeforeTheOldestSample_HoldsTheOldest()
        {
            _interpolator.Push(10.0, At(3f));
            _interpolator.Push(11.0, At(4f));

            Assert.IsTrue(_interpolator.TryEvaluate(0.0, out PlayerState state));
            Assert.AreEqual(3f, state.Position.x);
        }

        [Test]
        public void RenderTimePastTheNewestSample_HoldsTheNewestInsteadOfExtrapolating()
        {
            // A frozen remote for a frame reads as lag; an extrapolated one reads as a glitch and
            // then snaps back. The design chooses the first deliberately.
            _interpolator.Push(1.0, At(0f));
            _interpolator.Push(2.0, At(10f));

            Assert.IsTrue(_interpolator.TryEvaluate(5.0, out PlayerState state));
            Assert.AreEqual(10f, state.Position.x);
        }

        [Test]
        public void OutOfOrderSamples_AreDropped()
        {
            _interpolator.Push(2.0, At(10f));
            _interpolator.Push(1.0, At(0f));   // late arrival; unreliable delivery makes this normal

            Assert.AreEqual(1, _interpolator.SampleCount);
            Assert.AreEqual(2.0, _interpolator.NewestTime);
        }

        [Test]
        public void ADuplicateTimestamp_IsDropped()
        {
            _interpolator.Push(1.0, At(0f));
            _interpolator.Push(1.0, At(50f));

            Assert.AreEqual(1, _interpolator.SampleCount);

            Assert.IsTrue(_interpolator.TryEvaluate(1.0, out PlayerState state));
            Assert.AreEqual(0f, state.Position.x, "the first sample for a timestamp wins");
        }

        [Test]
        public void GroundedIsNeverBlended()
        {
            _interpolator.Push(1.0, At(0f, grounded: false));
            _interpolator.Push(2.0, At(10f, grounded: true));

            Assert.IsTrue(_interpolator.TryEvaluate(1.5, out PlayerState state));
            Assert.IsTrue(state.Grounded, "a bool must resolve to one side, never to an average");
        }

        [Test]
        public void VelocityIsInterpolatedAlongWithPosition()
        {
            var a = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(0f, 0f) };
            var b = new PlayerState { Position = new Vector2(10f, 0f), Velocity = new Vector2(10f, 0f) };

            _interpolator.Push(1.0, a);
            _interpolator.Push(2.0, b);

            Assert.IsTrue(_interpolator.TryEvaluate(1.5, out PlayerState state));
            Assert.AreEqual(5f, state.Velocity.x, 0.0001f);
        }

        [Test]
        public void OverflowingTheBuffer_KeepsTheNewestSamples()
        {
            for (int i = 0; i < 40; i++)
                _interpolator.Push(i, At(i));

            Assert.AreEqual(39.0, _interpolator.NewestTime);

            Assert.IsTrue(_interpolator.TryEvaluate(39.0, out PlayerState state));
            Assert.AreEqual(39f, state.Position.x);
        }

        [Test]
        public void Clear_EmptiesTheBuffer()
        {
            _interpolator.Push(1.0, At(1f));
            _interpolator.Clear();

            Assert.AreEqual(0, _interpolator.SampleCount);
            Assert.IsFalse(_interpolator.TryEvaluate(1.0, out _));
        }
    }
}
