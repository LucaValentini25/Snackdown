using NUnit.Framework;
using Snackdown.Netcode;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// The guess behind <see cref="PeerContactSource.Authoritative"/>: where a rival would be by now
    /// if they kept doing what the last snapshot said they were doing.
    /// </summary>
    /// <remarks>
    /// Both awkward cases here are ones that only appear on a bad connection with two players
    /// moving — a snapshot that arrives describing a tick already past, and a gap long enough to
    /// carry a rival through the scenery. Reproducing either by hand means a lossy link and luck.
    /// </remarks>
    public class PeerExtrapolationTests
    {
        const float TickDelta = 1f / 30f;

        [Test]
        public void WithNoGap_TheKnownPositionIsTheAnswer()
        {
            Vector2 at = PeerExtrapolation.PositionAt(
                new Vector2(3f, 1f), new Vector2(7f, 0f), knownTick: 40, tick: 40, TickDelta);

            Assert.AreEqual(3f, at.x, 1e-4f);
            Assert.AreEqual(1f, at.y, 1e-4f);
        }

        [Test]
        public void ATickAlreadyDescribed_IsNotRunBackwards()
        {
            // Snapshots arrive out of order, and a client's tick can be corrected backwards.
            // Inventing a past out of a present velocity would be guessing about something the
            // server has already stated.
            Vector2 at = PeerExtrapolation.PositionAt(
                new Vector2(3f, 0f), new Vector2(7f, 0f), knownTick: 40, tick: 34, TickDelta);

            Assert.AreEqual(3f, at.x, 1e-4f);
        }

        [Test]
        public void AcrossAFewTicks_ThePositionMovesAtTheKnownVelocity()
        {
            // Six ticks at 30 Hz is 0.2 s; at 7 u/s that is 1.4 units.
            Vector2 at = PeerExtrapolation.PositionAt(
                Vector2.zero, new Vector2(7f, 0f), knownTick: 40, tick: 46, TickDelta);

            Assert.AreEqual(1.4f, at.x, 1e-3f);
        }

        [Test]
        public void AStationaryRival_DoesNotDrift()
        {
            Vector2 at = PeerExtrapolation.PositionAt(
                new Vector2(-2f, 5f), Vector2.zero, knownTick: 1, tick: 900, TickDelta);

            Assert.AreEqual(-2f, at.x, 1e-4f);
            Assert.AreEqual(5f, at.y, 1e-4f);
        }

        [Test]
        public void AGapLongerThanTheCap_StopsAtTheCap()
        {
            // A rival whose snapshots stopped arriving. Without the bound, a second of silence at
            // 7 u/s puts them seven metres away — reliably inside the scenery.
            Vector2 capped = PeerExtrapolation.PositionAt(
                Vector2.zero, new Vector2(7f, 0f), knownTick: 0, tick: 300, TickDelta);

            Assert.AreEqual(7f * PeerExtrapolation.MaxSeconds, capped.x, 1e-3f);
        }

        [Test]
        public void TheCapDoesNotBiteBeforeItShould()
        {
            // The first tick whose gap reaches the cap, and the one before it. Ceil rather than
            // round: 0.25 s is 7.5 ticks at 30 Hz, and a test that has to guess which side of a
            // half a float lands on is testing the rounding, not the cap.
            uint atCap = (uint)Mathf.CeilToInt(PeerExtrapolation.MaxSeconds / TickDelta);

            Vector2 justUnder = PeerExtrapolation.PositionAt(
                Vector2.zero, new Vector2(7f, 0f), 0, atCap - 1, TickDelta);

            Vector2 exactly = PeerExtrapolation.PositionAt(
                Vector2.zero, new Vector2(7f, 0f), 0, atCap, TickDelta);

            Assert.Less(justUnder.x, exactly.x, "the tick before the cap should still be moving");
            Assert.AreEqual(7f * PeerExtrapolation.MaxSeconds, exactly.x, 1e-3f);
            Assert.Less(justUnder.x, 7f * PeerExtrapolation.MaxSeconds, "the cap bit a tick early");
        }

        [Test]
        public void TheCapAppliesInEveryDirection()
        {
            // Falling, and out of touch. Clamping the elapsed time rather than the distance is what
            // makes this work without a special case per axis.
            Vector2 falling = PeerExtrapolation.PositionAt(
                Vector2.zero, new Vector2(-7f, -20f), knownTick: 0, tick: 300, TickDelta);

            Assert.AreEqual(-7f * PeerExtrapolation.MaxSeconds, falling.x, 1e-3f);
            Assert.AreEqual(-20f * PeerExtrapolation.MaxSeconds, falling.y, 1e-3f);
        }
    }
}
