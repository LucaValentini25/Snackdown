using NUnit.Framework;
using Snackdown.Gameplay;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the frame a looping clip lands on, and how long one takes to play.
    /// </summary>
    /// <remarks>
    /// Small enough to look obviously right and wrong twice already: rounding instead of flooring
    /// starts every clip half a frame in, and a duration off by one frame either cuts the last frame
    /// of a pickup burst or leaves the object alive showing it.
    /// </remarks>
    public class PixelAnimationTests
    {
        const float Frame = 1f / PixelAnimation.FramesPerSecond;

        [Test]
        public void TimeZero_IsTheFirstFrame()
            => Assert.AreEqual(0, PixelAnimation.FrameAt(0f, 6));

        [Test]
        public void JustBeforeTheSecondFrame_IsStillTheFirst()
            => Assert.AreEqual(0, PixelAnimation.FrameAt(Frame * 0.99f, 6));

        [Test]
        public void FramesFollowTheRate()
        {
            Assert.AreEqual(1, PixelAnimation.FrameAt(Frame, 6));
            Assert.AreEqual(5, PixelAnimation.FrameAt(Frame * 5f, 6));
        }

        [Test]
        public void PastTheEnd_ItWrapsToTheStart()
            => Assert.AreEqual(0, PixelAnimation.FrameAt(Frame * 6f, 6));

        [Test]
        public void AnEmptyClip_AnswersZeroRatherThanThrowing()
            => Assert.AreEqual(0, PixelAnimation.FrameAt(Frame * 3f, 0));

        [Test]
        public void TimeBeforeTheStart_IsTheFirstFrame()
            => Assert.AreEqual(0, PixelAnimation.FrameAt(-Frame * 3f, 6));

        [Test]
        public void ADurationIsEveryFrameShownOnce()
            => Assert.AreEqual(Frame * 6f, PixelAnimation.DurationOf(6), 1e-5f);

        [Test]
        public void AnEmptyClip_TakesNoTime()
            => Assert.AreEqual(0f, PixelAnimation.DurationOf(0), 1e-5f);
    }
}
