using UnityEngine;

namespace Snackdown.Gameplay.Fruits
{
    /// <summary>
    /// Spins a fruit where it stands.
    /// </summary>
    /// <remarks>
    /// <para>No state to derive and nothing to agree about: a fruit does one thing until it is
    /// collected. That is what makes this a view in the sense the HUD is one — every peer runs it
    /// off its own clock and no peer can be wrong about it, because the animation is not an outcome
    /// anybody could disagree with.</para>
    /// <para>Which fruit it is still comes from the server. <see cref="Fruit"/> owns that question
    /// and hands the frames over, the same way <see cref="Snackdown.Gameplay.Player.CharacterAppearance"/>
    /// hands a skin to its animator.</para>
    /// </remarks>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FruitAnimator : MonoBehaviour
    {
        SpriteRenderer _renderer;
        Sprite[] _frames;
        float _elapsed;

        void Awake() => _renderer = GetComponent<SpriteRenderer>();

        /// <summary>Sets the sequence to spin. Called by <see cref="Fruit"/> when the kind is known.</summary>
        /// <remarks>
        /// Starts the spin over. Fruit is dressed on spawn and again if the kind changes, and
        /// carrying the elapsed time across would put a newly dressed fruit at a frame belonging to
        /// the sequence it is no longer playing.
        /// </remarks>
        public void Play(Sprite[] frames)
        {
            _frames = frames;
            _elapsed = 0f;
        }

        void Update()
        {
            if (_frames == null || _frames.Length == 0) return;

            _elapsed += Time.deltaTime;
            _renderer.sprite = _frames[PixelAnimation.FrameAt(_elapsed, _frames.Length)];
        }
    }
}
