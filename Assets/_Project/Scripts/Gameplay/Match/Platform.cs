using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// A ledge that stands on its own, drawn from the same tiles as the terrain around it.
    /// </summary>
    /// <remarks>
    /// <para><b>Not part of the tilemap, deliberately.</b> A tilemap cell is solid or it is not, and
    /// a platform is on its way to being neither: dropping through one, or moving, is a property of
    /// the ledge rather than of the ground it is made of. Keeping it a GameObject with its own
    /// collider is what leaves room for that; folding it into the tilemap would mean taking it back
    /// out again to add either.</para>
    /// <para>Three renderers rather than one stretched sprite, because the art is 16px tiles on a
    /// grid and a single renderer scaled to five units would make rectangles out of square pixels.
    /// Two caps of exactly one cell each and a tiled middle keeps every pixel square at any width
    /// that is a whole number of cells.</para>
    /// <para>The collider is the authority on size and the renderers follow it, never the reverse.
    /// The motor casts against the collider, so a platform that looked wider than it caught you
    /// would be a platform you fall through the end of.</para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(BoxCollider2D))]
    public class Platform : MonoBehaviour
    {
        [Tooltip("Cell at the left end of the ledge.")]
        [SerializeField] Sprite _leftCap;

        [Tooltip("Cell repeated across the middle.")]
        [SerializeField] Sprite _fill;

        [Tooltip("Cell at the right end of the ledge.")]
        [SerializeField] Sprite _rightCap;

        [SerializeField] SpriteRenderer _left;
        [SerializeField] SpriteRenderer _middle;
        [SerializeField] SpriteRenderer _right;

        /// <summary>Width and height of one cell of the terrain art, in world units.</summary>
        public const float Cell = 0.5f;

        void Awake() => Rebuild();

        void OnValidate() => Rebuild();

        /// <summary>Lays the three renderers out to cover exactly what the collider covers.</summary>
        /// <remarks>
        /// A ledge narrower than two cells is drawn as caps alone with no middle, which is the only
        /// case where the arithmetic could ask a renderer for a negative width.
        /// </remarks>
        public void Rebuild()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null || _left == null || _middle == null || _right == null) return;

            float width = box.size.x;
            float height = box.size.y;
            Vector2 center = box.offset;

            Apply(_left, _leftCap, new Vector2(center.x - width * 0.5f + Cell * 0.5f, center.y), new Vector2(Cell, height));
            Apply(_right, _rightCap, new Vector2(center.x + width * 0.5f - Cell * 0.5f, center.y), new Vector2(Cell, height));

            float middleWidth = width - Cell * 2f;
            _middle.gameObject.SetActive(middleWidth > 0f);
            if (middleWidth > 0f) Apply(_middle, _fill, center, new Vector2(middleWidth, height));
        }

        static void Apply(SpriteRenderer renderer, Sprite sprite, Vector2 position, Vector2 size)
        {
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;
            renderer.transform.localPosition = new Vector3(position.x, position.y, 0f);
        }
    }
}
