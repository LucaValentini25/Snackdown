using UnityEngine;
using UnityEngine.InputSystem;

namespace Snackdown.Input
{
    /// <summary>
    /// Turns raw device input into the two values the simulation understands: a quantized
    /// horizontal axis and a jump button.
    /// </summary>
    /// <remarks>
    /// <para><b>Everything the simulation consumes comes from this assembly</b> — that is the rule
    /// worth stating, and it is narrower than "nothing else reads the keyboard". Development
    /// controls elsewhere (the debug overlay's toggles, the life-bar style swap) poll
    /// <c>Keyboard.current</c> directly and are welcome to: they never reach a tick, so they cannot
    /// desynchronise anything, and routing them through a component that lives on a character would
    /// tie a debug key to owning one.</para>
    /// <para><b>Why the latch matters.</b> Rendering runs at 60+ fps while the network tick runs at
    /// 30 Hz, so a button pressed and released between two ticks would simply never be seen if we
    /// polled the device at tick time. <see cref="ConsumeJumpPressed"/> latches the press as it
    /// happens and holds it until the next tick collects it — the difference between a
    /// responsive character and one that eats your inputs.</para>
    /// <para>Actions are built in code rather than loaded from an .inputactions asset: there is no
    /// rebinding yet, and keeping them here means no asset dependency at all. Every action the
    /// simulation reads lives in this assembly, so swapping in a real action asset stays a
    /// one-folder change.</para>
    /// </remarks>
    public class InputReader : MonoBehaviour
    {
        [Tooltip("How far the stick must travel before it reads as a full direction.")]
        [SerializeField] float _axisDeadzone = 0.5f;

        InputAction _move;
        InputAction _jump;
        bool _jumpPressedLatch;

        void Awake()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            _move.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick/x");
            _move.AddCompositeBinding("1DAxis")
                .With("Negative", "<Gamepad>/dpad/left")
                .With("Positive", "<Gamepad>/dpad/right");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Keyboard>/w");
            _jump.AddBinding("<Keyboard>/upArrow");
            _jump.AddBinding("<Gamepad>/buttonSouth");
        }

        void OnEnable()
        {
            _move.Enable();
            _jump.Enable();
        }

        void OnDisable()
        {
            _move.Disable();
            _jump.Disable();
        }

        void OnDestroy()
        {
            _move?.Dispose();
            _jump?.Dispose();
        }

        void Update()
        {
            // Catch presses that happen between two network ticks.
            if (_jump.WasPressedThisFrame()) _jumpPressedLatch = true;
        }

        /// <summary>
        /// Horizontal intent as -1, 0 or 1. Quantized here, at the source, so the client and the
        /// server can never disagree about what "half right" meant.
        /// </summary>
        public sbyte MoveX
        {
            get
            {
                float raw = _move.ReadValue<float>();
                if (raw >= _axisDeadzone) return 1;
                if (raw <= -_axisDeadzone) return -1;
                return 0;
            }
        }

        public bool JumpHeld => _jump.IsPressed();

        /// <summary>
        /// True if a jump was pressed since the last call, and clears the latch.
        /// Call exactly once per network tick.
        /// </summary>
        public bool ConsumeJumpPressed()
        {
            bool pressed = _jumpPressedLatch;
            _jumpPressedLatch = false;
            return pressed;
        }
    }
}
