using NUnit.Framework;
using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for characters colliding with each other.
    /// </summary>
    /// <remarks>
    /// These are pure because peer collision is plain box arithmetic over positions passed in, not
    /// a physics query against the scene — which is precisely what allows a client to replay it
    /// during reconciliation and land where the server did.
    /// </remarks>
    public class PeerCollisionTests
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
            _config.ColliderSize = new Vector2(1f, 1f);   // round numbers make the gaps readable
            _config.SkinWidth = 0.02f;
            _config.GroundMask = 0;                       // no terrain: peers are the only obstacle
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        static InputCommand Input(sbyte moveX = 0) => new InputCommand { Tick = 1, MoveX = moveX };

        static SimulationContext WorldWith(params Vector2[] positions)
        {
            var bodies = new PeerBody[positions.Length];

            for (int i = 0; i < positions.Length; i++)
                bodies[i] = new PeerBody { Id = (ulong)(i + 1), Position = positions[i], Size = Vector2.one };

            return new SimulationContext(bodies, positions.Length, selfId: 99);
        }

        [Test]
        public void WalkingIntoAnotherPlayer_Stops()
        {
            // Self at x=0, peer at x=1.2 — a gap of 0.2 between their edges.
            var state = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(7f, 0f), Grounded = true };
            SimulationContext world = WorldWith(new Vector2(1.2f, 0f));

            state = PlayerMotor.Simulate(state, Input(1), _config, world, Dt);

            Assert.LessOrEqual(state.Position.x, 0.2001f, "should stop at the peer's edge");
            Assert.AreEqual(0f, state.Velocity.x, 0.0001f, "hitting someone should kill horizontal speed");
        }

        [Test]
        public void WithNobodyInTheWay_MovementIsUnaffected()
        {
            var free = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(7f, 0f), Grounded = true };
            var blocked = free;

            free = PlayerMotor.Simulate(free, Input(1), _config, SimulationContext.Empty, Dt);
            blocked = PlayerMotor.Simulate(blocked, Input(1), _config, WorldWith(new Vector2(50f, 0f)), Dt);

            Assert.AreEqual(free.Position.x, blocked.Position.x, 0.0001f);
        }

        [Test]
        public void APeerAtADifferentHeight_DoesNotBlock()
        {
            // Directly ahead horizontally but two units up: no vertical overlap, no obstruction.
            var state = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(7f, 0f), Grounded = true };

            state = PlayerMotor.Simulate(state, Input(1), _config, WorldWith(new Vector2(0.5f, 2f)), Dt);

            Assert.Greater(state.Position.x, 0.2f, "a peer overhead should not stop us");
        }

        [Test]
        public void LandingOnAPeer_GroundsYou()
        {
            // Falling, with a peer one unit below: the boxes meet exactly.
            var state = new PlayerState { Position = new Vector2(0f, 1.2f), Velocity = new Vector2(0f, -10f) };

            state = PlayerMotor.Simulate(state, Input(), _config, WorldWith(Vector2.zero), Dt);

            Assert.IsTrue(state.Grounded, "landing on someone's head should count as ground");
            Assert.AreEqual(0f, state.Velocity.y, 0.0001f, "the fall should stop");
            Assert.GreaterOrEqual(state.Position.y, 1f, "should rest on top, not sink through");
        }

        [Test]
        public void StandingOnAPeer_StaysGrounded()
        {
            // Resting exactly on top with no velocity: the still-standing probe must see the peer.
            var state = new PlayerState { Position = new Vector2(0f, 1f), Velocity = Vector2.zero };

            state = PlayerMotor.Simulate(state, Input(), _config, WorldWith(Vector2.zero), Dt);

            Assert.IsTrue(state.Grounded);
        }

        [Test]
        public void APlayerCannotCollideWithItself()
        {
            // The context includes an entry sharing the caller's id, which must be ignored.
            var bodies = new[] { new PeerBody { Id = 99, Position = new Vector2(0.2f, 0f), Size = Vector2.one } };
            var world = new SimulationContext(bodies, 1, selfId: 99);

            var state = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(7f, 0f), Grounded = true };
            state = PlayerMotor.Simulate(state, Input(1), _config, world, Dt);

            Assert.Greater(state.Position.x, 0.2f, "self must not block self");
        }

        [Test]
        public void AlreadyOverlappingPeers_CanStillMove()
        {
            // Overlaps happen: a correction can land two characters inside each other. Blocking
            // movement then would trap both of them forever, so an existing overlap is skipped
            // rather than resolved — they walk out of it. Nobody is teleported apart either, which
            // would move a character its owner never predicted moving.
            var state = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(7f, 0f), Grounded = true };

            state = PlayerMotor.Simulate(state, Input(1), _config, WorldWith(new Vector2(0.3f, 0f)), Dt);

            Assert.Greater(state.Position.x, 0f, "an overlapped character must not be stuck");
        }

        [Test]
        public void PeerCollisionIsReplayable()
        {
            // The property the whole design rests on: same state, same input, same world -> same
            // result, every time. If this fails, reconciliation cannot replay peer contact.
            var start = new PlayerState { Position = Vector2.zero, Velocity = new Vector2(4f, 2f), Grounded = false };
            SimulationContext world = WorldWith(new Vector2(1.3f, 0f), new Vector2(-1.3f, 0f));

            Assert.AreEqual(Replay(start, world).Position, Replay(start, world).Position);
        }

        PlayerState Replay(PlayerState state, SimulationContext world)
        {
            for (uint t = 0; t < 20; t++)
                state = PlayerMotor.Simulate(state, Input((sbyte)(t % 3 == 0 ? 1 : -1)), _config, world, Dt);

            return state;
        }
    }
}
