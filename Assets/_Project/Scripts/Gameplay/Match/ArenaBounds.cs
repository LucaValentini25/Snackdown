using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// The rectangle a camera is allowed to show in this arena.
    /// </summary>
    /// <remarks>
    /// <para>Lives in the arena scene, because it describes the arena. Arenas are not all the same
    /// size: some fit inside a single camera view and some do not, and a spectator who can pan off
    /// the edge of a small map is looking at empty space instead of at the match.</para>
    /// <para>Authored as a rectangle rather than derived from the level geometry. Deriving it would
    /// mean the reachable area and the visible area are the same thing, and they are not — a pit
    /// below the floor is somewhere a player can fall, not somewhere the camera should go.</para>
    /// </remarks>
    public class ArenaBounds : MonoBehaviour
    {
        [Tooltip("Centre of the area the camera may show, in world space.")]
        [SerializeField] Vector2 _center = Vector2.zero;

        [Tooltip("Full width and height of that area.")]
        [SerializeField] Vector2 _size = new Vector2(32f, 18f);

        public Vector2 Center => _center;
        public Vector2 Size => _size;

        /// <summary>The arena the camera is currently looking at, if one is loaded.</summary>
        public static ArenaBounds Current { get; private set; }

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <summary>
        /// Pulls a camera centre back inside the bounds, given the half-extents it can see.
        /// </summary>
        /// <remarks>
        /// When the arena is narrower than the view on an axis, the clamp collapses to the centre
        /// of the arena instead of inverting. That is what makes one component serve both kinds of
        /// map: on a small arena the camera simply has nowhere to go and holds still.
        /// </remarks>
        public Vector2 Clamp(Vector2 desired, Vector2 viewHalfExtents)
        {
            Vector2 half = _size * 0.5f;

            float x = half.x <= viewHalfExtents.x
                ? _center.x
                : Mathf.Clamp(desired.x, _center.x - half.x + viewHalfExtents.x, _center.x + half.x - viewHalfExtents.x);

            float y = half.y <= viewHalfExtents.y
                ? _center.y
                : Mathf.Clamp(desired.y, _center.y - half.y + viewHalfExtents.y, _center.y + half.y - viewHalfExtents.y);

            return new Vector2(x, y);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(_center, _size);
        }
    }
}
