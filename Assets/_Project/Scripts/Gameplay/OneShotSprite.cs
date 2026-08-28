using UnityEngine;

namespace Snackdown.Gameplay
{
    /// <summary>
    /// Plays a sprite sequence once where it was put, then removes itself.
    /// </summary>
    /// <remarks>
    /// <para>Built in code rather than from a prefab, and it matters why. The effect has to draw in
    /// the same layer and order as the thing it replaces, and a prefab would carry its own copy of
    /// those — a second place to keep in step, silently wrong the first time a sorting layer is
    /// renamed. Copying them off the renderer that is going away cannot drift.</para>
    /// <para>Nothing here is networked. The burst is a reaction to an event every peer is told
    /// about, so each peer plays its own; sending the effect as well would put a decoration on the
    /// wire and let it arrive without the thing it decorates.</para>
    /// </remarks>
    public class OneShotSprite : MonoBehaviour
    {
        Sprite[] _frames;
        SpriteRenderer _renderer;
        float _elapsed;

        /// <summary>
        /// Puts a burst of <paramref name="frames"/> where <paramref name="like"/> is drawing.
        /// </summary>
        /// <remarks>
        /// Answers null for an empty sequence rather than leaving an object that draws nothing and
        /// waits to be destroyed. A missing sheet should cost nothing, not a GameObject per pickup.
        /// </remarks>
        public static OneShotSprite Play(Sprite[] frames, Vector3 position, SpriteRenderer like)
        {
            if (frames == null || frames.Length == 0) return null;

            var host = new GameObject("Pop");
            host.transform.position = position;

            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = frames[0];

            if (like != null)
            {
                renderer.sortingLayerID = like.sortingLayerID;
                renderer.sortingOrder = like.sortingOrder;
                renderer.sharedMaterial = like.sharedMaterial;
                host.transform.localScale = like.transform.lossyScale;
            }

            var shot = host.AddComponent<OneShotSprite>();
            shot._frames = frames;
            shot._renderer = renderer;
            return shot;
        }

        void Update()
        {
            _elapsed += Time.deltaTime;

            if (_elapsed >= PixelAnimation.DurationOf(_frames.Length))
            {
                Destroy(gameObject);
                return;
            }

            _renderer.sprite = _frames[PixelAnimation.FrameAt(_elapsed, _frames.Length)];
        }
    }
}
