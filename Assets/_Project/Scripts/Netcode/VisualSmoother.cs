using UnityEngine;

namespace Snackdown.Netcode
{
    /// <summary>
    /// Decouples what the player <i>sees</i> from what the simulation <i>knows</i>. Sits on a child
    /// of the character and carries a decaying positional offset, so the logical transform can jump
    /// wherever it must while the sprite glides.
    /// </summary>
    /// <remarks>
    /// <para>It solves two problems with one mechanism. The simulation only moves on network ticks
    /// (30 Hz) while the screen refreshes far more often, so raw logical motion would visibly step;
    /// and a reconciliation can teleport the character mid-stride. In both cases the fix is the
    /// same: let the visual lag behind by an offset and shrink that offset toward zero every frame.</para>
    /// <para>Correction stays <b>logically instant</b> — <see cref="Snackdown.Gameplay.Player.PlayerMotor"/>
    /// always runs on the corrected state, never on the smoothed one. Interpolating the simulated
    /// state instead would make errors compound tick over tick instead of closing.</para>
    /// <para>Exponential decay (not a fixed-speed slide) so a large error closes fast and a small
    /// one closes gently, and the result never depends on framerate.</para>
    /// </remarks>
    public class VisualSmoother : MonoBehaviour
    {
        [Tooltip("Decay rate of the visual error, in e-folds per second. Higher = snappier and more honest; lower = smoother and laggier.")]
        [SerializeField] float _decayRate = 22f;

        [Tooltip("Errors larger than this are not smoothed at all — a respawn or teleport should not slide across the level.")]
        [SerializeField] float _maxSmoothedError = 3f;

        /// <summary>Global toggle, flipped from the debug overlay to show the raw stepping underneath.</summary>
        public static bool SmoothingEnabled = true;

        Vector3 _offset;
        Vector2 _velocity;
        float _sinceTick;
        float _tickDelta = 1f / 30f;

        /// <summary>Where the sprite is authored to sit, which is not necessarily the origin.</summary>
        /// <remarks>
        /// The character's art has its feet on the bottom edge of a square frame and empty space
        /// above its head, so the sprite has to hang a little above the transform for the drawing
        /// and the collider to have the same top and bottom. Smoothing around that rest position
        /// rather than around zero is what lets the prefab say so.
        /// </remarks>
        Vector3 _rest;

        /// <summary>How far the sprite currently sits from the truth. Read by the debug overlay.</summary>
        public float CurrentError => _offset.magnitude;

        /// <summary>
        /// Reports that the logical transform moved by <paramref name="delta"/>; the visual stays
        /// put for now and catches up over the next frames.
        /// </summary>
        public void AbsorbMovement(Vector3 delta)
        {
            _offset += delta;
            if (_offset.sqrMagnitude > _maxSmoothedError * _maxSmoothedError)
                _offset = Vector3.zero;
        }

        /// <summary>Drops any pending error immediately (spawn, teleport, respawn).</summary>
        public void Snap() => _offset = Vector3.zero;

        void Awake() => _rest = transform.localPosition;

        /// <summary>
        /// Reports that a tick has just landed, and how fast the character is now moving.
        /// </summary>
        /// <remarks>
        /// <para>What the sprite is drawn from between ticks. The simulation moves at 30Hz and the
        /// screen does not, so between two ticks the character's real position is somewhere the
        /// simulation has not computed yet; carrying the velocity forward puts the sprite there
        /// instead of leaving it at the last tick.</para>
        /// <para>The alternative this replaced was to absorb every tick's movement as if it were an
        /// error and decay it. That holds the sprite still for a tick and then drags it after the
        /// character, so the faster you move the further behind the drawing gets: at terminal
        /// falling speed it sat between 0.62 and 1.28 units above the truth, against a character
        /// 0.81 units tall. You could land, and jump again, while still drawn in the air.</para>
        /// </remarks>
        public void OnTick(Vector2 velocity, float tickDelta)
        {
            _velocity = velocity;
            _tickDelta = tickDelta;
            _sinceTick = 0f;
        }

        void LateUpdate()
        {
            _sinceTick += Time.deltaTime;

            if (!SmoothingEnabled)
            {
                _offset = Vector3.zero;
                transform.localPosition = _rest;
                return;
            }

            _offset *= Mathf.Exp(-_decayRate * Time.deltaTime);
            if (_offset.sqrMagnitude < 0.000001f) _offset = Vector3.zero;

            // Never past the next tick: a tick that does not arrive should leave the sprite
            // waiting where it was, not carry it across the level on a stale velocity.
            Vector2 lead = _velocity * Mathf.Min(_sinceTick, _tickDelta);

            transform.localPosition = _rest + (Vector3)lead + _offset;
        }
    }
}
