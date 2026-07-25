using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// The shared simulation. One function, one file, run identically by the server and by every
    /// predicting client: <c>state + input -> state</c>.
    /// </summary>
    /// <remarks>
    /// <para>This is the foundation the whole netcode layer stands on, so it obeys three rules:</para>
    /// <list type="number">
    /// <item>It never reads <c>Time</c>, a <c>Transform</c>, or a <c>Rigidbody2D</c>. Everything it
    /// needs arrives as an argument, so it can be called ten times in a single frame during a
    /// reconciliation replay and give the same answer every time.</item>
    /// <item>Collisions are resolved with <b>casts</b>, not with <c>Physics2D.Simulate</c>. A cast is
    /// a query against static geometry — repeatable and side-effect free. Stepping the physics
    /// world is neither: it advances every body at once and its contact ordering is not
    /// reproducible across machines, which is exactly what would make replay lie.</item>
    /// <item>The ground check happens HERE, where the input is consumed. In the original project it
    /// ran server-only while the owner read the result a round trip later — the authority mismatch
    /// this rebuild exists to fix.</item>
    /// </list>
    /// <para>Because <see cref="MovementConfig.GroundMask"/> excludes players, the geometry this
    /// queries is static, which is what makes the cast results deterministic. Player-vs-player
    /// interaction (the head bounce) is resolved separately, server-side, in Phase 3.</para>
    /// </remarks>
    public static class PlayerMotor
    {
        /// <summary>
        /// Advances one character by exactly one tick. Pure: same inputs, same output, always.
        /// </summary>
        public static PlayerState Simulate(PlayerState state, InputCommand input, MovementConfig cfg, float dt)
        {
            // --- horizontal intent ----------------------------------------------------------
            float target = input.MoveX * cfg.MoveSpeed;
            float acceleration = state.Grounded ? cfg.GroundAcceleration : cfg.AirAcceleration;
            state.Velocity.x = Mathf.MoveTowards(state.Velocity.x, target, acceleration * dt);

            // --- feel timers ----------------------------------------------------------------
            // Refreshed while grounded, drained while airborne. Part of the state precisely so a
            // rewind restores them along with position.
            state.CoyoteTimer = state.Grounded ? cfg.CoyoteTime : Mathf.Max(0f, state.CoyoteTimer - dt);
            state.JumpBufferTimer = input.JumpPressed
                ? cfg.JumpBufferTime
                : Mathf.Max(0f, state.JumpBufferTimer - dt);

            // --- jump -----------------------------------------------------------------------
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

            // --- gravity --------------------------------------------------------------------
            state.Velocity.y = Mathf.Max(state.Velocity.y - cfg.Gravity * dt, -cfg.MaxFallSpeed);

            return MoveAndCollide(state, cfg, dt);
        }

        /// <summary>
        /// Moves the box along each axis in turn, stopping it against solid geometry.
        /// Axis-separated so that sliding along a wall doesn't also cancel falling, and vice versa.
        /// </summary>
        static PlayerState MoveAndCollide(PlayerState state, MovementConfig cfg, float dt)
        {
            float skin = cfg.SkinWidth;
            // Cast a slightly shrunken box so a character resting flush against a surface doesn't
            // start its cast already overlapping it (which would report distance 0 and stick).
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

                state.Position.x += dx;
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

                state.Position.y += dy;
            }
            else
            {
                // Standing perfectly still: gravity is being cancelled every tick, so the cast
                // above never runs. Probe downward explicitly to keep Grounded honest.
                RaycastHit2D probe = Physics2D.BoxCast(
                    state.Position, probeSize, 0f, Vector2.down, skin * 2f, cfg.GroundMask);
                state.Grounded = probe.collider != null;
            }

            return state;
        }
    }
}
