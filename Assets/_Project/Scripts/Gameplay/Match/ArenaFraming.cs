using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Sizes the camera so the whole arena is on screen and as large as it will go.
    /// </summary>
    /// <remarks>
    /// <para>This replaced a Pixel Perfect Camera, and the reason is worth keeping. That component
    /// only ever zooms by a whole number, which is what makes pixel art land on exact pixels — and
    /// on any screen that is not a whole multiple of the reference, the largest whole number that
    /// fits leaves the game small. At 1366x768 it could only reach x2, so the arena drew at 960x540
    /// with 406 pixels of bar across and 228 down: less than half the screen used. Cropping made
    /// those bars black and not cropping filled them with background, and neither made the game any
    /// bigger.</para>
    /// <para>So the zoom is free now. The cost is real and it is not fairness: an arena is walled and
    /// fits entirely on screen either way, so a wider screen only ever reveals more background, never
    /// more of the fight. What is lost is exactness — a pixel of art can cover two screen pixels
    /// while its neighbour covers three, which shows up as a faint shimmer while the camera or a
    /// character moves.</para>
    /// <para>Fitting means the larger of the two demands: the height the arena needs, and the height
    /// its width implies at this aspect ratio. Taking the larger is what guarantees neither axis is
    /// cut off, and any surplus is background — which <see cref="ArenaBackground"/> already sizes
    /// against the same view, so nothing has to be told the screen changed shape.</para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public class ArenaFraming : MonoBehaviour
    {
        [Tooltip("The arena to fit. Falls back to whichever one is loaded.")]
        [SerializeField] ArenaBounds _bounds;

        [Tooltip("World units of breathing room past the arena on the tighter axis.")]
        [Min(0f)] [SerializeField] float _margin = 0.25f;

        Camera _camera;

        void Awake() => _camera = GetComponent<Camera>();

        void OnEnable() => Fit();

        /// <remarks>
        /// Every frame, because the aspect ratio changes without announcing it — a resized window, a
        /// phone rotating — and the assignment is skipped unless the answer actually moved.
        /// </remarks>
        void LateUpdate() => Fit();

        void Fit()
        {
            if (_camera == null || !_camera.orthographic) return;

            ArenaBounds bounds = _bounds != null ? _bounds : ArenaBounds.Current;
            if (bounds == null || _camera.aspect <= 0f) return;

            Vector2 arena = bounds.Size + new Vector2(_margin * 2f, _margin * 2f);
            float wanted = Mathf.Max(arena.y * 0.5f, arena.x * 0.5f / _camera.aspect);

            if (!Mathf.Approximately(_camera.orthographicSize, wanted)) _camera.orthographicSize = wanted;
        }
    }
}
