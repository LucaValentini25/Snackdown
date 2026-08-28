using UnityEngine;

namespace Snackdown.Gameplay
{
    /// <summary>
    /// The rate the sprite pack is drawn at, and the frame that rate lands on.
    /// </summary>
    /// <remarks>
    /// Shared rather than repeated because it is one fact about the art, not a setting per thing
    /// being animated: a fruit spinning at a different rate than a character runs would be a bug
    /// nobody would think to look for in two separate constants that used to agree.
    /// </remarks>
    public static class PixelAnimation
    {
        /// <summary>Frames per second every sheet in the pack is authored at.</summary>
        public const float FramesPerSecond = 20f;

        /// <summary>
        /// The frame a looping clip is showing after <paramref name="elapsed"/> seconds.
        /// </summary>
        /// <param name="frameCount">Frames in the clip. Zero or fewer answers 0.</param>
        /// <remarks>
        /// Floor rather than round, so frame zero is the one shown at time zero. Rounding starts
        /// every clip half a frame in, which on a two-frame clip means starting on the second one.
        /// </remarks>
        public static int FrameAt(float elapsed, int frameCount)
        {
            if (frameCount <= 0) return 0;

            int frame = Mathf.FloorToInt(Mathf.Max(0f, elapsed) * FramesPerSecond);
            return ((frame % frameCount) + frameCount) % frameCount;
        }

        /// <summary>How long a clip of <paramref name="frameCount"/> frames takes to play once.</summary>
        public static float DurationOf(int frameCount) => Mathf.Max(0, frameCount) / FramesPerSecond;
    }
}
