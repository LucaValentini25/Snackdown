using UnityEngine;

namespace Snackdown.Simulation
{
    /// <summary>
    /// The shared simulation. Run identically by the server and by every predicting client:
    /// <c>state + input + world -> state</c>.
    /// </summary>
    /// <remarks>
    /// <para>This is the foundation the whole netcode layer stands on, so it obeys three rules:</para>
    /// <list type="number">
    /// <item>It never reads <c>Time</c>, a <c>Transform</c>, or a <c>Rigidbody2D</c>. Everything it
    /// needs arrives as an argument, so it can be called ten times in a single frame during a
    /// reconciliation replay and give the same answer every time.</item>
    /// <item>Collisions are resolved with <b>casts</b>, not with <c>Physics2D.Simulate</c>. A cast is
    /// a query against the scene — repeatable and side-effect free. Stepping the physics world is
    /// neither: it advances every body at once and its contact ordering is not reproducible across
    /// machines, which is exactly what would make replay lie.</item>
    /// <item>The ground check happens HERE, where the input is consumed. In the original project it
    /// ran server-only while the owner read the result a round trip later — the authority mismatch
    /// this rebuild exists to fix.</item>
    /// </list>
    /// <para><b>Structured as ordered steps</b> so mechanics can be added without touching the ones
    /// already working. A double jump, a wall slide or a dash is a new step inserted at a defined
    /// point; the fixed order is what keeps two machines agreeing. One monolithic method stops
    /// being safe to extend at roughly the third mechanic.</para>
    /// <para>Other characters arrive through <see cref="SimulationContext"/> as plain boxes, never
    /// as live references — a replay of tick N must see the world as it was at tick N, and a live
    /// object would answer with where it is now.</para>
    /// </remarks>
    public static class PlayerMotor
    {
        /// <summary>
        /// Advances one character by exactly one tick. Deterministic: same inputs and same static
        /// geometry, same output, always — see the remarks on the class for why terrain is the one
        /// thing that does not arrive as an argument.
        /// </summary>
        public static PlayerState Simulate(
            PlayerState state, InputCommand input, MovementConfig cfg, in SimulationContext world, float dt)
        {
            state = StunStep(state, ref input, dt);
            state = HorizontalStep(state, input, cfg, dt);
            state = FeelTimersStep(state, input, cfg, dt);
            state = JumpStep(state, input, cfg);
            state = GravityStep(state, cfg, dt);
            return MoveAndCollideStep(state, cfg, world, dt);
        }

        /// <summary>Overload for callers with no other characters to consider.</summary>
        public static PlayerState Simulate(PlayerState state, InputCommand input, MovementConfig cfg, float dt)
            => Simulate(state, input, cfg, SimulationContext.Empty, dt);

        // ==================================================================================
        //  Steps, in the order they run
        // ==================================================================================

        /// <remarks>
        /// A stunned player keeps falling, sliding and colliding — they have lost control, not their
        /// place in the world. Clearing the input rather than skipping the remaining steps is what
        /// draws that distinction.
        /// </remarks>
        static PlayerState StunStep(PlayerState state, ref InputCommand input, float dt)
        {
            state.StunTimer = Mathf.Max(0f, state.StunTimer - dt);
            if (state.IsStunned) input = default;
            return state;
        }

        static PlayerState HorizontalStep(PlayerState state, InputCommand input, MovementConfig cfg, float dt)
        {
            float target = input.MoveX * cfg.MoveSpeed;
            float acceleration = state.Grounded ? cfg.GroundAcceleration : cfg.AirAcceleration;
            state.Velocity.x = Mathf.MoveTowards(state.Velocity.x, target, acceleration * dt);
            return state;
        }

        /// <remarks>
        /// Refreshed while grounded, drained while airborne. These live in the state precisely so a
        /// rewind restores them along with position.
        /// </remarks>
        static PlayerState FeelTimersStep(PlayerState state, InputCommand input, MovementConfig cfg, float dt)
        {
            state.CoyoteTimer = state.Grounded ? cfg.CoyoteTime : Mathf.Max(0f, state.CoyoteTimer - dt);
            state.JumpBufferTimer = input.JumpPressed
                ? cfg.JumpBufferTime
                : Mathf.Max(0f, state.JumpBufferTimer - dt);

            return state;
        }

        static PlayerState JumpStep(PlayerState state, InputCommand input, MovementConfig cfg)
        {
            if (state.JumpBufferTimer > 0f && state.CoyoteTimer > 0f)
            {
                state.Velocity.y = cfg.JumpVelocity;
                state.JumpBufferTimer = 0f;
                state.CoyoteTimer = 0f;
                state.Grounded = false;
            }
            else if (!input.JumpHeld && state.Velocity.y > cfg.JumpReleaseSpeed)
            {
                // Variable jump height. A clamp rather than a per-tick multiplier, so the result
                // doesn't depend on how many ticks the button happened to straddle.
                state.Velocity.y = cfg.JumpReleaseSpeed;
            }

            return state;
        }

        static PlayerState GravityStep(PlayerState state, MovementConfig cfg, float dt)
        {
            state.Velocity.y = Mathf.Max(state.Velocity.y - cfg.Gravity * dt, -cfg.MaxFallSpeed);
            return state;
        }

        // ==================================================================================
        //  Movement and collision
        // ==================================================================================

        /// <summary>
        /// Moves the box along each axis in turn, stopping it against solid geometry and against
        /// other characters.
        /// </summary>
        /// <remarks>
        /// Axis-separated so that sliding along a wall doesn't also cancel falling, and vice versa.
        /// Other characters are resolved after the terrain on each axis, so someone squeezed
        /// between another player and a wall ends up against the wall rather than pushed through
        /// it — the level always wins.
        /// </remarks>
        static PlayerState MoveAndCollideStep(
            PlayerState state, MovementConfig cfg, in SimulationContext world, float dt)
        {
            float skin = cfg.SkinWidth;
            Vector2 probeSize = cfg.ColliderSize - new Vector2(skin * 2f, skin * 2f);

            // --- X --------------------------------------------------------------------------
            float dx = state.Velocity.x * dt;
            if (!Mathf.Approximately(dx, 0f))
            {
                float sign = Mathf.Sign(dx);
                RaycastHit2D hit = Physics2D.BoxCast(
                    state.Position, probeSize, 0f,
                    new Vector2(sign, 0f), Mathf.Abs(dx) + skin, cfg.GroundMask);

                if (hit.collider != null)
                {
                    dx = sign * Mathf.Max(0f, hit.distance - skin);
                    state.Velocity.x = 0f;
                }

                float allowed = SweepPeers(state.Position, cfg.ColliderSize, dx, world, horizontal: true);
                if (!Mathf.Approximately(allowed, dx)) state.Velocity.x = 0f;

                state.Position.x += allowed;
            }

            // --- Y --------------------------------------------------------------------------
            // Grounded is recomputed from scratch every tick. Never carried over: a stale "true"
            // would hand out a free mid-air jump.
            state.Grounded = false;

            float dy = state.Velocity.y * dt;
            if (!Mathf.Approximately(dy, 0f))
            {
                float sign = Mathf.Sign(dy);
                RaycastHit2D hit = Physics2D.BoxCast(
                    state.Position, probeSize, 0f,
                    new Vector2(0f, sign), Mathf.Abs(dy) + skin, cfg.GroundMask);

                if (hit.collider != null)
                {
                    dy = sign * Mathf.Max(0f, hit.distance - skin);
                    if (sign < 0f) state.Grounded = true;
                    state.Velocity.y = 0f;
                }

                float allowed = SweepPeers(state.Position, cfg.ColliderSize, dy, world, horizontal: false);
                if (!Mathf.Approximately(allowed, dy))
                {
                    // Landing on someone's head grounds you on them, which is what makes standing
                    // on another player work at all.
                    if (sign < 0f) state.Grounded = true;
                    state.Velocity.y = 0f;
                }

                state.Position.y += allowed;
            }
            else
            {
                // Standing perfectly still: gravity is being cancelled every tick, so the cast
                // above never runs. Probe downward explicitly to keep Grounded honest.
                RaycastHit2D probe = Physics2D.BoxCast(
                    state.Position, probeSize, 0f, Vector2.down, skin * 2f, cfg.GroundMask);

                state.Grounded = probe.collider != null
                                 || StandingOnPeer(state.Position, cfg.ColliderSize, skin, world);
            }

            return state;
        }

        /// <summary>
        /// How far the character may actually move along one axis before meeting another character.
        /// </summary>
        /// <remarks>
        /// <para>Plain box arithmetic rather than a physics query, because the other characters are
        /// <i>positions from a tick</i>, not colliders in the scene. That is what makes peer
        /// collision predictable and replayable: the same numbers give the same answer on both
        /// machines, in any frame, in any order.</para>
        /// <para>Bodies already overlapping are skipped rather than pushed apart. Resolving an
        /// existing overlap would move a character it was never told to move, which the owner never
        /// predicted — the server's snapshot separates them instead.</para>
        /// </remarks>
        static float SweepPeers(
            Vector2 position, Vector2 size, float delta, in SimulationContext world, bool horizontal)
        {
            if (world.Count == 0 || Mathf.Approximately(delta, 0f)) return delta;

            float allowed = delta;
            float sign = Mathf.Sign(delta);
            Vector2 half = size * 0.5f;

            for (int i = 0; i < world.Count; i++)
            {
                PeerBody peer = world.Get(i);
                if (peer.Id == world.SelfId) continue;

                Vector2 peerHalf = peer.Size * 0.5f;

                // Only bodies overlapping on the other axis can block movement on this one.
                float acrossGap = horizontal
                    ? Mathf.Abs(position.y - peer.Position.y) - (half.y + peerHalf.y)
                    : Mathf.Abs(position.x - peer.Position.x) - (half.x + peerHalf.x);

                if (acrossGap >= 0f) continue;

                float selfEdge = horizontal
                    ? (sign > 0f ? position.x + half.x : position.x - half.x)
                    : (sign > 0f ? position.y + half.y : position.y - half.y);

                float peerEdge = horizontal
                    ? (sign > 0f ? peer.Position.x - peerHalf.x : peer.Position.x + peerHalf.x)
                    : (sign > 0f ? peer.Position.y - peerHalf.y : peer.Position.y + peerHalf.y);

                float gap = sign > 0f ? peerEdge - selfEdge : selfEdge - peerEdge;

                if (gap < 0f) continue;                  // already overlapping
                if (Mathf.Abs(delta) <= gap) continue;   // stops short of this peer

                float limited = sign * gap;
                if (Mathf.Abs(limited) < Mathf.Abs(allowed)) allowed = limited;
            }

            return allowed;
        }

        /// <summary>True when another character is directly underfoot, close enough to stand on.</summary>
        static bool StandingOnPeer(Vector2 position, Vector2 size, float skin, in SimulationContext world)
        {
            Vector2 half = size * 0.5f;

            for (int i = 0; i < world.Count; i++)
            {
                PeerBody peer = world.Get(i);
                if (peer.Id == world.SelfId) continue;

                Vector2 peerHalf = peer.Size * 0.5f;
                if (Mathf.Abs(position.x - peer.Position.x) >= half.x + peerHalf.x) continue;

                float gap = (position.y - half.y) - (peer.Position.y + peerHalf.y);
                if (gap >= -skin && gap <= skin * 2f) return true;
            }

            return false;
        }
    }
}
