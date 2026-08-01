using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// Every tunable the predicted simulation reads. Lives in an asset so movement can be
    /// re-tuned without recompiling — and so the netcode code stays free of magic numbers.
    /// </summary>
    /// <remarks>
    /// Both the server and every predicting client must read the SAME values, or prediction
    /// diverges on purpose. The asset ships with the build; it is never replicated.
    /// </remarks>
    [CreateAssetMenu(fileName = "MovementConfig", menuName = "Snackdown/Movement Config")]
    public class MovementConfig : ScriptableObject
    {
        [Header("Horizontal")]
        [Tooltip("Top horizontal speed, in units per second.")]
        public float MoveSpeed = 7f;

        [Tooltip("How fast horizontal speed changes while grounded (units/s^2).")]
        public float GroundAcceleration = 90f;

        [Tooltip("Same, in the air. Lower than grounded gives that floaty platformer feel.")]
        public float AirAcceleration = 45f;

        [Header("Vertical")]
        [Tooltip("Downward acceleration, in units per second squared.")]
        public float Gravity = 55f;

        [Tooltip("Terminal fall speed. Also caps how far a single tick can move us.")]
        public float MaxFallSpeed = 25f;

        [Tooltip("Upward speed applied the moment a jump starts.")]
        public float JumpVelocity = 16f;

        [Tooltip("Rising speed is clamped to this once the jump button is released (variable jump height).")]
        public float JumpReleaseSpeed = 5f;

        [Header("Feel")]
        [Tooltip("Grace period after walking off a ledge where a jump still works.")]
        public float CoyoteTime = 0.1f;

        [Tooltip("A jump pressed this long before landing still fires on touchdown.")]
        public float JumpBufferTime = 0.12f;

        [Header("Collision")]
        [Tooltip("Size of the collision box used by the simulation. Must match the prefab's BoxCollider2D.")]
        public Vector2 ColliderSize = new Vector2(0.7f, 0.9f);

        [Tooltip("Gap kept between the box and whatever it hits. Prevents casts starting inside geometry.")]
        public float SkinWidth = 0.02f;

        [Tooltip("What counts as solid ground. Deliberately excludes players — see PlayerMotor.")]
        public LayerMask GroundMask = 1;
    }
}
