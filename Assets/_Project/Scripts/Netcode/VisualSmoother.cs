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

        void LateUpdate()
        {
            if (!SmoothingEnabled)
            {
                _offset = Vector3.zero;
                transform.localPosition = Vector3.zero;
                return;
            }

            _offset *= Mathf.Exp(-_decayRate * Time.deltaTime);
            if (_offset.sqrMagnitude < 0.000001f) _offset = Vector3.zero;

            transform.localPosition = _offset;
        }
    }
}
