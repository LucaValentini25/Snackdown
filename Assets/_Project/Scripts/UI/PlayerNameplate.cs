using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Option A — the nickname and life bar floating above a character, drawn in world space.
    /// </summary>
    /// <remarks>
    /// <para>A world-space UI Toolkit panel rather than a screen-space element reprojected onto the
    /// character's head. Reprojection means every bar re-reads a transform and recomputes a screen
    /// position every frame, and gets it subtly wrong whenever the camera moves between the read
    /// and the draw. Attaching the panel to the character makes the bar follow because it is
    /// genuinely there.</para>
    /// <para>It hangs off the same child the sprite does, so it inherits the correction smoothing
    /// rather than jittering independently of the character it labels — and disappears with them
    /// when they go out, which is the behaviour wanted anyway.</para>
    /// <para>Reads life; replicates nothing. The number already crossed the wire because the server
    /// owns it.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class PlayerNameplate : MonoBehaviour
    {
        [Tooltip("How wide the plate is, in world units. The character sprite is 1 unit wide.")]
        [SerializeField] float _widthInWorldUnits = 1.4f;

        [Tooltip("How tall the plate is, in world units — the name and the bar together.")]
        [SerializeField] float _heightInWorldUnits = 0.56f;

        [Tooltip("How far above the character's centre it floats, in world units.")]
        [SerializeField] float _offsetAboveCharacter = 0.85f;

        [Tooltip("Life this player has, in seconds, at which the bar turns red.")]
        [SerializeField] float _lowLifeSeconds = 10f;

        UIDocument _document;
        VisualElement _root;
        Label _name;
        VisualElement _fill;

        PlayerLife _life;

        void Awake()
        {
            _document = GetComponent<UIDocument>();
            _life = GetComponentInParent<PlayerLife>();

            ApplyWorldSize();
        }

        /// <summary>
        /// Sizes the panel from a width in world units rather than a scale factor.
        /// </summary>
        /// <remarks>
        /// <para><b>The transform is left at scale 1 and the panel is authored at its real size.</b>
        /// A world-space panel already converts its pixels to metres through
        /// <c>PanelSettings.pixelsPerUnit</c>, so scaling the transform on top applies a second
        /// conversion to something Unity has already converted. Doing both is what produced a plate
        /// 0.014 units wide — a bar a couple of pixels tall — from numbers that read like they asked
        /// for 1.4.</para>
        /// <para>So the size in metres drives the document's size in pixels, and nothing is scaled.
        /// One consequence worth knowing when editing the USS: at 100 pixels per unit, every 100
        /// pixels there is one world unit, which is what makes a 34px name 0.34 units tall against a
        /// character of 1.</para>
        /// <para>The parent's scale is still divided out of the offset. This hangs off the
        /// character's visual child so it inherits the correction smoothing, and if that child is
        /// ever scaled again the offset would silently move with it.</para>
        /// </remarks>
        void ApplyWorldSize()
        {
            if (_document == null || _widthInWorldUnits <= 0f || _heightInWorldUnits <= 0f) return;

            _document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            _document.worldSpaceSize = new Vector2(
                _widthInWorldUnits * PanelPixelsPerUnit,
                _heightInWorldUnits * PanelPixelsPerUnit);

            Transform parent = transform.parent;
            float parentScale = parent != null ? parent.lossyScale.x : 1f;
            if (Mathf.Approximately(parentScale, 0f)) return;

            transform.localScale = Vector3.one / parentScale;
            transform.localPosition = new Vector3(0f, _offsetAboveCharacter / parentScale, 0f);
        }

        /// <summary>
        /// Pixels the panel puts in one world unit. Must match <c>WorldSpacePanelSettings</c>.
        /// </summary>
        /// <remarks>
        /// A constant because <c>PanelSettings.pixelsPerUnit</c> is internal to UI Toolkit and not
        /// readable from game code. <see cref="OnValidate"/> compares it against the asset in the
        /// editor, so the two cannot drift apart quietly — which is the only real danger in copying
        /// a number that lives somewhere else.
        /// </remarks>
        const float PanelPixelsPerUnit = 100f;

        /// <remarks>Lets the size be dragged in the Inspector and seen at once, rather than tuned by restart.</remarks>
        void OnValidate()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            ApplyWorldSize();

#if UNITY_EDITOR
            if (_document == null || _document.panelSettings == null) return;

            var settings = new UnityEditor.SerializedObject(_document.panelSettings);
            UnityEditor.SerializedProperty ppu = settings.FindProperty("m_PixelsPerUnit");

            if (ppu != null && !Mathf.Approximately(ppu.floatValue, PanelPixelsPerUnit))
            {
                Debug.LogWarning(
                    $"[Snackdown] {_document.panelSettings.name} is set to {ppu.floatValue} pixels per unit, "
                    + $"but {nameof(PlayerNameplate)} assumes {PanelPixelsPerUnit}. The nameplate will be "
                    + $"{ppu.floatValue / PanelPixelsPerUnit:0.##}x the size asked for.", this);
            }
#endif
        }

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("nameplate-root");
            _name = root.Q<Label>("nameplate-name");
            _fill = root.Q<VisualElement>("nameplate-fill");
        }

        void LateUpdate()
        {
            if (_root == null || _life == null) return;

            MatchDirector director = MatchDirector.Current;

            bool visible = LifeBarStyle.Placement == LifeBarPlacement.OverTheCharacter
                           && _life.IsAlive
                           && director != null
                           && (director.Phase == MatchPhase.Countdown
                               || director.Phase == MatchPhase.Playing);

            _root.EnableInClassList("hidden", !visible);
            if (!visible) return;

            _name.text = LifeText.NameOf(_life.OwnerClientId);
            _fill.style.width = Length.Percent(Mathf.Clamp01(_life.Fraction) * 100f);
            _fill.EnableInClassList("nameplate__fill--low", _life.Remaining <= _lowLifeSeconds);
        }
    }
}
