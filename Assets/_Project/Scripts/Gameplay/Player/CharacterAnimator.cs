using Snackdown.Simulation;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Draws the frame <see cref="CharacterAnimation"/> chose, on whichever skin the owner picked.
    /// </summary>
    /// <remarks>
    /// <para>The whole component is a renderer and a clock. Which clip, which frame and which way
    /// round are all decided by <see cref="CharacterAnimation"/>, which has no Unity in it and is
    /// covered by tests; what is left here is reading the state, looking the sprites up and
    /// assigning them, and that is deliberately all it is.</para>
    /// <para>It reads <see cref="PredictedPlayer.State"/> rather than the transform it sits under,
    /// because that field is the one state every peer draws from: predicted for the owner,
    /// authoritative on the server, interpolated for everybody else. Deriving the animation from it
    /// means a character animates in step with the body being drawn on that machine, including
    /// during a correction, and it means nothing about animation is ever sent.</para>
    /// <para>Reading <c>Time.deltaTime</c> here is allowed and would not be one line lower. This is
    /// a view: it never feeds the simulation, so it cannot make a replay produce a different answer
    /// than it did the first time.</para>
    /// </remarks>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CharacterAnimator : MonoBehaviour
    {
        [Tooltip("The character this belongs to. Found in the parents when left empty.")]
        [SerializeField] PredictedPlayer _player;

        SpriteRenderer _renderer;
        CharacterCatalog.Entry _entry;
        bool _dressed;
        readonly CharacterAnimation _animation = new CharacterAnimation();

        void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_player == null) _player = GetComponentInParent<PredictedPlayer>();
        }

        /// <summary>
        /// Hands over the skin to draw. Called by <see cref="CharacterAppearance"/>, which owns the
        /// question of which skin that is.
        /// </summary>
        public void Wear(CharacterCatalog.Entry entry)
        {
            _entry = entry;
            _dressed = true;
        }

        void LateUpdate()
        {
            if (!_dressed || _player == null) return;

            PlayerState state = _player.State;
            CharacterClip clip = CharacterAnimation.Choose(state);
            _animation.Face(state);

            Sprite[] frames = _entry.Frames(clip);
            if (frames == null || frames.Length == 0)
            {
                // A skin with no frames for this clip still faces the right way and still shows the
                // character, rather than vanishing: an unfilled catalog should look unfinished, not
                // broken.
                if (_entry.Portrait != null) _renderer.sprite = _entry.Portrait;
                _renderer.flipX = !_animation.FacingRight;
                return;
            }

            int frame = _animation.Advance(clip, Time.deltaTime, frames.Length);
            _renderer.sprite = frames[frame];
            _renderer.flipX = !_animation.FacingRight;
        }
    }
}
