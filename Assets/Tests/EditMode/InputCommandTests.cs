using NUnit.Framework;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the one struct a client is allowed to send about its own character.
    /// </summary>
    /// <remarks>
    /// These exist because <see cref="InputCommand"/> has two ranges, not one: the range its docs
    /// declare (-1/0/1, two button bits) and the range the wire permits (any <c>sbyte</c>, any
    /// <c>byte</c>). Every guard downstream — including <c>MoveSpeed</c> acting as a speed ceiling —
    /// assumes the first. <see cref="InputCommand.Sanitized"/> is what makes that assumption true,
    /// so it is worth a test that fails if it ever stops being.
    /// </remarks>
    public class InputCommandTests
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

        // ── the declared range ─────────────────────────────────────────────────────────────

        [Test]
        public void Sanitized_LeavesLegalCommandsUntouched()
        {
            foreach (sbyte moveX in new sbyte[] { -1, 0, 1 })
            {
                var command = new InputCommand
                {
                    Tick = 42,
                    MoveX = moveX,
                    Buttons = InputCommand.Pack(jumpHeld: true, jumpPressed: true)
                };

                InputCommand clean = InputCommand.Sanitized(command);

                Assert.AreEqual(command.Tick, clean.Tick, "The tick is validated elsewhere and must survive.");
                Assert.AreEqual(command.MoveX, clean.MoveX);
                Assert.AreEqual(command.Buttons, clean.Buttons);
            }
        }

        [Test]
        public void Sanitized_CollapsesAnyMagnitudeToItsSign()
        {
            // The whole sbyte range, not a sample: 127 is the interesting one because
            // MoveX * MoveSpeed is what the motor accelerates toward, so 127 is a 127x speed hack.
            for (int raw = sbyte.MinValue; raw <= sbyte.MaxValue; raw++)
            {
                InputCommand clean = InputCommand.Sanitized(new InputCommand { MoveX = (sbyte)raw });

                Assert.AreEqual(System.Math.Sign(raw), (int)clean.MoveX, $"MoveX {raw} escaped the clamp.");
                Assert.That((int)clean.MoveX, Is.InRange(-1, 1));
            }
        }

        [Test]
        public void Sanitized_DropsUndefinedButtonBits()
        {
            // Six of the eight bits mean nothing today. A future mechanic that claims one must not
            // find it already set by a client that sent 0xFF.
            InputCommand clean = InputCommand.Sanitized(new InputCommand { Buttons = 0xFF });

            Assert.AreEqual(InputCommand.JumpHeldBit | InputCommand.JumpPressedBit, clean.Buttons);
            Assert.IsTrue(clean.JumpHeld);
            Assert.IsTrue(clean.JumpPressed);
        }

        [Test]
        public void Sanitized_PreservesEachButtonIndependently()
        {
            InputCommand heldOnly = InputCommand.Sanitized(
                new InputCommand { Buttons = InputCommand.Pack(jumpHeld: true, jumpPressed: false) });

            Assert.IsTrue(heldOnly.JumpHeld);
            Assert.IsFalse(heldOnly.JumpPressed, "A held jump is not a pressed jump; the edge matters.");

            InputCommand pressedOnly = InputCommand.Sanitized(
                new InputCommand { Buttons = InputCommand.Pack(jumpHeld: false, jumpPressed: true) });

            Assert.IsFalse(pressedOnly.JumpHeld);
            Assert.IsTrue(pressedOnly.JumpPressed);
        }

        // ── what it buys the simulation ────────────────────────────────────────────────────

        [Test]
        public void SanitizedExtremeInput_MovesNoFasterThanLegalInput()
        {
            // The property that matters: MoveSpeed is only a ceiling while MoveX is +/-1. This is the
            // assertion that fails if the sanitize call is ever removed from the ingest path.
            var start = new PlayerState { Grounded = true };

            PlayerState legal = start;
            PlayerState hostile = start;

            for (uint tick = 1; tick <= 60; tick++)
            {
                legal = PlayerMotor.Simulate(
                    legal, new InputCommand { Tick = tick, MoveX = 1 }, _config, Dt);

                hostile = PlayerMotor.Simulate(
                    hostile,
                    InputCommand.Sanitized(new InputCommand { Tick = tick, MoveX = sbyte.MaxValue }),
                    _config,
                    Dt);
            }

            Assert.AreEqual(legal.Position.x, hostile.Position.x, 1e-4f);
            Assert.AreEqual(legal.Velocity.x, hostile.Velocity.x, 1e-4f);
            Assert.That(hostile.Velocity.x, Is.LessThanOrEqualTo(_config.MoveSpeed + 1e-4f));
        }

        [Test]
        public void UnsanitizedExtremeInput_ShowsWhatTheClampPrevents()
        {
            // Kept deliberately: it documents the size of the hole rather than asserting the fix,
            // so the two tests together say "this is what we stop" and "this is why it mattered".
            var start = new PlayerState { Grounded = true };

            PlayerState raw = start;
            for (uint tick = 1; tick <= 60; tick++)
                raw = PlayerMotor.Simulate(raw, new InputCommand { Tick = tick, MoveX = sbyte.MaxValue }, _config, Dt);

            Assert.That(raw.Velocity.x, Is.GreaterThan(_config.MoveSpeed * 10f),
                "If this ever fails, the motor grew its own clamp and Sanitized may be redundant.");
        }
    }
}
