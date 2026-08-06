using NUnit.Framework;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the shared simulation step — the function client and server must agree on.
    /// </summary>
    /// <remarks>
    /// These run without a scene, a NetworkManager or Play mode, because <see cref="PlayerMotor"/>
    /// takes everything it needs as arguments. That property is not an accident of style: it is
    /// what makes reconciliation replay possible at all, so a test that needs the engine running
    /// would be evidence the design had regressed.
    /// <para>The collision cases are deliberately absent. <c>MoveAndCollide</c> calls
    /// <c>Physics2D.BoxCast</c> against scene geometry, which is exactly the part that is not pure;
    /// covering it means an EditMode scene with colliders, which is a different kind of test.
    /// What is covered here is everything above the cast: acceleration, the feel timers, the jump
    /// rules and the gravity clamp.</para>
    /// </remarks>
    public class PlayerMotorTests
    {
        const float Dt = 1f / 30f;

        MovementConfig _config;

        [SetUp]
        public void SetUp()
        {
            // A ScriptableObject cannot be newed. These are the shipped defaults, restated so a
            // change to the asset cannot silently change what the tests mean.
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
            _config.GroundMask = 0;   // nothing is solid, so casts never hit and stay out of the way
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        static InputCommand Input(uint tick, sbyte moveX = 0, bool jumpHeld = false, bool jumpPressed = false)
            => new InputCommand { Tick = tick, MoveX = moveX, Buttons = InputCommand.Pack(jumpHeld, jumpPressed) };

        // ── determinism ────────────────────────────────────────────────────────────────────

        [Test]
        public void SameStateAndInput_ProduceIdenticalResults()
        {
            var start = new PlayerState { Position = new Vector2(3f, 1f), Velocity = new Vector2(2f, -1f), Grounded = true };
            InputCommand input = Input(10, moveX: 1, jumpHeld: true);

            PlayerState a = PlayerMotor.Simulate(start, input, _config, Dt);
            PlayerState b = PlayerMotor.Simulate(start, input, _config, Dt);

            Assert.AreEqual(a.Position, b.Position);
            Assert.AreEqual(a.Velocity, b.Velocity);
            Assert.AreEqual(a.CoyoteTimer, b.CoyoteTimer);
            Assert.AreEqual(a.JumpBufferTimer, b.JumpBufferTimer);
        }

        [Test]
        public void ReplayingASequence_LandsOnTheSameStateEveryTime()
        {
            // This is the property reconciliation depends on: replaying N ticks from a known state
            // must reproduce exactly, or a corrected client and the server would drift apart.
            var start = new PlayerState { Position = Vector2.zero, Grounded = true };

            PlayerState first = Replay(start);
            PlayerState second = Replay(start);

            Assert.AreEqual(first.Position, second.Position);
            Assert.AreEqual(first.Velocity, second.Velocity);
        }

        PlayerState Replay(PlayerState state)
        {
            for (uint t = 0; t < 40; t++)
            {
                sbyte moveX = (sbyte)(t % 7 < 3 ? 1 : t % 5 == 0 ? -1 : 0);
                bool pressed = t % 11 == 0;
                state = PlayerMotor.Simulate(state, Input(t, moveX, jumpHeld: pressed, jumpPressed: pressed), _config, Dt);
            }

            return state;
        }

        [Test]
        public void Simulate_DoesNotMutateTheStateItWasGiven()
        {
            // PlayerState is a struct passed by value, but the buffer stores past states and a
            // replay reads them repeatedly. If this ever stopped holding, corrections would be
            // computed against states the replay had already overwritten.
            var start = new PlayerState { Position = new Vector2(1f, 2f), Velocity = new Vector2(3f, 4f), Grounded = true };

            PlayerMotor.Simulate(start, Input(1, moveX: 1), _config, Dt);

            Assert.AreEqual(new Vector2(1f, 2f), start.Position);
            Assert.AreEqual(new Vector2(3f, 4f), start.Velocity);
        }

        // ── horizontal movement ────────────────────────────────────────────────────────────

        [Test]
        public void HorizontalSpeed_NeverExceedsMoveSpeed()
        {
            var state = new PlayerState { Grounded = true };

            for (uint t = 0; t < 60; t++)
                state = PlayerMotor.Simulate(state, Input(t, moveX: 1), _config, Dt);

            Assert.AreEqual(_config.MoveSpeed, state.Velocity.x, 0.0001f);
        }

        [Test]
        public void AirAcceleration_IsSlowerThanGround()
        {
            var grounded = new PlayerState { Grounded = true };
            var airborne = new PlayerState { Grounded = false };

            grounded = PlayerMotor.Simulate(grounded, Input(1, moveX: 1), _config, Dt);
            airborne = PlayerMotor.Simulate(airborne, Input(1, moveX: 1), _config, Dt);

            Assert.Greater(grounded.Velocity.x, airborne.Velocity.x);
        }

        // ── jumping ────────────────────────────────────────────────────────────────────────

        [Test]
        public void JumpFromGround_AppliesJumpVelocityMinusOneTickOfGravity()
        {
            var state = new PlayerState { Grounded = true, CoyoteTimer = _config.CoyoteTime };

            state = PlayerMotor.Simulate(state, Input(1, jumpPressed: true, jumpHeld: true), _config, Dt);

            // Gravity is applied in the same step the jump starts, so the observable result is
            // JumpVelocity less one tick of it.
            Assert.AreEqual(_config.JumpVelocity - _config.Gravity * Dt, state.Velocity.y, 0.0001f);
            Assert.IsFalse(state.Grounded);
        }

        [Test]
        public void CoyoteTime_AllowsAJumpShortlyAfterLeavingTheGround()
        {
            // Airborne, but still inside the grace window.
            var state = new PlayerState { Grounded = false, CoyoteTimer = 0.05f };

            state = PlayerMotor.Simulate(state, Input(1, jumpPressed: true, jumpHeld: true), _config, Dt);

            Assert.Greater(state.Velocity.y, 0f, "a jump inside the coyote window must fire");
        }

        [Test]
        public void CoyoteTime_ExpiresAndThenRefusesTheJump()
        {
            var state = new PlayerState { Grounded = false, CoyoteTimer = 0f };

            state = PlayerMotor.Simulate(state, Input(1, jumpPressed: true, jumpHeld: true), _config, Dt);

            Assert.Less(state.Velocity.y, 0f, "with no coyote time left the character should only fall");
        }

        [Test]
        public void CoyoteTimer_DrainsWhileAirborneAndNeverGoesNegative()
        {
            var state = new PlayerState { Grounded = false, CoyoteTimer = _config.CoyoteTime };

            for (uint t = 0; t < 30; t++)
                state = PlayerMotor.Simulate(state, Input(t), _config, Dt);

            Assert.AreEqual(0f, state.CoyoteTimer);
        }

        [Test]
        public void JumpBuffer_FiresOnLandingForAJumpPressedInTheAir()
        {
            // Pressed while falling: the buffer remembers it.
            var state = new PlayerState { Grounded = false, CoyoteTimer = 0f };
            state = PlayerMotor.Simulate(state, Input(1, jumpPressed: true, jumpHeld: true), _config, Dt);

            Assert.Greater(state.JumpBufferTimer, 0f, "the press should have been buffered");

            // Now touch down, without pressing again.
            state.Grounded = true;
            state = PlayerMotor.Simulate(state, Input(2, jumpHeld: true), _config, Dt);

            Assert.Greater(state.Velocity.y, 0f, "the buffered jump should fire on landing");
        }

        [Test]
        public void JumpBuffer_ExpiresIfLandingTakesTooLong()
        {
            var state = new PlayerState { Grounded = false, CoyoteTimer = 0f };
            state = PlayerMotor.Simulate(state, Input(1, jumpPressed: true, jumpHeld: true), _config, Dt);

            // Stay airborne well past JumpBufferTime (0.12s ≈ 4 ticks).
            for (uint t = 2; t < 12; t++)
                state = PlayerMotor.Simulate(state, Input(t, jumpHeld: true), _config, Dt);

            Assert.AreEqual(0f, state.JumpBufferTimer);

            state.Grounded = true;
            state.CoyoteTimer = _config.CoyoteTime;
            float before = state.Velocity.y;
            state = PlayerMotor.Simulate(state, Input(12, jumpHeld: true), _config, Dt);

            Assert.LessOrEqual(state.Velocity.y, before, "a stale buffered jump must not fire");
        }

        [Test]
        public void ReleasingJump_ClampsTheRise()
        {
            var state = new PlayerState { Grounded = false, Velocity = new Vector2(0f, 12f) };

            state = PlayerMotor.Simulate(state, Input(1, jumpHeld: false), _config, Dt);

            Assert.LessOrEqual(state.Velocity.y, _config.JumpReleaseSpeed);
        }

        [Test]
        public void ReleasingJump_DoesNotBoostAFallingCharacter()
        {
            var state = new PlayerState { Grounded = false, Velocity = new Vector2(0f, -10f) };

            state = PlayerMotor.Simulate(state, Input(1, jumpHeld: false), _config, Dt);

            Assert.Less(state.Velocity.y, -10f, "releasing jump while falling must not slow the fall");
        }

        // ── gravity ────────────────────────────────────────────────────────────────────────

        [Test]
        public void FallSpeed_IsClampedToMaxFallSpeed()
        {
            var state = new PlayerState { Grounded = false };

            for (uint t = 0; t < 120; t++)
                state = PlayerMotor.Simulate(state, Input(t), _config, Dt);

            Assert.AreEqual(-_config.MaxFallSpeed, state.Velocity.y, 0.0001f);
        }

        [Test]
        public void GroundedIsRecomputedEveryTick_AndNeverSurvivesBeingAirborne()
        {
            // The regression this guards: a stale Grounded == true hands out a free mid-air jump.
            var state = new PlayerState { Grounded = true, Velocity = new Vector2(0f, 5f) };

            state = PlayerMotor.Simulate(state, Input(1), _config, Dt);

            Assert.IsFalse(state.Grounded, "moving upward through empty space cannot leave us grounded");
        }
    }
}
