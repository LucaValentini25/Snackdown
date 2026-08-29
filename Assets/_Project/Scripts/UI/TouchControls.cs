using Snackdown.Gameplay.Match;
using Snackdown.Input;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Snackdown.UI
{
    /// <summary>
    /// On-screen controls for a phone, published as an ordinary gamepad.
    /// </summary>
    /// <remarks>
    /// <para><b>It creates a real <c>Gamepad</c> device and writes state into it.</b> Nothing
    /// downstream is told a thumb was involved: <see cref="InputReader"/>'s existing
    /// <c>&lt;Gamepad&gt;/leftStick</c> and <c>&lt;Gamepad&gt;/buttonSouth</c> bindings pick it up
    /// unchanged, and so does the escape menu's <c>&lt;Gamepad&gt;/start</c> — which matters, because
    /// a phone has no Escape key and without that binding there would be no way out of a match.</para>
    /// <para><b>Phones only, enforced by the compiler.</b> Everything that builds a device or reads a
    /// screen is inside the platform gate, so a Windows or WebGL build does not contain it. The
    /// editor gets a switch instead, because the build target on this machine is Windows and a panel
    /// that can only be seen by switching platform is a panel nobody checks.</para>
    /// <para>It lives here rather than beside <see cref="TouchDirection"/> because it has to know
    /// whether a match is being played, and <c>Snackdown.Input</c> is a leaf that depends on the
    /// Input System and nothing else. Reaching from there into the match would invert the layering
    /// ADR 0001 was written about; reaching the other way, from the UI assembly that already knows
    /// both, does not.</para>
    /// </remarks>
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] UIDocument _document;

#if UNITY_EDITOR
        [Tooltip("Show the pad in the editor, for the Device Simulator. Never affects a build.")]
        [SerializeField] bool _showInEditor;
#endif

#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
        /// <summary>The one pad allowed to exist, so a second scene cannot register a second device.</summary>
        static TouchControls _live;

        readonly TouchDirection _direction = new TouchDirection();
        VisualElement _root;
        VisualElement _safe;
        Gamepad _pad;
        bool _jump;
        bool _pause;
        bool _shown;
        Rect _safeArea;

        /// <remarks>
        /// The guard is not defensive tidiness. This registers a device with the Input System, which
        /// is global, so a second copy of this component is a second gamepad the player never
        /// plugged in - and the sandbox scene mirrors the bootstrap overlays, so loading both at once
        /// is a supported thing to do rather than a mistake.
        /// </remarks>
        void Awake()
        {
            if (!WantedHere() || _live != null)
            {
                Destroy(gameObject);
                return;
            }

            _live = this;
            _pad = InputSystem.AddDevice<Gamepad>("SnackdownTouchPad");
        }

        void OnEnable()
        {
            if (_pad == null) return;

            _root = _document.rootVisualElement.Q<VisualElement>("touch-root");
            _safe = _root.Q<VisualElement>("touch-safe");
            Hold(_root.Q<Button>("touch-left"), pressed => Steer(-1, pressed));
            Hold(_root.Q<Button>("touch-right"), pressed => Steer(1, pressed));
            Hold(_root.Q<Button>("touch-jump"), pressed => _jump = pressed);
            Hold(_root.Q<Button>("touch-pause"), pressed => _pause = pressed);

            _safeArea = new Rect();
            Show(false);
        }

        void OnDisable() => LetGo();

        void OnDestroy()
        {
            if (ReferenceEquals(_live, this)) _live = null;
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
        }

        /// <remarks>
        /// Only while a round is actually being played. The countdown ignores input and the end
        /// screen is read rather than played, so a pad on either is a pad offering to do nothing.
        /// </remarks>
        static bool DuringGameplay()
        {
            MatchDirector director = MatchDirector.Current;
            return director != null && director.Phase == MatchPhase.Playing;
        }

        void Update()
        {
            if (_pad == null) return;

            bool wanted = DuringGameplay();
            if (wanted != _shown) Show(wanted);
            if (!_shown) return;

            ApplySafeArea();

            var state = new GamepadState { leftStick = new Vector2(_direction.Value, 0f) };
            if (_jump) state = state.WithButton(GamepadButton.South);
            if (_pause) state = state.WithButton(GamepadButton.Start);

            // The whole state every frame rather than an event per touch: a queued event that went
            // missing would leave a button stuck down for the rest of the round.
            InputSystem.QueueStateEvent(_pad, state);
        }

        void Show(bool shown)
        {
            _shown = shown;
            _root.EnableInClassList("hidden", !shown);
            if (!shown) LetGo();
        }

        /// <summary>Drops every button. For a panel that goes away with a thumb still on it.</summary>
        void LetGo()
        {
            _direction.ReleaseAll();
            _jump = false;
            _pause = false;
        }

        /// <summary>
        /// Keeps the pad out of the notch, the rounded corners and the home indicator.
        /// </summary>
        /// <remarks>
        /// <para>Applied as the offsets of an inner container rather than as padding on the root.
        /// Padding was the obvious way and it moved nothing: an absolutely positioned child is laid
        /// out against the border edge, so the four corner buttons ignored it entirely. Measuring
        /// where the buttons actually landed is what caught it. Insetting a container they sit
        /// inside moves all four at once, and none of them needs to know which corner it is in.</para>
        /// <para><c>Screen.safeArea</c> is in real pixels and the panel is scaled from a 960x540
        /// reference, so each edge is converted through the ratio of the two rather than used as-is;
        /// pasting pixel insets into a scaled panel is how a pad ends up inset by three times the
        /// notch. Recomputed only when the rectangle changes, which is on rotation and almost
        /// never.</para>
        /// </remarks>
        void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe == _safeArea) return;

            float width = _root.resolvedStyle.width;
            float height = _root.resolvedStyle.height;
            if (width <= 0f || height <= 0f || Screen.width == 0 || Screen.height == 0) return;

            _safeArea = safe;
            _safe.style.left = safe.xMin / Screen.width * width;
            _safe.style.right = (Screen.width - safe.xMax) / Screen.width * width;
            _safe.style.bottom = safe.yMin / Screen.height * height;
            _safe.style.top = (Screen.height - safe.yMax) / Screen.height * height;
        }

        void Steer(sbyte direction, bool pressed)
        {
            if (pressed) _direction.Press(direction);
            else _direction.Release(direction);
        }

        /// <summary>
        /// Reports a button as held rather than clicked, and keeps reporting through a thumb that
        /// slides off it.
        /// </summary>
        /// <remarks>
        /// Capturing the pointer is the part that matters. Without it, dragging a thumb past the
        /// edge of a button never sends the release and the character keeps running.
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
        /// What is left of this on a platform with no touchscreen: it removes its own object before
        /// anything is drawn, so the panel cannot appear even though the document is in the scene.
        /// </remarks>
        void Awake() => Destroy(gameObject);
#endif
    }
}
