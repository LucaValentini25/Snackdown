using NUnit.Framework;
using Snackdown.Input;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for a pair of on-screen direction buttons.
    /// </summary>
    /// <remarks>
    /// The cases worth pinning are the ones a keyboard almost never produces and two thumbs produce
    /// constantly: both buttons held at once, and one of them let go while the other stays down.
    /// </remarks>
    public class TouchDirectionTests
    {
        TouchDirection _direction;

        [SetUp]
        public void SetUp() => _direction = new TouchDirection();

        [Test]
        public void NothingHeld_IsStill()
            => Assert.AreEqual(0, _direction.Value);

        [Test]
        public void OneButton_GoesThatWay()
        {
            _direction.Press(-1);
            Assert.AreEqual(-1, _direction.Value);

            _direction.Release(-1);
            _direction.Press(1);
            Assert.AreEqual(1, _direction.Value);
        }

        [Test]
        public void BothHeld_TheLatestWins()
        {
            _direction.Press(-1);
            _direction.Press(1);

            Assert.AreEqual(1, _direction.Value, "pressing right while left is held should go right");
        }

        [Test]
        public void ReleasingTheLatest_FallsBackToTheOther()
        {
            _direction.Press(-1);
            _direction.Press(1);
            _direction.Release(1);

            Assert.AreEqual(-1, _direction.Value, "the thumb still down should get the character");
        }

        [Test]
        public void ReleasingBoth_Stops()
        {
            _direction.Press(-1);
            _direction.Press(1);
            _direction.Release(-1);
            _direction.Release(1);

            Assert.AreEqual(0, _direction.Value);
        }

        [Test]
        public void ReleasingAButtonNeverPressed_ChangesNothing()
        {
            _direction.Press(1);
            _direction.Release(-1);

            Assert.AreEqual(1, _direction.Value);
        }

        /// <remarks>The panel can be hidden with a thumb still down, and a held button that nothing
        /// will ever release would walk the character into a wall for the rest of the round.</remarks>
        [Test]
        public void HidingThePanel_LetsGoOfEverything()
        {
            _direction.Press(-1);
            _direction.Press(1);
            _direction.ReleaseAll();

            Assert.AreEqual(0, _direction.Value);
        }
    }
}
