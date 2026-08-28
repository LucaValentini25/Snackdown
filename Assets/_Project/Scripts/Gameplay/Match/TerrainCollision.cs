using UnityEngine;
using UnityEngine.Tilemaps;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Makes the terrain's collision match its tiles, and refuses to be quiet when it cannot.
    /// </summary>
    /// <remarks>
    /// <para><b>A composite collider bakes its geometry into the scene file, and loading a scene is
    /// not a change.</b> Edit the tilemap, save, and the tiles are stored correctly while the
    /// outline stored beside them is whatever was last generated - which in the editor, where no
    /// physics step runs on its own, can be several layouts old. The result is a level that looks
    /// right and collides like a different one: ledges that are not there, platforms you fall
    /// through. It cost an afternoon to find once, because every check that reads the tiles agrees
    /// with itself.</para>
    /// <para>Regenerating on enable makes the stored geometry irrelevant. It costs one rebuild per
    /// arena load, which is the same work the editor does when you nudge a tile.</para>
    /// <para>The error is the other half. An arena whose collision failed to build is one every
    /// player falls out of, and that has to arrive as a message rather than as a bug report.</para>
    /// </remarks>
    [RequireComponent(typeof(CompositeCollider2D))]
    [RequireComponent(typeof(TilemapCollider2D))]
    public class TerrainCollision : MonoBehaviour
    {
        void OnEnable()
        {
            var tilemapCollider = GetComponent<TilemapCollider2D>();
            var composite = GetComponent<CompositeCollider2D>();

            // The tilemap collider hands its shapes to the composite, so it has to have looked at
            // the tiles before the composite is asked to merge them.
            tilemapCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();

            if (composite.pathCount == 0)
                Debug.LogError($"[Snackdown] {name} built no collision at all. Every player will "
                               + "fall through this arena.", this);
        }
    }
}
