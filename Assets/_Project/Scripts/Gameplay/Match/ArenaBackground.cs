using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Tiles a pattern behind the arena, wide enough that the camera never reaches its edge.
    /// </summary>
    /// <remarks>
    /// <para><b>Sized at runtime, not authored.</b> The area that has to be covered is not the
    /// arena: it is whichever is larger of the arena and the camera's view, on each axis
    /// independently. Arena01 is 26x9 against a 24.9x14 view, so the height that matters comes from
    /// the camera and the width from the arena. Baking a number in the scene would mean re-deriving
    /// it for every arena, and re-deriving it again the moment a Pixel Perfect Camera decides the
    /// orthographic size for itself.</para>
    /// <para>Static, with no parallax. The camera can travel about a unit in this arena, which is
    /// less than a single tile: a parallax layer would be doing arithmetic to produce a movement
    /// nobody can see. It is left out because it would not show, not because it would be hard.</para>
    /// </remarks>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ArenaBackground : MonoBehaviour
    {
        [Tooltip("The arena to cover. Falls back to whichever one is loaded.")]
        [SerializeField] ArenaBounds _bounds;

        [Tooltip("The camera that has to stay inside it. Falls back to the main camera.")]
        [SerializeField] Camera _camera;

        [Tooltip("Extra world units past the area that must be covered, on every side.")]
        [Min(0f)] [SerializeField] float _margin = 2f;

        SpriteRenderer _renderer;
        Vector2 _fitted = Vector2.zero;

        void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_camera == null) _camera = Camera.main;
        }

        void OnEnable() => Fit();

        /// <remarks>
        /// Every frame, because the two things it depends on both change without saying so: the
        /// aspect ratio when the window is resized, and the orthographic size when something else
        /// owns it. The assignment is skipped unless the answer actually moved — writing
        /// <c>size</c> rebuilds the tiled mesh, and doing that sixty times a second for a
        /// rectangle that has not changed is the kind of cost nothing would ever attribute to a
        /// background.
        /// </remarks>
        void LateUpdate() => Fit();

        void Fit()
        {
            if (_renderer == null) return;

            ArenaBounds bounds = _bounds != null ? _bounds : ArenaBounds.Current;
            if (_camera == null) _camera = Camera.main;

            Vector2 arena = bounds != null ? bounds.Size : Vector2.zero;
            Vector2 center = bounds != null ? bounds.Center : Vector2.zero;

            Vector2 view = Vector2.zero;
            if (_camera != null && _camera.orthographic)
                view = new Vector2(_camera.orthographicSize * 2f * _camera.aspect, _camera.orthographicSize * 2f);

            var wanted = new Vector2(
                Mathf.Max(arena.x, view.x) + _margin * 2f,
                Mathf.Max(arena.y, view.y) + _margin * 2f);

            if (wanted == _fitted && (Vector2)transform.position == center) return;

            _fitted = wanted;
            _renderer.size = wanted;
            transform.position = new Vector3(center.x, center.y, transform.position.z);
        }
    }
}
