using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// A bounded number the player moves with a direction — arrow keys, a d-pad or a stick — rather
    /// than by typing it.
    /// </summary>
    /// <remarks>
    /// <para>This exists because the match rules were <see cref="FloatField"/>s, and a text field is
    /// only reachable by something that can carry a caret. On a gamepad the lobby's numbers could be
    /// read and never changed: a host playing on a pad could not set up the match they were hosting.
    /// A stepper has no text entry to reach — it is one focusable control that answers left and
    /// right — which is the whole of the fix.</para>
    /// <para>It reports through <c>ChangeEvent&lt;float&gt;</c> and implements
    /// <see cref="INotifyValueChanged{T}"/>, so <c>RegisterValueChangedCallback</c> keeps working and
    /// a caller trading a field for a stepper changes the declared type and nothing else.</para>
    /// <para>The value-facing properties are lower-case. <c>value</c> is fixed by
    /// <see cref="INotifyValueChanged{T}"/>, and <c>lowValue</c>, <c>highValue</c>, <c>step</c> and
    /// <c>formatString</c> follow <see cref="Slider"/> and <see cref="FloatField"/> so the element
    /// reads like a built-in wherever it is written. The rest of the file is the repository's
    /// PascalCase.</para>
    /// </remarks>
    [UxmlElement]
    public partial class NumberStepper : VisualElement, INotifyValueChanged<float>
    {
        private const string UssClassName = "number-stepper";
        private const string ArrowUssClassName = UssClassName + "__arrow";
        private const string DecreaseUssClassName = ArrowUssClassName + "--decrease";
        private const string IncreaseUssClassName = ArrowUssClassName + "--increase";
        private const string ReadoutUssClassName = UssClassName + "__readout";

        private const string DefaultFormatString = "0.##";

        /// <summary>
        /// How long a direction must be held before it starts repeating, in milliseconds.
        /// </summary>
        /// <remarks>
        /// Long enough that a deliberate single step is never read as a hold, short enough that
        /// crossing a wide range does not become a drumming exercise.
        /// </remarks>
        private const long HoldDelayMilliseconds = 350;

        /// <summary>Milliseconds between steps once a hold has started repeating.</summary>
        private const long RepeatIntervalMilliseconds = 60;

        /// <summary>Smallest step that still moves the number, so a step of zero cannot be authored.</summary>
        private const float MinimumStep = 0.0001f;

        private readonly Label _decrease;
        private readonly Label _increase;
        private readonly Label _readout;

        private float _value;
        private float _lowValue;
        private float _highValue = 100f;
        private float _step = 1f;
        private string _formatString = DefaultFormatString;

        /// <summary>-1 while a direction is physically held down, +1, or 0 when nothing is held.</summary>
        private int _heldDirection;

        private IVisualElementScheduledItem _repeat;

        private int _lastStepFrame = -1;
        private int _lastStepDirection;

        public NumberStepper()
        {
            AddToClassList(UssClassName);

            // A tab stop and a navigation stop. A number the player cannot land on is the bug this
            // control was written to remove.
            focusable = true;
            tabIndex = 0;

            _decrease = BuildArrow("<", DecreaseUssClassName);
            _increase = BuildArrow(">", IncreaseUssClassName);

            _readout = new Label();
            _readout.AddToClassList(ReadoutUssClassName);
            _readout.pickingMode = PickingMode.Ignore;

            Add(_decrease);
            Add(_readout);
            Add(_increase);

            _decrease.RegisterCallback<PointerDownEvent>(OnDecreasePressed);
            _increase.RegisterCallback<PointerDownEvent>(OnIncreasePressed);

            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            RegisterCallback<FocusOutEvent>(OnFocusOut);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            RedrawReadout();
        }

        /// <summary>The number on show, held inside <c>lowValue</c>..<c>highValue</c>.</summary>
        /// <remarks>
        /// Assigning raises <c>ChangeEvent&lt;float&gt;</c>, and only when the clamped result differs
        /// from what was already there. Writing back a number the control just reported — which is
        /// what a replicated setting does on every peer — therefore cannot start a loop, and a step
        /// that runs into a bound is silent rather than a change of nothing into itself.
        /// </remarks>
        [UxmlAttribute("value")]
        public float value
        {
            get => _value;
            set
            {
                float clamped = Clamp(value);
                if (clamped == _value) return;

                float previous = _value;
                _value = clamped;
                RedrawReadout();

                using (ChangeEvent<float> changed = ChangeEvent<float>.GetPooled(previous, clamped))
                {
                    changed.target = this;
                    SendEvent(changed);
                }
            }
        }

        /// <summary>Floor the value is held at.</summary>
        /// <remarks>
        /// Moving a bound re-clamps the current value without notifying. Bounds are configuration,
        /// not something the player did, and a caller that tightens them is about to write the value
        /// it wants anyway — announcing the clamp in between would report a number nobody chose.
        /// </remarks>
        [UxmlAttribute("low-value")]
        public float lowValue
        {
            get => _lowValue;
            set
            {
                _lowValue = value;
                SetValueWithoutNotify(_value);
            }
        }

        /// <summary>Ceiling the value is held at.</summary>
        /// <remarks>Re-clamps without notifying, for the reason given on <c>lowValue</c>.</remarks>
        [UxmlAttribute("high-value")]
        public float highValue
        {
            get => _highValue;
            set
            {
                _highValue = value;
                SetValueWithoutNotify(_value);
            }
        }

        /// <summary>How far one press moves the value.</summary>
        /// <remarks>
        /// Stored as a magnitude with a floor under it. A step of zero is a control that responds to
        /// every press by doing nothing, which reads as a broken build rather than as a bad number
        /// in a UXML file.
        /// </remarks>
        [UxmlAttribute("step")]
        public float step
        {
            get => _step;
            set => _step = Mathf.Max(MinimumStep, Mathf.Abs(value));
        }

        /// <summary>Numeric format for the readout, rendered with the invariant culture.</summary>
        /// <remarks>
        /// Invariant rather than the machine's own: these are rules two peers have to agree on, and a
        /// host reading "1,5" while a client reads "1.5" is a disagreement about the match that is
        /// really a disagreement about a locale.
        /// </remarks>
        [UxmlAttribute("format-string")]
        public string formatString
        {
            get => _formatString;
            set
            {
                _formatString = string.IsNullOrEmpty(value) ? DefaultFormatString : value;
                RedrawReadout();
            }
        }

        /// <summary>Assigns the value without raising <c>ChangeEvent&lt;float&gt;</c>.</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = Clamp(newValue);
            RedrawReadout();
        }

        // ==================================================================================
        //  Input
        // ==================================================================================

        private void OnKeyDown(KeyDownEvent evt)
        {
            int direction = DirectionOf(evt.keyCode);
            if (direction == 0) return;

            evt.StopPropagation();
            BeginHold(direction);
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (DirectionOf(evt.keyCode) == 0) return;

            evt.StopPropagation();
            EndHold();
        }

        /// <summary>
        /// The gamepad path: a d-pad or a stick reaches a focused element as a navigation move.
        /// </summary>
        /// <remarks>
        /// <para>Horizontal moves are consumed whether or not the number actually moved. An
        /// unhandled one walks the focus to the next control, which is the exact failure this
        /// element exists to prevent — a player nudging a rule would find themselves somewhere else
        /// in the panel. Vertical moves are left alone so the settings list is still walkable.</para>
        /// <para>A key that is physically down already owns the repeat rate through the schedule
        /// below, and the event system sends its own navigation repeats for that same press: an
        /// arrow key arrives here as well as at <see cref="OnKeyDown"/>. Deferring to the hold is
        /// what keeps one tap from counting twice and one hold from stepping at two rates at
        /// once.</para>
        /// </remarks>
        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int direction = DirectionOf(evt.direction);
            if (direction == 0) return;

            evt.StopPropagation();

            if (_heldDirection != 0) return;

            Step(direction);
        }

        private void OnDecreasePressed(PointerDownEvent evt) => BeginPointerHold(evt, -1);

        private void OnIncreasePressed(PointerDownEvent evt) => BeginPointerHold(evt, 1);

        /// <remarks>
        /// The pointer is captured by the stepper rather than by the arrow, so the release still
        /// arrives when the cursor slides off mid-press. Without it the number would go on climbing
        /// after the button had been let go.
        /// </remarks>
        private void BeginPointerHold(PointerDownEvent evt, int direction)
        {
            if (!enabledInHierarchy) return;

            evt.StopPropagation();

            this.CapturePointer(evt.pointerId);
            Focus();

            BeginHold(direction);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);

            EndHold();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt) => EndHold();

        private void OnFocusOut(FocusOutEvent evt) => EndHold();

        private void OnDetachFromPanel(DetachFromPanelEvent evt) => EndHold();

        // ==================================================================================
        //  Holding
        // ==================================================================================

        /// <remarks>
        /// Idempotent while the same direction is held, so the operating system's own key repeat —
        /// which arrives as a stream of <see cref="KeyDownEvent"/> at whatever rate the machine is
        /// configured for — cannot restart the schedule or add a step of its own.
        /// </remarks>
        private void BeginHold(int direction)
        {
            if (_heldDirection == direction) return;

            _heldDirection = direction;

            Step(direction);
            StartRepeating();
        }

        private void EndHold()
        {
            _heldDirection = 0;
            StopRepeating();
        }

        /// <remarks>
        /// A scheduled item rather than a per-frame check. Nothing here has anything to do until the
        /// hold delay has passed, and a control that ran code every frame to discover that would be
        /// paying for a number nobody is touching — there is one of these per rule in the panel.
        /// </remarks>
        private void StartRepeating()
        {
            StopRepeating();

            _repeat = schedule.Execute(RepeatStep)
                .StartingIn(HoldDelayMilliseconds)
                .Every(RepeatIntervalMilliseconds);
        }

        private void StopRepeating()
        {
            _repeat?.Pause();
            _repeat = null;
        }

        /// <remarks>
        /// Ends the hold if the control was disabled underneath it. The rules panel is greyed out the
        /// moment a match starts, and a hold that survived that would keep changing a number the
        /// server has already stopped accepting.
        /// </remarks>
        private void RepeatStep()
        {
            if (_heldDirection == 0 || !enabledInHierarchy)
            {
                EndHold();
                return;
            }

            Step(_heldDirection);
        }

        // ==================================================================================

        /// <remarks>
        /// At most one step per direction per frame. An arrow key reaches a focused element twice in
        /// the same frame — once as a <see cref="KeyDownEvent"/>, once as the
        /// <see cref="NavigationMoveEvent"/> the event system synthesises from the same press — and
        /// the two arrive in no guaranteed order. Whichever lands first moves the number and the
        /// other is dropped here, so a tap is one step whether it came from a key, a d-pad or a
        /// stick.
        /// </remarks>
        private void Step(int direction)
        {
            if (_lastStepFrame == Time.frameCount && _lastStepDirection == direction) return;

            _lastStepFrame = Time.frameCount;
            _lastStepDirection = direction;

            value = _value + direction * _step;
        }

        /// <remarks>
        /// Sorts the two bounds before clamping between them. <see cref="Mathf.Clamp"/> given a floor
        /// above its ceiling returns whichever bound it happens to test first, so a UXML file with
        /// the pair the wrong way round would produce a number that jumps rather than an obviously
        /// wrong range.
        /// </remarks>
        private float Clamp(float raw)
            => Mathf.Clamp(raw, Mathf.Min(_lowValue, _highValue), Mathf.Max(_lowValue, _highValue));

        private void RedrawReadout()
            => _readout.text = _value.ToString(_formatString, CultureInfo.InvariantCulture);

        private Label BuildArrow(string glyph, string modifierUssClassName)
        {
            // Not a Button: its Clickable manipulator fires a step of its own on release, on top of
            // the one the press already started, so a single click would move the number twice. Not
            // focusable either — the stepper is one navigation stop, not three.
            var arrow = new Label(glyph);
            arrow.AddToClassList(ArrowUssClassName);
            arrow.AddToClassList(modifierUssClassName);
            arrow.focusable = false;

            return arrow;
        }

        private static int DirectionOf(KeyCode key)
        {
            if (key == KeyCode.LeftArrow) return -1;
            if (key == KeyCode.RightArrow) return 1;

            return 0;
        }

        private static int DirectionOf(NavigationMoveEvent.Direction direction)
        {
            if (direction == NavigationMoveEvent.Direction.Left) return -1;
            if (direction == NavigationMoveEvent.Direction.Right) return 1;

            return 0;
        }
    }
}
