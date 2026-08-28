using NUnit.Framework;
using Snackdown.Gameplay.Player;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the clip a character state resolves to, and the frame it plays.
    /// </summary>
    /// <remarks>
    /// These exist because the alternative way to check an animation is to run two peers and watch
    /// them, which cannot tell a wrong precedence from a slow connection. Every case here is one a
    /// player would otherwise have to catch by eye: a run that keeps playing off a ledge, a stun
    /// that turns somebody around, a clip that restarts from the middle.
    /// </remarks>
    public class CharacterAnimationTests
    {
        const float Frame = 1f / CharacterAnimation.FramesPerSecond;

        static PlayerState Standing() => new PlayerState { Grounded = true };

        static PlayerState Running(float speed)
            => new PlayerState { Grounded = true, Velocity = new Vector2(speed, 0f) };

        static PlayerState Airborne(float verticalSpeed)
            => new PlayerState { Grounded = false, Velocity = new Vector2(0f, verticalSpeed) };

        [Test]
        public void StandingStill_IsIdle()
            => Assert.AreEqual(CharacterClip.Idle, CharacterAnimation.Choose(Standing()));

        [Test]
        public void MovingOnTheGround_Runs()
            => Assert.AreEqual(CharacterClip.Run, CharacterAnimation.Choose(Running(7f)));

        [Test]
        public void MovingBackwards_AlsoRuns()
            => Assert.AreEqual(CharacterClip.Run, CharacterAnimation.Choose(Running(-7f)));

        [Test]
        public void TheRemainderOfAStop_IsStillIdle()
        {
            var sliding = Running(CharacterAnimation.MinimumRunSpeed * 0.5f);
            Assert.AreEqual(CharacterClip.Idle, CharacterAnimation.Choose(sliding));
        }

        [Test]
        public void RisingOffTheGround_Jumps()
            => Assert.AreEqual(CharacterClip.Jump, CharacterAnimation.Choose(Airborne(9f)));

        [Test]
        public void FallingOffTheGround_Falls()
            => Assert.AreEqual(CharacterClip.Fall, CharacterAnimation.Choose(Airborne(-9f)));

        [Test]
        public void TheTopOfAJump_HasAlreadyBecomeAFall()
            => Assert.AreEqual(CharacterClip.Fall, CharacterAnimation.Choose(Airborne(0f)));

        [Test]
        public void RunningOffALedge_StopsRunning()
        {
            var offTheEdge = new PlayerState { Grounded = false, Velocity = new Vector2(7f, -1f) };
            Assert.AreEqual(CharacterClip.Fall, CharacterAnimation.Choose(offTheEdge));
        }

        [Test]
        public void BeingStunned_OutranksBeingAirborne()
        {
            var thrown = new PlayerState { Grounded = false, Velocity = new Vector2(6f, 4f), StunTimer = 0.3f };
            Assert.AreEqual(CharacterClip.Hit, CharacterAnimation.Choose(thrown));
        }

        [Test]
        public void AStunThatHasExpired_StopsBeingAHit()
        {
            var recovered = new PlayerState { Grounded = true, StunTimer = 0f };
            Assert.AreEqual(CharacterClip.Idle, CharacterAnimation.Choose(recovered));
        }

        [Test]
        public void AClipStartsOnItsFirstFrame()
        {
            var animation = new CharacterAnimation();
            Assert.AreEqual(0, animation.Advance(CharacterClip.Run, 0f, 12));
        }

        [Test]
        public void FramesAdvanceAtThePackRate()
        {
            var animation = new CharacterAnimation();
            animation.Advance(CharacterClip.Run, 0f, 12);

            Assert.AreEqual(1, animation.Advance(CharacterClip.Run, Frame, 12));
            Assert.AreEqual(2, animation.Advance(CharacterClip.Run, Frame, 12));
        }

        [Test]
        public void AClipLoops()
        {
            var animation = new CharacterAnimation();
            animation.Advance(CharacterClip.Run, 0f, 4);

            Assert.AreEqual(0, animation.Advance(CharacterClip.Run, Frame * 4f, 4));
        }

        [Test]
        public void ChangingClip_StartsTheNewOneOver()
        {
            var animation = new CharacterAnimation();
            animation.Advance(CharacterClip.Run, 0f, 12);
            animation.Advance(CharacterClip.Run, Frame * 6f, 12);

            Assert.AreEqual(0, animation.Advance(CharacterClip.Fall, 0f, 12));
        }

        [Test]
        public void AClipWithNoFrames_AnswersZeroRatherThanThrowing()
        {
            var animation = new CharacterAnimation();
            Assert.AreEqual(0, animation.Advance(CharacterClip.Idle, Frame * 3f, 0));
        }

        [Test]
        public void TimeNeverRunsBackwards()
        {
            var animation = new CharacterAnimation();
            animation.Advance(CharacterClip.Run, 0f, 12);
            animation.Advance(CharacterClip.Run, Frame * 2f, 12);

            Assert.AreEqual(2, animation.Advance(CharacterClip.Run, -Frame * 5f, 12));
        }

        [Test]
        public void FacingFollowsTheDirectionOfTravel()
        {
            var animation = new CharacterAnimation();

            animation.Face(Running(-7f));
            Assert.IsFalse(animation.FacingRight);

            animation.Face(Running(7f));
            Assert.IsTrue(animation.FacingRight);
        }

        [Test]
        public void StoppingDoesNotTurnTheCharacterAround()
        {
            var animation = new CharacterAnimation();
            animation.Face(Running(-7f));
            animation.Face(Standing());

            Assert.IsFalse(animation.FacingRight);
        }

        [Test]
        public void BeingThrownBackwardsDoesNotTurnTheCharacterAround()
        {
            var animation = new CharacterAnimation();
            animation.Face(Running(-7f));

            var thrown = new PlayerState { Grounded = false, Velocity = new Vector2(9f, 3f), StunTimer = 0.3f };
            animation.Face(thrown);

            Assert.IsFalse(animation.FacingRight);
        }
    }
}
