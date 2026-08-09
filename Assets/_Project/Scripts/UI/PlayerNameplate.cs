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
        [Tooltip("How wide the plate should be in world units. The character is about 0.7 wide.")]
        [SerializeField] float _widthInWorldUnits = 1.6f;

        [Tooltip("How far above the character's centre it floats, in world units. The character is 0.9 tall.")]
        [SerializeField] float _heightInWorldUnits = 0.75f;

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
        /// <para>A world-space panel has two sizes that both matter and mean different things: how
        /// many UI pixels it is authored at, and how big that rectangle is in metres. Only the
        /// second is something anyone can reason about — "as wide as two characters" — while the
        /// scale factor that connects them is a number nobody can sanity-check by looking at it. So
        /// the scale is derived here and the authored resolution stays a detail of the layout.</para>
        /// <para>The ratio between the two is also what decides whether the text is legible: it
        /// fixes how many UI pixels fall on one world unit, and therefore how much the rasterized
        /// text is squeezed when the panel is drawn on a small window.</para>
        /// <para><b>The parent's scale is divided out.</b> This hangs off the character's visual
        /// child so it inherits the correction smoothing, and that child is scaled up to size the
        /// sprite — so a plate authored as "1.6 units wide, 0.75 up" silently came out three times
        /// both. Compensating here is what lets the two fields above keep meaning world units no
        /// matter what the art needs the sprite scaled to.</para>
        /// </remarks>
        void ApplyWorldSize()
        {
            if (_document == null || _widthInWorldUnits <= 0f) return;

            Vector2 authored = _document.worldSpaceSize;
            if (authored.x <= 0f) return;

            Transform parent = transform.parent;
            float parentScale = parent != null ? parent.lossyScale.x : 1f;
            if (Mathf.Approximately(parentScale, 0f)) return;

            transform.localScale = Vector3.one * (_widthInWorldUnits / authored.x / parentScale);
            transform.localPosition = new Vector3(0f, _heightInWorldUnits / parentScale, 0f);
        }

        /// <remarks>Lets the width be dragged in the Inspector and seen at once, rather than tuned by restart.</remarks>
        void OnValidate()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            ApplyWorldSize();
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
