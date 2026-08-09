using NUnit.Framework;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for what a stunned character can and cannot do.
    /// </summary>
    /// <remarks>
    /// The distinction being pinned down is that a stun removes <i>control</i>, not physics. A
    /// stunned player still falls, still slides to a stop, still collides — they simply stop
    /// steering. Implementing it as "skip the simulation step" would leave them frozen in mid-air,
    /// which is a different game.
    /// </remarks>
    public class StunTests
    {
        const float Dt = 1f / 30f;

        MovementConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<MovementConfig>();
            _config.MoveSpeed = 7f;
            _config.GroundAcceleration = 90f;
            _config.AirAcceleration = 45f;
            _config.Gravity = 55f;
            _config.MaxFallSpeed = 25f;
            _config.JumpVelocity = 16f;
            _config.JumpReleaseSpeed = 5f;
            _config.CoyoteTime = 0.1f;
            _config.JumpBufferTime = 0.12f;
            _config.ColliderSize = new Vector2(0.7f, 0.9f);
            _config.SkinWidth = 0.02f;
            _config.GroundMask = 0;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        static InputCommand Input(uint tick, sbyte moveX = 0, bool jumpHeld = false, bool jumpPressed = false)
            => new InputCommand { Tick = tick, MoveX = moveX, Buttons = InputCommand.Pack(jumpHeld, jumpPressed) };

        [Test]
        public void AStunnedPlayer_CannotSteer()
        {
            var stunned = new PlayerState { Grounded = true, StunTimer = 2f };
            var free = new PlayerState { Grounded = true };

            stunned = PlayerMotor.Simulate(stunned, Input(1, moveX: 1), _config, Dt);
            free = PlayerMotor.Simulate(free, Input(1, moveX: 1), _config, Dt);

            Assert.AreEqual(0f, stunned.Velocity.x, 0.0001f, "input must be ignored while stunned");
            Assert.Greater(free.Velocity.x, 0f, "the control case should have accelerated");
        }

        [Test]
        public void AStunnedPlayer_CannotJump()
        {
            var state = new PlayerState { Grounded = true, CoyoteTimer = _config.CoyoteTime, StunTimer = 2f };

            state = PlayerMotor.Simulate(state, Input(1, jumpPressed: true, jumpHeld: true), _config, Dt);

            Assert.Less(state.Velocity.y, 0f, "a stunned player should only fall");
        }

        [Test]
        public void AStunnedPlayer_StillFalls()
        {
            // The point of discarding input rather than skipping the step: gravity keeps applying.
            var state = new PlayerState { Grounded = false, StunTimer = 2f };

            state = PlayerMotor.Simulate(state, Input(1), _config, Dt);

            Assert.AreEqual(-_config.Gravity * Dt, state.Velocity.y, 0.0001f);
        }

        [Test]
        public void AStunnedPlayer_KeepsExistingMomentum()
        {
            // Being stomped mid-run should carry you, not stop you dead.
            var state = new PlayerState { Grounded = false, Velocity = new Vector2(5f, 0f), StunTimer = 2f };

            state = PlayerMotor.Simulate(state, Input(1), _config, Dt);

            Assert.Greater(state.Velocity.x, 0f, "horizontal momentum should survive the stun");
        }

        [Test]
        public void TheStunTimer_DrainsAndReleasesControl()
        {
            var state = new PlayerState { Grounded = true, StunTimer = 0.1f };   // three ticks

            for (uint t = 0; t < 3; t++)
            {
                Assert.IsTrue(state.IsStunned, $"still stunned at tick {t}");
                state = PlayerMotor.Simulate(state, Input(t, moveX: 1), _config, Dt);
            }

            Assert.IsFalse(state.IsStunned, "the stun should have expired");

            state = PlayerMotor.Simulate(state, Input(9, moveX: 1), _config, Dt);
            Assert.Greater(state.Velocity.x, 0f, "control should be back");
        }

        [Test]
        public void TheStunTimer_NeverGoesNegative()
        {
            var state = new PlayerState { Grounded = true, StunTimer = 0.01f };

            for (uint t = 0; t < 10; t++)
                state = PlayerMotor.Simulate(state, Input(t), _config, Dt);

            Assert.AreEqual(0f, state.StunTimer);
        }

        [Test]
        public void ReplayingAStunnedSequence_Reproduces()
        {
            // Reconciliation replays ticks, and the stun is part of the state precisely so that a
            // replay across stunned ticks lands where the server did. If StunTimer were held
            // outside PlayerState, this is the test that would fail.
            var start = new PlayerState { Grounded = true, StunTimer = 0.5f, Velocity = new Vector2(3f, 0f) };

            Assert.AreEqual(Replay(start).Position, Replay(start).Position);
            Assert.AreEqual(Replay(start).StunTimer, Replay(start).StunTimer);
        }

        PlayerState Replay(PlayerState state)
        {
            for (uint t = 0; t < 30; t++)
                state = PlayerMotor.Simulate(state, Input(t, moveX: 1, jumpPressed: t % 5 == 0), _config, Dt);

            return state;
        }
    }
}
