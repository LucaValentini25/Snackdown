using UnityEngine;
using UnityEngine.InputSystem;

namespace Snackdown.Input
{
    /// <summary>
    /// The free-look axis a dead player uses to pan the camera around the arena.
    /// </summary>
    /// <remarks>
    /// <para>Separate from <see cref="InputReader"/> rather than an extra axis on it. That one is a
    /// component on a character, disabled on every peer that does not own it, and its output is
    /// quantized to -1/0/1 because it crosses the wire into a shared simulation. None of that
    /// applies here: a camera is local, belongs to no character, and wants smooth analog motion.
    /// Bolting the two together would mean a spectator camera that only works while you still have
    /// a body to hang it on.</para>
    /// <para>It lives in this assembly anyway, so the Input System stays confined to one place and
    /// swapping the code-built actions for an <c>.inputactions</c> asset stays a single-folder
    /// change.</para>
    /// </remarks>
    public class SpectatorInput : MonoBehaviour
    {
        InputAction _pan;

        /// <summary>Pan intent, analog, in the -1..1 square.</summary>
        public Vector2 Pan => _pan != null && _pan.enabled ? _pan.ReadValue<Vector2>() : Vector2.zero;

        void Awake()
        {
            _pan = new InputAction("SpectatorPan", InputActionType.Value);

            _pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _pan.AddBinding("<Gamepad>/leftStick");
        }

        void OnEnable() => _pan.Enable();

        void OnDisable() => _pan.Disable();

        void OnDestroy() => _pan?.Dispose();
    }
}
