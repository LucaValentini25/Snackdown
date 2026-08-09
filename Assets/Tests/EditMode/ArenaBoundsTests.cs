using NUnit.Framework;
using Snackdown.Gameplay.Match;
using UnityEngine;

namespace Snackdown.Tests
{
    /// <summary>
    /// Unit tests for the rectangle that decides where a spectator camera may look.
    /// </summary>
    /// <remarks>
    /// Worth testing on its own because the interesting case is the one the shipped arena cannot
    /// show: Arena01 fits inside a single camera view, so the camera is locked there by design and
    /// panning is only exercised on a map bigger than the screen. These tests cover that map before
    /// it exists.
    /// </remarks>
    public class ArenaBoundsTests
    {
        ArenaBounds _bounds;
        GameObject _host;

        void Build(Vector2 center, Vector2 size)
        {
            _host = new GameObject("bounds");
            _bounds = _host.AddComponent<ArenaBounds>();

            var so = new UnityEditor.SerializedObject(_bounds);
            so.FindProperty("_center").vector2Value = center;
            so.FindProperty("_size").vector2Value = size;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        /// <summary>A 16:9 camera with an orthographic size of 7 — the project's own setup.</summary>
        static Vector2 View => new Vector2(7f * (16f / 9f), 7f);

        [Test]
        public void OnAMapBiggerThanTheScreen_TheCameraCanPan()
        {
            Build(Vector2.zero, new Vector2(80f, 40f));

            Vector2 clamped = _bounds.Clamp(new Vector2(10f, 5f), View);

            Assert.AreEqual(10f, clamped.x, 0.0001f);
            Assert.AreEqual(5f, clamped.y, 0.0001f);
        }

        [Test]
        public void PanningPastTheEdge_StopsAtTheEdge()
        {
            Build(Vector2.zero, new Vector2(80f, 40f));

            Vector2 clamped = _bounds.Clamp(new Vector2(999f, 999f), View);

            Assert.AreEqual(40f - View.x, clamped.x, 0.0001f, "the view's right edge should rest on the bound");
            Assert.AreEqual(20f - View.y, clamped.y, 0.0001f);
        }

        [Test]
        public void OnArena01_TheCameraBarelyMoves()
        {
            // Arena01's actual numbers: 26 x 9, sitting half a unit above the origin. It is 9 units
            // shorter than the view and only ~1 unit wider, so a spectator there gets half a unit
            // of horizontal slack and nothing vertically. That is the map fitting the screen, and
            // it is why panning cannot be judged in this arena.
            Build(new Vector2(0f, 0.5f), new Vector2(26f, 9f));

            Vector2 clamped = _bounds.Clamp(new Vector2(50f, -50f), View);

            Assert.AreEqual(13f - View.x, clamped.x, 0.0001f, "half a unit of slack, no more");
            Assert.Less(clamped.x, 0.6f);
            Assert.AreEqual(0.5f, clamped.y, 0.0001f, "shorter than the view, so pinned vertically");
        }

        [Test]
        public void AMapWiderThanTallLocksOneAxisAndNotTheOther()
        {
            // 80 wide against a 24.9-wide view: free. 10 tall against a 14-tall view: locked.
            Build(Vector2.zero, new Vector2(80f, 10f));

            Vector2 clamped = _bounds.Clamp(new Vector2(12f, 99f), View);

            Assert.AreEqual(12f, clamped.x, 0.0001f, "horizontal panning survives");
            Assert.AreEqual(0f, clamped.y, 0.0001f, "vertical is pinned to the centre");
        }

        [Test]
        public void BoundsExactlyTheSizeOfTheView_Lock()
        {
            // The boundary case between the two behaviours. Clamping here must not invert into a
            // range whose minimum exceeds its maximum.
            Build(Vector2.zero, View * 2f);

            Vector2 clamped = _bounds.Clamp(new Vector2(5f, -5f), View);

            Assert.AreEqual(0f, clamped.x, 0.0001f);
            Assert.AreEqual(0f, clamped.y, 0.0001f);
        }

        [Test]
        public void AnOffCentreMapClampsAroundItsOwnCentre()
        {
            Build(new Vector2(100f, -40f), new Vector2(80f, 40f));

            Vector2 clamped = _bounds.Clamp(new Vector2(0f, 0f), View);

            Assert.AreEqual(100f - 40f + View.x, clamped.x, 0.0001f);
            Assert.AreEqual(-40f + 20f - View.y, clamped.y, 0.0001f);
        }
    }
}
