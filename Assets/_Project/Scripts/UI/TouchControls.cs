using System.Collections.Generic;
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

        /// <summary>Which key each finger is currently over, by pointer id.</summary>
        readonly Dictionary<int, VisualElement> _fingers = new Dictionary<int, VisualElement>();

        VisualElement _left;
        VisualElement _right;
        VisualElement _jumpKey;
        VisualElement _pauseKey;
        VisualElement _root;
        VisualElement _safe;
        Gamepad _pad;
        bool _jump;
        bool _pause;
        bool _shown;
        bool _leftHeld;
        bool _rightHeld;
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
            _left = _root.Q<VisualElement>("touch-left");
            _right = _root.Q<VisualElement>("touch-right");
            _jumpKey = _root.Q<VisualElement>("touch-jump");
            _pauseKey = _root.Q<VisualElement>("touch-pause");

            // The keys are drawings, not controls. Every pointer is tracked on the root instead,
            // which is what lets a thumb slide from one to the next - see OnPointerDown.
            foreach (VisualElement key in new[] { _left, _right, _jumpKey, _pauseKey })
                if (key != null) key.pickingMode = PickingMode.Ignore;

            _root.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _root.RegisterCallback<PointerUpEvent>(OnPointerLifted);
            _root.RegisterCallback<PointerCancelEvent>(OnPointerLifted);

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
            _fingers.Clear();
            _direction.ReleaseAll();
            _leftHeld = false;
            _rightHeld = false;
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
        /// Tracks one finger from the moment it lands until it is lifted.
        /// </summary>
        /// <remarks>
        /// <para><b>Captured on the root, never on a key.</b> Capturing on the key was the first
        /// attempt and it made the pad feel broken: a captured element keeps every later event for
        /// that finger, so sliding a thumb from left to right delivered nothing to right until the
        /// thumb was lifted. UI Toolkit's own Button does the same thing through its clickable, which
        /// is why the keys have their picking turned off and are only drawings now.</para>
        /// <para>Holding the pointer at the root means every move is reported here and the key under
        /// the finger is worked out each time. Sliding between keys is then just the answer
        /// changing, and each finger is its own entry, so jumping with one thumb while steering with
        /// the other is two independent answers rather than a fight over one capture.</para>
        /// </remarks>
        void OnPointerDown(PointerDownEvent evt)
        {
            _root.CapturePointer(evt.pointerId);
            _fingers[evt.pointerId] = KeyUnder(evt.position);
            Recompute();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_fingers.ContainsKey(evt.pointerId)) return;

            _fingers[evt.pointerId] = KeyUnder(evt.position);
            Recompute();
        }

        void OnPointerLifted(IPointerEvent evt)
        {
            if (_root.HasPointerCapture(evt.pointerId)) _root.ReleasePointer(evt.pointerId);
            _fingers.Remove(evt.pointerId);
            Recompute();
        }

        VisualElement KeyUnder(Vector3 position)
        {
            foreach (VisualElement key in new[] { _left, _right, _jumpKey, _pauseKey })
                if (key != null && key.worldBound.Contains(position)) return key;

            return null;
        }

        /// <remarks>
        /// Rebuilt from every finger rather than toggled per event, so a key stays held while any
        /// finger is on it and a finger that slid away stops holding the one it left. The direction
        /// is fed as presses and releases so that the last one pressed still wins.
        /// </remarks>
        void Recompute()
        {
            bool left = false, right = false;
            _jump = false;
            _pause = false;

            foreach (VisualElement key in _fingers.Values)
            {
                if (key == null) continue;
                if (key == _left) left = true;
                else if (key == _right) right = true;
                else if (key == _jumpKey) _jump = true;
                else if (key == _pauseKey) _pause = true;
            }

            if (left != _leftHeld) { _leftHeld = left; Steer(-1, left); }
            if (right != _rightHeld) { _rightHeld = right; Steer(1, right); }
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
