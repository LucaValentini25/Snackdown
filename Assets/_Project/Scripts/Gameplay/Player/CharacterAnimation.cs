using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Turns the state a character is in into the frame that should be drawn for it.
    /// </summary>
    /// <remarks>
    /// <para><b>Derived, never replicated.</b> Every peer already agrees about
    /// <see cref="PlayerState"/> — the owner predicts it, the server owns it, and a spectator gets it
    /// interpolated — so every peer can reach the same clip from it without a byte being sent. A
    /// <c>NetworkAnimator</c> would have put the same conclusion on the wire a second time, at which
    /// point it can disagree with the state it was drawn from: a character sliding while its run
    /// animation says it stopped. Deriving makes that disagreement unrepresentable.</para>
    /// <para>Split from the component for the reason <see cref="Snackdown.Gameplay.Match.SpectatorTargetRing"/>
    /// was: the interesting part is a decision, and a decision reachable only from a running match
    /// with two peers is a decision nothing tests. No <c>Time</c>, no <c>Transform</c>, no renderer —
    /// the elapsed seconds arrive as an argument.</para>
    /// </remarks>
    public class CharacterAnimation
    {
        /// <summary>
        /// Horizontal speed below which a character counts as standing still, in units per second.
        /// </summary>
        /// <remarks>
        /// Not zero. Ground friction leaves a fading remainder in the velocity for a few ticks after
        /// the stick is released, and comparing that against zero flickers between run and idle on
        /// alternating frames. Half a unit is under a tenth of walking speed: far enough above the
        /// remainder to be quiet, far enough below a real step never to swallow one.
        /// </remarks>
        public const float MinimumRunSpeed = 0.5f;

        CharacterClip _clip = CharacterClip.Idle;
        float _elapsed;
        bool _facingRight = true;

        /// <summary>Which way the character is drawn. Kept across the moments it is not moving.</summary>
        public bool FacingRight => _facingRight;

        /// <summary>The clip currently being played.</summary>
        public CharacterClip Clip => _clip;

        /// <summary>
        /// The clip a character in this state should be showing.
        /// </summary>
        /// <remarks>
        /// Order is the whole rule. Being stunned outranks being airborne because a stun is what a
        /// player needs to see and it lasts a fraction of a second; airborne outranks running
        /// because horizontal speed survives a jump and would otherwise keep the run playing with
        /// nothing underfoot.
        /// </remarks>
        public static CharacterClip Choose(in PlayerState state)
        {
            if (state.IsStunned) return CharacterClip.Hit;
            if (!state.Grounded) return state.Velocity.y > 0f ? CharacterClip.Jump : CharacterClip.Fall;
            return Mathf.Abs(state.Velocity.x) > MinimumRunSpeed ? CharacterClip.Run : CharacterClip.Idle;
        }

        /// <summary>
        /// Advances the animation by <paramref name="deltaTime"/> and says which frame to draw.
        /// </summary>
        /// <param name="frameCount">How many frames the chosen clip has. Zero or fewer answers 0.</param>
        /// <remarks>
        /// Changing clip restarts it rather than carrying the elapsed time across. Landing mid-way
        /// through a fall and continuing the run animation from frame six looks like a skipped step,
        /// and the clips have different lengths anyway.
        /// </remarks>
        public int Advance(CharacterClip clip, float deltaTime, int frameCount)
        {
            if (clip != _clip)
            {
                _clip = clip;
                _elapsed = 0f;
            }
            else
            {
                _elapsed += Mathf.Max(0f, deltaTime);
            }

            return PixelAnimation.FrameAt(_elapsed, frameCount);
        }

        /// <summary>
        /// Updates which way the character faces from the direction it is moving.
        /// </summary>
        /// <remarks>
        /// A stun does not turn anybody around. Being bounced off a head sends a character backwards
        /// at speed, and reading facing from velocity through it would spin them to look the way
        /// they are being thrown — which reads as the player having chosen it.
        /// </remarks>
        public void Face(in PlayerState state)
        {
            if (state.IsStunned) return;
            if (state.Velocity.x > MinimumRunSpeed) _facingRight = true;
            else if (state.Velocity.x < -MinimumRunSpeed) _facingRight = false;
        }
    }
}
