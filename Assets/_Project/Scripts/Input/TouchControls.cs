using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Snackdown.Input
{
    /// <summary>
    /// On-screen controls for a phone, published as an ordinary gamepad.
    /// </summary>
    /// <remarks>
    /// <para><b>It creates a real <c>Gamepad</c> device and writes state into it.</b> Nothing
    /// downstream is told that a thumb was involved: <see cref="InputReader"/>'s existing
    /// <c>&lt;Gamepad&gt;/leftStick</c> and <c>&lt;Gamepad&gt;/buttonSouth</c> bindings pick it up
    /// unchanged, and so does the escape menu's <c>&lt;Gamepad&gt;/start</c> — which matters,
    /// because a phone has no Escape key and without that binding there would be no way to leave a
    /// match. The alternative, a second input path feeding the reader directly, would have been a
    /// second source of truth to keep in step with the first, and the pause button would have
    /// needed wiring of its own.</para>
    /// <para><b>Phones only, and enforced by the compiler.</b> Everything that builds a device or
    /// reads a screen is inside the platform gate, so a Windows or WebGL build does not contain it;
    /// what remains there is a component that destroys its own object on the first frame. The
    /// editor gets a switch instead of the gate, because the build target on Luca's machine is
    /// Windows and a panel that could only be seen by switching platforms is a panel nobody
    /// checks.</para>
    /// </remarks>
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

#if UNITY_EDITOR
        [Tooltip("Show the pad in the editor, for the Device Simulator. Never affects a build.")]
        [SerializeField] bool _showInEditor;
#endif

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
        readonly TouchDirection _direction = new TouchDirection();
        Gamepad _pad;
        bool _jump;
        bool _pause;

        void Awake()
        {
            if (!WantedHere())
            {
                Destroy(gameObject);
                return;
            }

            _pad = InputSystem.AddDevice<Gamepad>("SnackdownTouchPad");
        }

        void OnEnable()
        {
            if (_pad == null) return;

            VisualElement root = _document.rootVisualElement;
            Hold(root.Q<Button>("touch-left"), pressed => Steer(-1, pressed));
            Hold(root.Q<Button>("touch-right"), pressed => Steer(1, pressed));
            Hold(root.Q<Button>("touch-jump"), pressed => _jump = pressed);
            Hold(root.Q<Button>("touch-pause"), pressed => _pause = pressed);
        }

        void OnDisable()
        {
            // A panel hidden with a thumb still down would leave the character walking.
            _direction.ReleaseAll();
            _jump = false;
            _pause = false;
        }

        void OnDestroy()
        {
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
        }

        void Steer(sbyte direction, bool pressed)
        {
            if (pressed) _direction.Press(direction);
            else _direction.Release(direction);
        }

        /// <remarks>
        /// The whole state every frame rather than an event per touch. A queued event that went
        /// missing would leave a button stuck down for the rest of the round, and re-sending what is
        /// true costs one small struct.
        /// </remarks>
        void Update()
        {
            if (_pad == null) return;

            var state = new GamepadState { leftStick = new Vector2(_direction.Value, 0f) };
            if (_jump) state = state.WithButton(GamepadButton.South);
            if (_pause) state = state.WithButton(GamepadButton.Start);

            InputSystem.QueueStateEvent(_pad, state);
        }

        /// <summary>
        /// Reports a button as held rather than clicked, and keeps reporting through a thumb that
        /// slides off it.
        /// </summary>
        /// <remarks>
        /// Capturing the pointer is what makes the difference. Without it, dragging a thumb past the
        /// edge of a button never sends the release, and the character keeps running.
        /// </remarks>
        static void Hold(Button button, System.Action<bool> report)
        {
            if (button == null) return;

            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                button.CapturePointer(evt.pointerId);
                report(true);
            });

            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (button.HasPointerCapture(evt.pointerId)) button.ReleasePointer(evt.pointerId);
                report(false);
            });

            button.RegisterCallback<PointerCaptureOutEvent>(_ => report(false));
        }

        bool WantedHere()
        {
#if UNITY_ANDROID || UNITY_IOS
            return true;
#else
            return _showInEditor;
#endif
        }
#else
        /// <remarks>
        /// What is left of this component on a platform without a touchscreen: it removes itself
        /// before anything is drawn, so the panel cannot appear even though the document is still
        /// sitting in the scene.
        /// </remarks>
        void Awake() => Destroy(gameObject);
#endif
    }
}
